using UnityEngine;
using Live2D.Cubism.Core;

public class Live2DEmotionController : MonoBehaviour
{
    [SerializeField]
    private CubismModel model;

    private string currentEmotion = "neutral";
    private float currentIntensity = 0.5f;
    private float _eyeOpening = 1f;

    private CubismParameter eyeLSmile;
    private CubismParameter eyeRSmile;

    private CubismParameter browLForm;
    private CubismParameter browRForm;

    private CubismParameter mouthForm;

    private CubismParameter eyeLOpen;
    private CubismParameter eyeROpen;

    private void Awake()
    {
        if (model == null)
        {
            model = GetComponent<CubismModel>();
        }

        CacheParameters();
    }

    private void CacheParameters()
    {
        foreach (var parameter in model.Parameters)
        {
            switch (parameter.Id)
            {
                case "ParamEyeLSmile":
                    eyeLSmile = parameter;
                    break;

                case "ParamEyeRSmile":
                    eyeRSmile = parameter;
                    break;

                case "ParamBrowLForm":
                    browLForm = parameter;
                    break;

                case "ParamBrowRForm":
                    browRForm = parameter;
                    break;

                case "ParamMouthForm":
                    mouthForm = parameter;
                    break;

                case "ParamEyeLOpen":
                    eyeLOpen = parameter;
                    break;

                case "ParamEyeROpen":
                    eyeROpen = parameter;
                    break;
            }
        }
    }

    public void SetEmotion(
        string emotion,
        float intensity)
    {
        currentEmotion = string.IsNullOrEmpty(emotion) ? "neutral" : emotion.ToLowerInvariant();
        currentIntensity = Mathf.Clamp01(intensity);
    }

    public void SetEyeOpening(float eyeOpening)
    {
        _eyeOpening = Mathf.Clamp01(eyeOpening);
    }

    private void LateUpdate()
    {
        ApplyEmotion();
    }

    private void ApplyEmotion()
    {
        float intensity = currentIntensity;

        ResetEmotion();

        switch (currentEmotion)
        {
            case "happy":
                SetValue(eyeLSmile, intensity);
                SetValue(eyeRSmile, intensity);
                SetValue(mouthForm, intensity);
                break;

            case "angry":
                SetValue(browLForm, -intensity);
                SetValue(browRForm, -intensity);
                SetValue(mouthForm, -intensity);
                break;

            case "sad":
                SetValue(browLForm, intensity);
                SetValue(browRForm, intensity);

                // 슬픔을 좀 더 강하게
                SetValue(
                    mouthForm,
                    Mathf.Lerp(0f, -1f, intensity)
                );

                SetValue(
                    eyeLOpen,
                    Mathf.Lerp(1f, 0.75f, intensity)
                );

                SetValue(
                    eyeROpen,
                    Mathf.Lerp(1f, 0.75f, intensity)
                );

                break;

            case "surprised":
                SetValue(
                    eyeLOpen,
                    Mathf.Lerp(1f, 1.5f, intensity)
                );

                SetValue(
                    eyeROpen,
                    Mathf.Lerp(1f, 1.5f, intensity)
                );

                break;
        }

        ApplyEyeOpening();
    }

    private void ApplyEyeOpening()
    {
        if (eyeLOpen != null)
        {
            SetValue(eyeLOpen, eyeLOpen.Value * _eyeOpening);
        }

        if (eyeROpen != null)
        {
            SetValue(eyeROpen, eyeROpen.Value * _eyeOpening);
        }
    }

    private void ResetEmotion()
    {
        SetValue(eyeLSmile, 0f);
        SetValue(eyeRSmile, 0f);

        SetValue(browLForm, 0f);
        SetValue(browRForm, 0f);

        SetValue(mouthForm, 0f);

        SetValue(eyeLOpen, 1f);
        SetValue(eyeROpen, 1f);
    }

    private void SetValue(
        CubismParameter parameter,
        float value)
    {
        if (parameter == null)
            return;

        parameter.Value = Mathf.Clamp(
            value,
            parameter.MinimumValue,
            parameter.MaximumValue
        );
    }
}
