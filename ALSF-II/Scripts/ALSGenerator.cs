using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

#if UNITY_EDITOR

// --- JSON 数据映射类 ---
[System.Serializable]
public class ALSConfigData
{
    public string systemName;
    public int nodeCount;
    public ALSNodeData[] nodes;
}

[System.Serializable]
public class ALSNodeData
{
    public string name;
    public Vector3 position;
    public Quaternion rotation;
}

// --- 核心生成器 ---
public class ALSGenerator : MonoBehaviour
{
    public enum Axis { XPlus, XMinus, YPlus, YMinus, ZPlus, ZMinus }

    [Header("Data Source (JSON)")]
    public TextAsset jsonConfigFile;

    [Header("Ref Point (For Flashing Logic)")]
    public Transform runwayThreshold;
    public Vector3 approachVector = Vector3.back;
    public float cycleTime = 0.5f;
    public bool reverseFlashing = false; 

    [Header("Prefab Mapping")]
    public GameObject lampPostSingle; 
    public GameObject lampPostDouble; 
    public GameObject frame4;         
    public GameObject frame5;         
    public GameObject frame8;         
    public GameObject lampHolder;      
    public GameObject flasherLampHolder; 

    [Header("Alignment Settings")]
    public Axis postForward = Axis.YPlus; 
    public Vector3 postRotationOffset;          // 单灯杆校准
    public Vector3 postDoubleRotationOffset;    // 双灯杆独立校准 (解决F8偏移问题)

    public Axis frameForward = Axis.ZPlus;
    public Vector3 frameRotationOffset;

    public Axis lampForward = Axis.ZPlus;
    public Vector3 lampRotationOffset;

    [Header("Generation Settings")]
    public bool setStatic = true;

    [ContextMenu("Generate System")]
    public void Generate()
    {
        if (jsonConfigFile == null) return;

        Debug.Log("[ALS] === 开始生成系统 ===");
        ClearGenerated();

        ALSConfigData configData = JsonUtility.FromJson<ALSConfigData>(jsonConfigFile.text);
        if (configData == null || configData.nodes == null) return;

        List<GameObject> flasherList = new List<GameObject>();
        List<float> delayList = new List<float>();

        foreach (ALSNodeData node in configData.nodes)
        {
            if (!node.name.StartsWith("ALS_")) continue;
            string[] parts = node.name.Split('_');
            if (parts.Length < 4) continue;

            GameObject anchorObj = new GameObject(node.name);
            anchorObj.transform.SetParent(this.transform);
            anchorObj.transform.localPosition = node.position;
            anchorObj.transform.localRotation = node.rotation;

            ProcessNode(anchorObj.transform, parts, flasherList, delayList);
        }

        // 自动注入 Udon 控制器
        ALSFlasherController controller = GetComponent<ALSFlasherController>();
        if (controller == null) controller = gameObject.AddComponent<ALSFlasherController>();

        controller.flasherObjects = flasherList.ToArray();
        controller.delays = delayList.ToArray();
        controller.cycleTime = this.cycleTime;
        EditorUtility.SetDirty(controller);

        Debug.Log($"[ALS] 生成结束。注入闪光灯: {flasherList.Count}个");
    }

    private bool ProcessNode(Transform anchor, string[] parts, List<GameObject> flasherList, List<float> delayList)
    {
        string postType = parts[1];  
        string frameType = parts[2]; 
        string config = parts[3];    

        Transform currentMountPoint = anchor;

        // --- 1. 实例化灯杆 (解耦单/双杆旋转) ---
        if (postType != "P0")
        {
            bool isDouble = (frameType == "F8");
            GameObject postPrefab = isDouble ? lampPostDouble : lampPostSingle;
            Vector3 currentPostOffset = isDouble ? postDoubleRotationOffset : postRotationOffset;

            if (postPrefab != null)
            {
                GameObject postObj = InstantiateNode(postPrefab, anchor, postForward, currentPostOffset);
                postObj.name = $"Post_{anchor.name}";

                Transform postPort = FindDeepChild(postObj.transform, "LampPort");
                if (postPort != null) currentMountPoint = postPort;
            }
        }

        // --- 2. 实例化灯架 ---
        if (frameType != "F0")
        {
            GameObject framePrefab = null;
            switch (frameType)
            {
                case "F4": framePrefab = frame4; break;
                case "F5": framePrefab = frame5; break;
                case "F8": framePrefab = frame8; break;
            }

            if (framePrefab != null)
            {
                GameObject frameObj = InstantiateNode(framePrefab, currentMountPoint, frameForward, frameRotationOffset);
                frameObj.name = $"Frame_{anchor.name}";
                currentMountPoint = frameObj.transform;
            }
        }

        // --- 3. 确定挂载点与安装灯具 ---
        Transform[] lampPorts = FindAllDeepChildren(currentMountPoint, "LampPort");
        if (lampPorts.Length == 0) lampPorts = new Transform[] { currentMountPoint };

        bool isFlasher = config.Contains("S");
        GameObject selectedLampPrefab = (isFlasher && flasherLampHolder != null) ? flasherLampHolder : lampHolder;

        if (selectedLampPrefab != null)
        {
            foreach (Transform port in lampPorts)
            {
                GameObject holderObj = InstantiateNode(selectedLampPrefab, port, lampForward, lampRotationOffset);
                SetupLampLogic(holderObj, config, anchor.position, flasherList, delayList);
            }
        }
        
        return true;
    }

    private void SetupLampLogic(GameObject holder, string config, Vector3 worldPos, List<GameObject> flasherList, List<float> delayList)
    {
        Color targetColor = Color.white;
        if (config.Contains("R")) targetColor = Color.red;
        else if (config.Contains("G")) targetColor = Color.green;
        else if (config.Contains("Y")) targetColor = Color.yellow;
        else if (config.Contains("B")) targetColor = Color.blue;

        foreach (ParticleSystem ps in holder.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = targetColor;
        }

        GameObject specificFlashObj = null;
        foreach (Transform child in holder.GetComponentsInChildren<Transform>(true))
        {
            if ((child.name.Contains("Lamp") || child.name.Contains("Flash")) && !child.name.Contains("Mesh"))
            {
                specificFlashObj = child.gameObject;
                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_Color", targetColor);
                    mpb.SetColor("_EmissionColor", targetColor * 5.0f);
                    renderer.SetPropertyBlock(mpb);
                }
            }
        }

        if (config.Contains("S") && runwayThreshold != null)
        {
            Vector3 toThreshold = worldPos - runwayThreshold.position;
            float distance = Vector3.Dot(toThreshold, approachVector.normalized);
            float t = Mathf.Clamp01(distance / 900f);
            float delay = (reverseFlashing ? t : (1.0f - t)) * cycleTime;
            
            if (specificFlashObj != null)
            {
                flasherList.Add(specificFlashObj);
                delayList.Add(delay);
                specificFlashObj.SetActive(false); 
            }
        }

        if (setStatic && !config.Contains("S"))
            GameObjectUtility.SetStaticEditorFlags(holder, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
    }

    private GameObject InstantiateNode(GameObject prefab, Transform parent, Axis forwardAxis, Vector3 eulerOffset)
    {
        GameObject go = PrefabUtility.IsPartOfPrefabAsset(prefab) 
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent) 
            : Instantiate(prefab, parent);
        
        go.transform.localPosition = Vector3.zero;
        Quaternion axisRot = GetRotationFromAxis(forwardAxis);
        Quaternion offsetRot = Quaternion.Euler(eulerOffset);
        go.transform.localRotation = prefab.transform.localRotation * axisRot * offsetRot;
        
        Vector3 parentScale = parent.lossyScale; 
        go.transform.localScale = new Vector3(
            prefab.transform.localScale.x / (parentScale.x != 0 ? parentScale.x : 1),
            prefab.transform.localScale.y / (parentScale.y != 0 ? parentScale.y : 1),
            prefab.transform.localScale.z / (parentScale.z != 0 ? parentScale.z : 1)
        );
        return go;
    }

    private Quaternion GetRotationFromAxis(Axis axis)
    {
        switch (axis)
        {
            case Axis.XPlus: return Quaternion.LookRotation(Vector3.right, Vector3.up);
            case Axis.XMinus: return Quaternion.LookRotation(Vector3.left, Vector3.up);
            case Axis.YPlus: return Quaternion.LookRotation(Vector3.up, Vector3.back);
            case Axis.YMinus: return Quaternion.LookRotation(Vector3.down, Vector3.forward);
            case Axis.ZPlus: return Quaternion.LookRotation(Vector3.forward, Vector3.up);
            case Axis.ZMinus: return Quaternion.LookRotation(Vector3.back, Vector3.up);
            default: return Quaternion.identity;
        }
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name.StartsWith(name)) return child;
        return null;
    }

    private Transform[] FindAllDeepChildren(Transform parent, string name)
    {
        List<Transform> result = new List<Transform>();
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name.Contains(name) && child != parent) result.Add(child);
        return result.ToArray();
    }

    [ContextMenu("Clear Generated")]
    public void ClearGenerated()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
    }
}
#endif