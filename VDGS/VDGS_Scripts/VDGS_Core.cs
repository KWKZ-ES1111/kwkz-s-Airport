using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class VDGS_Core : UdonSharpBehaviour
{
    [Header("=== 1. 物理参考点 (地面) ===")]
    [SerializeField] private GameObject targetAircraft;
    [SerializeField] private Transform stopAnchor;
    [SerializeField] private Transform centerlineAnchor;

    [Header("=== 2. 视觉轨道锚点 (屏幕) ===")]
    [SerializeField] private Transform vCenter; 
    [SerializeField] private Transform vMaxL;   
    [SerializeField] private Transform vMaxR;   
    [SerializeField] private Transform vBottom; 
    [SerializeField] private Transform vTop;    

    [Header("=== 3. 逻辑阈值设定 ===")]
    [SerializeField] private float verticalStartDist = 5.0f; 
    [SerializeField] private float stopThreshold = 0.25f;    
    [SerializeField] private float lateralMaxMeters = 3.0f;  
    [SerializeField] private float lateralWarningLimit = 1.5f;

    [Header("=== 4. 基础 UI 元素 ===")]
    [SerializeField] private GameObject arrowSingle;
    [SerializeField] private GameObject arrowDouble;
    [SerializeField] private GameObject labelINOP;
    [SerializeField] private GameObject labelTooFar;
    [SerializeField] private GameObject digitUnitM;
    [SerializeField] private GameObject digitPoint;
    [SerializeField] private MeshRenderer digit10;
    [SerializeField] private MeshRenderer digit1;
    [SerializeField] private MeshRenderer digit01;

    [Header("=== 5. 警告与地面设备 ===")]
    [SerializeField] private GameObject arrowRedL; 
    [SerializeField] private GameObject arrowRedR; 
    [SerializeField] private GameObject labelPAX_ON; 
    [SerializeField] private GameObject labelCHOCK_ON; 

    [Header("=== 6. 状态数组 ===")]
    [SerializeField] private GameObject[] stopIndicators;         
    [SerializeField] private GameObject[] flashingStopIndicators; 
    [SerializeField] private GameObject[] aircraftTypeLabels;     

    [Header("=== 7. 检测对象数组 ===")]
    [SerializeField] private Transform[] chockObjects;
    [SerializeField] private Transform[] paxObjects;

    private MaterialPropertyBlock _propBlock;
    private bool _isInZone, _isAuth, _isAtStop;
    private float _stepU = 0.043f;
    void Start()
    {
        _propBlock = new MaterialPropertyBlock();
        ResetSystem();
    }

    void Update()
    {
        if (!_isInZone || targetAircraft == null) return;

        Vector3 aPos = targetAircraft.transform.position;
        float dist = Vector2.Distance(new Vector2(aPos.x, aPos.z), new Vector2(stopAnchor.position.x, stopAnchor.position.z));
        float lateralOffset = centerlineAnchor.InverseTransformPoint(aPos).x;
        bool isPast = stopAnchor.InverseTransformPoint(aPos).z > 0;

        if (isPast || dist < stopThreshold) {
            _isAtStop = true;
            UpdateGroundStatus(); 
            SetUIState("STOP", lateralOffset);
        } else if (dist <= 20.0f) {
            _isAtStop = false;
            UpdateDigits(dist);
            UpdateArrowVisuals(dist, lateralOffset);
            SetUIState("GUIDING", lateralOffset);
        } else {
            _isAtStop = false;
            SetUIState("TOOFAR", lateralOffset);
        }

        if (_isAtStop) DriveFlashing();
    }

    private void UpdateArrowVisuals(float dist, float offset)
    {
        Transform p = arrowSingle.transform.parent;
        if (!p) return;

        // 统一转换为箭头父级下的本地坐标
        Vector3 lCenter = p.InverseTransformPoint(vCenter.position);
        Vector3 lBottom = p.InverseTransformPoint(vBottom.position);
        Vector3 lTop    = p.InverseTransformPoint(vTop.position);
        Vector3 lMaxL   = p.InverseTransformPoint(vMaxL.position);
        Vector3 lMaxR   = p.InverseTransformPoint(vMaxR.position);

        // 1. 水平进度插值 (基于 X 轴)
        float tx = Mathf.Clamp(offset / lateralMaxMeters, -1f, 1f);
        float targetX = tx < 0 ? Mathf.Lerp(lCenter.x, lMaxR.x, Mathf.Abs(tx)) : Mathf.Lerp(lCenter.x, lMaxL.x, Mathf.Abs(tx));

        // 2. 垂直进度插值 (基于 Z 轴)
        float tz = 0;
        if (dist < verticalStartDist) {
            float range = verticalStartDist - stopThreshold;
            tz = 1.0f - Mathf.Clamp01((dist - stopThreshold) / (range > 0 ? range : 0.01f));
        }
        
        float targetZ = Mathf.Lerp(lBottom.z, lTop.z, tz);

        // 3. 组合最终坐标
        // X = 水平, Y = 屏幕深度(由 Center 决定), Z = 垂直高度
        Vector3 finalPos = new Vector3(targetX, lCenter.y, targetZ);

        // Debug 输出：确保你看到 Z 在变化
        if (Time.frameCount % 30 == 0) {
            Debug.Log($"[VDGS] Dist:{dist:F2} | tz:{tz:F2} | ResultZ:{targetZ:F4}");
        }

        bool isNear = dist <= verticalStartDist;
        if (arrowSingle) {
            arrowSingle.SetActive(isNear);
            arrowSingle.transform.localPosition = finalPos;
        }
        if (arrowDouble) {
            arrowDouble.SetActive(!isNear);
            arrowDouble.transform.localPosition = finalPos;
        }
    }

    private void SetUIState(string state, float offset)
    {
        bool isGuiding = (state == "GUIDING");
        bool isStop = (state == "STOP");
        bool isTooFar = (state == "TOOFAR");

        // 控制显示单位 m 和小数点
        if (digitUnitM) digitUnitM.SetActive(isGuiding);
        if (digitPoint) digitPoint.SetActive(isGuiding);
        if (digit10) digit10.enabled = isGuiding;
        if (digit1) digit1.enabled = isGuiding;
        if (digit01) digit01.enabled = isGuiding;

        // 控制红色侧偏警告箭头 (左/右)
        if (arrowRedL) arrowRedL.SetActive(isGuiding && offset < -lateralWarningLimit);
        if (arrowRedR) arrowRedR.SetActive(isGuiding && offset > lateralWarningLimit);

        if (labelTooFar) labelTooFar.SetActive(isTooFar);
        
        // 控制常亮 STOP 数组
        foreach (GameObject g in stopIndicators) if (g) g.SetActive(isStop);

        // 如果不是停止状态，强制关闭 PAX、CHOCK 和闪烁组
        if (!isStop) {
            if (labelPAX_ON) labelPAX_ON.SetActive(false);
            if (labelCHOCK_ON) labelCHOCK_ON.SetActive(false);
            foreach (GameObject g in flashingStopIndicators) if (g) g.SetActive(false);
        }
    }

    private void UpdateGroundStatus()
    {
        if (!_isAtStop) return;

        // 轮挡检测
        bool chock = false;
        foreach (Transform t in chockObjects) 
            if (t != null && Vector3.Distance(t.position, stopAnchor.position) < 0.5f) { chock = true; break; }
        if (labelCHOCK_ON) labelCHOCK_ON.SetActive(chock);

        // PAX (客梯/廊桥) 检测
        bool pax = false;
        foreach (Transform t in paxObjects)
            if (t != null && Vector3.Distance(t.position, targetAircraft.transform.position) < 2.0f) { pax = true; break; }
        if (labelPAX_ON) labelPAX_ON.SetActive(pax);
    }

    private void UpdateDigits(float d)
    {
        d = Mathf.Clamp(d, 0, 19.9f);
        ApplyUV(digit10, (int)(d / 10));
        ApplyUV(digit1, (int)(d % 10));
        ApplyUV(digit01, (int)((d * 10) % 10));
    }

    private void ApplyUV(MeshRenderer r, int n)
    {
        if (!r) return;
        r.GetPropertyBlock(_propBlock);
        _propBlock.SetVector("_MainTex_ST", new Vector4(1, 1, n * _stepU, 0)); // 再次确认 V 偏移为 0
        r.SetPropertyBlock(_propBlock);
    }

    private void DriveFlashing()
    {
        bool on = (Time.time % 0.8f) < 0.4f;
        foreach (GameObject g in flashingStopIndicators) if (g) g.SetActive(on);
    }

    private void ResetSystem()
    {
        _isInZone = _isAuth = _isAtStop = false;
        if (labelINOP) labelINOP.SetActive(true);
        SetUIState("OFF", 0);
        foreach (GameObject g in aircraftTypeLabels) if (g) g.SetActive(false);
    }

    private void UpdateTypeDisplay(string n)
    {
        if (labelINOP) labelINOP.SetActive(false);
        foreach (GameObject g in aircraftTypeLabels) if (g) g.SetActive(n.Contains(g.name));
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (other.gameObject == targetAircraft) { _isInZone = _isAuth = true; }
        else if (!_isInZone) { _isInZone = true; _isAuth = false; TriggerEmergencyStop(); }
    }

    private void OnTriggerExit(Collider other) { if (other.gameObject == targetAircraft || !_isAuth) ResetSystem(); }

    private void TriggerEmergencyStop() { _isAtStop = true; SetUIState("STOP", 0); if (labelINOP) labelINOP.SetActive(false); }
}