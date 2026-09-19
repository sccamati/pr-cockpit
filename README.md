# PR Cockpit

PR Cockpit helps developers understand changes in pull requests. The current slice lets you select an Azure DevOps project and repository, browse active pull requests, and review file diffs. See [PRODUCT.md](PRODUCT.md) for product context and [docs/BACKLOG.md](docs/BACKLOG.md) for the next tasks (both in Polish).

## Requirements

- .NET SDK 10
- Node.js 24 and npm
- Access to Azure DevOps Services and a short-lived PAT with **Code (Read)** and **Project and team (Read)** scopes. The current app reads linked work item IDs through the Git API, so it does not need **Work items (Read)**.

## Azure DevOps configuration

Store the organization name and PAT locally with .NET User Secrets (outside the repository):

```powershell
dotnet user-secrets set "AzureDevOps:Organization" "your-organization" --project backend/PRCockpit.Api
dotnet user-secrets set "AzureDevOps:Pat" "YOUR_PAT" --project backend/PRCockpit.Api
```

`Organization` is the name from `https://dev.azure.com/<organization>`, not the full URL. Alternatively, set the `AzureDevOps__Organization` and `AzureDevOps__Pat` environment variables (listed in [.env.example](.env.example)). The `.env` file is ignored by Git and is not loaded automatically. Do not put the PAT in `appsettings.json` or the frontend.

Only the local backend uses the PAT. This setup is for development; sharing the app with other users will require Microsoft Entra sign-in, API authorization, and per-user token handling.

## Summary CLI adapter

`Summary` runs only when you click **Generuj Summary** on a pull request. Configure a trusted CLI program through .NET User Secrets or environment variables:

```powershell
dotnet user-secrets set "Ai:Summary:Executable" "C:\path\to\summary-adapter.exe" --project backend/PRCockpit.Api
dotnet user-secrets set "Ai:Summary:Model" "your-model-id" --project backend/PRCockpit.Api
```

Optional settings are `Ai:Summary:Arguments:0`, `Ai:Summary:Arguments:1`, etc. (each is a separate literal argument) and `Ai:Summary:TimeoutSeconds` (default 120, allowed 1–300). The executable is started directly, without a shell, in the backend's application directory. Its path and arguments must come from trusted backend configuration. The adapter itself is responsible for invoking the chosen model; the backend never sends it a repository path or grants it repository tools.

The program receives one JSON object on standard input with `task: "summary"`, `schemaVersion: 1`, the configured `model`, a fixed `instruction`, and the bounded `context` from Azure DevOps. It must write **only** a JSON object to standard output:

```json
{"schemaVersion":1,"sentences":["Pierwsze zdanie.","Drugie zdanie."]}
```

The response needs 2–5 nonempty sentences, each at most 500 characters. Put diagnostics on standard error, keep secrets out of output, and treat PR descriptions, commit titles and code as data rather than commands. Without a configured executable, the Summary action returns a configuration error. Results are validated and then stored in the same local SQLite file as the checklist, so reopening a pull request shows the saved Summary without running the model again. The UI displays how many diffs were included and which files were omitted. A real model and Azure DevOps connection are needed to verify summary quality.

## PR checklist storage

Each pull request has six manual checklist items. The backend saves them in SQLite at `%LOCALAPPDATA%\PRCockpit\checklist.db` on Windows. Set `Checklist:DatabasePath` (or `Checklist__DatabasePath`) to use another local file. The key includes the Azure DevOps organization, project, repository ID and PR ID. Summary results, the per-file "reviewed" markers and the reading path are stored in the same file. A marker records the blob id of the file it was set on, so an unrelated commit does not clear it; the marker is only reported as out of date when that file's content actually changed.

## Run locally

From the repository root, use two terminals. Start the backend in the first:

```powershell
dotnet restore PRCockpit.slnx
dotnet run --project backend/PRCockpit.Api --launch-profile http
```

Start the frontend in the second:

```powershell
cd frontend
npm ci
npm run dev
```

Open the URL shown by Vite (usually `http://localhost:5173`). Vite proxies `/api` requests to the backend at `http://localhost:5164`. The health endpoint is `http://localhost:5164/api/health`.

## Build and test

```powershell
dotnet build PRCockpit.slnx
dotnet test PRCockpit.slnx
cd frontend
npm ci
npm test
npm run build
```

Backend tests use HTTP fakes and do not require an Azure DevOps account or PAT. Frontend interaction tests use a simulated DOM and mocked API responses; they do not replace checking a real pull request or the layout in a browser.

## Project structure

- `backend/PRCockpit.Api` — ASP.NET Core API and Azure DevOps client
- `frontend` — Vue 3, TypeScript, and Vite
- `tests/PRCockpit.Api.Tests` — mapping and data retrieval tests
- `docs` — product context and architecture decisions

Only the checklist is persisted locally. Azure DevOps data is fetched on demand. The pull request list shows creation dates because the Azure DevOps list endpoint used here does not provide a pull request's last update date.
