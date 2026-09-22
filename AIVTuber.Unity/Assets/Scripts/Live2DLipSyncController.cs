using System.Collections;
using UnityEngine;
using Live2D.Cubism.Core;

public class Live2DLipSyncController : MonoBehaviour
{
    [SerializeField]
    private CubismModel model;

    [SerializeField]
    private float speed = 12f;

    private CubismParameter mouthOpen;

    private Coroutine speakingCoroutine;
    public bool IsSpeaking => speakingCoroutine != null;

    private void Awake()
    {
        if (model == null)
        {
            model = GetComponent<CubismModel>();
        }

        foreach (var parameter in model.Parameters)
        {
            if (parameter.Id == "ParamMouthOpenY")
            {
                mouthOpen = parameter;
                break;
            }
        }
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            StopSpeaking();
            return;
        }

        if (speakingCoroutine != null)
        {
            StopCoroutine(speakingCoroutine);
        }

        speakingCoroutine =
            StartCoroutine(SpeakRoutine(text));
    }

    public void StopSpeaking()
    {
        if (speakingCoroutine != null)
        {
            StopCoroutine(speakingCoroutine);
            speakingCoroutine = null;
        }

        if (mouthOpen != null)
        {
            mouthOpen.Value = 0f;
        }
    }

    private void OnDisable()
    {
        StopSpeaking();
    }

    private IEnumerator SpeakRoutine(string text)
    {
        // 한글 대사 길이로 임시 발화시간 추정
        float duration =
            Mathf.Clamp(
                text.Length * 0.07f,
                0.5f,
                8f
            );

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // 0~1 사이에서 입을 반복적으로 열고 닫음
            float mouth =
                (Mathf.Sin(
                    elapsed * speed
                ) + 1f) * 0.5f;

            if (mouthOpen != null)
            {
                mouthOpen.Value = mouth;
            }

            yield return null;
        }

        if (mouthOpen != null)
        {
            mouthOpen.Value = 0f;
        }

        speakingCoroutine = null;
    }
}
