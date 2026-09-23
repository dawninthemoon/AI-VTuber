using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;
using Grpc.Core;
using Grpc.Net.Client;
using AIVTuber.Web.YouTubeStreaming;

namespace AIVTuber.Web.Services;

public sealed class YouTubeLiveChatService : BackgroundService
{
    private readonly HttpClient _httpClient;
    private readonly ChatResponseService _responseService;
    private readonly YouTubeOptions _options;
    private readonly ILogger<YouTubeLiveChatService> _logger;
    private readonly Channel<YouTubeChatMessage> _messages;

    public YouTubeLiveChatService(
        HttpClient httpClient,
        ChatResponseService responseService,
        IOptions<YouTubeOptions> options,
        ILogger<YouTubeLiveChatService> logger)
    {
        _httpClient = httpClient;
        _responseService = responseService;
        _options = options.Value;
        _logger = logger;
        _messages = Channel.CreateBounded<YouTubeChatMessage>(new BoundedChannelOptions(
            Math.Max(1, _options.QueueCapacity))
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("YouTube live chat collection is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.VideoId))
        {
            _logger.LogError(
                "YouTube chat is enabled, but YouTube:ApiKey or YouTube:VideoId is empty.");
            return;
        }

        _logger.LogInformation("YouTube live chat collector starting for video {VideoId}.", _options.VideoId);

        Task producer = StreamAsync(stoppingToken);
        Task consumer = ConsumeAsync(stoppingToken);
        await Task.WhenAll(producer, consumer);
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
        int retrySeconds = 5;
        var session = Stopwatch.StartNew();
        var reconnect = new YouTubeReconnectPolicy();
        int streamAttempts = 0;
        int videoRequests = 0;
        int discoveryAttempts = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var connection = Stopwatch.StartNew();
                int batches = 0;
                int receivedMessages = 0;
                bool streamStarted = false;
                bool successful = false;
                try
                {
                    if (liveChatId == null)
                    {
                        if (++discoveryAttempts > 12)
                        {
                            _logger.LogWarning("YouTube discovery stopped after 12 attempts without an active chat. Restart collection when the broadcast is live.");
                            return;
                        }
                        videoRequests++;
                        liveChatId = await FindLiveChatIdAsync(cancellationToken);
                    }
                    if (liveChatId == null)
                    {
                        _logger.LogWarning(
                            "Video {VideoId} is not live yet or live chat is unavailable. Retrying.",
                            _options.VideoId);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(60 * Math.Pow(2, discoveryAttempts - 1), 300)), cancellationToken);
                        continue;
                    }

                    if (!reconnect.TryStart(session.Elapsed))
                    {
                        _logger.LogError("YouTube collection stopped to protect quota: 12 stream attempts within one hour. Check connection logs before restarting; automatic reconnect is disabled for this session.");
                        return;
                    }
                    streamStarted = true;
                    streamAttempts++;
                    connection.Restart();

                    var request = new LiveChatMessageListRequest { LiveChatId = liveChatId };
                    request.Part.Add(new[] { "id", "snippet", "authorDetails" });
                    if (!string.IsNullOrEmpty(pageToken))
                    {
                        request.PageToken = pageToken;
                    }
                    using var call = client.StreamList(request,
                        new Metadata { { "x-goog-api-key", _options.ApiKey } },
                        cancellationToken: cancellationToken);
                    _logger.LogInformation("YouTube streamList attempt {Attempt}; video lookups {Lookups}; resume: {Resume}.", streamAttempts, videoRequests, pageToken != null);
                    await foreach (var batch in call.ResponseStream.ReadAllAsync(cancellationToken))
                    {
                        batches++;
                        receivedMessages += batch.Items.Count;
                        foreach (var item in batch.Items)
                        {
                            if (item.Snippet?.Type == 4)
                            {
                                _logger.LogInformation("YouTube live chat ended.");
                                return;
                            }
                            // Initial history may span multiple batches. Filter by publication
                            // time rather than discarding the first batch on every reconnect.
                            if (item.Snippet?.Type != 1 ||
                                !DateTimeOffset.TryParse(item.Snippet.PublishedAt, out var publishedAt) ||
                                publishedAt < startedAt || string.IsNullOrEmpty(item.Id) || !seen.Add(item.Id))
                            {
                                continue;
                            }
                            seenOrder.Enqueue(item.Id);
                            if (seenOrder.Count > 10000)
                            {
                                seen.Remove(seenOrder.Dequeue());
                            }
                            TryQueue(new LiveChatItem(item.Id,
                                new LiveChatSnippet("textMessageEvent", item.Snippet.DisplayMessage),
                                new LiveChatAuthor(item.AuthorDetails?.DisplayName, item.AuthorDetails?.ChannelId)));
                        }
                        if (!string.IsNullOrEmpty(batch.NextPageToken))
                        {
                            pageToken = batch.NextPageToken;
                        }
                        if (!string.IsNullOrEmpty(batch.OfflineAt))
                        {
                            _logger.LogInformation("YouTube live chat ended at {OfflineAt}.", batch.OfflineAt);
                            return;
                        }
                    }
                    successful = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (RpcException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (RpcException ex) when (ex.StatusCode is StatusCode.PermissionDenied or
                    StatusCode.Unauthenticated or StatusCode.FailedPrecondition or StatusCode.NotFound or
                    StatusCode.InvalidArgument)
                {
                    _logger.LogError("YouTube streamList stopped: {Code}. Check API permissions, video/chat status or saved cursor, then restart the server.", ex.StatusCode);
                    break;
                }
                catch (YouTubeApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.BadRequest)
                {
                    _logger.LogError("YouTube collection stopped: {Reason}. Resolve quota/key restrictions and restart the server.", ex.Reason);
                    break;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.ResourceExhausted)
                {
                    _logger.LogError("YouTube streamList stopped: ResourceExhausted (quota/rate limit). No automatic retries. Check Google Cloud quota before restarting.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("YouTube connection interrupted: {ErrorType}, RPC status: {Status}.", ex.GetType().Name, (ex as RpcException)?.StatusCode);
                }
                finally
                {
                    if (streamStarted)
                    {
                        _logger.LogInformation("YouTube stream attempt {Attempt} ended: {Seconds:F1}s, {Batches} batches, {Messages} messages, clean EOF: {Clean}. Session video lookups: {Lookups}. Counts are requests, not quota units.",
                            streamAttempts, connection.Elapsed.TotalSeconds, batches, receivedMessages, successful, videoRequests);
                    }
                }
                retrySeconds = (int)reconnect.AfterClose(connection.Elapsed, successful).TotalSeconds;
                _logger.LogWarning("YouTube reconnect in {Seconds}s; short connections increase backoff even when they deliver messages.", retrySeconds);
                await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            _messages.Writer.TryComplete();
        }
    }

    private async Task<string?> FindLiveChatIdAsync(CancellationToken cancellationToken)
    {
        string url =
            "https://www.googleapis.com/youtube/v3/videos" +
            $"?part=liveStreamingDetails&id={Uri.EscapeDataString(_options.VideoId)}";

        VideoListResponse? response = await GetAsync<VideoListResponse>(url, cancellationToken);
        return response?.Items.FirstOrDefault()?.LiveStreamingDetails?.ActiveLiveChatId;
    }

    private void TryQueue(LiveChatItem item)
    {
        string text = item.Snippet?.DisplayMessage?.Trim() ?? "";
        string authorChannelId = item.AuthorDetails?.ChannelId ?? "";
        string prefix = _options.TriggerPrefix.Trim();

        if (item.Snippet?.Type != "textMessageEvent" ||
            string.IsNullOrWhiteSpace(text) ||
            text.Length > Math.Max(1, _options.MaxMessageLength) ||
            (!string.IsNullOrWhiteSpace(_options.IgnoreChannelId) &&
             string.Equals(authorChannelId, _options.IgnoreChannelId, StringComparison.Ordinal)) ||
            (!string.IsNullOrEmpty(prefix) && !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!string.IsNullOrEmpty(prefix))
        {
            text = text[prefix.Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var message = new YouTubeChatMessage(
            item.Id ?? "", item.AuthorDetails?.DisplayName ?? "시청자", authorChannelId, text);

        if (!_messages.Writer.TryWrite(message))
        {
            _logger.LogWarning("YouTube chat response queue is full; dropped message {MessageId}.", item.Id);
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (YouTubeChatMessage message in _messages.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                _logger.LogInformation(
                    "Answering YouTube chat from {Author}: {Text}", message.AuthorName, message.Text);
                string prompt = $"유튜브 라이브 시청자 '{message.AuthorName}'의 채팅: {message.Text}";
                await _responseService.RespondAsync(prompt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to answer YouTube chat message {MessageId}.", message.Id);
            }
        }
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Keep credentials out of the URL so standard HttpClient request logs do not expose them.
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", _options.ApiKey);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string reason = response.StatusCode.ToString();
            try
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                reason = error.RootElement.GetProperty("error").GetProperty("errors")[0]
                    .GetProperty("reason").GetString() ?? reason;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException) { }
            throw new YouTubeApiException(response.StatusCode, reason);
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private sealed record VideoListResponse(
        [property: JsonPropertyName("items")] List<VideoItem> Items);

    private sealed record VideoItem(
        [property: JsonPropertyName("liveStreamingDetails")] LiveStreamingDetails? LiveStreamingDetails);

    private sealed record LiveStreamingDetails(
        [property: JsonPropertyName("activeLiveChatId")] string? ActiveLiveChatId);

    private sealed class YouTubeApiException(System.Net.HttpStatusCode statusCode, string reason)
        : HttpRequestException($"YouTube API: {reason}", null, statusCode)
    {
        public string Reason { get; } = reason;
    }

    private sealed record LiveChatItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("snippet")] LiveChatSnippet? Snippet,
        [property: JsonPropertyName("authorDetails")] LiveChatAuthor? AuthorDetails);

    private sealed record LiveChatSnippet(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("displayMessage")] string? DisplayMessage);

    private sealed record LiveChatAuthor(
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("channelId")] string? ChannelId);
}
