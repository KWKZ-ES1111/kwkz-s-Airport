using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class VDGS_Switch : UdonSharpBehaviour
{
    [Header("=== 1. 目标控制组 ===")]
    [Tooltip("在此放入所有需要开关的物体 (例如: VDGS_Display)")]
    [SerializeField] private GameObject[] targetObjects;

    [Header("=== 2. 初始状态 ===")]
    [SerializeField] private bool isOn = false;

    [Header("=== 3. 交互设置 ===")]
    [Tooltip("交互显示的文字提示")]
    [SerializeField] private string interactText = "Toggle VDGS Power";
    [Tooltip("碰撞触发的冷却时间 (秒)，防止连闪")]
    [SerializeField] private float toggleCooldown = 0.5f;

    private float _lastToggleTime;

    void Start()
    {
        // 初始化交互文字
        this.InteractionText = interactText;
        UpdateVisuals();
    }

    // --- 模式 A: 选中并按 E 键交互 ---
    public override void Interact()
    {
        ToggleState();
    }

    // --- 模式 B: 物理碰撞触发 (手部或身体触碰) ---
    private void OnTriggerEnter(Collider other)
    {
        // 仅响应玩家触发
        if (other == null || !other.name.Contains("Hand") && !other.name.Contains("Finger")) return;
        
        if (Time.time - _lastToggleTime > toggleCooldown)
        {
            _lastToggleTime = Time.time;
            ToggleState();
        }
    }

    private void ToggleState()
    {
        isOn = !isOn;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (targetObjects == null) return;

        foreach (GameObject obj in targetObjects)
        {
            if (obj != null)
            {
                obj.SetActive(isOn);
            }
        }
    }
}