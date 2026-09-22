using Live2D.Cubism.Core;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class Live2DLipSyncController : MonoBehaviour
{
    [SerializeField] private CubismModel model;
    [SerializeField] private AudioSource audioSource;
    [SerializeField, Range(0f, 0.2f)] private float noiseFloor = 0.015f;
    [SerializeField, Range(1f, 30f)] private float sensitivity = 12f;
    [SerializeField, Min(0.01f)] private float attackTime = 0.04f;
    [SerializeField, Min(0.01f)] private float releaseTime = 0.12f;

    private readonly float[] samples = new float[256];
    private CubismParameter mouthOpen;
    private float opening;

    public bool IsSpeaking => audioSource != null && audioSource.isPlaying;

    public void SetAudioSource(AudioSource source)
    {
        audioSource = source;
    }

    private void Awake()
    {
        if (model == null)
        {
            model = GetComponent<CubismModel>();
        }

        if (model == null)
        {
            Debug.LogError("Lip sync requires a CubismModel.", this);
            enabled = false;
            return;
        }

        foreach (CubismParameter parameter in model.Parameters)
        {
            if (parameter.Id == "ParamMouthOpenY")
            {
                mouthOpen = parameter;
                break;
            }
        }
    }

    private void LateUpdate()
    {
        float target = 0f;

        if (IsSpeaking)
        {
            audioSource.GetOutputData(samples, 0);

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }

            float rms = Mathf.Sqrt(sum / samples.Length);
            target = Mathf.Clamp01((rms - noiseFloor) * sensitivity);
        }

        float smoothTime = target > opening ? attackTime : releaseTime;
        float blend = 1f - Mathf.Exp(-Time.deltaTime / smoothTime);
        opening = Mathf.Lerp(opening, target, blend);

        if (mouthOpen != null)
        {
            mouthOpen.Value = Mathf.Lerp(
                mouthOpen.MinimumValue,
                mouthOpen.MaximumValue,
                opening);
        }
    }

    public void StopSpeaking()
    {
        opening = 0f;
        if (mouthOpen != null)
        {
            mouthOpen.Value = 0f;
        }
    }

    private void OnDisable()
    {
        StopSpeaking();
    }
}
