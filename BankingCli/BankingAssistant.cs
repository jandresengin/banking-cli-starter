namespace BankingCli;

/// <summary>Coordinates interpretation, one simulator execution, and response generation.</summary>
internal sealed class BankingAssistant : IDisposable
{
    private readonly OpenAiClient client;
    private readonly RequestInterpreter interpreter;
    private readonly ReplyWriter replyWriter;
    private string? pendingCommand;
    private string? pendingInterpretation;

    public BankingAssistant(HttpClient? httpClient = null, string? baseUrl = null, string? modelName = null, string? apiKey = null)
    {
        client = new OpenAiClient(httpClient, baseUrl, modelName, apiKey);
        interpreter = new RequestInterpreter(client);
        replyWriter = new ReplyWriter(client);
    }

    public static bool IsOriginalCommand(string input) => CommandRouter.IsOriginalCommand(input);

    public void CancelPendingConfirmation() { pendingCommand = null; pendingInterpretation = null; }

    public async Task<string> ExecuteAsync(string input, BankSession bank) => (await ExecuteDetailedAsync(input, bank)).Output;

    /// <summary>Returns a trace of verified processing steps, not the model's private reasoning.</summary>
    public async Task<AssistantResult> ExecuteDetailedAsync(string input, BankSession bank)
    {
        if (pendingCommand is not null)
        {
            string confirmation = input.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
            string previousCommand = pendingCommand;
            string? previousInterpretation = pendingInterpretation;
            pendingCommand = null;
            pendingInterpretation = null;
            if (confirmation is "yes" or "y" or "confirm" or "sí" or "si" or "oui")
                return (await ExecuteCommandAsync(previousCommand, bank)) with { AiInterpretation = previousInterpretation };
            if (confirmation is "no" or "n" or "cancel" or "non")
                return new("Cancelled. No transaction was made.", "cancelled", null, false);
            // A new request replaces any pending proposal; it is never implicitly confirmed.
        }

        if (!client.IsConfigured)
            return new("The AI service is not configured. Set OPENAI_API_KEY and try again.", "configuration_error", null, false);

        Interpretation interpretation;
        try { interpretation = await interpreter.InterpretAsync(input); }
        catch (AiServiceException error)
        {
            string? diagnostic = error.InnerException is HttpRequestException http && http.StatusCode is not null
                ? $"AI provider returned HTTP {(int)http.StatusCode.Value}." : null;
            return new("I couldn't interpret that request because the AI service is unavailable or returned an invalid response. No transaction was made; please try again.", "interpretation_error", null, false, AiInterpretation: client.LastResponse, AiError: diagnostic);
        }
        string? rawInterpretation = client.LastResponse;

        if (interpretation.Command is null)
        {
            string details = interpretation.Understood is null ? "" : $"I may have understood {interpretation.Understood}. ";
            return new(details + "Please try again with one clear operation, the account or accounts, and the amount when needed. No transaction was made.", "clarification", null, false, AiInterpretation: rawInterpretation);
        }

        if (interpretation.NeedsConfirmation)
        {
            pendingCommand = interpretation.Command;
            pendingInterpretation = rawInterpretation;
            string description = ReplyWriter.DescribeCommand(pendingCommand);
            return new($"It sounds like you'd like to {description}. Is that right? Reply yes to continue or no to cancel. Nothing has changed yet.",
                "confirmation_required", pendingCommand, false, AiInterpretation: rawInterpretation);
        }

        return (await ExecuteCommandAsync(interpretation.Command, bank)) with { AiInterpretation = rawInterpretation };
    }

    private async Task<AssistantResult> ExecuteCommandAsync(string command, BankSession bank)
    {
        // The command must never be executed again when reply generation fails.
        string result = bank.Execute(command).Output;
        return await ExplainResultAsync(command, result);
    }

    /// <summary>Explains an already-executed simulator result without executing it again.</summary>
    public async Task<AssistantResult> ExplainResultAsync(string command, string result)
    {
        if (!client.IsConfigured) return new(result, "reply_error", command, true, result);
        try
        {
            string reply = await replyWriter.ExplainAsync(result);
            return new(reply, reply == result ? "reply_fallback" : "processed", command, true, result, AiReply: client.LastResponse);
        }
        catch (AiServiceException) { return new(result, "reply_error", command, true, result); }
    }

    public void Dispose() => client.Dispose();
}

internal sealed record AssistantResult(string Output, string Stage, string? Command, bool Executed, string? SimulatorResult = null, string? AiInterpretation = null, string? AiReply = null, string? AiError = null);
