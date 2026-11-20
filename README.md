# Virtual Bank Assistant

A .NET 10 virtual banking assistant with a web interface for deposits, withdrawals, transfers, balance checks, and help. The `BankingCli` module simulates a banking system with predefined commands and banking rules.

The assistant routes original commands directly to the simulator. For natural-language requests, the selected model—local Gemma 3 through Ollama or OpenAI Luna—interprets the original message through an OpenAI-compatible API. `RequestInterpreter.cs` requests structured operation details, normalizes supported account aliases, validates the result, and builds a simulator command. Missing or unclear details prompt the user to try again. Interpreted transfers and other uncertain requests require confirmation before execution.

After the simulator executes a command, `ReplyWriter.cs` asks the selected model to explain the actual result in everyday English. Its prompt includes examples, and the application checks the generated reply against the simulator result. If generation fails or validation rejects the reply, the original result is shown without executing the transaction again. In the web interface, **View processing details** shows the selected provider, available interpretation and command, simulator result, generated reply, and processing outcome. Model outputs are not a view of the model's hidden reasoning.

Balances are simulated and stored in server memory for each session. They reset when the user ends the session with `exit` or the server restarts. The application uses two Docker containers: one for the .NET web application and simulator, and one for Ollama and the local Gemma model.

## Screenshots

**Local Gemma deposit.** The assistant interprets the request, asks for confirmation, and displays the simulator result and generated reply.

![Gemma deposit with confirmation and processing details](docs/screenshots/gemma-deposit.png)

**OpenAI transfer with French confirmation.**

The user confirms with “Oui”; the assistant executes the transfer and displays the updated balances.

![OpenAI transfer confirmed in French](docs/screenshots/openai-transfer.png)

**Single-account and combined balance checks.**

![OpenAI balance checks for one and both accounts](docs/screenshots/balance-checks.png)

**Insufficient funds and a request needing clarification.**

The simulator declines the withdrawal, and the incomplete transfer prompts for the missing account.

![Declined withdrawal and incomplete transfer](docs/screenshots/declined-and-clarification.png)

## Run locally

1. Install [Git](https://git-scm.com/install/) and Docker Desktop for [Windows](https://docs.docker.com/desktop/setup/install/windows-install/), [macOS](https://docs.docker.com/desktop/setup/install/mac-install/), or [Linux](https://docs.docker.com/desktop/setup/install/linux/). Start Docker Desktop with Linux containers and wait for the engine to be running.
2. Clone this branch and enter the project directory:

   ```sh
   git clone --branch release/multilingual-banking-demo https://github.com/jandresengin/banking-cli-starter.git
   cd banking-cli-starter
   ```

   Example console output after cloning and entering the project directory:

   ![Console output showing the repository being cloned](docs/screenshots/clone-console.png)

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

   Building and downloading images can take several minutes depending on your internet connection and whether the images are already cached locally.

   ![Console output showing the container images being built](docs/screenshots/docker-build-console.png)

   On first launch, Ollama also downloads `gemma3:1b`; this may take several minutes. Check model readiness with:

   ```sh
   docker compose -f compose.azure-test.yaml logs -f ollama
   ```

   When the log says the model is available, open [http://127.0.0.1:5082/](http://127.0.0.1:5082/) or [http://localhost:5082](http://localhost:5082) and select **Gemma 3 · local** or **OpenAI**. If you change `.env` later, rerun the build and start command to recreate the web container.

5. To start the containers and follow their logs in the console, run:

   ```sh
   docker compose -f compose.azure-test.yaml up
   ```

   ![Console output showing running containers and application logs](docs/screenshots/docker-running-console.png)

   The console remains attached to the containers and displays their logs. Press `Ctrl+C` to stop them, or use a second terminal to stop and remove the containers and network:

   ```sh
   docker compose -f compose.azure-test.yaml down
   ```

   The local model volume is retained. Sessions and balances reset when the server restarts.

## Try it

- `Deposit ten dollars into savings.`
- `Move four dollars from savings to chequing.`
- `What are my balances?`
- `Withdraw 100 dollars from savings.`
- `Move four dollars.` — missing accounts require clarification.

Original commands remain available: `deposit <account> <amount>`, `withdraw <account> <amount>`, `transfer <from> <to> <amount>`, `balance [account]`, `help`, `?`, and `exit`. Accounts are `savings` and `chequing`; amounts must be positive with at most two decimal places.

To run the original CLI in Docker, use `docker compose run --rm banking`. For natural-language requests in the CLI, set `OPENAI_API_KEY`, `OPENAI_MODEL`, and optionally `OPENAI_BASE_URL` in your environment or local `.env` file. The base URL defaults to `https://api.openai.com/v1` and must be an OpenAI-compatible API endpoint. These CLI variables are separate from the web selector's `DIRECT_OPENAI_*` settings above.

## Checks

Run the automated checks with `docker compose run --rm banking dotnet run --project Checks`. They use a simulated API. Observed checks:

1. Deposit $10 into savings and transfer $4 to chequing: balances were $6 and $4.
2. Ask to move $4 without accounts: the app requested clarification and balances stayed unchanged.
3. Simulate a reply API failure after a $1 transfer: the original simulator result was returned and the transfer ran once.

The following 20 English requests were also tested against the hosted application using separate simulated sessions for Gemma 3 and OpenAI Luna. Confirmation was given when requested. Results describe one observed run and may vary with model output.

| # | Conversational request | Gemma 3 local | OpenAI Luna |
|---|---|---|---|
| 1 | Could you deposit 20 dollars into my savings account? | Omitted amount; asked for clarification | Correct deposit |
| 2 | Please put 15 dollars in my chequing account. | Omitted amount; asked for clarification | Correct deposit |
| 3 | Add 7 dollars to savings, please. | Correct deposit after confirmation | Correct deposit |
| 4 | Can you put 8 dollars into checking? | Correct deposit after confirmation | Correct deposit |
| 5 | I would like to deposit 4 dollars into savings. | Correct deposit after confirmation | Correct deposit |
| 6 | Please withdraw 3 dollars from savings. | Correct withdrawal after confirmation | Correct withdrawal |
| 7 | Take 2 dollars out of my chequing account. | Correct withdrawal after confirmation | Correct withdrawal |
| 8 | Can I withdraw 5 dollars from savings? | Correct withdrawal after confirmation | Correct withdrawal |
| 9 | Please take 1 dollar from my checking account. | Correct withdrawal after confirmation | Correct withdrawal |
| 10 | Withdraw 999 dollars from savings, please. | Omitted amount; asked for clarification | Declined for insufficient funds |
| 11 | Move 4 dollars from savings to chequing. | Correct interpretation; declined for insufficient funds | Correct transfer |
| 12 | Please transfer 3 dollars from chequing to savings. | Correct transfer; response contained “Transfered” | Correct transfer |
| 13 | Can you move 2 dollars from savings into checking? | Correct transfer after confirmation | Correct transfer |
| 14 | Send 1 dollar from my chequing account to my savings account. | Correct transfer; original simulator result shown | Correct transfer |
| 15 | Transfer 5 dollars to savings. | Asked for missing source account | Asked for missing source account |
| 16 | What are my balances? | Correct balances after confirmation | Correct balances |
| 17 | How much money is in my savings account? | Correct balance; terse reply `Savings=$5.00` | Correct balance |
| 18 | What is my chequing account balance? | Correct balance; reply included `Reply:` | Correct balance |
| 19 | Can you show my checking balance? | Correct balance; reply included `Reply:` | Correct balance |
| 20 | How much do I have in both accounts? | Correct balances after confirmation | Correct balances |

In case 11, the Gemma session had less money because earlier deposits were not executed. English, French, and Spanish requests were also exercised. Luna handled all 20 English requests in this run, while Gemma needed clarification or produced less conversational replies in some cases. These observations do not establish performance across all inputs or languages.

## Azure deployment

The Docker images are built locally and pushed to Azure Container Registry (ACR). Azure Container Apps runs the .NET web application and Ollama as two containers in the same replica. The environment is configured to keep at least one replica available. The web application is exposed over HTTPS, while Ollama is reached by the web application over localhost.

The OpenAI API key is stored as a Container Apps secret and referenced by the web container. The model names and service endpoints are configured as environment variables. The deployed app follows the local two-container design; local Docker volumes and Azure's temporary container storage have different persistence behavior. See [Azure deployment notes](deploy/AZURE.md) for configuration details.
