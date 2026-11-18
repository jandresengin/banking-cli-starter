# banking-cli-starter

A small C# command-line banking simulator with in-memory chequing and savings accounts. Both accounts start at $0.00 each time you run the program. Original commands work directly; English requests use an OpenAI-compatible chat-completions API.

## API configuration

Set `OPENAI_API_KEY` in your environment. `OPENAI_BASE_URL` defaults to `https://api.openai.com/v1` and `OPENAI_MODEL` defaults to `gpt-6-luna`; both can be changed. The model must support chat completions and JSON Schema structured output. Availability depends on your API account. API usage may incur charges.

For Docker Compose, create a local `.env` file in the repository root:

```text
OPENAI_API_KEY=your-key-here
OPENAI_BASE_URL=https://api.openai.com/v1
OPENAI_MODEL=gpt-6-luna
```

Replace the placeholder with your own key. Compose passes these values into the container. The file is ignored by Git; do not commit or share credentials. If VS Code is already open in the dev container, rebuild or reopen it after changing the Compose configuration. For local .NET execution, set the same values as environment variables instead.

Example English inputs: `Deposit ten dollars into savings`, `Move four dollars from savings to chequing`, `What's the balance in both accounts?`, `Withdraw one hundred dollars from savings`, and `Move four dollars`.

English requests go to the API for interpretation and are validated before the simulator runs. Unclear requests are rejected without modifying balances. After execution, the API writes a conversational explanation based on the simulator's actual result. The application checks amounts, account names, and certain outcome and direction indicators; these checks cannot prove semantic equivalence. If the call fails or the response fails validation, the original simulator result is displayed without rerunning the command. Original commands do not require an API key.

## Design

`Program.cs` starts `BankingApplication`, which owns the console loop and a single `BankSession`. The original simulator remains responsible for all banking rules and balances. `CommandRouter` recognizes direct commands; `BankingAssistant` coordinates natural-language requests. `RequestInterpreter` validates the structured interpretation and creates one command, `OpenAiClient` handles HTTP calls and provider response errors, and `ReplyWriter` produces the response based on the simulator output. Separating the two API calls ensures the command is never repeated when response generation fails.

The model generates the explanation. Validation is deliberately conservative but cannot guarantee a model's wording is semantically correct. The simulator output remains available in processing details. This is a simulation, not a real banking system.

## Verification

Run `docker compose run --rm banking dotnet run --project Checks` to run the checks with a simulated API. The following were observed:

- Deposit $10 in savings, then ask to transfer $4 to chequing: the response reports the transfer; balances are $6 and $4.
- Ask to move $4 without specifying accounts: a clarification is requested; balances remain $6 and $4.
- Ask to withdraw $100 from savings: the simulator declines due to insufficient funds. A separate check simulates failure of the response-writing API and confirms that a $1 transfer runs once and returns its original result.

These checks use simulated API responses, not a live provider. A live API check is still required after configuring a key.

## How this was built

- Used OpenAI Codex to help implement the API integration, container setup, and checks. Reviewers should update this account if additional tools were used.
- Chose to execute the simulator exactly once and return its original output if the reply call fails. Checked this with a simulated API failure and verified that the transfer changed each balance only once.

## Run

### Optimized two-container demo

The separate `compose.optimized.yaml` runs a published ASP.NET application as a non-root user and Ollama on an internal Docker network. No development container or SDK is needed at runtime. The optimized web demo listens only on `http://localhost:5081` and uses a separate model volume and separate simulated sessions.

On the first run, temporarily allow Ollama to reach the model registry:

```sh
docker compose -f compose.optimized.yaml -f compose.model-setup.yaml up -d ollama
docker compose -f compose.optimized.yaml -f compose.model-setup.yaml exec ollama ollama pull gemma3:1b
docker compose -f compose.optimized.yaml up -d --build --force-recreate
```

The final command recreates Ollama without the temporary external network. Ollama publishes no host port; only containers attached to the internal network can reach it. Docker administrators can attach other containers, so this is network isolation, not authentication. Later starts only need `docker compose -f compose.optimized.yaml up -d`.

The web model selector defaults to local Gemma. To enable the optional direct OpenAI choice, set `DIRECT_OPENAI_API_KEY` in your local `.env` and optionally `DIRECT_OPENAI_MODEL` (defaults to `gpt-6-luna`), then recreate the optimized web container. The key stays server-side and `.env` must not be committed. Direct OpenAI billing is separate from Azure credits. Changing providers cancels an outstanding confirmation; balances remain in the same simulated session. The optimized Compose setup currently starts Ollama even when OpenAI is selected.

This Compose configuration is for local testing. Azure Container Apps does not directly use these Docker networks; private ingress, model provisioning, HTTPS, allowed hosts, and state behavior need an Azure-specific deployment design before public access.

### Local AI with Ollama

This branch includes a local Ollama service. The model runs on your machine; requests sent to this service are not sent to OpenAI. Docker Desktop and enough disk space and RAM for the image and model are required. The first run downloads both.

```sh
docker compose -f compose.yaml -f compose.local.yaml up -d ollama web
docker compose -f compose.yaml -f compose.local.yaml exec ollama ollama pull gemma3:1b
```

Open `http://localhost:5080` to use the local web interface. It shows the validated command and simulator result under processing details; these are observable steps, not the model's hidden reasoning. Alternatively, run the CLI with:

```sh
docker compose -f compose.yaml -f compose.local.yaml run --rm banking
```

Ollama is not exposed on a host port. The local override uses `ollama` as a non-secret placeholder; it does not read or transmit an OpenAI key. To stop these services without deleting the downloaded model, run `docker compose -f compose.yaml -f compose.local.yaml stop ollama web`.

To try another model, pull it into the Ollama container and set `OLLAMA_MODEL` in your environment before starting Compose. Smaller models may misinterpret a request; verify behavior before relying on them.

### Web interface and security

The web demo runs with `docker compose up -d web` and is bound to `127.0.0.1:5080`. The API key remains on the server and never belongs in browser code. Sessions and balances are held in memory and reset on restart. The app is a local exercise, not production banking software: it has no user authentication, durable storage, transaction audit trail, or production-grade access controls. Hosting a static page alone cannot run the .NET simulator; public deployment would require a secured server and further design.

In the web interface, direct transaction commands also receive a conversational explanation based on the simulator result; `help` and `?` show a friendly guide with examples. If the explanation service fails, the original result is displayed. The CLI preserves the original command output and works for those commands without an API key.

The account cards read balances from the same simulator session after each request. The model receives the original request and its structured interpretation and generated reply can be viewed in processing details. Account aliases and uncertain interpretations require confirmation; other unsupported or ambiguous phrases require the user to try again. Account names not present in the request or a supported alias are not used to execute a generated command. Language handling is limited and models can still misinterpret amounts or intent. In the web demo, `exit` ends the current simulated session; a new session starts at zero.

For HTTPS behind Azure ingress, `APP_PUBLIC_ORIGIN` can be set to the exact public HTTPS origin (for example `https://example.azurecontainerapps.io`). This enables that host and secure session cookies. Azure-specific ingress, secret management, access control, request limiting, and persistent session decisions must be configured and tested separately before exposing the demo.

### Docker (no local .NET SDK required)

Install and start Docker Desktop with Linux containers. From the repository root:

```sh
docker compose run --rm banking
```

Enter commands at the `bank>` prompt, for example `deposit savings 10`,
`transfer savings chequing 4`, `balance`, and `exit`. The accounts are kept
in memory and reset when the application exits.

To build without starting the application:

```sh
docker compose run --rm banking dotnet build BankingCli.sln
```

The repository is mounted into the container, so edits on your machine are
available there. A named Docker volume caches NuGet packages. To work in VS
Code, install the Dev Containers extension, open this repository, and choose
**Dev Containers: Reopen in Container**. Run `dotnet run --project BankingCli`
from its terminal.

### Local .NET SDK

With the .NET 10 SDK installed, run this from the repo root.

```sh
dotnet run --project BankingCli
```

In Visual Studio 2026 (18.0 or later), open `BankingCli.sln` and press F5. Visual Studio 2022 users can run the app from a terminal using the .NET 10 SDK.

## Commands

| Command | Effect |
| --- | --- |
| `deposit <account> <amount>` | Add money to an account. |
| `withdraw <account> <amount>` | Take money out of an account. |
| `transfer <from> <to> <amount>` | Move money between accounts. |
| `balance [account]` | Show one balance or both balances when the account is omitted. |
| `help` or `?` | Show the command list. |
| `exit` | End the session. |

Accounts are `chequing` and `savings`. Amounts are positive, with up to two decimal places, such as `5` or `5.25`. Withdrawals and transfers require sufficient funds, and transfers require two different accounts. A failed command leaves both balances as they were.

```text
bank> deposit savings 10
DEPOSIT $10.00 TO savings | savings=$10.00
bank> transfer savings chequing 4
TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00
bank> withdraw chequing 1
WITHDRAW $1.00 FROM chequing | chequing=$3.00
bank> balance
chequing=$3.00 | savings=$6.00
bank> exit
EXIT
```
