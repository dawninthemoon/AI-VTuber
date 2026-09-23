using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using AIVTuber.Web.Models;
using AIVTuber.Web.YouTubeStreaming;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;

namespace AIVTuber.Web.Services;

public sealed class YouTubeLiveChatService : BackgroundService
{
    private readonly HttpClient _httpClient;
    private readonly ChatResponseService _responseService;
    private readonly YouTubeOptions _options;
    private readonly ILogger<YouTubeLiveChatService> _logger;
    private readonly YouTubeChatStatus _status;
    private readonly Channel<YouTubeChatMessage> _messages;

    public YouTubeLiveChatService(HttpClient httpClient, ChatResponseService responseService,
        IOptions<YouTubeOptions> options, ILogger<YouTubeLiveChatService> logger, YouTubeChatStatus status)
    {
        _httpClient = httpClient;
        _responseService = responseService;
        _options = options.Value;
        _logger = logger;
        _status = status;
        _messages = Channel.CreateBounded<YouTubeChatMessage>(new BoundedChannelOptions(
            Math.Clamp(_options.QueueCapacity, 1, 10000))
        {
            SingleReader = true,
            SingleWriter = true,
            // TryWrite returns false on overflow. Never block the stream reader on TTS.
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _status.Connection("disabled");
            return;
        }
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.VideoId))
        {
            _status.Connection("stopped", "missing_api_key_or_video_id");
            _logger.LogError("YouTube API key or video ID is missing.");
            return;
        }
        await Task.WhenAll(StreamAsync(stoppingToken), ConsumeAsync(stoppingToken));
    }

    private async Task StreamAsync(CancellationToken cancellationToken)
    {
        using var channel = GrpcChannel.ForAddress("https://youtube.googleapis.com");
        var client = new V3DataLiveChatMessageService.V3DataLiveChatMessageServiceClient(channel);
        string? liveChatId = null;
        string? pageToken = null;
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seenOrder = new Queue<string>();
        var reconnect = new YouTubeReconnectPolicy();
        int streamAttempts = 0, videoRequests = 0, discoveryAttempts = 0;
        bool cursorReset = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var connection = Stopwatch.StartNew();
                int batches = 0, receivedMessages = 0;
                bool streamStarted = false, successful = false, progressed = false, rateLimited = false;
                TimeSpan? quotaDelay = null;
                string reason = "stream_closed_without_progress";
                try
                {
                    if (liveChatId == null)
                    {
                        discoveryAttempts = Math.Min(discoveryAttempts + 1, 4);
                        videoRequests++;
                        _status.Request(stream: false);
                        _status.Connection("discovering");
                        liveChatId = await FindLiveChatIdAsync(cancellationToken);
                    }
                    if (liveChatId == null)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Min(60 * Math.Pow(2, discoveryAttempts - 1), 300));
                        _status.Connection("waiting_for_live", "no_active_chat", delay);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    streamStarted = true;
                    streamAttempts++;
                    _status.Request(stream: true);
                    _status.Connection("connecting");
                    connection.Restart();
                    var request = new LiveChatMessageListRequest { LiveChatId = liveChatId };
                    request.Part.Add(new[] { "id", "snippet", "authorDetails" });
                    if (!string.IsNullOrEmpty(pageToken)) request.PageToken = pageToken;
                    using var call = client.StreamList(request,
                        new Metadata { { "x-goog-api-key", _options.ApiKey } }, cancellationToken: cancellationToken);
                    _logger.LogInformation("YouTube streamList attempt {Attempt}; video lookups {Lookups}; resume: {Resume}.",
                        streamAttempts, videoRequests, pageToken != null);
                    await foreach (var batch in call.ResponseStream.ReadAllAsync(cancellationToken))
                    {
                        _status.Connection("connected");
                        batches++;
                        receivedMessages += batch.Items.Count;
                        foreach (var item in batch.Items)
                        {
                            if (item.Snippet?.Type == 4)
                            {
                                _status.Connection("ended");
                                return;
                            }
                            if (string.IsNullOrEmpty(item.Id) || !seen.Add(item.Id)) continue;
                            seenOrder.Enqueue(item.Id);
                            if (seenOrder.Count > 10000) seen.Remove(seenOrder.Dequeue());
                            _status.Received(item.Id, item.AuthorDetails?.DisplayName ?? "시청자",
                                item.Snippet?.DisplayMessage ?? "");
                            // Initial history can span several batches. Keep the same cutoff on reconnect.
                            string? skip = item.Snippet?.Type != 1 ? "not_text_message" :
                                !DateTimeOffset.TryParse(item.Snippet.PublishedAt, out var publishedAt) ? "invalid_timestamp" :
                                publishedAt < startedAt ? "before_session" : null;
                            if (skip != null)
                            {
                                _status.Message(item.Id, "filtered", skip);
                                continue;
                            }
                            TryQueue(new YouTubeChatMessage(item.Id,
                                item.AuthorDetails?.DisplayName ?? "시청자",
                                item.AuthorDetails?.ChannelId ?? "", item.Snippet!.DisplayMessage));
                        }
                        if (!string.IsNullOrEmpty(batch.NextPageToken))
                        {
                            progressed |= !string.Equals(pageToken, batch.NextPageToken, StringComparison.Ordinal);
                            pageToken = batch.NextPageToken;
                        }
                        if (!string.IsNullOrEmpty(batch.OfflineAt))
                        {
                            _status.Connection("ended");
                            return;
                        }
                    }
                    successful = true;
                    if (progressed) reason = "resume_with_cursor";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (RpcException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (YouTubeApiException ex) when (ex.Reason is "quotaExceeded" or "dailyLimitExceeded")
                {
                    quotaDelay = YouTubeReconnectPolicy.UntilQuotaReset(DateTimeOffset.UtcNow);
                    reason = ex.Reason;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument &&
                    pageToken != null && !cursorReset &&
                    ex.Status.Detail.Contains("page", StringComparison.OrdinalIgnoreCase) &&
                    ex.Status.Detail.Contains("token", StringComparison.OrdinalIgnoreCase))
                {
                    pageToken = null;
                    cursorReset = true;
                    reason = "invalid_cursor_reset_once";
                }
                catch (RpcException ex) when (ex.StatusCode is StatusCode.PermissionDenied or
                    StatusCode.Unauthenticated or StatusCode.FailedPrecondition or StatusCode.NotFound or StatusCode.InvalidArgument)
                {
                    _status.Connection("stopped", ex.StatusCode.ToString());
                    _logger.LogError("YouTube collection stopped: {Code}. Check permissions, video/chat status and parameters.", ex.StatusCode);
                    return;
                }
                catch (YouTubeApiException ex) when (ex.Reason == "rateLimitExceeded" || ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rateLimited = true;
                    reason = ex.Reason;
                }
                catch (YouTubeApiException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or
                    HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
                {
                    _status.Connection(ex.Reason == "broadcast_ended" ? "ended" : "stopped", ex.Reason);
                    _logger.LogError("YouTube collection stopped: {Reason}.", ex.Reason);
                    return;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.ResourceExhausted)
                {
                    // This status can also be a temporary rate limit; do not permanently stop.
                    rateLimited = true;
                    reason = "ResourceExhausted";
                    if (ex.Status.Detail.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase) ||
                        ex.Status.Detail.Contains("dailyLimit", StringComparison.OrdinalIgnoreCase))
                        quotaDelay = YouTubeReconnectPolicy.UntilQuotaReset(DateTimeOffset.UtcNow);
                }
                catch (Exception ex)
                {
                    reason = (ex as RpcException)?.StatusCode.ToString() ?? ex.GetType().Name;
                    _logger.LogWarning("YouTube connection interrupted: {Reason}.", reason);
                }
                finally
                {
                    if (streamStarted)
                        _logger.LogInformation("YouTube stream {Attempt}: {Seconds:F1}s, {Batches} batches, {Messages} messages, clean EOF: {Clean}, cursor advanced: {Progressed}. Video lookups: {Lookups}. These counts are not quota units.",
                            streamAttempts, connection.Elapsed.TotalSeconds, batches, receivedMessages, successful, progressed, videoRequests);
                }
                var retry = quotaDelay ?? reconnect.AfterClose(connection.Elapsed, successful, progressed, rateLimited);
                _status.Connection(quotaDelay != null ? "quota_wait" : "reconnecting", reason, retry);
                _logger.LogInformation("YouTube reconnect in {Seconds:F1}s: {Reason}.", retry.TotalSeconds, reason);
                await Task.Delay(retry, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (cancellationToken.IsCancellationRequested) _status.Connection("stopped", "server_shutdown");
            _messages.Writer.TryComplete();
        }
    }

    private async Task<string?> FindLiveChatIdAsync(CancellationToken cancellationToken)
    {
        string url = "https://www.googleapis.com/youtube/v3/videos" +
            $"?part=liveStreamingDetails&id={Uri.EscapeDataString(_options.VideoId)}";
        var response = await GetAsync<VideoListResponse>(url, cancellationToken);
        var video = response?.Items.FirstOrDefault();
        if (video == null) throw new YouTubeApiException(HttpStatusCode.BadRequest, "video_not_found");
        if (video.LiveStreamingDetails?.ActualEndTime != null)
            throw new YouTubeApiException(HttpStatusCode.BadRequest, "broadcast_ended");
        return video.LiveStreamingDetails?.ActiveLiveChatId;
    }

    internal void TryQueue(YouTubeChatMessage message)
    {
        string text = message.Text.Trim();
        string prefix = _options.TriggerPrefix.Trim();
        string? skip = string.IsNullOrWhiteSpace(text) ? "empty_message" :
            text.Length > Math.Max(1, _options.MaxMessageLength) ? "message_too_long" :
            (!string.IsNullOrWhiteSpace(_options.IgnoreChannelId) &&
             string.Equals(message.AuthorChannelId, _options.IgnoreChannelId, StringComparison.Ordinal)) ? "ignored_channel" :
            (!string.IsNullOrEmpty(prefix) && !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ? "missing_trigger" : null;
        if (skip == null && !string.IsNullOrEmpty(prefix)) text = text[prefix.Length..].Trim();
        if (skip != null || string.IsNullOrWhiteSpace(text))
        {
            _status.Message(message.Id, "filtered", skip ?? "empty_after_trigger");
            return;
        }
        // Record before publishing to the consumer to avoid overwriting a newer status.
        _status.Message(message.Id, "queued");
        if (!_messages.Writer.TryWrite(message with { Text = text }))
        {
            _status.Message(message.Id, "not_selected", "queue_full");
            _logger.LogWarning("YouTube message {MessageId} received but not selected: queue full ({Capacity}). See /youtube.html.",
                message.Id, _options.QueueCapacity);
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    _status.Message(message.Id, "processing");
                    _logger.LogInformation("Answering YouTube chat from {Author}: {Text}", message.AuthorName, message.Text);
                    var response = await _responseService.RespondAsync(
                        $"유튜브 라이브 시청자 '{message.AuthorName}'의 채팅: {message.Text}", cancellationToken);
                    // Generation completion is not proof that Unity played the audio.
                    _status.Message(message.Id, "response_ready", response.AudioVersion > 0 ? null : "no_audio");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _status.Message(message.Id, "cancelled", "server_shutdown");
                    break;
                }
                catch (Exception ex)
                {
                    _status.Message(message.Id, "failed", ex.GetType().Name);
                    _logger.LogError(ex, "Failed to answer YouTube chat message {MessageId}.", message.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
                while (_messages.Reader.TryRead(out var pending)) _status.Message(pending.Id, "cancelled", "server_shutdown");
        }
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", _options.ApiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string reason = response.StatusCode.ToString();
            try
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                reason = error.RootElement.GetProperty("error").GetProperty("errors")[0].GetProperty("reason").GetString() ?? reason;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException) { }
            throw new YouTubeApiException(response.StatusCode, reason);
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private sealed record VideoListResponse([property: JsonPropertyName("items")] List<VideoItem> Items);
    private sealed record VideoItem([property: JsonPropertyName("liveStreamingDetails")] LiveStreamingDetails? LiveStreamingDetails);
    private sealed record LiveStreamingDetails(
        [property: JsonPropertyName("activeLiveChatId")] string? ActiveLiveChatId,
        [property: JsonPropertyName("actualEndTime")] string? ActualEndTime);
    private sealed class YouTubeApiException(HttpStatusCode statusCode, string reason)
        : HttpRequestException($"YouTube API: {reason}", null, statusCode)
    {
        public string Reason { get; } = reason;
    }
}
