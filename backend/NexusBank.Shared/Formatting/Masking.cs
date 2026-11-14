using System.Globalization;
using System.Text;

namespace NexusBank.Shared.Formatting;

/// <summary>
/// Central masking helpers. Sensitive account identifiers, emails and tokens are
/// never returned in full by list endpoints.
/// </summary>
public static class Masking
{
    /// <summary>NZ-style account number: keep bank and suffix, mask the middle.</summary>
    public static string AccountNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length < 6) return new string('•', Math.Max(value.Length, 4));
        return $"{digits[..2]}-••••-{digits[^4..]}";
    }

    public static string Email(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var at = value.IndexOf('@');
        if (at <= 1) return "•••";
        var name = value[..at];
        var visible = name.Length <= 2 ? name[..1] : name[..2];
        return $"{visible}{new string('•', Math.Max(3, name.Length - visible.Length))}{value[at..]}";
    }

    public static string Secret(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : "••••••••";
}

public static class References
{
    public static string Payment() => $"PAY-{DateTime.UtcNow:yyMMdd}-{Suffix(6)}";
    public static string Incident(int sequence) => $"INC-{DateTime.UtcNow:yyyy}-{sequence:D4}";
    public static string Correlation() => $"cor_{Suffix(16).ToLowerInvariant()}";

    private static string Suffix(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(alphabet[Random.Shared.Next(alphabet.Length)]);
        }
        return builder.ToString();
    }
}

public static class Display
{
    public static string Relative(DateTimeOffset value, DateTimeOffset now)
    {
        var delta = now - value;
        if (delta.TotalSeconds < 0) return "just now";
        if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        return delta.TotalDays < 7
            ? $"{(int)delta.TotalDays}d ago"
            : value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    }
}
