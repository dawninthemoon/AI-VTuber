using System.Text;

namespace AIVTuber.Web.Services;

public static class TtsTextSegmenter
{
    private const int MinSegmentLength = 10;
    private const int MaxSegmentLength = 36;

    private static readonly char[] SentenceEndings = ['.', '?', '!', '。', '？', '！'];
    private static readonly char[] PhraseBreaks = [',', '，', '、', ';', ':', ' '];

    public static IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<string> sentences = [];
        StringBuilder current = new();

        foreach (char character in text.Trim())
        {
            current.Append(character);

            if (SentenceEndings.Contains(character))
            {
                AddIfPresent(sentences, current);
            }
        }

        AddIfPresent(sentences, current);

        List<string> segments = [];
        foreach (string sentence in sentences)
        {
            SplitLongSentence(sentence, segments);
        }

        MergeShortSegments(segments);
        return segments;
    }

    private static void SplitLongSentence(string sentence, List<string> segments)
    {
        string remaining = sentence.Trim();

        while (remaining.Length > MaxSegmentLength)
        {
            int splitAt = FindSplitPoint(remaining);
            segments.Add(remaining[..splitAt].Trim());
            remaining = remaining[splitAt..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            segments.Add(remaining);
        }
    }

    private static int FindSplitPoint(string text)
    {
        int upperBound = Math.Min(MaxSegmentLength, text.Length - 1);

        for (int index = upperBound; index >= MinSegmentLength; index--)
        {
            if (PhraseBreaks.Contains(text[index]))
            {
                return index + 1;
            }
        }

        return upperBound;
    }

    private static void MergeShortSegments(List<string> segments)
    {
        for (int index = segments.Count - 1; index > 0; index--)
        {
            if (segments[index].Length >= MinSegmentLength)
            {
                continue;
            }

            string merged = $"{segments[index - 1]} {segments[index]}";
            if (merged.Length <= MaxSegmentLength + MinSegmentLength)
            {
                segments[index - 1] = merged;
                segments.RemoveAt(index);
            }
        }
    }

    private static void AddIfPresent(List<string> results, StringBuilder builder)
    {
        string value = builder.ToString().Trim();
        builder.Clear();

        if (value.Length > 0)
        {
            results.Add(value);
        }
    }
}
