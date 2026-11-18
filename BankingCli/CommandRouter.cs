using System.Globalization;
using System.Text.RegularExpressions;

namespace BankingCli;

/// <summary>Recognizes the original command form so it remains usable without the API.</summary>
internal static class CommandRouter
{
    public static bool IsOriginalCommand(string input)
    {
        string[] parts = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true;
        string first = parts[0].ToLowerInvariant();
        if (first is "help" or "?" or "exit") return true;
        if (first is not ("balance" or "deposit" or "withdraw" or "transfer")) return false;
        // "saving" is common natural language; only the exact original account is a CLI command.
        if (parts.Skip(1).Any(p => p.Equals("saving", StringComparison.OrdinalIgnoreCase))) return false;

        // Also pass short malformed commands through so the simulator returns its own errors.
        return parts.Skip(1).All(p => Regex.IsMatch(p, @"^(chequing|savings|[0-9.]+)$", RegexOptions.IgnoreCase))
            || (parts.Length <= (first == "transfer" ? 4 : first == "balance" ? 2 : 3)
                && !parts.Skip(1).Any(p => Regex.IsMatch(p, @"^(to|from|in|into|dollars?|please|my|the|and)$", RegexOptions.IgnoreCase))
                && parts.Skip(1).Any(p => p.Equals("chequing", StringComparison.OrdinalIgnoreCase)
                    || p.Equals("savings", StringComparison.OrdinalIgnoreCase)
                    || decimal.TryParse(p, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)));
    }
}
