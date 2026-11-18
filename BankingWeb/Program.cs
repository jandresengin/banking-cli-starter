using System.Collections.Concurrent;
using System.Security.Cryptography;
using BankingCli;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

// This is a local demonstration: sessions are in memory and lost on restart.
ConcurrentDictionary<string, BankingSession> sessions = new();
string? publicOrigin = Environment.GetEnvironmentVariable("APP_PUBLIC_ORIGIN")?.TrimEnd('/');
if (string.IsNullOrWhiteSpace(publicOrigin)) publicOrigin = null;
if (!string.IsNullOrEmpty(publicOrigin) && (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out Uri? publicUri) || publicUri.Scheme != Uri.UriSchemeHttps || publicUri.AbsolutePath != "/" || !string.IsNullOrEmpty(publicUri.Query) || !string.IsNullOrEmpty(publicUri.Fragment)))
    throw new InvalidOperationException("APP_PUBLIC_ORIGIN must be an HTTPS origin without a path.");
app.Use(async (context, next) =>
{
    // The demo only accepts local hostnames; do not expose it as a public banking service.
    bool localHost = context.Request.Host.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
    bool publicHost = publicOrigin is not null && string.Equals(context.Request.Host.Value, new Uri(publicOrigin).Authority, StringComparison.OrdinalIgnoreCase);
    if (!localHost && !publicHost)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    string? origin = context.Request.Headers.Origin;
    string allowedOrigin = publicHost ? publicOrigin! : $"{context.Request.Scheme}://{context.Request.Host}";
    if (!string.IsNullOrEmpty(origin) && !string.Equals(origin, allowedOrigin, StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

BankingSession GetSession(HttpContext context)
{
    string? id = context.Request.Cookies["banking-session"];
    if (id is not null && sessions.TryGetValue(id, out BankingSession? existing)) return existing;
    id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    BankingSession session = new();
    sessions[id] = session;
    context.Response.Cookies.Append("banking-session", id, new CookieOptions
    {
        HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, Secure = context.Request.IsHttps || publicOrigin is not null
    });
    return session;
}

app.MapGet("/api/session", async (HttpContext context) =>
{
    BankingSession session = GetSession(context);
    await session.Gate.WaitAsync();
    try { return Results.Ok(new { balances = session.GetBalances(), openAiAvailable = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DIRECT_OPENAI_API_KEY")) }); }
    finally { session.Gate.Release(); }
});

app.MapPost("/api/command", async (BankingRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Input) || request.Input.Length > 500)
        return Results.BadRequest(new { error = "Enter a request of up to 500 characters." });

    BankingSession session = GetSession(context);
    string provider = request.Provider ?? "ollama";
    if (provider is not ("ollama" or "openai")) return Results.BadRequest(new { error = "Unknown provider." });
    if (provider == "openai" && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DIRECT_OPENAI_API_KEY")))
        return Results.BadRequest(new { error = "OpenAI is not configured on this server." });

    // Serialize requests per session so two concurrent transactions cannot race.
    await session.Gate.WaitAsync();
    try
    {
        BankingAssistant assistant = session.SelectProvider(provider);
        if (BankingAssistant.IsOriginalCommand(request.Input))
        {
            assistant.CancelPendingConfirmation();
            CommandResult original = session.Bank.Execute(request.Input);
            if (original.ShouldExit)
            {
                string? id = context.Request.Cookies["banking-session"];
                if (id is not null) sessions.TryRemove(id, out _);
                context.Response.Cookies.Delete("banking-session");
                session.Dispose();
                return Results.Ok(new { output = original.Output, stage = "original_command", command = request.Input, executed = true, simulatorResult = original.Output, ended = true });
            }
            string command = request.Input.Trim();
            if (command.Equals("help", StringComparison.OrdinalIgnoreCase) || command == "?")
                return Results.Ok(new { output = WebHelp.Text, stage = "original_command", command, executed = true, simulatorResult = original.Output, balances = session.GetBalances(), ended = false });

            AssistantResult explained = await assistant.ExplainResultAsync(command, original.Output);
            string stage = explained.Stage switch
            {
                "processed" => "original_explained",
                "reply_error" => "original_reply_error",
                _ => "original_fallback"
            };
            return Results.Ok(new { output = explained.Output, stage, command, executed = true, simulatorResult = original.Output, aiReply = explained.AiReply, balances = session.GetBalances(), ended = false });
        }
        AssistantResult result = await assistant.ExecuteDetailedAsync(request.Input, session.Bank);
        return Results.Ok(new { output = result.Output, stage = result.Stage, command = result.Command, executed = result.Executed, simulatorResult = result.SimulatorResult, aiInterpretation = result.AiInterpretation, aiReply = result.AiReply, aiError = result.AiError, balances = session.GetBalances() });
    }
    finally { session.Gate.Release(); }
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    foreach (BankingSession session in sessions.Values) session.Dispose();
});
app.Run();

internal sealed record BankingRequest(string? Input, string? Provider);
internal static class WebHelp
{
    public const string Text = """
        Here's what I can help you with:

        • Deposit — Add money to an account.
          Example: deposit savings 10

        • Withdraw — Take money out of an account.
          Example: withdraw chequing 1

        • Transfer — Move money between accounts.
          Example: transfer savings chequing 4

        • Balance — Check one or both balances.
          Examples: balance savings or balance

        • Help — See this guide. Type help or ?
        • Exit — End your simulated session. Type exit

        Use savings or chequing and a positive amount with up to two decimal places. You can also ask me in everyday language!
        """;
}

internal sealed class BankingSession : IDisposable
{
    public BankSession Bank { get; } = new();
    public BankingAssistant Assistant { get; } = new();
    private readonly BankingAssistant openAi = new(baseUrl: "https://api.openai.com/v1", modelName: Environment.GetEnvironmentVariable("DIRECT_OPENAI_MODEL") ?? "gpt-6-luna", apiKey: Environment.GetEnvironmentVariable("DIRECT_OPENAI_API_KEY") ?? "");
    private string currentProvider = "ollama";
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public BankingAssistant SelectProvider(string provider)
    {
        if (provider != currentProvider)
        {
            Assistant.CancelPendingConfirmation();
            openAi.CancelPendingConfirmation();
            currentProvider = provider;
        }
        return provider == "openai" ? openAi : Assistant;
    }

    public void Dispose() { Assistant.Dispose(); openAi.Dispose(); }

    // Read through the existing simulator; never calculate balances in the web layer.
    public object GetBalances()
    {
        string[] balances = Bank.Execute("balance").Output.Split(" | ");
        return new { chequing = balances[0]["chequing=".Length..], savings = balances[1]["savings=".Length..] };
    }
}
