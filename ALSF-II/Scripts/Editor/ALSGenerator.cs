using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;
using System.Collections.Generic;

#if UNITY_EDITOR
public class ALSGenerator : MonoBehaviour
{
    public enum Axis { XPlus, XMinus, YPlus, YMinus, ZPlus, ZMinus }

    [Header("Ref Point (For Flashing Logic)")]
    public Transform runwayThreshold; // 跑道入口中心点
    public Vector3 approachVector = Vector3.back; // 进近方向（从远端指向跑道）
    public float cycleTime = 0.5f; // 顺序闪光总周期

    [Header("Prefab Mapping")]
    public GameObject lampPostSingle; // 单灯柱 (P5/P8)
    public GameObject lampPostDouble; // 双灯柱 (P8 for F8)
    public GameObject frame4;         // F4
    public GameObject frame5;         // F5
    public GameObject frame8;         // F8 (LampPost4Double)
    public GameObject lampHolder;     // 灯具本体

    [Header("Alignment Settings")]
    public Axis postForward = Axis.YPlus; 
    public Axis frameForward = Axis.ZPlus;
    public Axis lampForward = Axis.ZPlus;

    [Header("Generation Settings")]
    public bool generateLight = false;
    public bool setStatic = true;

    // 清理生成的物体并重新生成
    [ContextMenu("Generate System")]
    public void Generate()
    {
        ClearGenerated();

        // 查找所有子物体（占位符）
        Transform[] children = GetComponentsInChildren<Transform>();
        List<Transform> holdersToSetup = new List<Transform>();

        foreach (var anchor in children)
        {
            if (anchor == transform) continue;
            
            string cleanName = Regex.Replace(anchor.name, @"(\.\d+)+$", ""); // 过滤Blender后缀
            if (!cleanName.StartsWith("ALS_")) continue;

            // 解析：ALS_[Post]_[Frame]_[ColorFlag]_[Index]
            string[] parts = cleanName.Split('_');
            if (parts.Length < 4) 
            {
                Debug.LogWarning($"[ALS] 跳过识别失败的命名: {anchor.name}");
                continue;
            }

            ProcessNode(anchor, parts);
        }

        Debug.Log("[ALS] 系统生成完成。");
    }

    private void ProcessNode(Transform anchor, string[] parts)
    {
        string postType = parts[1];  // P5, P8
        string frameType = parts[2]; // F4, F5, F8
        string config = parts[3];    // W, WS, R, G, Y, B...

        // 1. 实例化灯柱 (Post)
        GameObject postPrefab = (frameType == "F8") ? lampPostDouble : lampPostSingle;
        if (postPrefab == null) return;

        GameObject postObj = InstantiatePrefab(postPrefab, anchor, postForward);
        postObj.name = $"Post_{anchor.name}";

        // 2. 找到灯柱上的接口 (LampPort)
        Transform postPort = FindDeepChild(postObj.transform, "LampPort");
        if (postPort == null) return;

        // 3. 实例化灯架 (Frame)
        GameObject framePrefab = null;
        switch (frameType)
        {
            case "F4": framePrefab = frame4; break;
            case "F5": framePrefab = frame5; break;
            case "F8": framePrefab = frame8; break;
        }
        if (framePrefab == null) return;

        GameObject frameObj = InstantiatePrefab(framePrefab, postPort, frameForward);
        frameObj.name = $"Frame_{anchor.name}";

        // 4. 遍历灯架接口并安装灯具
        Transform[] framePorts = FindAllDeepChildren(frameObj.transform, "LampPort");
        foreach (var fPort in framePorts)
        {
            GameObject holderObj = InstantiatePrefab(lampHolder, fPort, lampForward);
            SetupLampLogic(holderObj, config, anchor.position);
        }
    }

    private void SetupLampLogic(GameObject holder, string config, Vector3 worldPos)
    {
        // 识别颜色
        Color targetColor = Color.white;
        if (config.Contains("R")) targetColor = Color.red;
        else if (config.Contains("G")) targetColor = Color.green;
        else if (config.Contains("Y")) targetColor = Color.yellow;
        else if (config.Contains("B")) targetColor = Color.blue;

        // 获取自发光面片 MeshRenderer
        MeshRenderer renderer = holder.GetComponentInChildren<MeshRenderer>();
        if (renderer != null)
        {
            // 使用 MaterialPropertyBlock 以优化 VRChat Draw Calls (Batching)
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", targetColor);
            mpb.SetColor("_EmissionColor", targetColor * 5.0f); // 增强自发光强度
            renderer.SetPropertyBlock(mpb);
        }

        // 处理顺序闪光 (Rabbit)
        if (config.Contains("S") && runwayThreshold != null)
        {
            // 计算点在进近轴线上的投影距离
            Vector3 toThreshold = worldPos - runwayThreshold.position;
            float distance = Vector3.Dot(toThreshold, approachVector.normalized);
            
            // 假设最远灯距离为 900m，计算 0-1 范围的延迟
            float delay = Mathf.Clamp01(distance / 900f) * cycleTime;

            // 自动为生成的灯具添加 Udon 变量（此处仅示范数据注入，需配合您的Udon脚本使用）
            // 如果使用 UdonSharp，可在此设置变量同步
            holder.name += $"_S_Delay_{delay:F3}"; 
        }

        if (setStatic)
        {
            GameObjectUtility.SetStaticEditorFlags(holder, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
        }
    }

    // --- 工具方法 ---

    private GameObject InstantiatePrefab(GameObject prefab, Transform parent, Axis forwardAxis)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = GetRotationFromAxis(forwardAxis);
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
        foreach (Transform child in parent.GetComponentsInChildren<Transform>())
        {
            if (child.name.StartsWith(name)) return child;
        }
        return null;
    }
    

    private Transform[] FindAllDeepChildren(Transform parent, string name)
    {
        List<Transform> result = new List<Transform>();
        foreach (Transform child in parent.GetComponentsInChildren<Transform>())
        {
            if (child.name.Contains(name) && child != parent) result.Add(child);
        }
        return result.ToArray();
    }

    [ContextMenu("Clear Generated")]
    public void ClearGenerated()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            // 只清理脚本生成的层级，不删除占位符本身
            for (int j = child.childCount - 1; j >= 0; j--)
            {
                DestroyImmediate(child.GetChild(j).gameObject);
            }
        }
    }
}
#endif