using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;

namespace AIVTuber.Web.Services;

public sealed class ChatService
{
    private readonly OllamaService _ollamaService;
    private readonly ChatClassifier _classifier;
    private readonly OllamaOptions _options;
    private readonly ILogger<ChatService> _logger;
    private readonly string _systemPrompt;

    private readonly List<ChatMessage> _history = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ChatService(
        OllamaService ollamaService,
        ChatClassifier classifier,
        IOptions<OllamaOptions> options,
        IWebHostEnvironment environment,
        ILogger<ChatService> logger)
    {
        _ollamaService = ollamaService;
        _classifier = classifier;
        _options = options.Value;
        _logger = logger;

        var promptPath = Path.Combine(
            environment.ContentRootPath,
            "Prompts",
            "system.txt");

        _systemPrompt = File.ReadAllText(promptPath).Trim();

        Reset();
    }

    public async Task<string> SendAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            _history.Add(new ChatMessage("user", userMessage));

            var profile = _classifier.Classify(userMessage);

            _logger.LogInformation(
                "Chat mode: {Mode}, Think: {Think}, NumPredict: {NumPredict}, Temperature: {Temperature}",
                profile.Mode,
                profile.Think,
                profile.NumPredict,
                profile.Temperature);

            var context = BuildContext();

            var aiText = await _ollamaService.ChatAsync(
                context,
                profile,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(aiText))
            {
                aiText = "잠깐, 방금 머리가 멈췄어. 한 번만 다시 말해줘.";
            }

            _history.Add(new ChatMessage("assistant", aiText));
            TrimHistory();

            return aiText;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Reset()
    {
        _history.Clear();
        _history.Add(new ChatMessage("system", _systemPrompt));
    }

    private IReadOnlyList<ChatMessage> BuildContext()
    {
        if (_history.Count <= _options.MaxHistoryMessages + 1)
        {
            return _history.ToList();
        }

        var recent = _history
            .Skip(Math.Max(1, _history.Count - _options.MaxHistoryMessages))
            .ToList();

        recent.Insert(0, _history[0]);

        return recent;
    }

    private void TrimHistory()
    {
        var maxStored = Math.Max(
            _options.MaxHistoryMessages * 2,
            _options.MaxHistoryMessages + 1);

        if (_history.Count <= maxStored)
        {
            return;
        }

        var system = _history[0];

        var recent = _history
            .Skip(_history.Count - _options.MaxHistoryMessages)
            .ToList();

        _history.Clear();
        _history.Add(system);
        _history.AddRange(recent);
    }
}
