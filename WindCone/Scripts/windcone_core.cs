using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public enum RotationAxis { X, Y, Z }

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class windcone_core : UdonSharpBehaviour
{
    [Header("References")]
    public Transform FlagpolePivot; 
    public Transform[] BoneChain;

    [Header("Auto Detection")]
    public bool AutoDetectWind = true;
    public UdonBehaviour WindSource; 

    [Header("Axis Settings")]
    public RotationAxis HeadingAxis = RotationAxis.Y;
    [Tooltip("手动校准旋转偏差。")]
    public float HeadingOffset = 0f;
    public bool InvertHeading = false;
    
    [Header("Kinematics (Sequential Lifting)")]
    public float MaxWindSpeed = 24.0f; 
    public float ResponseSpeed = 3f;  

    [Tooltip("每节骨骼在无风时的下垂角")]
    public float[] AbsoluteDroopAngles = new float[] { -42f, -90f, -90f, -90f, -90f, -90f, -90f };
    
    public RotationAxis LiftingAxis = RotationAxis.X;
    public bool InvertLifting = false;

    [Header("Wind Data")]
    [HideInInspector] public Vector3 Wind; 
    [HideInInspector] public float WindGustStrength = 15f;
    [HideInInspector] public float WindGustiness = 0.03f;

    private Quaternion[] _restLocalRotations;
    private float _currentTotalLift;
    private bool _initialized = false;

    void Start() { EnsureInitialized(); if (AutoDetectWind && WindSource == null) FindWindChanger(); }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        if (BoneChain != null && BoneChain.Length > 0)
        {
            _restLocalRotations = new Quaternion[BoneChain.Length];
            for (int i = 0; i < BoneChain.Length; i++)
            {
                if (BoneChain[i] != null)
                {
                    _restLocalRotations[i] = BoneChain[i].localRotation;
                }
            }
            _initialized = true;
        }
    }

    private void FindWindChanger()
    {
        GameObject changerObj = GameObject.Find("WindChanger");
        if (changerObj != null)
            WindSource = (UdonBehaviour)changerObj.GetComponent(typeof(UdonBehaviour));
    }

    void Update()
    {
        EnsureInitialized();
        if (!_initialized) return;

        if (WindSource != null)
        {
            // 从 SAV_WindChanger 提取风力矢量
            var w = WindSource.GetProgramVariable("_windStrenth_3");
            if (w != null) Wind = (Vector3)w;
            var gs = WindSource.GetProgramVariable("_windGustStrength");
            if (gs != null) WindGustStrength = (float)gs;
            var gn = WindSource.GetProgramVariable("_windGustiness");
            if (gn != null) WindGustiness = (float)gn;
        }

        float time = Time.time;
        float gust = Mathf.PerlinNoise(time * WindGustiness, 0);
        Vector3 windDir = Wind.magnitude > 0.001f ? Wind.normalized : Vector3.forward;
        Vector3 effectiveWind = Wind + windDir * (gust * WindGustStrength);
        float windMag = effectiveWind.magnitude;

        // 1. 偏航指向控制
        if (windMag > 0.1f && FlagpolePivot != null)
        {
            // 在 Atan2 计算出的基础上叠加偏移量进行校准
            float angle = (Mathf.Atan2(effectiveWind.x, effectiveWind.z) * Mathf.Rad2Deg) + HeadingOffset;
            if (InvertHeading) angle += 180f;

            Vector3 euler = Vector3.zero;
            if (HeadingAxis == RotationAxis.X) euler.x = angle;
            else if (HeadingAxis == RotationAxis.Y) euler.y = angle;
            else euler.z = angle;

            FlagpolePivot.localRotation = Quaternion.Lerp(FlagpolePivot.localRotation, Quaternion.Euler(euler), Time.deltaTime * ResponseSpeed);
        }

        // 2. 增量补偿运动学抬升逻辑
        UpdateKinematicLifting(windMag);
    }

    private void UpdateKinematicLifting(float windSpeed)
    {
        if (BoneChain == null || _restLocalRotations == null) return;

        float targetTotalLift = (windSpeed / MaxWindSpeed) * BoneChain.Length;
        _currentTotalLift = Mathf.Lerp(_currentTotalLift, targetTotalLift, Time.deltaTime * ResponseSpeed);

        float parentAbsoluteAngle = 0f;

        for (int i = 0; i < BoneChain.Length; i++)
        {
            if (BoneChain[i] == null) continue;
            
            float boneLiftProgress = Mathf.Clamp01(_currentTotalLift - i);
            float initialAbsDroop = i < AbsoluteDroopAngles.Length ? AbsoluteDroopAngles[i] : -90f;
            float parentInitialAbsDroop = (i == 0 || i - 1 >= AbsoluteDroopAngles.Length) ? 0f : AbsoluteDroopAngles[i - 1];

            float targetAbsoluteAngle = Mathf.Lerp(initialAbsDroop, 0f, boneLiftProgress);
            float requiredLocalAngle = targetAbsoluteAngle - parentAbsoluteAngle;
            float initialLocalAngle = initialAbsDroop - parentInitialAbsDroop;
            float deltaAngle = requiredLocalAngle - initialLocalAngle;
            
            if (InvertLifting) deltaAngle = -deltaAngle;

            Vector3 axis = Vector3.right;
            if (LiftingAxis == RotationAxis.Y) axis = Vector3.up;
            else if (LiftingAxis == RotationAxis.Z) axis = Vector3.forward;

            BoneChain[i].localRotation = _restLocalRotations[i] * Quaternion.AngleAxis(deltaAngle, axis);
            parentAbsoluteAngle = targetAbsoluteAngle;
        }
    }
}