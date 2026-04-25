using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ALSFlasherController : UdonSharpBehaviour
{
    [Header("Flasher Objects (Auto-filled)")]
    public GameObject[] flasherObjects; // 改为存储 GameObject
    public float[] delays;
    
    [Header("Settings")]
    public float cycleTime = 0.5f;
    public float flashDuration = 0.06f; // 闪光持续时间（建议略大于一帧，如 0.06s）

    private float timer = 0f;

    void Update()
    {
        if (flasherObjects == null || flasherObjects.Length == 0) return;

        timer += Time.deltaTime;
        if (timer > cycleTime) timer -= cycleTime;

        for (int i = 0; i < flasherObjects.Length; i++)
        {
            if (flasherObjects[i] == null) continue;

            float start = delays[i];
            float end = start + flashDuration;
            bool shouldBeActive = false;

            // 处理时间环绕判定
            if (end <= cycleTime)
            {
                shouldBeActive = (timer >= start && timer < end);
            }
            else
            {
                shouldBeActive = (timer >= start || timer < (end - cycleTime));
            }

            // 仅在状态改变时调用 SetActive，优化性能
            if (flasherObjects[i].activeSelf != shouldBeActive)
            {
                flasherObjects[i].SetActive(shouldBeActive);
            }
        }
    }
}