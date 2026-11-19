# Virtual Bank Assistant

A .NET 10 banking simulator with a web interface. Ask in everyday English, or use the original commands. Choose local Gemma through Ollama or an OpenAI model through the selector. Both use an OpenAI-compatible API. Balances are simulated and kept in memory.

## Run locally

1. Install [Git](https://git-scm.com/install/) and Docker Desktop for [Windows](https://docs.docker.com/desktop/setup/install/windows-install/), [macOS](https://docs.docker.com/desktop/setup/install/mac-install/), or [Linux](https://docs.docker.com/desktop/setup/install/linux/). Start Docker Desktop with Linux containers.
2. Clone this branch and enter the project directory:

   ```sh
   git clone --branch release/multilingual-banking-demo https://github.com/jandresengin/banking-cli-starter.git
   cd banking-cli-starter
   ```

3. To use OpenAI, create a file named `.env` in the project directory with these two lines:

   ```dotenv
   DIRECT_OPENAI_API_KEY=your_openai_api_key
   DIRECT_OPENAI_MODEL=gpt-6-luna
   ```

   Replace the key with your own and use a model available to your OpenAI API account. API usage may incur charges. The `.env` file is ignored by Git; never share or commit it. **Gemma works without an OpenAI key**: skip this step if you only want the local model.

4. Build and start both containers:

   ```sh
   docker compose -f compose.azure-test.yaml up -d --build
   ```

   On first launch, Ollama downloads `gemma3:1b`; this can take several minutes. Check readiness with:

   ```sh
   docker compose -f compose.azure-test.yaml logs -f ollama
   ```

   When the log says the model is available, open [http://localhost:5082](http://localhost:5082) and select **Gemma 3 · local** or **OpenAI**. If you change `.env` later, rerun the `up` command to recreate the web container.

5. Stop the app with `docker compose -f compose.azure-test.yaml down`. The local model volume is retained. Sessions and balances reset when the server restarts.

## Try it

- `Deposit ten dollars into savings.`
- `Move four dollars from savings to chequing.`
- `What are my balances?`
- `Withdraw 100 dollars from savings.`
- `Move four dollars.` — missing accounts require clarification.

Original commands remain available: `deposit <account> <amount>`, `withdraw <account> <amount>`, `transfer <from> <to> <amount>`, `balance [account]`, `help`, `?`, and `exit`. Accounts are `savings` and `chequing`; amounts must be positive with at most two decimal places.

The model interprets natural-language requests. Code validates the details and requests clarification or confirmation before executing when required. The simulator then executes once, and the model explains the result. If explanation generation fails or its facts fail validation, the original simulator result is shown without executing the command again. Processing details show the simulator result and model responses.

## Checks

Run the automated checks with `docker compose run --rm banking dotnet run --project Checks`. They use a simulated API. Observed checks:

1. Deposit $10 into savings and transfer $4 to chequing: balances were $6 and $4.
2. Ask to move $4 without accounts: the app requested clarification and balances stayed unchanged.
3. Simulate a reply API failure after a $1 transfer: the original simulator result was returned and the transfer ran once.

## How I worked

- Used OpenAI Codex to assist with implementation, containers, and verification.
- Accepted the suggestion to validate model responses and return the simulator result if explanation generation fails. Checked it with simulated incorrect responses and an API failure to verify the transaction was not repeated.

This is a demo, not a production banking service. See [Azure deployment notes](deploy/AZURE.md) for the hosted configuration.
