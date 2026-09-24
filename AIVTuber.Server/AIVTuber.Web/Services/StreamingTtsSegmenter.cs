using System.Text;

namespace AIVTuber.Web.Services;

public sealed class StreamingTtsSegmenter
{
    private readonly StringBuilder _pending = new();
    private bool _first = true;

    public IReadOnlyList<string> Push(string text)
    {
        List<string> ready = [];
        foreach (char character in text)
        {
            _pending.Append(character);
            bool sentenceEnd = character is '.' or '!' or '?' or '。' or '！' or '？' or '\n';
            bool firstClause = _first && _pending.Length >= 10 &&
                character is ',' or '，';
            bool longClause = _pending.Length >= 36 &&
                (char.IsWhiteSpace(character) || _pending.Length >= 48) &&
                !char.IsHighSurrogate(character);
            if (!sentenceEnd && !firstClause && !longClause) continue;

            string segment = _pending.ToString().Trim();
            _pending.Clear();
            if (segment.Length == 0) continue;
            ready.Add(segment);
            _first = false;
        }
        return ready;
    }

    public string Flush()
    {
        string remaining = _pending.ToString().Trim();
        _pending.Clear();
        return remaining;
    }
}
