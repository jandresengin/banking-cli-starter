using System.Text.Json;
using System.Globalization;

namespace BankingCli;

/// <summary>Asks the model to explain an outcome without changing its authoritative facts.</summary>
internal sealed class ReplyWriter(OpenAiClient client)
{
    private const string Prompt = """
        Select the best everyday-English explanation of the banking simulator result.
        Return JSON with choice set to "1" or "2". Do not create or change facts.
        """;

    public async Task<string> ExplainAsync(string result)
    {
        string[] options = CreateOptions(result);
        string message = $"Result: {result}\nOption 1: {options[0]}\nOption 2: {options[1]}";
        string response = await client.CompleteAsync(Prompt, message, structured: true, schema: ChoiceSchema);
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            return document.RootElement.GetProperty("choice").GetString() switch
            {
                "1" => options[0],
                "2" => options[1],
                _ => result
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return result;
        }
    }

    private static string[] CreateOptions(string result)
    {
        string[] sections = result.Split(" | ");
        string first = sections[0];
        bool operation = first.StartsWith("ERROR") || first.StartsWith("DEPOSIT") || first.StartsWith("WITHDRAW") || first.StartsWith("TRANSFER");
        string balances = string.Join(" and ", sections.Skip(operation ? 1 : 0).Select(DescribeBalance));
        string suffix = balances.Length > 0 ? $" You now have {balances}." : "";
        string text;

        if (first.StartsWith("TRANSFER "))
        {
            string[] tokens = first.Split(' ');
            text = $"I moved {tokens[1]} from {tokens[3]} to {tokens[5]}." + suffix;
        }
        else if (first.StartsWith("DEPOSIT "))
        {
            string[] tokens = first.Split(' ');
            text = $"I deposited {tokens[1]} into {tokens[3]}." + suffix;
        }
        else if (first.StartsWith("WITHDRAW "))
        {
            string[] tokens = first.Split(' ');
            text = $"I withdrew {tokens[1]} from {tokens[3]}." + suffix;
        }
        else if (first == "ERROR INSUFFICIENT_FUNDS") text = "I couldn't complete that transaction because there aren't enough funds." + (balances.Length > 0 ? $" You have {balances}." : "");
        else if (first == "ERROR BALANCE_LIMIT") text = "I couldn't complete that transaction because it would exceed the account balance limit.";
        else if (first == "ERROR INVALID_ACCOUNT") text = "I couldn't find that account. Please choose savings or chequing and try again.";
        else if (first == "ERROR INVALID_AMOUNT") text = "Please enter a positive amount with no more than two decimal places, then try again.";
        else if (first == "ERROR SAME_ACCOUNT") text = "Please choose two different accounts for your transfer.";
        else if (first == "ERROR INVALID_ARGUMENTS") text = "I couldn't complete that command. Type help to see the required details and examples.";
        else if (first.StartsWith("ERROR")) text = "I couldn't complete that request. " + first;
        else text = "You have " + balances + ".";

        return [text, "Here is the result: " + text];
    }

    private static string DescribeBalance(string balance)
    {
        string[] parts = balance.Split('=', 2);
        return parts.Length == 2 ? $"{parts[1]} in {parts[0]}" : balance;
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

    private static object ChoiceSchema => new
    {
        type = "object",
        properties = new { choice = new { type = "string", @enum = new[] { "1", "2" } } },
        required = new[] { "choice" },
        additionalProperties = false
    };
}
