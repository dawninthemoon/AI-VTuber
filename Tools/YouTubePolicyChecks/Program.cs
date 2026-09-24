using System.Text.Json;
using AIVTuber.Web.Services;
using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Threading.Channels;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}

var policy = new YouTubeReconnectPolicy();
for (int i = 0; i < 30; i++)
{
    Check(policy.AfterClose(TimeSpan.FromSeconds(10), true, true).TotalSeconds == 1,
        $"Productive stream {i + 1} resumes promptly without an hourly stop");
}
Check(policy.AfterClose(TimeSpan.Zero, true, true).TotalSeconds == 3,
    "Subsecond streams cannot reconnect in a tight loop");
foreach (int seconds in new[] { 5, 10, 20, 40, 80, 160, 300, 300 })
{
    Check(policy.AfterClose(TimeSpan.FromSeconds(10), true, false).TotalSeconds == seconds,
        $"No-progress EOF backs off to {seconds}s");
}
Check(policy.AfterClose(TimeSpan.FromSeconds(10), true, true).TotalSeconds == 1, "Progress resets backoff");
Check(policy.AfterClose(TimeSpan.FromSeconds(10), false, true).TotalSeconds == 5, "Error still backs off with progress");
Check(policy.AfterClose(TimeSpan.Zero, false, false, true).TotalSeconds >= 30, "Temporary rate limit waits at least 30s");
var summer = DateTimeOffset.Parse("2026-09-24T06:50:00Z");
Check(YouTubeReconnectPolicy.UntilQuotaReset(summer) == TimeSpan.FromMinutes(11), "Quota reset in daylight saving time");
var winter = DateTimeOffset.Parse("2026-12-24T07:50:00Z");
Check(YouTubeReconnectPolicy.UntilQuotaReset(winter) == TimeSpan.FromMinutes(11), "Quota reset in standard time");

var status = new YouTubeChatStatus();
status.Connection("quota_wait", "quotaExceeded", TimeSpan.FromMinutes(10));
status.Request(true);
status.Request(false);
for (int i = 0; i < 501; i++)
{
    status.Received(i.ToString(), "viewer", "<script>untrusted chat</script>");
    status.Message(i.ToString(), i == 500 ? "not_selected" : "queued", i == 500 ? "queue_full" : null);
}
using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot()));
var root = snapshot.RootElement;
Check(root.GetProperty("recent").GetArrayLength() == 500, "History is bounded independently of pending work");
Check(root.GetProperty("received").GetInt64() == 501, "Received count survives history eviction");
Check(root.GetProperty("recent")[0].GetProperty("Status").GetString() == "not_selected", "Overflow has explicit status");
Check(root.GetProperty("recent")[0].GetProperty("Reason").GetString() == "queue_full", "Overflow reason is retained");
Check(root.GetProperty("retryAt").ValueKind == JsonValueKind.String, "Quota wait exposes next retry time");
Check(root.GetProperty("streamAttempts").GetInt64() == 1 && root.GetProperty("videoLookups").GetInt64() == 1, "Request counters separate discovery and streaming");
status.Message("499", "response_ready");
using var updated = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot()));
Check(updated.RootElement.GetProperty("recent")[1].GetProperty("Status").GetString() == "response_ready", "Consumer updates observation history");

var buffered = Channel.CreateUnbounded<YouTubeChatMessage>();
var arrived = DateTimeOffset.UtcNow.AddSeconds(-10);
for (int i = 0; i < 4; i++)
    buffered.Writer.TryWrite(new YouTubeChatMessage(i.ToString(), "viewer", "channel", "chat", arrived.AddMilliseconds(i * 700)));
buffered.Writer.TryWrite(new YouTubeChatMessage("later", "viewer", "channel", "next topic", arrived.AddSeconds(8)));
buffered.Writer.TryComplete();
var batcher = new YouTubeChatBatcher(buffered.Reader, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), 12);
var firstBatch = await batcher.ReadAsync(CancellationToken.None);
var secondBatch = await batcher.ReadAsync(CancellationToken.None);
Check(firstBatch?.Count == 4 && secondBatch?.Count == 1 && secondBatch[0].Id == "later",
    "Buffered messages form one burst and preserve the next burst");
Check(await batcher.ReadAsync(CancellationToken.None) == null, "Completed chat queue drains after the last burst");

var capped = Channel.CreateUnbounded<YouTubeChatMessage>();
for (int i = 0; i < 4; i++)
    capped.Writer.TryWrite(new YouTubeChatMessage(i.ToString(), "viewer", "channel", "chat", arrived.AddMilliseconds(i * 100)));
capped.Writer.TryComplete();
var cappedBatcher = new YouTubeChatBatcher(capped.Reader, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), 3);
Check((await cappedBatcher.ReadAsync(CancellationToken.None))?.Count == 3 &&
      (await cappedBatcher.ReadAsync(CancellationToken.None))?.Count == 1,
    "Busy chat has a bounded number of messages per response");

var arriving = Channel.CreateUnbounded<YouTubeChatMessage>();
var arrivingBatcher = new YouTubeChatBatcher(arriving.Reader, TimeSpan.FromMilliseconds(300),
    TimeSpan.FromMilliseconds(600), 12);
arriving.Writer.TryWrite(new YouTubeChatMessage("a", "viewer", "channel", "ㅋㅋㅋ", DateTimeOffset.UtcNow));
var readArriving = arrivingBatcher.ReadAsync(CancellationToken.None);
await Task.Delay(40);
arriving.Writer.TryWrite(new YouTubeChatMessage("b", "viewer", "channel", "맞짱뜰래?", DateTimeOffset.UtcNow));
arriving.Writer.TryComplete();
Check((await readArriving)?.Count == 2, "Chat arriving during the quiet window joins the response");

var stopping = Channel.CreateUnbounded<YouTubeChatMessage>();
stopping.Writer.TryWrite(new YouTubeChatMessage("shutdown", "viewer", "channel", "chat", DateTimeOffset.UtcNow));
var stoppingBatcher = new YouTubeChatBatcher(stopping.Reader, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), 12);
using (var stopRead = new CancellationTokenSource(TimeSpan.FromMilliseconds(40)))
{
    try { await stoppingBatcher.ReadAsync(stopRead.Token); }
    catch (OperationCanceledException) when (stopRead.IsCancellationRequested) { }
}
Check(stoppingBatcher.InFlight?.Count == 1 && stoppingBatcher.InFlight[0].Id == "shutdown",
    "Shutdown retains an unfinished burst for cancellation status");

var queueStatus = new YouTubeChatStatus();
using var queueHttp = new HttpClient();
using var queueService = new YouTubeLiveChatService(queueHttp, null!, Options.Create(new YouTubeOptions { QueueCapacity = 100 }),
    NullLogger<YouTubeLiveChatService>.Instance, queueStatus, new SpeechTurnCoordinator());
for (int i = 0; i < 101; i++)
{
    var message = new YouTubeChatMessage(i.ToString(), "viewer", "channel", "안녕");
    queueStatus.Received(message.Id, message.AuthorName, message.Text);
    queueService.TryQueue(message);
}
using var queueSnapshot = JsonDocument.Parse(JsonSerializer.Serialize(queueStatus.Snapshot()));
var entries = queueSnapshot.RootElement.GetProperty("recent").EnumerateArray().ToArray();
Check(entries.Count(e => e.GetProperty("Status").GetString() == "queued") == 100, "Real service retains 100 pending messages without a consumer");
Check(entries[0].GetProperty("Status").GetString() == "not_selected", "Real service detects overflow instead of DropWrite silently succeeding");

var quotaStatus = new YouTubeChatStatus();
var handler = new QuotaHandler();
using var quotaHttp = new HttpClient(handler);
using var quotaService = new YouTubeLiveChatService(quotaHttp, null!, Options.Create(new YouTubeOptions
    { Enabled = true, ApiKey = "fake-test-key", VideoId = "test" }), NullLogger<YouTubeLiveChatService>.Instance, quotaStatus,
    new SpeechTurnCoordinator());
await quotaService.StartAsync(CancellationToken.None);
for (int i = 0; i < 300; i++)
{
    using var current = JsonDocument.Parse(JsonSerializer.Serialize(quotaStatus.Snapshot()));
    if (current.RootElement.GetProperty("connection").GetString() == "quota_wait") break;
    await Task.Delay(10);
}
using var quotaSnapshot = JsonDocument.Parse(JsonSerializer.Serialize(quotaStatus.Snapshot()));
Check(quotaSnapshot.RootElement.GetProperty("connection").GetString() == "quota_wait", "Real 403 quota response enters reset wait");
Check(handler.Requests == 1, "No repeated requests during quota wait");
using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
await quotaService.StopAsync(stopTimeout.Token);
Check(quotaService.ExecuteTask!.IsCompleted, "Quota wait and consumer cancel promptly on shutdown");

sealed class QuotaHandler : HttpMessageHandler
{
    public int Requests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Requests);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":{\"errors\":[{\"reason\":\"quotaExceeded\"}]}}")
        });
    }
}
