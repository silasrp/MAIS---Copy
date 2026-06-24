using System.Globalization;
using System.Text.RegularExpressions;
using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion.Client;

public sealed class LogLineParser
{
    private static readonly Regex EntryHeader = new(
        @"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<time>\d{2}:\d{2}:\d{2}),(?<ms>\d{3})\s+\[(?<thread>[^\]]+)\]\s+(?<level>\w+)\s+(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Strips the logger-name prefix that log4net inserts between the level and
    // the actual message: e.g. "com.example.SomeClass - the real message".
    // Non-greedy so only the first " - " delimiter is consumed.
    private static readonly Regex StripPrefix = new(@"^.*? - ", RegexOptions.Compiled);

    public ParsedLogEntry Parse(RawLogEntry entry)
    {
        var m = EntryHeader.Match(entry.FirstLine);
        if (!m.Success) return ParsedLogEntry.Unparsed(entry);

        try
        {
            var rest = (entry.AdditionalLines.Count > 0
                ? m.Groups["rest"].Value + "\n" + string.Join('\n', entry.AdditionalLines)
                : m.Groups["rest"].Value).Trim();

            var message = StripPrefix.Replace(rest, "", 1).Trim();

            return new ParsedLogEntry
            {
                AppId       = entry.AppId,
                MachineName = entry.MachineName,
                AssetTag    = entry.AssetTag,
                Timestamp   = DateTime.ParseExact(
                    $"{m.Groups["date"].Value} {m.Groups["time"].Value}",
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture),
                Thread  = m.Groups["thread"].Value,
                Level   = m.Groups["level"].Value,
                Message = message
            };
        }
        catch (FormatException)
        {
            return ParsedLogEntry.Unparsed(entry);
        }
    }
}
