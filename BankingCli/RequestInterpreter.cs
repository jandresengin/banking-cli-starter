using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BankingCli;

/// <summary>Converts an AI interpretation into one validated simulator command.</summary>
internal sealed class RequestInterpreter(OpenAiClient client)
{
    private const string Prompt = """
        Interpret the user's original request in English, Spanish, French or a mixture. Extract ONE banking request as JSON: operation, account, destination, amount, needs_clarification, needs_confirmation.
        Operations: deposit, withdraw, transfer, balance, clarify. Accounts: savings, chequing.
        Convert number words to a decimal amount string. account is the source for a transfer.
        Null means absent. Only one account in deposit or withdraw; destination must be null.
        For a general or both-accounts balance, account and destination must be null.
        Recognize obvious spelling mistakes only when the intent is unambiguous.
        Do not invent accounts or amounts. If information is missing, return the understood operation
        and any known fields, with unknown fields null. For unclear intent or multiple operations,
        return clarify and any details that are certain. The application will ask for clarification.
        There is no conversation history. Examples:
        Request: Deposit ten dollars into savings.
        JSON: {"operation":"deposit","account":"savings","destination":null,"amount":"10"}
        Request: Deposit 52 dollars into savings.
        JSON: {"operation":"deposit","account":"savings","destination":null,"amount":"52"}
        Request: Move four dollars from savings to chequing.
        JSON: {"operation":"transfer","account":"savings","destination":"chequing","amount":"4"}
        Request: What are my balances?
        JSON: {"operation":"balance","account":null,"destination":null,"amount":null}
        Request: What is my savings balance?
        JSON: {"operation":"balance","account":"savings","destination":null,"amount":null}
        Request: Move four dollars.
        JSON: {"operation":"transfer","account":null,"destination":null,"amount":"4"}
        Request: Depsoit ten dollars into savings.
        JSON: {"operation":"deposit","account":"savings","destination":null,"amount":"10"}
        Request: Withdraw six dollars from chequing.
        JSON: {"operation":"withdraw","account":"chequing","destination":null,"amount":"6"}
        Request: Withdraw one hundred dollars from savings.
        JSON: {"operation":"withdraw","account":"savings","destination":null,"amount":"100"}
        Request: withdraw 50 from savings
        JSON: {"operation":"withdraw","account":"savings","destination":null,"amount":"50"}
        Request: withdraw savings 7
        JSON: {"operation":"withdraw","account":"savings","destination":null,"amount":"7"}
        Request: deposit 10 dollars to savings
        JSON: {"operation":"deposit","account":"savings","destination":null,"amount":"10"}
        Request: retira 1 de la cuenta corriente
        JSON: {"operation":"withdraw","account":"chequing","destination":null,"amount":"1","needs_clarification":false,"needs_confirmation":true}
        Request: retira 1 de la cuenta de ahorros
        JSON: {"operation":"withdraw","account":"savings","destination":null,"amount":"1","needs_clarification":false,"needs_confirmation":true}
        Request: transfiere de la cuenta de ahorros a la cuenta corriente 5
        JSON: {"operation":"transfer","account":"savings","destination":"chequing","amount":"5","needs_clarification":false,"needs_confirmation":true}
        Request: Transfer 4 dollars from my current account to savings.
        JSON: {"operation":"transfer","account":"chequing","destination":"savings","amount":"4","needs_clarification":false,"needs_confirmation":true}
        Withdrawal amounts must be positive, never negative. A numeric amount may appear at the end of the request.
        Set needs_clarification true if essential information is absent or ambiguous, or multiple operations are requested.
        Set needs_confirmation true if spelling, translation, or uncertainty could change the intended operation, account, amount, or direction.
        """;

    public async Task<Interpretation> InterpretAsync(string input)
    {
        string normalizedInput = input;
        string json = await client.CompleteAsync(Prompt, input, structured: true);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string? operation = root.GetProperty("operation").GetString();
            string? account = NormalizeAccount(root.GetProperty("account").GetString(), out bool accountAlias);
            string? destination = NormalizeAccount(root.GetProperty("destination").GetString(), out bool destinationAlias);
            string? amount = root.GetProperty("amount").GetString();
            if (amount is not null && Regex.IsMatch(amount, @"^\+\d+(?:\.\d{1,2})?$")) amount = amount[1..];
            bool needsClarification = root.TryGetProperty("needs_clarification", out JsonElement clarify) && clarify.ValueKind == JsonValueKind.True;
            bool needsConfirmation = root.TryGetProperty("needs_confirmation", out JsonElement confirm) && confirm.ValueKind == JsonValueKind.True;
            needsConfirmation |= accountAlias || destinationAlias;
            // The model can reverse transfer direction even when both accounts are named.
            if (operation == "transfer") needsConfirmation = true;
            bool validAccount = account is "savings" or "chequing";
            bool validDestination = destination is "savings" or "chequing";
            bool validAmount = amount is not null && Regex.IsMatch(amount, @"^\d+(\.\d{1,2})?$")
                && decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value) && value > 0;

            string? command = operation switch
            {
                "balance" when account is null && destination is null && amount is null => "balance",
                "balance" when validAccount && destination is null && amount is null => $"balance {account}",
                "deposit" or "withdraw" when validAccount && destination is null && validAmount => $"{operation} {account} {amount}",
                "transfer" when validAccount && validDestination && account != destination && validAmount => $"transfer {account} {destination} {amount}",
                _ => null
            };
            if (needsClarification) command = null;
            // Aliases and non-English account names require explicit user confirmation.
            if (command is not null && Regex.IsMatch(input, @"\b(?:saving|ahorros?|[ée]pargne|corriente|cheques)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) needsConfirmation = true;
            // Do not allow a model to invent an account the user never named.
            // A misspelled account is treated as ambiguous and requires clarification.
            if (command is not null && validAccount && !MentionsAccount(normalizedInput, account!)) command = null;
            if (command is not null && validDestination && !MentionsAccount(normalizedInput, destination!)) command = null;
            if (command is not null && operation == "balance" && account is null
                && (MentionsAccount(normalizedInput, "savings") != MentionsAccount(normalizedInput, "chequing"))) command = null;
            // A visible number must never be silently changed or invented by the model.
            MatchCollection numericAmounts = Regex.Matches(normalizedInput, @"(?<![\w.])\d+(?:\.\d+)?(?![\w.])");
            bool mentionsNumberWord = Regex.IsMatch(normalizedInput, @"\b(?:zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred|thousand|uno|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce|veinte|cien|ciento)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (command is not null && operation != "balance" && numericAmounts.Count == 0 && !mentionsNumberWord) command = null;
            if (command is not null && operation != "balance" && numericAmounts.Count > 0
                && (numericAmounts.Count != 1 || !decimal.TryParse(numericAmounts[0].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal entered)
                    || !decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal parsed) || entered != parsed)) command = null;
            if (command is not null) return new(command, null, needsConfirmation);

            // Describe only recognized fields, as a tentative interpretation; never execute it.
            List<string> details = [];
            if (operation is "deposit" or "withdraw" or "transfer" or "balance") details.Add($"operation: {operation}");
            // Small models can invent missing fields. Only display account names that
            // are actually present in the input; omit amounts in tentative summaries.
            if (validAccount && MentionsAccount(normalizedInput, account!)) details.Add($"account: {account}");
            if (validDestination && MentionsAccount(normalizedInput, destination!)) details.Add($"destination: {destination}");
            string? understood = details.Count == 0 ? null : string.Join(", ", details);
            return new(null, understood, false);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new AiServiceException("Invalid interpretation response.", ex);
        }
    }

    private static bool MentionsAccount(string input, string account)
    {
        string pattern = account == "chequing" ? @"\b(?:chequing|checking|current|corriente|cheques|courant)\b" : @"\b(?:savings|saving|ahorros?|[ée]pargne)\b";
        return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // Accept only known aliases; the original user input is checked separately before execution.
    private static string? NormalizeAccount(string? value, out bool alias)
    {
        alias = false;
        if (value is null) return null;
        string name = value.Trim().ToLowerInvariant();
        string normalized = name switch
        {
            "checking" or "current" or "corriente" or "cheques" or "courant" => "chequing",
            "saving" or "ahorro" or "ahorros" or "épargne" or "epargne" => "savings",
            _ => name
        };
        alias = normalized != name && normalized is "chequing" or "savings";
        return normalized;
    }
}

internal sealed record Interpretation(string? Command, string? Understood, bool NeedsConfirmation);
