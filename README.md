# PR Cockpit

PR Cockpit helps developers understand changes in pull requests. This first slice lets you select an Azure DevOps project and repository, browse active pull requests, and open their details. See [docs/PRODUCT.md](docs/PRODUCT.md) for more product context (currently in Polish).

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
npm run build
```

Backend tests use HTTP fakes and do not require an Azure DevOps account or PAT.

## Project structure

- `backend/PRCockpit.Api` — ASP.NET Core API and Azure DevOps client
- `frontend` — Vue 3, TypeScript, and Vite
- `tests/PRCockpit.Api.Tests` — mapping and data retrieval tests
- `docs` — product context and architecture decisions

This slice does not persist data. SQLite can be added when the first feature needs durable state. The API fetches data on demand. The pull request list shows creation dates because the Azure DevOps list endpoint used here does not provide a pull request's last update date.
