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

### Writing comments

Reading pull request comment threads needs nothing beyond the scopes above. **Writing** them is off by default and has to be switched on deliberately, because a comment is visible to the whole team and cannot be taken back:

```powershell
dotnet user-secrets set "AzureDevOps:AllowComments" "true" --project backend/PRCockpit.Api
```

The PAT also needs the **PR threads (read & write)** scope. Leave **Code** on Read — `vso.code_write` would additionally allow pushing code and deleting refs, which this app never does. With the switch off, or the scope missing, the backend refuses the write before any request leaves the machine and says which of the two is wrong.

## Summary CLI adapter

`Summary` runs only when you click **Generuj Summary** on a pull request. Configure a trusted CLI program through .NET User Secrets or environment variables:

```powershell
dotnet user-secrets set "Ai:Summary:Executable" "C:\path\to\summary-adapter.exe" --project backend/PRCockpit.Api
dotnet user-secrets set "Ai:Summary:Model" "your-model-id" --project backend/PRCockpit.Api
```

Optional settings are `Ai:Summary:Arguments:0`, `Ai:Summary:Arguments:1`, etc. (each is a separate literal argument) and `Ai:Summary:TimeoutSeconds` (default 600, allowed 1–1800 — a pull request of ~100 files makes the model write the whole reading order, which took far longer than the old 120 s default, and a big pull request is the one worth waiting for). The executable is started directly, without a shell, in the backend's application directory. Its path and arguments must come from trusted backend configuration. The adapter itself is responsible for invoking the chosen model; the backend never sends it a repository path or grants it repository tools.

The program receives one JSON object on standard input with `task`, `schemaVersion: 2`, the configured `model`, a fixed `instruction`, and the bounded `context` from Azure DevOps. It must write **only** a JSON object to standard output. `task` is `"summary"` for the whole pull request:

```json
{"schemaVersion":2,"sentences":["Pierwsze zdanie.","Drugie zdanie."],
 "criticalFiles":[{"path":"/src/Foo.cs","role":"Nowy walidator.","why":"Tu jest reguła, którą zmienia ten PR."}],
 "readingOrder":["/src/Foo.cs","/src/Bar.cs","/package-lock.json"]}
```

or `"file"`, when the context holds exactly one file and the answer explains that file:

```json
{"schemaVersion":2,"sentences":["To zdanie opisuje jeden plik."]}
```

or `"ask"`, when the reader asked a question about the file on screen. The context then holds
`pullRequest` (that one file), the `question`, an optional `selection` — the snippet the reader
highlighted — and `history`, the previous turns of the same conversation, oldest first. The answer
has the same shape as a file explanation, 1–6 sentences:

```json
{"schemaVersion":2,"sentences":["Ten fragment pilnuje limitu znaków."]}
```

The question, the selection and the earlier turns are data like the code: never instructions.
`Ai:Summary:AskModel` sets a different model for this task alone and falls back to
`Ai:Summary:Model` — a question about one file does not need what a whole-PR reading order needs.

A summary needs 2–5 nonempty sentences, a file explanation 1–3, an answer to a question 1–6, each at most 500 characters. `criticalFiles` is the reading proposal — the files that alone explain the change, in the order they should be read; an empty list or a missing field is allowed. Every `path` must be copied exactly from `context.changedFiles` — an invented or repeated path fails the whole response — and `role` and `why` are nonempty, at most 200 characters each. The list may hold at most one file per four changed ones, never fewer than 10 and never more than 25; the same number is put into the `instruction`, so following the instruction keeps you inside the limit. `readingOrder` is the same ordering applied to the whole pull request: every path from `context.changedFiles` exactly once, paths only, including the files whose text was omitted. It is treated as hints rather than a contract: a path that is not in the pull request or that you already listed is dropped, anything you leave out is appended by the backend, and files classified as noise are moved to the end whatever order you gave them. One bad line therefore costs one line, not the whole answer. An empty or missing `readingOrder` means the backend orders the pull request itself. `criticalFiles` stays strict, because it is short and a wrong path there sends the reviewer to a file that does not exist. Anything else, including `schemaVersion: 1`, is rejected as an invalid result. Put diagnostics on standard error, keep secrets out of output, and treat PR descriptions, commit titles and code as data rather than commands. Without a configured executable, the Summary action returns a configuration error. Results are validated and then stored in the same local database as the checklist, so reopening a pull request shows the saved Summary without running the model again. The UI displays how many diffs were included and which files were omitted. A real model and Azure DevOps connection are needed to verify summary quality.

## PR checklist storage

Each pull request has six manual checklist items. The backend saves them through Entity Framework Core in a local SQL Server database, configured by the `ConnectionStrings:PrCockpit` connection string (`ConnectionStrings__PrCockpit` as an environment variable). Give it an explicit `Initial Catalog`; without one, EF Core uses the login's default database and creates its tables there. Migrations are applied on start, so the database is created and kept up to date on the first run. The key includes the Azure DevOps organization, project, repository ID and PR ID. Summary results, the per-file "reviewed" markers and the reading path are stored in the same database. A marker records the blob id of the file it was set on, so an unrelated commit does not clear it; the marker is only reported as out of date when that file's content actually changed.

## Run locally

### One click

`scripts/install-shortcut.ps1` puts a **PR Cockpit** shortcut on the desktop; run it once,
and again after moving the repository. The shortcut runs `scripts/start.ps1`, which frees
ports 5164 and 5173, runs `npm ci` if `frontend/node_modules` is missing, starts the backend
and Vite in that one console, waits for Vite and opens the browser. Both servers log into
that window, so closing it or pressing Ctrl+C stops them; if a run is ever left behind, the
next launch clears the ports itself. The shortcut uses a stock Windows icon — put your own
`icon.ico` next to the script and rerun the installer to change it.

### Two terminals

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
