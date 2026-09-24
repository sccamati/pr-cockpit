# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
dotnet build PRCockpit.slnx
dotnet test PRCockpit.slnx
dotnet test PRCockpit.slnx --filter "FullyQualifiedName~PrContextBuilderTests"   # one class/test

dotnet run --project backend/PRCockpit.Api --launch-profile http                 # http://localhost:7180
cd frontend; npm ci; npm run dev                                                 # Vite on 7181, proxies /api
cd frontend; npm test                                                            # vitest run
cd frontend; npm test -- tests/App.test.ts -t "filters unreviewed"               # one file/test
cd frontend; npm run build                                                       # vue-tsc --noEmit + vite build
cd frontend; npm run visual:baseline                                             # screenshots BEFORE a UI change
cd frontend; npm run visual                                                      # screenshots after, pixel diff, exit 1 on change
```

Backend and frontend tests both run without Azure DevOps (HTTP fakes / mocked `src/api`). Nothing verifies real PR data — when a change touches it, say what is unverified rather than implying it passed. Layout is checked by `npm run visual`: the real `App` on fixtures (`frontend/visual/main.ts`), 15 scenarios × light/dark/700 px in headless Chrome or Edge, against a baseline you take yourself before the change (`visual/out/`, never committed — pixels are machine-specific). Under 150 px is antialiasing noise. A new screen or state needs a scenario there, and an API change needs its fixture route (unmocked calls are reported).

Secrets (`AzureDevOps:Organization`, `AzureDevOps:Pat`, `Ai:Summary:*`) come from User Secrets or `AzureDevOps__*` env vars. See [README.md](README.md).

## Architecture

`Vue 3 + Monaco → ASP.NET Core minimal API → Azure DevOps REST 7.1`.

Four backend projects with dependencies pointing inwards: `PRCockpit.Domain` (models and rules, no external technology), `PRCockpit.Application` (ports and use cases), `PRCockpit.Infrastructure` (Azure DevOps client, Roslyn, EF Core, the stores) and `PRCockpit.Api` (host, routes, the `Execute` failure mapper). This replaced the earlier deliberately flat layout at the user's explicit request, after the trade-off was put to them. `ArchitectureTests` reads the compiled assembly references and fails if a layer is crossed, so the rule is executable rather than aspirational. Still no mediator and no CQRS — the modules do not earn it. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), which is the detailed and current design record.

Load-bearing facts:

- **Everything is fetched on demand.** No sync, no cache of Azure DevOps data — with one user-approved exception: `MemorySourceSnapshotCache` holds the repository's source files per head commit SHA (max 2, 10 idle minutes, in memory) for the usage counts, because content under a SHA cannot go stale. Persisted through EF Core in a local SQL Server database (`ConnectionStrings:PrCockpit`, which must name an explicit `Initial Catalog`): the manual 6-item checklist, the generated Summary, the per-file "reviewed" markers, the reading path and the per-file AI conversation. Migrations run on start.
- **PR details carry the iteration SHAs.** `GetPullRequestAsync` resolves the last iteration's `commonRefCommit`/`sourceRefCommit` once; diffs, `/context` and Summary all derive from those, and the file list from that iteration is the allowlist for which paths a diff may be served for.
- **Bounded content, never truncated.** Per-version 256 KB and 4000-line diff limits (`TextLineLimit`); `PrContextBuilder` adds 20 000 chars/file and 100 000 chars/PR for Summary input. Over budget → the file keeps its path, change type and an `omissionReason`; text is dropped whole, never cut. Keep this property when editing budgets.
- **One AI adapter, three tasks** (`summary`, `file`, `ask`) — the whole-PR Summary on `POST .../summary`, one file on `POST .../summary/file`, and a question about the open file on `POST .../summary/ask`. The backend spawns the configured executable without a shell, writes one JSON request to stdin, reads one JSON object from stdout, and `SummaryRunner` validates schema version, sentence count and length before anything is stored or shown. PR text, commit titles and code are data in a `context` field, separate from the fixed `instruction` — never fold untrusted text into the instruction, and never hand the adapter a local repo path. `scripts/summary-adapter.mjs` is a working reference adapter, and it needs no change for a new task: it forwards only `instruction` and `context`.
- **The PAT never leaves the backend.** The frontend talks only to `/api`.
- **Stale-response guards are a real requirement.** `App.vue` and each composable track request ids and drop late responses after a project/repo/PR switch; `resetPullRequest()` in `App.vue` bumps them all. Several tests exist for this. New async loads need the same guard.
- Failure handling is centralized in the `Execute` helper in `Program.cs` — map new failure kinds there rather than try/catch per endpoint.

Frontend: `App.vue` is the shell — it creates every feature once, owns what crosses features (`resetPullRequest()`, stepping through files, the shortcut map) and hands the features to the child components through `cockpit.ts` provide/inject. Each feature is a composable (`usePullRequests`, `useDiff` — the open file, `useFileList` — the tree's filters and order, `useSummary`, `useChecklist`, `useReviewProgress`, `useWalkthrough`, `useComments`, `useFileAi`) next to the components that render it (`PullRequestPicker`, `PullRequestList`, `PrHeader`, `FileListPane`, `DiffPanel`, `ContextRail`, …). Features that need each other both ways are linked by getters (`() => comments.leaveFile()`) resolved after all of them exist. `src/features/<feature>/` holds each feature's components next to its composable; pure helpers without Vue in `src/lib/` (`format.ts`, `description.ts`); the root keeps the shell, the typed `api.ts` and `cockpit.ts`. Imports outside the own folder use the `@/` alias. Styles are `<style scoped>` in the component; `style.css` holds only tokens, element defaults, building blocks shared by several components and the `v-html` Markdown. Scoped rules outrank global ones by one attribute, so a selector reaching into a child component or `v-html` needs `:deep()`, and nodes Monaco creates are styled in a plain `<style>` in `MonacoDiff.vue`. Tests mount `App` and go through the DOM, so a split does not need test changes as long as markup stays. Monaco loads only after a text file is picked and is disposed with both models on file change or leaving the PR. C# hovers and semantic colors come from `POST /api/csharp/hovers` (Roslyn over the two diff texts only — no project graph, so unresolved symbols are expected and silently skipped). Usage counts are the exception: `POST .../usages` runs Roslyn `SymbolFinder` over the whole repository snapshot for C# and name matching (`mode: "name"`, shown as "~") for TS/JS/Vue; `MonacoDiff` shows them as CodeLens, which needs `diffCodeLens: true`.

## Conventions

- Code, identifiers and commit messages in English; UI text, `docs/` and `PRODUCT.md` in Polish.
- [docs/BACKLOG.md](docs/BACKLOG.md) is the working task list and records verification results per item. Update the relevant task status and `docs/ARCHITECTURE.md` in the same change as the code — the repo relies on them being accurate.
- `// ponytail:` comments mark deliberate simplifications and their ceiling. Preserve them; add one when taking a shortcut.
- A `.codegraph/` index exists — `codegraph_explore` with `projectPath` works here.
