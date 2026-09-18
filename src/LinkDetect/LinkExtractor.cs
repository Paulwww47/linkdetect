using System.Text.RegularExpressions;

namespace LinkDetect;

public static partial class LinkExtractor
{
    private static readonly char[] TrailingPunctuation =
    [
        '.', ',', ';', ':', '!', '?',
        '，', '。', '；', '：', '！', '？',
        '\'', '"', '’', '”', '》', '】', '〉', '」', '』'
    ];

    public static IReadOnlyList<string> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in HttpUrlRegex().Matches(text))
        {
            var candidate = TrimTrailingPunctuation(match.Value);
            if (candidate.Length == 0 ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                continue;
            }

            var normalized = uri.AbsoluteUri;
            if (seen.Add(normalized))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    private static string TrimTrailingPunctuation(string value)
    {
        var candidate = value.TrimEnd(TrailingPunctuation);

        while (candidate.Length > 0)
        {
            var last = candidate[^1];
            if (last == ')' && Count(candidate, '(') < Count(candidate, ')') ||
                last == ']' && Count(candidate, '[') < Count(candidate, ']') ||
                last == '}' && Count(candidate, '{') < Count(candidate, '}') ||
                last == '）' && Count(candidate, '（') < Count(candidate, '）'))
            {
                candidate = candidate[..^1];
                candidate = candidate.TrimEnd(TrailingPunctuation);
                continue;
            }

            break;
        }

        return candidate;
    }

    private static int Count(string value, char character)
        => value.Count(current => current == character);

    [GeneratedRegex(@"https?://[^\s<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrlRegex();
}
