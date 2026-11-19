using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BankingCli;

/// <summary>Asks the model to explain an outcome without changing its authoritative facts.</summary>
internal sealed class ReplyWriter(OpenAiClient client)
{
    private const string Prompt = """
        Explain the simulator's result in friendly everyday English. Preserve the exact outcome,
        amounts, accounts, direction and balances. If the transaction was declined, say so.
        Do not add balances, amounts, accounts, or claims not present in the result. Return only the reply.
        Examples:
        Result: DEPOSIT $10.00 TO savings | savings=$10.00
        Reply: I deposited $10.00 into savings. You now have $10.00 in savings.
        Result: TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00
        Reply: I moved $4.00 from savings to chequing. You now have $6.00 in savings and $4.00 in chequing.
        Result: ERROR INSUFFICIENT_FUNDS | savings=$6.00
        Reply: I couldn't complete that transaction because there aren't enough funds. You have $6.00 in savings.
        Result: chequing=$0.00 | savings=$10.00
        Reply: You have $0.00 in chequing and $10.00 in savings.
        Result: savings=$6.00
        Reply: You have $6.00 in savings.
        If the result contains only account balances, no transaction occurred. Do not say money was moved, deposited or withdrawn.
        """;

    public async Task<string> ExplainAsync(string result)
    {
        string response = (await client.CompleteAsync(Prompt, result, structured: false)).Trim();
        if (IsPlausible(response, result)) return response;
        // Retry only the explanation, never the simulator transaction.
        string retryPrompt = "You explain a banking simulator result in everyday English. " +
            "Your previous explanation was rejected. Use only the authoritative result below; " +
            "match each account to its own balance exactly. Do not swap balances or invent a transaction. " +
            "Return only the corrected explanation.";
        string retry = (await client.CompleteAsync(retryPrompt, $"Authoritative simulator result: {result}\nRejected explanation: {response}", structured: false)).Trim();
        return IsPlausible(retry, result) ? retry : result;
    }

    private static bool IsPlausible(string response, string result)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Length > 1000) return false;
        response = response.Replace('’', '\'');
        var expected = Regex.Matches(result, @"\$\d+(?:\.\d{2})").Select(m => m.Value).OrderBy(x => x).ToArray();
        var actual = Regex.Matches(response, @"\$\d+(?:\.\d{2})").Select(m => m.Value).OrderBy(x => x).ToArray();
        if (!expected.SequenceEqual(actual)) return false;
        foreach (string account in new[] { "savings", "chequing" })
            if (Regex.IsMatch(result, $@"\b{account}\b", RegexOptions.IgnoreCase) != Regex.IsMatch(response, $@"\b{account}\b", RegexOptions.IgnoreCase)) return false;
        if (result.StartsWith("ERROR") && !Regex.IsMatch(response, @"\b(?:cannot|can't|couldn't|unable|declined|failed|invalid|insufficient|not|please)\b", RegexOptions.IgnoreCase)) return false;
        if (!result.StartsWith("ERROR") && Regex.IsMatch(response, @"\b(?:cannot|can't|couldn't|unable|declined|failed|insufficient)\b", RegexOptions.IgnoreCase)) return false;
        bool isBalance = result.StartsWith("savings=") || result.StartsWith("chequing=");
        if (isBalance && Regex.IsMatch(response, @"\b(?:move[ds]?|moving|transfer(?:red|s|ring)?|deposit(?:ed|s|ing)?|withdraw(?:n|s|ing)?|withdrew|sent)\b", RegexOptions.IgnoreCase)) return false;
        if (isBalance || result.StartsWith("TRANSFER "))
        {
            foreach (string item in result.Split(" | ").Where(item => item.Contains('=')))
            {
                string[] pair = item.Split('=', 2);
                if (pair.Length != 2) return false;
                string account = Regex.Escape(pair[0]);
                string amount = Regex.Escape(pair[1]);
                string associated = $@"(?:{amount}\s+(?:in|for|on)\s+(?:your\s+)?{account}\b|\b{account}(?:\s+account|\s+balance|\s+is|\s+has|\s+of|\s*[:=])*\s*{amount})";
                if (!Regex.IsMatch(response, associated, RegexOptions.IgnoreCase)) return false;
            }
        }
        if (result.StartsWith("TRANSFER "))
        {
            string[] tokens = result.Split(' ');
            if (!Regex.IsMatch(response, $@"\bfrom\s+{tokens[3]}\s+to\s+{tokens[5]}\b", RegexOptions.IgnoreCase)) return false;
        }
        return true;
    }

    public static string DescribeCommand(string command)
    {
        string[] tokens = command.Split(' ');
        string Format(string amount) => "$" + decimal.Parse(amount, CultureInfo.InvariantCulture).ToString("F2", CultureInfo.InvariantCulture);
        return tokens[0] switch
        {
            "deposit" => $"deposit {Format(tokens[2])} into {tokens[1]}",
            "withdraw" => $"withdraw {Format(tokens[2])} from {tokens[1]}",
            "transfer" => $"move {Format(tokens[3])} from {tokens[1]} to {tokens[2]}",
            "balance" when tokens.Length == 1 => "check both account balances",
            "balance" => $"check your {tokens[1]} balance",
            _ => command
        };
    }

}
