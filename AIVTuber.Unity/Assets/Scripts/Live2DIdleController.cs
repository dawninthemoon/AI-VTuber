using Live2D.Cubism.Core;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class Live2DIdleController : MonoBehaviour
{
    [SerializeField] private CubismModel _model;
    [SerializeField] private Live2DEmotionController _emotionController;
    [SerializeField] private Live2DLipSyncController _lipSyncController;

    [Header("Idle Motion")]
    [SerializeField, Range(0f, 10f)] private float _headAngle = 3f;
    [SerializeField, Range(0f, 10f)] private float _bodyAngle = 2f;
    [SerializeField, Range(0f, 1f)] private float _breathAmount = 0.35f;
    [SerializeField, Min(0.1f)] private float _motionSpeed = 0.8f;
    [SerializeField, Min(0.1f)] private float _speakingFadeSpeed = 3f;

    [Header("Blink And Gaze")]
    [SerializeField] private Vector2 _blinkInterval = new Vector2(2.5f, 5f);
    [SerializeField, Min(0.01f)] private float _blinkDuration = 0.18f;
    [SerializeField] private Vector2 _gazeInterval = new Vector2(3f, 7f);
    [SerializeField, Range(0f, 1f)] private float _gazeAmount = 0.35f;
    [SerializeField, Min(0.01f)] private float _gazeSmoothTime = 0.4f;

    private CubismParameter _angleX;
    private CubismParameter _angleY;
    private CubismParameter _angleZ;
    private CubismParameter _bodyAngleX;
    private CubismParameter _bodyAngleZ;
    private CubismParameter _breath;
    private CubismParameter _eyeBallX;
    private CubismParameter _eyeBallY;

    private float _time;
    private float _idleWeight = 1f;
    private float _nextBlinkTime;
    private float _blinkStartTime = -1f;
    private float _nextGazeTime;
    private Vector2 _gazeTarget;
    private Vector2 _gaze;
    private Vector2 _gazeVelocity;

    private void Awake()
    {
        if (_model == null)
        {
            _model = GetComponent<CubismModel>();
        }

        if (_emotionController == null)
        {
            _emotionController = GetComponent<Live2DEmotionController>();
        }

        if (_model == null)
        {
            Debug.LogError("Idle motion requires a CubismModel.", this);
            enabled = false;
            return;
        }

        foreach (CubismParameter parameter in _model.Parameters)
        {
            switch (parameter.Id)
            {
                case "ParamAngleX": _angleX = parameter; break;
                case "ParamAngleY": _angleY = parameter; break;
                case "ParamAngleZ": _angleZ = parameter; break;
                case "ParamBodyAngleX": _bodyAngleX = parameter; break;
                case "ParamBodyAngleZ": _bodyAngleZ = parameter; break;
                case "ParamBreath": _breath = parameter; break;
                case "ParamEyeBallX": _eyeBallX = parameter; break;
                case "ParamEyeBallY": _eyeBallY = parameter; break;
            }
        }

        _nextBlinkTime = Time.time + RandomInterval(_blinkInterval);
        _nextGazeTime = Time.time + RandomInterval(_gazeInterval);
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        _time += deltaTime * _motionSpeed;

        bool isSpeaking = _lipSyncController != null && _lipSyncController.IsSpeaking;
        _idleWeight = Mathf.MoveTowards(
            _idleWeight,
            isSpeaking ? 0.25f : 1f,
            deltaTime * _speakingFadeSpeed);

        ApplyMotion();
        UpdateGaze(deltaTime, isSpeaking);
        UpdateBlink();
    }

    private void ApplyMotion()
    {
        float sway = Mathf.Sin(_time * 1.1f);
        float nod = Mathf.Sin(_time * 0.7f + 0.9f);

        SetValue(_angleX, sway * _headAngle * _idleWeight);
        SetValue(_angleY, nod * _headAngle * 0.45f * _idleWeight);
        SetValue(_angleZ, Mathf.Sin(_time * 0.85f + 1.6f) * _headAngle * 0.4f * _idleWeight);
        SetValue(_bodyAngleX, Mathf.Sin(_time * 0.75f + 0.5f) * _bodyAngle * _idleWeight);
        SetValue(_bodyAngleZ, Mathf.Sin(_time * 0.9f) * _bodyAngle * 0.5f * _idleWeight);
        SetValue(_breath, (Mathf.Sin(_time * 1.8f) + 1f) * 0.5f * _breathAmount);
    }

    private void UpdateGaze(float deltaTime, bool isSpeaking)
    {
        if (Time.time >= _nextGazeTime)
        {
            _gazeTarget = isSpeaking
                ? Vector2.zero
                : new Vector2(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f)) * _gazeAmount;
            _nextGazeTime = Time.time + RandomInterval(_gazeInterval);
        }

        if (isSpeaking)
        {
            _gazeTarget = Vector2.zero;
        }

        _gaze = Vector2.SmoothDamp(_gaze, _gazeTarget, ref _gazeVelocity, _gazeSmoothTime, Mathf.Infinity, deltaTime);
        SetValue(_eyeBallX, _gaze.x);
        SetValue(_eyeBallY, _gaze.y);
    }

    private void UpdateBlink()
    {
        if (_blinkStartTime < 0f && Time.time >= _nextBlinkTime)
        {
            _blinkStartTime = Time.time;
        }

        float eyeOpening = 1f;
        if (_blinkStartTime >= 0f)
        {
            float progress = (Time.time - _blinkStartTime) / _blinkDuration;
            if (progress >= 1f)
            {
                _blinkStartTime = -1f;
                _nextBlinkTime = Time.time + RandomInterval(_blinkInterval);
            }
            else
            {
                eyeOpening = Mathf.Abs(2f * progress - 1f);
            }
        }

        if (_emotionController != null)
        {
            _emotionController.SetEyeOpening(eyeOpening);
        }
    }

    private static float RandomInterval(Vector2 range)
    {
        float minimum = Mathf.Max(0.1f, range.x);
        float maximum = Mathf.Max(minimum, range.y);
        return Random.Range(minimum, maximum);
    }

    private static void SetValue(CubismParameter parameter, float value)
    {
        if (parameter != null)
        {
            parameter.Value = Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue);
        }
    }
}
