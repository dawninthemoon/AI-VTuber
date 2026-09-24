using AIVTuber.Web.Services;
using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine($"PASS: {message}");
}

var state = new CharacterStateService();
var playback = new PlaybackStatusService();
long messageId = state.BeginResponse("가나다라 마바사아", segmentCount: 2);
Check(state.PublishAudioSegment(messageId, "가나다라 마바사아", "neutral", 0.5f,
    0, 2, "가나다라", [1], out long firstVersion), "First clip publishes");
Check(state.PublishAudioSegment(messageId, "가나다라 마바사아", "neutral", 0.5f,
    1, 2, "마바사아", [1], out long secondVersion), "Second clip publishes");

playback.Set(true, playingVersion: firstVersion, playedSeconds: 1, clipSeconds: 2);
Check(state.GetHeardText(messageId, playback.Get()) == "가나",
    "Interrupted clip stores only the estimated spoken prefix");

playback.Set(true, completedVersion: firstVersion, playingVersion: secondVersion,
    playedSeconds: 1, clipSeconds: 2);
Check(state.GetHeardText(messageId, playback.Get()) == "가나다라 마바",
    "Completed and active clips combine in playback order");

playback.Set(false, completedVersion: secondVersion);
Check(state.GetHeardText(messageId, playback.Get()) == "가나다라 마바사아",
    "Completed playback keeps the full response");

state.Reset();
Check(state.GetHeardText(messageId, playback.Get()) == "",
    "Reset removes stale playback text");

const string streamedJson = "{\"emotion\":\"happy\",\"intensity\":0.7,\"text\":\"안녕, \\uD83D\\uDE00!\"}";
var parser = new StreamingCharacterResponseParser();
StringBuilder decodedText = new();
foreach (string fragment in new[]
{
    "{\"emo", "tion\":\"happy\",\"intens", "ity\":0.7,\"text\":\"안",
    "녕, \\uD83", "D\\uDE00!\"}"
}) decodedText.Append(parser.Feed(fragment));
Check(parser.RawJson == streamedJson && decodedText.ToString() == "안녕, 😀!" &&
      parser.Emotion == "happy" && Math.Abs(parser.Intensity - 0.7f) < 0.01f,
    "Structured JSON can be decoded across arbitrary stream boundaries");

var segmenter = new StreamingTtsSegmenter();
var firstSegments = segmenter.Push("오늘은 반짝이는 노래를 듣고, 다음 생각을 이어갈게.");
Check(firstSegments.Count >= 1 && firstSegments[0].EndsWith(','),
    "The first TTS clause can start at a comma");

string? previousKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
try
{
    Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
    var handler = new StreamingHandler(streamedJson);
    var openAi = new OpenAiChatService(new HttpClient(handler),
        Options.Create(new OpenAiOptions { Model = "test-model" }));
    StringBuilder streamed = new();
    string response = await openAi.ChatAsync(
        [new ChatMessage("user", "안녕")],
        new GenerationProfile(ChatMode.Chat, false, 100, 0.7, 4),
        onTextDelta: (text, emotion, intensity, _) =>
        {
            Check(emotion == "happy" && Math.Abs(intensity - 0.7f) < 0.01f,
                "SSE exposes emotion before speech text");
            streamed.Append(text);
            return Task.CompletedTask;
        });
    Check(response == streamedJson && streamed.ToString() == "안녕, 😀!" && handler.StreamRequested,
        "Responses SSE is reconstructed and requested with stream=true");
}
finally
{
    Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousKey);
}

sealed class StreamingHandler(string output) : HttpMessageHandler
{
    public bool StreamRequested { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = await request.Content!.ReadAsStringAsync(cancellationToken);
        using JsonDocument parsed = JsonDocument.Parse(body);
        StreamRequested = parsed.RootElement.GetProperty("stream").GetBoolean();
        string[] pieces = [output[..21], output[21..40], output[40..]];
        StringBuilder sse = new();
        foreach (string piece in pieces)
        {
            sse.Append("data: ").Append(JsonSerializer.Serialize(new
            {
                type = "response.output_text.delta", delta = piece
            })).Append("\n\n");
        }
        sse.Append("data: {\"type\":\"response.completed\"}\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse.ToString(), Encoding.UTF8, "text/event-stream")
        };
    }
}
