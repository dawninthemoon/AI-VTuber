using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class AIVTuberStateReceiver : MonoBehaviour
{
    [Header("Server")]
    [SerializeField]
    private string serverBaseUrl =
        "http://127.0.0.1:5000";

    [SerializeField]
    private float pollingInterval = 0.25f;

    [Header("Live2D")]
    [SerializeField]
    private Live2DExpressionController live2DExpression;

    [SerializeField]
    private Live2DEmotionController emotionController;

    [SerializeField]
    private Live2DLipSyncController lipSyncController;

    [Header("Audio")]
    [SerializeField]
    private AudioSource audioSource;

    [Header("Subtitle")]
    [SerializeField]
    private TMP_Text subtitleText;

    private long lastVersion = -1;

    private Coroutine voiceCoroutine;


    private string StateUrl =>
        $"{serverBaseUrl}/unity/state";

    private string GetAudioUrl(long version)
    {
        return
            $"{serverBaseUrl}/unity/audio?version={version}";
    }


    private void Start()
    {
        if (subtitleText != null)
        {
            subtitleText.text =
                string.Empty;
        }

        if (emotionController != null)
        {
            emotionController.SetEmotion(
                "neutral",
                0.5f
            );
        }

        StartCoroutine(
            PollState()
        );
    }


    private IEnumerator PollState()
    {
        while (true)
        {
            using UnityWebRequest request =
                UnityWebRequest.Get(
                    StateUrl
                );

            // 캐시된 state를 받지 않도록 함
            request.SetRequestHeader(
                "Cache-Control",
                "no-cache"
            );

            yield return
                request.SendWebRequest();

            if (
                request.result ==
                UnityWebRequest.Result.Success
            )
            {
                string json =
                    request.downloadHandler.text;

                CharacterState state =
                    JsonUtility.FromJson<CharacterState>(
                        json
                    );

                if (state != null)
                {
                    // 첫 요청은 서버에 남아 있는
                    // 이전 상태일 수 있으므로 재생하지 않는다.
                    if (lastVersion < 0)
                    {
                        lastVersion =
                            state.version;

                        Debug.Log(
                            $"Initial AI state ignored. version={state.version}"
                        );
                    }
                    else if (
                        state.version !=
                        lastVersion
                    )
                    {
                        lastVersion =
                            state.version;

                        OnNewState(
                            state
                        );
                    }
                }
            }
            else
            {
                Debug.LogWarning(
                    $"AI Server Error: " +
                    $"{request.responseCode} " +
                    $"{request.error} " +
                    $"URL={StateUrl}"
                );
            }

            yield return
                new WaitForSeconds(
                    pollingInterval
                );
        }
    }


    private void OnNewState(
        CharacterState state)
    {
        Debug.Log(
            $"AI: {state.text}"
        );

        Debug.Log(
            $"Emotion: {state.emotion}"
        );

        Debug.Log(
            $"Intensity: {state.intensity}"
        );

        Debug.Log(
            $"Version: {state.version}"
        );


        // ------------------------
        // Subtitle
        // ------------------------

        if (subtitleText != null)
        {
            subtitleText.text =
                state.text;
        }


        // ------------------------
        // Emotion
        // ------------------------

        if (emotionController != null)
        {
            emotionController.SetEmotion(
                state.emotion,
                state.intensity
            );
        }


        // ------------------------
        // Lip Sync
        // ------------------------

        if (lipSyncController != null)
        {
            if (
                string.IsNullOrWhiteSpace(
                    state.text
                )
            )
            {
                lipSyncController
                    .StopSpeaking();
            }
            else
            {
                lipSyncController
                    .Speak(
                        state.text
                    );
            }
        }


        // ------------------------
        // TTS Voice
        // ------------------------

        if (voiceCoroutine != null)
        {
            StopCoroutine(
                voiceCoroutine
            );
        }

        voiceCoroutine =
            StartCoroutine(
                PlayVoice(
                    state.version
                )
            );
    }


    private IEnumerator PlayVoice(
        long version)
    {
        if (audioSource == null)
        {
            Debug.LogWarning(
                "AudioSource가 연결되어 있지 않습니다."
            );

            yield break;
        }

        string url =
            GetAudioUrl(
                version
            );

        // 혹시 서버에서 WAV 저장과
        // state 갱신 사이에 미세한 타이밍 차이가
        // 생길 경우를 대비한다.
        const int maxRetries = 20;
        const float retryDelay = 0.15f;


        for (
            int attempt = 0;
            attempt < maxRetries;
            attempt++
        )
        {
            using UnityWebRequest request =
                UnityWebRequestMultimedia
                    .GetAudioClip(
                        url,
                        AudioType.WAV
                    );

            request.SetRequestHeader(
                "Cache-Control",
                "no-cache"
            );

            yield return
                request.SendWebRequest();


            if (
                request.result ==
                UnityWebRequest.Result.Success
            )
            {
                AudioClip clip =
                    DownloadHandlerAudioClip
                        .GetContent(
                            request
                        );

                if (clip == null)
                {
                    Debug.LogWarning(
                        $"TTS AudioClip 생성 실패. version={version}"
                    );

                    yield break;
                }


                if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }


                audioSource.clip =
                    clip;

                audioSource.Play();


                Debug.Log(
                    $"TTS 재생 시작. version={version}"
                );

                voiceCoroutine =
                    null;

                yield break;
            }


            // TTS 오디오가 아직 준비되지 않았으면
            // 잠깐 기다렸다 다시 요청
            if (
                request.responseCode ==
                404
            )
            {
                yield return
                    new WaitForSeconds(
                        retryDelay
                    );

                continue;
            }


            Debug.LogWarning(
                $"TTS download failed: " +
                $"{request.responseCode} " +
                $"{request.error} " +
                $"URL={url}"
            );

            voiceCoroutine =
                null;

            yield break;
        }


        Debug.LogWarning(
            $"TTS audio not found after retry. " +
            $"version={version}"
        );

        voiceCoroutine =
            null;
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