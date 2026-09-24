using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class AIVTuberStateReceiver : MonoBehaviour
{
    [Header("Server")]
    [SerializeField]
    private string serverBaseUrl =
        "http://127.0.0.1:5050";

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
    private readonly HashSet<long> interruptedMessageIds =
        new HashSet<long>();

    private readonly List<Coroutine> voiceCoroutines =
        new List<Coroutine>();
    private readonly Queue<VoiceSegment> voiceQueue =
        new Queue<VoiceSegment>();
    private AudioClip downloadedClip;
    private bool reportedSpeaking;
    private long playingVersion;
    private long completedVersion;
    private long reportedCompletedVersion = -1;
    private long reportedPlayingVersion = -1;
    private float reportedPlayedSeconds = -1f;
    private float lastProgressReportAt = -1f;
    private int pendingDownloads;
    private bool sendingPlaybackStatus;


    private string StateUrl =>
        $"{serverBaseUrl}/unity/state";

    private string PlaybackStatusUrl =>
        $"{serverBaseUrl}/voice/playback";

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
            ReportPlaybackStatus(audioSource != null && audioSource.isPlaying || pendingDownloads > 0);
            return;
        }

        if (downloadedClip != null)
        {
            completedVersion = System.Math.Max(completedVersion, playingVersion);
            Destroy(downloadedClip);
            downloadedClip = null;
        }

        if (voiceQueue.Count == 0)
        {
            if (subtitleText != null)
            {
                subtitleText.text = string.Empty;
            }

            if (lipSyncController != null)
            {
                lipSyncController.StopSpeaking();
            }

            ReportPlaybackStatus(pendingDownloads > 0);

            return;
        }

        VoiceSegment segment = voiceQueue.Dequeue();
        downloadedClip = segment.clip;
        playingVersion = segment.version;

        if (subtitleText != null)
        {
            subtitleText.text = segment.text;
        }

        if (emotionController != null)
        {
            emotionController.SetEmotion(segment.emotion, segment.intensity);
        }

        audioSource.clip = downloadedClip;
        audioSource.Play();
        ReportPlaybackStatus(true);
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
        // Emotion
        // ------------------------

        if (emotionController != null &&
            string.IsNullOrWhiteSpace(state.text) && state.segmentCount == 0)
            emotionController.SetEmotion("neutral", 0.5f);


        // ------------------------
        // TTS Voice
        // ------------------------

        bool isNewMessage =
            state.messageId != activeMessageId;

        if (isNewMessage)
        {
            interruptedMessageIds.RemoveWhere(
                id => id < state.messageId - 16
            );

            bool isReset =
                string.IsNullOrWhiteSpace(state.text) &&
                state.segmentCount == 0;

            // Finish already-started speech, including idle segments, in queue order.
            // Only an explicit conversation reset interrupts audio.
            if (isReset)
            {
                if (activeMessageId >= 0)
                {
                    interruptedMessageIds.Add(activeMessageId);
                }

                StopVoiceDownloads();
                StopVoice();
            }

            activeMessageId = state.messageId;

            if (isReset && subtitleText != null)
            {
                subtitleText.text = string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(state.text) || !state.hasAudio)
        {
            return;
        }

        pendingDownloads++;
        ReportPlaybackStatus(true);
        Coroutine coroutine = StartCoroutine(
            DownloadVoiceSegment(
                state.version,
                state.messageId,
                string.IsNullOrEmpty(state.segmentDisplayText)
                    ? state.segmentText : state.segmentDisplayText,
                state.emotion,
                state.intensity
            )
        );
        voiceCoroutines.Add(coroutine);
    }


    private IEnumerator DownloadVoiceSegment(
        long version,
        long messageId,
        string displayText,
        string emotion,
        float intensity)
    {
        if (audioSource == null)
        {
            pendingDownloads--;
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

        pendingDownloads--;

        if (interruptedMessageIds.Contains(messageId))
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

        voiceQueue.Enqueue(
            new VoiceSegment(
                clip,
                displayText,
                version,
                emotion,
                intensity
            )
        );
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
            VoiceSegment queuedSegment = voiceQueue.Dequeue();
            if (queuedSegment.clip != null)
            {
                Destroy(queuedSegment.clip);
            }
        }

        if (lipSyncController != null)
        {
            lipSyncController.StopSpeaking();
        }

        ReportPlaybackStatus(false);
    }

    private void ReportPlaybackStatus(bool speaking)
    {
        if (!isActiveAndEnabled || sendingPlaybackStatus)
        {
            return;
        }

        bool playing = speaking && audioSource != null && audioSource.isPlaying;
        long activeVersion = playing ? playingVersion : 0;
        float playedSeconds = playing ? audioSource.time : 0f;
        float clipSeconds = playing && audioSource.clip != null ? audioSource.clip.length : 0f;
        bool progressDue = activeVersion > 0 &&
            (activeVersion != reportedPlayingVersion ||
             playedSeconds - reportedPlayedSeconds >= 0.35f) &&
            Time.unscaledTime - lastProgressReportAt >= 0.25f;
        if (reportedSpeaking == speaking &&
            reportedCompletedVersion == completedVersion &&
            reportedPlayingVersion == activeVersion && !progressDue)
            return;

        sendingPlaybackStatus = true;
        StartCoroutine(SendPlaybackStatus(
            speaking, completedVersion, activeVersion, playedSeconds, clipSeconds));
    }

    private IEnumerator SendPlaybackStatus(
        bool speaking, long finishedVersion, long activeVersion,
        float playedSeconds, float clipSeconds)
    {
        string json = $"{{\"speaking\":{speaking.ToString().ToLowerInvariant()}," +
            $"\"completedVersion\":{finishedVersion},\"playingVersion\":{activeVersion}," +
            $"\"playedSeconds\":{playedSeconds.ToString(CultureInfo.InvariantCulture)}," +
            $"\"clipSeconds\":{clipSeconds.ToString(CultureInfo.InvariantCulture)}}}";

        using UnityWebRequest request = new UnityWebRequest(
            PlaybackStatusUrl,
            UnityWebRequest.kHttpVerbPOST);
        request.timeout = 3;
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();
        sendingPlaybackStatus = false;

        if (request.result == UnityWebRequest.Result.Success)
        {
            reportedSpeaking = speaking;
            reportedCompletedVersion = finishedVersion;
            reportedPlayingVersion = activeVersion;
            reportedPlayedSeconds = playedSeconds;
            lastProgressReportAt = Time.unscaledTime;
        }
        else
        {
            Debug.LogWarning(
                $"Playback status update failed: {request.responseCode} {request.error}"
            );
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
        pendingDownloads = 0;
    }

    private void OnDestroy()
    {
        StopVoiceDownloads();
        StopVoice();
    }


    private sealed class VoiceSegment
    {
        public readonly AudioClip clip;
        public readonly string text;
        public readonly long version;
        public readonly string emotion;
        public readonly float intensity;

        public VoiceSegment(
            AudioClip clip,
            string text,
            long version,
            string emotion,
            float intensity)
        {
            this.clip = clip;
            this.text = text;
            this.version = version;
            this.emotion = emotion;
            this.intensity = intensity;
        }
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
        public string segmentText;
        public string segmentDisplayText;
        public string segmentSpeechText;
        public bool isIdle;
    }
}
