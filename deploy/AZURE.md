# Azure Container Apps: one replica, two containers

This is a demo, not a production banking service. One Container App runs the
.NET web application and an Ollama sidecar in the same replica. The public
HTTPS ingress exposes only port 8080; Ollama listens on `127.0.0.1:11434`.
Gemma runs on CPU and is downloaded into temporary container storage. The application
stores simulated balances in memory, so restarts erase them. OpenAI is optional.

This guide describes the intended configuration; verify your region, quota,
image architecture and pricing before deployment. Portal labels can change.
Keep the minimum and maximum replica counts at 1, use single revision mode,
and do not expose an additional Ollama ingress port.

## Local approximation

With Docker Desktop running, from the repository root:

```powershell
docker compose -f compose.azure-test.yaml up --build -d
docker compose -f compose.azure-test.yaml logs -f ollama
```

The first download can take several minutes. When the log says the model is
available, open `http://localhost:5082`. Test a deposit, a transfer, a balance
check and an unclear request. Stop with `docker compose -f compose.azure-test.yaml down`.
The model volume remains for the next run. The web app can start before the model
is ready; requests to Ollama will fail safely during the initial download.

## Images

Create an Azure Container Registry (ACR) in the portal, in the same region as
the planned application. Sign in with Azure CLI and Docker, then build and push
both images from the repository root. Replace the registry placeholder with the
registry's **Login server**, without `https://` (for example,
`myuniqueregistry.azurecr.io`):

```powershell
az login
az account set --subscription "YOUR_SUBSCRIPTION_ID"
az acr login --name YOUR_REGISTRY_NAME
docker build --platform linux/amd64 -f Dockerfile.runtime -t YOUR_REGISTRY.azurecr.io/banking-web:demo .
docker build --platform linux/amd64 -f deploy/ollama/Dockerfile -t YOUR_REGISTRY.azurecr.io/banking-ollama:demo .
docker push YOUR_REGISTRY.azurecr.io/banking-web:demo
docker push YOUR_REGISTRY.azurecr.io/banking-ollama:demo
```

Use the registry's real login server in all four image references. The model
is downloaded on first start and is not baked into the image. `.dockerignore`
excludes `.env`; never put credentials in an image or in Git. The image tag
must point to the build you actually tested.

## Portal setup

1. Create a resource group and a Container Apps environment in one region.
   Use a workload-profiles environment with the Consumption profile. Confirm
   quota and review pricing in the Azure calculator. One replica runs continuously.
2. No Azure Files mount is required. The model can be downloaded again when the
   container is replaced and temporary storage is lost.
3. Create one Container App in that environment. Configure a system-assigned
   managed identity and grant it `AcrPull` on the ACR; select that identity for
   registry authentication. Depending on portal order, create the app first,
   then grant the role and create a new revision using the private images.
4. Configure **single revision** mode, min replicas **1**, max replicas **1**.
   Set external HTTP ingress to target port **8080** and disable insecure HTTP.
   Do not configure any additional exposed port. If HTTP health probes are added,
   account for host validation; TCP probes on port 8080 avoid an unrecognized host.
5. Configure the web container from `banking-web:demo`, with **0.5 vCPU / 1 GiB**:

   | Variable | Value |
   | --- | --- |
   | `OPENAI_BASE_URL` | `http://127.0.0.1:11434/v1` |
   | `OPENAI_API_KEY` | `ollama` (placeholder, not a credential) |
   | `OPENAI_MODEL` | `gemma3:1b` |
   | `OPENAI_TEMPERATURE` | `0` |

6. Add the Ollama container from `banking-ollama:demo`, with **1.5 vCPU / 3 GiB**.
   Set `OLLAMA_MODEL=gemma3:1b` and `OLLAMA_HOST=127.0.0.1:11434`.
   Both containers together use 2 vCPU and 4 GiB. They share a network namespace;
   no port for Ollama needs to be exposed. The first start needs outbound access
   to Ollama's model registry. Inspect Ollama logs until the model is available.
7. Copy the app's generated HTTPS Application URL. Add a web-container variable
   `APP_PUBLIC_ORIGIN` equal to that origin, e.g.
   `https://bank-demo.example.region.azurecontainerapps.io` (no trailing path).
   Save and deploy a new revision. Before this setting, external requests are
   rejected by the application's host check. Do not invent the domain.
8. Optional OpenAI: under the Container App's **Secrets**, add a secret containing
   the API key. Add `DIRECT_OPENAI_API_KEY` to the web container as a reference
   to that secret, and set `DIRECT_OPENAI_MODEL` to an available model in your
   OpenAI account. Never paste the key in source, images, logs or screenshots.
   Without it, the local model remains usable. Save a new revision.
9. Open the generated HTTPS URL and test both available providers: deposit,
   successful transfer, balance, missing transfer account, and insufficient
   funds. Check the processing details and confirm the displayed balances. Restart
   the replica and verify that balances reset; allow time for a model download
   if the container's temporary files are lost. Review logs without secrets.

During deployment a revision can briefly overlap its replacement even with a
maximum of one replica per revision. There is no high availability guarantee.
The public demo currently has no user authentication or application-level rate
limit. Anyone who finds the URL can invoke it and potentially spend OpenAI
credits; restrict ingress or add access control and rate limits before enabling
the OpenAI key on a public deployment. Budgets and alerts are not a hard spending
cap. Monitor Container Apps, file storage, registry and log costs, and delete the
resource group when the demo ends.
