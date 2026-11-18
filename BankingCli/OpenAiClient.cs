using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BankingCli;

/// <summary>Small client for an OpenAI-compatible chat-completions endpoint.</summary>
internal sealed class OpenAiClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string endpoint = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1";
    private readonly string model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-6-luna";
    private readonly string? key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    public bool IsConfigured => !string.IsNullOrWhiteSpace(key);

    public OpenAiClient(HttpClient? httpClient = null) => http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

    public async Task<string> CompleteAsync(string system, string user, bool structured, object? schema = null)
    {
        try
        {
            if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/chat/completions", UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != "https" && uri.Scheme != "http"))
                throw new ArgumentException("Invalid API URL.");

            using HttpRequestMessage request = new(HttpMethod.Post, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            object[] messages = [new { role = "system", content = system }, new { role = "user", content = user }];
            Dictionary<string, object> body = new() { ["model"] = model, ["messages"] = messages };
            if (structured)
                body["response_format"] = new { type = "json_schema", json_schema = new { name = "bank_response", strict = true, schema = schema ?? InterpretationSchema } };
            // Some providers and models do not support temperature; configure it only when requested.
            if (decimal.TryParse(Environment.GetEnvironmentVariable("OPENAI_TEMPERATURE"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out decimal temperature) && temperature is >= 0 and <= 2)
                body["temperature"] = temperature;
            request.Content = JsonContent.Create(body);

            using HttpResponseMessage response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                ?? throw new JsonException("Missing response content.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException)
        {
            // Do not expose provider errors, which may contain request details, to the console.
            throw new AiServiceException("The AI service call failed.", ex);
        }
    }

    private static object InterpretationSchema => new
    {
        type = "object",
        properties = new
        {
            operation = new { type = "string", @enum = new[] { "deposit", "withdraw", "transfer", "balance", "clarify" } },
            account = new { type = new[] { "string", "null" } },
            destination = new { type = new[] { "string", "null" } },
            amount = new { type = new[] { "string", "null" } }
        },
        required = new[] { "operation", "account", "destination", "amount" },
        additionalProperties = false
    };

    public void Dispose() => http.Dispose();
}

internal sealed class AiServiceException(string message, Exception innerException) : Exception(message, innerException);
