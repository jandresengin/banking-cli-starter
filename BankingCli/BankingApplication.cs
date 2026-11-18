namespace BankingCli;

/// <summary>Owns the console loop and a single in-memory banking session.</summary>
internal sealed class BankingApplication : IDisposable
{
    private readonly BankSession bank = new();
    private readonly BankingAssistant assistant = new();

    public async Task RunAsync()
    {
        while (true)
        {
            // Preserve the original redirected-input behavior for scripts and reviewers.
            if (!Console.IsInputRedirected) Console.Write("bank> ");
            string? input = Console.ReadLine();
            if (input is null) return;

            CommandResult result;
            if (BankingAssistant.IsOriginalCommand(input))
            {
                assistant.CancelPendingConfirmation();
                result = bank.Execute(input);
            }
            else result = new CommandResult(await assistant.ExecuteAsync(input, bank));

            if (result.Output.Length > 0) Console.WriteLine(result.Output);
            if (result.ShouldExit) return;
        }
    }

    public void Dispose() => assistant.Dispose();
}
