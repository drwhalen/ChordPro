using System.Globalization;

namespace ChordPro.Core;

public static class SongTime
{
    /// <summary>Parses ChordPro times such as "3:45", "1:02:30", "225" (seconds) or "1:05.5".</summary>
    public static TimeSpan? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Trim().Split(':');
        if (parts.Length > 3)
            return null;

        var seconds = 0.0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                return null;
            seconds = seconds * 60 + value;
        }
        return TimeSpan.FromSeconds(seconds);
    }
}
