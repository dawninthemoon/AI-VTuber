using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;

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

        Task producer = PollAsync(stoppingToken);
        Task consumer = ConsumeAsync(stoppingToken);
        await Task.WhenAll(producer, consumer);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                string? liveChatId = await FindLiveChatIdAsync(cancellationToken);
                if (liveChatId == null)
                {
                    _logger.LogWarning(
                        "Video {VideoId} is not live yet or live chat is unavailable. Retrying.",
                        _options.VideoId);
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                    continue;
                }

                await PollChatAsync(liveChatId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "YouTube live chat polling failed. Retrying.");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }

        _messages.Writer.TryComplete();
    }

    private async Task<string?> FindLiveChatIdAsync(CancellationToken cancellationToken)
    {
        string url =
            "https://www.googleapis.com/youtube/v3/videos" +
            $"?part=liveStreamingDetails&id={Uri.EscapeDataString(_options.VideoId)}";

        VideoListResponse? response = await GetAsync<VideoListResponse>(url, cancellationToken);
        return response?.Items.FirstOrDefault()?.LiveStreamingDetails?.ActiveLiveChatId;
    }

    private async Task PollChatAsync(string liveChatId, CancellationToken cancellationToken)
    {
        string? pageToken = null;
        bool initialPage = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            string url =
                "https://www.googleapis.com/youtube/v3/liveChat/messages" +
                $"?liveChatId={Uri.EscapeDataString(liveChatId)}" +
                "&part=id,snippet,authorDetails&maxResults=200";

            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            LiveChatMessageListResponse? response =
                await GetAsync<LiveChatMessageListResponse>(url, cancellationToken);

            if (response == null)
            {
                throw new InvalidOperationException("YouTube returned an empty live chat response.");
            }

            // The first page can contain old chat history. Establish a cursor without
            // making the character answer messages sent before this process started.
            if (!initialPage)
            {
                foreach (LiveChatItem item in response.Items)
                {
                    TryQueue(item);
                }
            }
            else
            {
                initialPage = false;
                _logger.LogInformation(
                    "YouTube live chat connected. Skipped {Count} existing messages.",
                    response.Items.Count);
            }

            pageToken = response.NextPageToken;

            if (response.OfflineAt != null)
            {
                _logger.LogInformation("YouTube live chat ended at {OfflineAt}.", response.OfflineAt);
                return;
            }

            int delayMilliseconds = Math.Clamp(response.PollingIntervalMillis, 1000, 60_000);
            await Task.Delay(delayMilliseconds, cancellationToken);
        }
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
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private sealed record VideoListResponse(
        [property: JsonPropertyName("items")] List<VideoItem> Items);

    private sealed record VideoItem(
        [property: JsonPropertyName("liveStreamingDetails")] LiveStreamingDetails? LiveStreamingDetails);

    private sealed record LiveStreamingDetails(
        [property: JsonPropertyName("activeLiveChatId")] string? ActiveLiveChatId);

    private sealed record LiveChatMessageListResponse(
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
        [property: JsonPropertyName("pollingIntervalMillis")] int PollingIntervalMillis,
        [property: JsonPropertyName("offlineAt")] DateTimeOffset? OfflineAt,
        [property: JsonPropertyName("items")] List<LiveChatItem> Items);

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
