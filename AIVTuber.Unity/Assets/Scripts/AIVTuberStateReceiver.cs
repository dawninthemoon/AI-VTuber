using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class AIVTuberStateReceiver : MonoBehaviour
{
    [SerializeField]
    private string serverUrl =
        "http://localhost:5000/unity/state";

    [SerializeField]
    private float pollingInterval = 0.25f;

    [SerializeField]
    private Live2DExpressionController live2DExpression;

    [SerializeField]
    private Live2DEmotionController emotionController;

    [SerializeField]
    private Live2DLipSyncController lipSyncController;

    [SerializeField]
    private TMP_Text subtitleText;

    private long lastVersion = -1;

    private void Start()
    {
        StartCoroutine(PollState());
    }

    private IEnumerator PollState()
    {
        while (true)
        {
            using UnityWebRequest request =
                UnityWebRequest.Get(serverUrl);

            yield return request.SendWebRequest();

            if (request.result
                == UnityWebRequest.Result.Success)
            {
                string json =
                    request.downloadHandler.text;

                CharacterState state =
                    JsonUtility.FromJson<CharacterState>(
                        json
                    );

                if (state != null &&
                    state.version != lastVersion)
                {
                    lastVersion = state.version;

                    OnNewState(state);
                }
            }
            else
            {
                Debug.LogWarning(
                    $"AI Server Error: {request.error}"
                );
            }

            yield return new WaitForSeconds(
                pollingInterval
            );
        }
    }

    private void OnNewState(CharacterState state)
    {
        Debug.Log($"AI: {state.text}");
        Debug.Log($"Emotion: {state.emotion}");
        Debug.Log($"Intensity: {state.intensity}");

        if (subtitleText != null)
        {
            subtitleText.text = state.text;
        }

        if (emotionController != null)
        {
            emotionController.SetEmotion(
                state.emotion,
                state.intensity
            );
        }

        if (lipSyncController != null)
        {
            lipSyncController.Speak(state.text);
        }
    }

    [System.Serializable]
    private class CharacterState
    {
        public string text;
        public string emotion;
        public float intensity;
        public long version;
    }
}