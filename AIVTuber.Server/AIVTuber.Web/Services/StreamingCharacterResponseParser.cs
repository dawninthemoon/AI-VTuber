using System.Text;
using System.Text.Json;

namespace AIVTuber.Web.Services;

// Reads the text field of a structured JSON response before the JSON is complete.
// The schema places emotion and intensity before text, so those values are known
// before any speech is prepared.
public sealed class StreamingCharacterResponseParser
{
    private readonly StringBuilder _raw = new();
    private int _cursor;
    private bool _insideText;
    private bool _textComplete;

    public string RawJson => _raw.ToString();
    public string Emotion { get; private set; } = "neutral";
    public float Intensity { get; private set; } = 0.5f;

    public string Feed(string delta)
    {
        _raw.Append(delta);
        if (_textComplete) return "";

        string raw = _raw.ToString();
        if (!_insideText)
        {
            int key = raw.IndexOf("\"text\"", StringComparison.Ordinal);
            if (key < 0) return "";
            int colon = raw.IndexOf(':', key + 6);
            if (colon < 0) return "";
            int openingQuote = colon + 1;
            while (openingQuote < raw.Length && char.IsWhiteSpace(raw[openingQuote])) openingQuote++;
            if (openingQuote >= raw.Length || raw[openingQuote] != '"') return "";

            using JsonDocument prefix = JsonDocument.Parse(raw[..openingQuote] + "\"\"}");
            JsonElement root = prefix.RootElement;
            Emotion = root.GetProperty("emotion").GetString() ?? "neutral";
            Intensity = root.GetProperty("intensity").GetSingle();
            _insideText = true;
            _cursor = openingQuote + 1;
        }

        StringBuilder decoded = new();
        while (_cursor < raw.Length)
        {
            char current = raw[_cursor];
            if (current == '"')
            {
                _textComplete = true;
                _cursor++;
                break;
            }
            if (current != '\\')
            {
                decoded.Append(current);
                _cursor++;
                continue;
            }
            if (_cursor + 1 >= raw.Length) break;
            char escaped = raw[_cursor + 1];
            if (escaped == 'u')
            {
                if (_cursor + 6 > raw.Length) break;
                decoded.Append((char)Convert.ToInt32(raw.Substring(_cursor + 2, 4), 16));
                _cursor += 6;
                continue;
            }
            decoded.Append(escaped switch
            {
                'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f',
                '"' => '"', '\\' => '\\', '/' => '/',
                _ => throw new FormatException($"Invalid JSON escape: {escaped}")
            });
            _cursor += 2;
        }
        return decoded.ToString();
    }
}
