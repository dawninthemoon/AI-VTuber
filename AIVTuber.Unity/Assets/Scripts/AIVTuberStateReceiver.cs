using System.Collections;
using System.Collections.Generic;
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
    private long activeMessageId = -1;

    private readonly List<Coroutine> voiceCoroutines =
        new List<Coroutine>();
    private readonly Queue<AudioClip> voiceQueue =
        new Queue<AudioClip>();
    private AudioClip downloadedClip;


    private string StateUrl =>
        $"{serverBaseUrl}/unity/state";

    private string GetAudioUrl(long version)
    {
        return
            $"{serverBaseUrl}/unity/audio?version={version}";
    }


    private void Start()
    {
        if (lipSyncController != null)
        {
            lipSyncController.SetAudioSource(audioSource);
        }

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


    private void Update()
    {
        if (audioSource == null || audioSource.isPlaying)
        {
            return;
        }

        if (downloadedClip != null)
        {
            Destroy(downloadedClip);
            downloadedClip = null;
        }

        if (voiceQueue.Count == 0)
        {
            if (lipSyncController != null)
            {
                lipSyncController.StopSpeaking();
            }

            return;
        }

        downloadedClip = voiceQueue.Dequeue();
        audioSource.clip = downloadedClip;
        audioSource.Play();
        Debug.Log(
            $"TTS 큐 재생 시작. 남은 조각={voiceQueue.Count}"
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

        Debug.Log(
            $"TTS Segment: {state.segmentIndex + 1}/{state.segmentCount}, " +
            $"Message: {state.messageId}"
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
        // TTS Voice
        // ------------------------

        bool isNewMessage =
            state.messageId != activeMessageId;

        if (isNewMessage)
        {
            StopVoiceDownloads();
            StopVoice();
            activeMessageId = state.messageId;
        }

        if (string.IsNullOrWhiteSpace(state.text) || !state.hasAudio)
        {
            return;
        }

        Coroutine coroutine = StartCoroutine(
            DownloadVoiceSegment(
                state.version,
                state.messageId
            )
        );
        voiceCoroutines.Add(coroutine);
    }


    private IEnumerator DownloadVoiceSegment(
        long version,
        long messageId)
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

        using UnityWebRequest request =
            UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV);
        request.SetRequestHeader("Cache-Control", "no-cache");
        yield return request.SendWebRequest();

        if (messageId != activeMessageId)
        {
            yield break;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning(
                $"TTS download failed: {request.responseCode} {request.error} URL={url}");
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
        if (clip == null)
        {
            Debug.LogWarning($"TTS AudioClip 생성 실패. version={version}");
            yield break;
        }

        voiceQueue.Enqueue(clip);
        Debug.Log(
            $"TTS 조각 큐 추가. version={version}, 대기={voiceQueue.Count}"
        );
    }

    private void StopVoice()
    {
        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }

        if (downloadedClip != null)
        {
            Destroy(downloadedClip);
            downloadedClip = null;
        }

        while (voiceQueue.Count > 0)
        {
            AudioClip queuedClip = voiceQueue.Dequeue();
            if (queuedClip != null)
            {
                Destroy(queuedClip);
            }
        }

        if (lipSyncController != null)
        {
            lipSyncController.StopSpeaking();
        }
    }

    private void StopVoiceDownloads()
    {
        foreach (Coroutine coroutine in voiceCoroutines)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }

        voiceCoroutines.Clear();
    }

    private void OnDestroy()
    {
        StopVoiceDownloads();
        StopVoice();
    }


    [System.Serializable]
    private class CharacterState
    {
        public string text;
        public string emotion;
        public float intensity;
        public long version;
        public bool hasAudio;
        public long messageId;
        public int segmentIndex;
        public int segmentCount;
    }
}
