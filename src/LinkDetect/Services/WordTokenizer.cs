using System.Globalization;
using System.IO;
using JiebaNet.Segmenter;

namespace LinkDetect.Services;

public sealed record WordToken(string Text, int Start, int Length)
{
    public int End => Start + Length;
}

public sealed class WordTokenizer
{
    public const int MaxTextLength = 3000;

    private const int MaxPieceLength = 3;
    private const string ResourcePrefix = "jieba/";

    private static readonly string[] ResourceNames =
    [
        "dict.txt",
        "prob_emit.json",
        "prob_trans.json",
        "char_state_tab.json"
    ];

    private readonly object _gate = new();
    private JiebaSegmenter? _segmenter;

    public void WarmUp()
    {
        // 词典在首次切分时才加载，这里主动触发一次，避免第一次点“分词”卡顿。
        _ = EnsureSegmenter().Tokenize("预热", TokenizerMode.Default).ToList();
    }

    /// <summary>整段文本按词切分，返回按原文位置排列的词块；空白字符不成块。</summary>
    public IReadOnlyList<WordToken> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var segmenter = EnsureSegmenter();
        var tokens = segmenter.Tokenize(text, TokenizerMode.Default).ToList();
        var boundaries = GetGraphemeBoundaries(text);
        var result = new List<WordToken>(tokens.Count);
        var index = 0;

        while (index < tokens.Count)
        {
            var start = tokens[index].StartIndex;
            var end = tokens[index].EndIndex;

            // jieba 会把代理对（emoji）拆成两个码元，合并回一个 grapheme 簇。
            while (index < tokens.Count - 1 && (end <= 0 || end >= text.Length || !boundaries[end]))
            {
                index++;
                end = Math.Max(end, tokens[index].EndIndex);
            }

            index++;

            if (start < 0 || end > text.Length || end <= start)
            {
                continue;
            }

            var piece = text[start..end];
            if (string.IsNullOrWhiteSpace(piece))
            {
                continue;
            }

            result.Add(new WordToken(piece, start, end - start));
        }

        return result;
    }

    /// <summary>把一个词块拆成更细的词块，返回相对词块起点为 0 的位置；无法再拆时返回空。</summary>
    public IReadOnlyList<WordToken> Split(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var boundaries = GetGraphemeBoundaries(text);
        if (CountElements(boundaries) <= 1)
        {
            return [];
        }

        if (TrySplitRuns(text, out var runs))
        {
            return runs;
        }

        if (IsAsciiLettersOrDigits(text))
        {
            return SplitToSingleElements(text, boundaries);
        }

        var segmenter = EnsureSegmenter();
        var words = new HashSet<(int Start, int Length)>();
        foreach (var token in segmenter.Tokenize(text, TokenizerMode.Search))
        {
            var length = token.EndIndex - token.StartIndex;
            if (length is >= 1 and <= MaxPieceLength)
            {
                words.Add((token.StartIndex, length));
            }
        }

        var result = new List<WordToken>();
        var position = 0;

        while (position < text.Length)
        {
            var remaining = text.Length - position;
            if (remaining <= 1)
            {
                result.Add(new WordToken(text[position..], position, remaining));
                break;
            }

            var chosen = 0;
            var limit = Math.Min(MaxPieceLength, remaining);
            for (var length = limit; length >= 2; length--)
            {
                var consumesWholePiece = length == remaining && position == 0;
                if (consumesWholePiece || !boundaries[position + length])
                {
                    continue;
                }

                if (words.Contains((position, length)))
                {
                    chosen = length;
                    break;
                }
            }

            if (chosen == 0)
            {
                chosen = remaining > 2 ? 2 : 1;
            }

            chosen = SnapLength(boundaries, position, chosen, remaining);
            result.Add(new WordToken(text.Substring(position, chosen), position, chosen));
            position += chosen;
        }

        return result.Count <= 1 ? [] : result;
    }

    private JiebaSegmenter EnsureSegmenter()
    {
        lock (_gate)
        {
            if (_segmenter is not null)
            {
                return _segmenter;
            }

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LinkDetect",
                "jieba");
            Directory.CreateDirectory(directory);

            var assembly = typeof(WordTokenizer).Assembly;
            foreach (var name in ResourceNames)
            {
                using var stream = assembly.GetManifestResourceStream(ResourcePrefix + name)
                    ?? throw new InvalidOperationException($"缺少内嵌词典资源：{name}");
                var destination = Path.Combine(directory, name);
                if (!IsExtracted(destination, stream.Length))
                {
                    using var file = File.Create(destination);
                    stream.CopyTo(file);
                }
            }

            ConfigManager.ConfigFileBaseDir = directory;
            _segmenter = new JiebaSegmenter();
            return _segmenter;
        }
    }

    private static bool IsExtracted(string path, long length)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length == length;
    }

    private static bool[] GetGraphemeBoundaries(string text)
    {
        var boundaries = new bool[text.Length + 1];
        boundaries[0] = true;
        boundaries[text.Length] = true;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            boundaries[enumerator.ElementIndex] = true;
        }

        return boundaries;
    }

    private static int CountElements(bool[] boundaries)
    {
        var count = 0;
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            if (boundaries[index])
            {
                count++;
            }
        }

        return count;
    }

    private static bool TrySplitRuns(string text, out IReadOnlyList<WordToken> parts)
    {
        parts = [];

        var runs = new List<WordToken>();
        var index = 0;
        while (index < text.Length)
        {
            var kind = CharKind(text[index]);
            var start = index;
            while (index < text.Length && CharKind(text[index]) == kind)
            {
                index++;
            }

            runs.Add(new WordToken(text[start..index], start, index - start));
        }

        if (runs.Count <= 1)
        {
            return false;
        }

        parts = runs;
        return true;
    }

    private static int CharKind(char value) => char.IsAsciiDigit(value) ? 1 : char.IsAsciiLetter(value) ? 2 : 0;

    private static bool IsAsciiLettersOrDigits(string text)
    {
        foreach (var value in text)
        {
            if (!char.IsAsciiLetterOrDigit(value))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<WordToken> SplitToSingleElements(string text, bool[] boundaries)
    {
        var result = new List<WordToken>(text.Length);
        var position = 0;
        while (position < text.Length)
        {
            var length = SnapLength(boundaries, position, 1, text.Length - position);
            result.Add(new WordToken(text.Substring(position, length), position, length));
            position += length;
        }

        return result;
    }

    /// <summary>把期望长度收敛到 grapheme 边界上，避免拆开 emoji。</summary>
    private static int SnapLength(bool[] boundaries, int start, int desired, int remaining)
    {
        var length = Math.Min(desired, remaining);
        while (length > 1 && !boundaries[start + length])
        {
            length--;
        }

        if (length == 1 && !boundaries[start + 1])
        {
            var elementLength = 1;
            while (start + elementLength < boundaries.Length && !boundaries[start + elementLength])
            {
                elementLength++;
            }

            return Math.Min(elementLength, remaining);
        }

        return length;
    }
}
