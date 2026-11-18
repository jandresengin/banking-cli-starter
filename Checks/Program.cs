using System.Net;
using System.Text;
using System.Text.Json;
using BankingCli;

Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-only");
BankSession bank = new();
bank.Execute("deposit savings 10");
FakeApi api = new();
using BankingAssistant assistant = new(new HttpClient(api));

api.Enqueue("{\"operation\":\"transfer\",\"account\":\"savings\",\"destination\":\"chequing\",\"amount\":\"4\"}");
api.Enqueue("{\"choice\":\"1\"}");
string success = await assistant.ExecuteAsync("Move four dollars from savings to chequing", bank);
Check(success == "I moved $4.00 from savings to chequing. You now have $6.00 in savings and $4.00 in chequing.", "successful conversational transfer");
Check(bank.Execute("balance").Output == "chequing=$4.00 | savings=$6.00", "transfer balances");

api.Enqueue("{\"operation\":\"clarify\",\"account\":null,\"destination\":null,\"amount\":\"4\"}");
string clarification = await assistant.ExecuteAsync("Move four dollars", bank);
Check(clarification.Contains("No transaction was made"), "clarification does not execute a transaction");
Check(bank.Execute("balance").Output == "chequing=$4.00 | savings=$6.00", "clarification does not change balances");
api.Enqueue("{\"operation\":\"transfer\",\"account\":\"savings\",\"destination\":\"chequing\",\"amount\":\"4\"}");
string invented = await assistant.ExecuteAsync("Move four dollars", bank);
Check(invented.Contains("No transaction was made") && !invented.Contains("account: savings"), "invented accounts are not executed or attributed to the user");
Check(bank.Execute("balance").Output == "chequing=$4.00 | savings=$6.00", "invented accounts leave balances unchanged");

api.Enqueue("{\"operation\":\"withdraw\",\"account\":\"savings\",\"destination\":null,\"amount\":\"100\"}");
api.Enqueue("{\"choice\":\"1\"}");
Check((await assistant.ExecuteAsync("Withdraw one hundred dollars from savings", bank)).Contains("aren't enough funds"), "declined withdrawal");

api.Enqueue("{\"operation\":\"transfer\",\"account\":\"savings\",\"destination\":\"chequing\",\"amount\":\"1\"}");
api.EnqueueFailure();
Check((await assistant.ExecuteAsync("Move one dollar from savings to chequing", bank)).StartsWith("TRANSFER $1.00"), "reply failure returns original result");
Check(bank.Execute("balance").Output == "chequing=$5.00 | savings=$5.00", "reply failure does not repeat transfer");

api.EnqueueFailure();
Check((await assistant.ExecuteAsync("Check my balances", bank)).Contains("No transaction was made"), "interpretation failure");
api.Enqueue("{\"operation\":\"deposit\",\"account\":\"savings\",\"destination\":null,\"amount\":\"2\"}");
api.Enqueue("I deposited $200.00 into savings.");
Check((await assistant.ExecuteAsync("Deposit two dollars into savings", bank)).StartsWith("DEPOSIT $2.00"), "incorrect generated reply falls back to simulator result");
Check(bank.Execute("balance").Output == "chequing=$5.00 | savings=$7.00", "rejected reply does not repeat deposit");
api.Enqueue("{\"operation\":\"transfer\"}");
Check((await assistant.ExecuteAsync("Move some money", bank)).Contains("No transaction was made"), "malformed interpretation is rejected");
Check(bank.Execute("balance").Output == "chequing=$5.00 | savings=$7.00", "malformed response leaves balances unchanged");
api.Enqueue("{\"operation\":\"deposit\",\"account\":\"savings\",\"destination\":null,\"amount\":\"10\"}");
AssistantResult proposal = await assistant.ExecuteDetailedAsync("Deposita 10 dolares a cuenta de ahorros", bank);
Check(proposal.Stage == "confirmation_required" && proposal.Command == "deposit savings 10", "translated request requires confirmation");
Check(bank.Execute("balance").Output == "chequing=$5.00 | savings=$7.00", "proposal does not change balances");
api.Enqueue("{\"choice\":\"1\"}");
Check((await assistant.ExecuteAsync("yes", bank)).Contains("deposited $10.00"), "confirmation executes proposed command");
Check(bank.Execute("balance").Output == "chequing=$5.00 | savings=$17.00", "confirmed deposit runs exactly once");
api.Enqueue("{\"operation\":\"withdraw\",\"account\":\"savings\",\"destination\":null,\"amount\":\"7\"}");
Check((await assistant.ExecuteDetailedAsync("withdraw saving 7", bank)).Stage == "confirmation_required", "account alias requires confirmation");
Check((await assistant.ExecuteAsync("no", bank)).StartsWith("Cancelled"), "confirmation can be cancelled");
Check(bank.Execute("balance").Output == "chequing=$5.00 | savings=$17.00", "cancelled proposal leaves balances unchanged");
string original = bank.Execute("deposit savings 2").Output;
api.Enqueue("{\"choice\":\"1\"}");
AssistantResult explained = await assistant.ExplainResultAsync("deposit savings 2", original);
Check(explained.Output == "I deposited $2.00 into savings. You now have $19.00 in savings.", "original command receives a conversational reply");
Check(bank.Execute("balance").Output == "chequing=$5.00 | savings=$19.00", "explaining original command does not repeat it");
original = bank.Execute("withdraw savings 1").Output;
api.EnqueueFailure();
Check((await assistant.ExplainResultAsync("withdraw savings 1", original)).Output == original, "original command reply failure returns simulator result");
Check(bank.Execute("balance").Output == "chequing=$5.00 | savings=$18.00", "original command reply failure does not repeat withdrawal");
api.Enqueue("{\"operation\":\"deposit\",\"account\":\"savings\",\"destination\":null,\"amount\":\"10\"}");
Check((await assistant.ExecuteDetailedAsync("Dépose 10 dollars dans mon compte d'épargne", bank)).Stage == "confirmation_required", "French translation requires confirmation");
Check((await assistant.ExecuteAsync("no", bank)).StartsWith("Cancelled"), "French proposal can be cancelled");
api.Enqueue("{\"operation\":\"withdraw\",\"account\":\"savings\",\"destination\":null,\"amount\":\"5\"}");
Check((await assistant.ExecuteDetailedAsync("Retire 5 from my épargne", bank)).Stage == "confirmation_required", "mixed-language request requires confirmation");
Check((await assistant.ExecuteAsync("no", bank)).StartsWith("Cancelled"), "mixed-language proposal can be cancelled");
Check(!BankingAssistant.IsOriginalCommand("withdraw saving 7"), "account alias goes to interpreter");
Check(BankingAssistant.IsOriginalCommand("help") && BankingAssistant.IsOriginalCommand("transfer savings chequing 4") && !BankingAssistant.IsOriginalCommand("deposit ten dollars into savings"), "original commands and natural language routing");
Console.WriteLine("All checks passed.");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("Failed: " + name);
    Console.WriteLine("PASS " + name);
}

sealed class FakeApi : HttpMessageHandler
{
    private readonly Queue<string?> replies = new();
    public void Enqueue(string reply) => replies.Enqueue(reply);
    public void EnqueueFailure() => replies.Enqueue(null);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? content = replies.Dequeue();
        if (content is null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        string response = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") });
    }
}
