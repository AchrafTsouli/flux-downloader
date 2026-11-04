using System.Text.RegularExpressions;

namespace SwiftFetch.Core;

public static class BatchUrlExpander
{
    private static readonly Regex PatternRegex = new(@"\[(?<start>\d+)-(?<end>\d+)\]", RegexOptions.Compiled);

    public static List<string> Expand(string pattern)
    {
        var result = new List<string>(); ExpandInto(pattern, result); return result.Distinct().ToList();
    }
    private static void ExpandInto(string pattern, List<string> results)
    {
        if (results.Count >= 2000) throw new ArgumentException("A batch is limited to 2,000 links.");
        var match = PatternRegex.Match(pattern);
        if (!match.Success) { results.Add(pattern); return; }

        string startStr = match.Groups["start"].Value;
        string endStr = match.Groups["end"].Value;

        if (!long.TryParse(startStr, out long start) || !long.TryParse(endStr, out long end))
            throw new ArgumentException("Invalid batch number range.");

        int padLength = startStr.Length;
        long step = start <= end ? 1 : -1;
        long count = Math.Abs(end - start);
        if (count >= 2000) throw new ArgumentException("A batch is limited to 2,000 links.");
        count++;

        string prefix = pattern[..match.Index];
        string suffix = pattern[(match.Index + match.Length)..];

        for (long i = 0; i < count; i++)
        {
            long current = start + (i * step);
            string formatted = current.ToString().PadLeft(padLength, '0');
            string candidate = prefix + formatted + suffix;
            // Recursive expansion if pattern has multiple brackets
            ExpandInto(candidate, results);
        }

    }
}
