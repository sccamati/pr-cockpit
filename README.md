# PR Cockpit

PR Cockpit pomaga developerowi zrozumieć zmiany w Pull Requestach. Ten pierwszy etap pozwala wybrać projekt i repozytorium Azure DevOps, zobaczyć aktywne PR-y oraz otworzyć ich szczegóły. Szerszy kontekst produktu jest w [docs/PRODUCT.md](docs/PRODUCT.md).

## Wymagania

- .NET SDK 10
- Node.js 24 i npm
- Dostęp do Azure DevOps Services i krótko ważny PAT z uprawnieniami odczytu do projektów, kodu i powiązanych Work Items

## Konfiguracja Azure DevOps

Najwygodniej przechowywać dane lokalnie w .NET User Secrets (poza repozytorium):

```powershell
dotnet user-secrets set "AzureDevOps:Organization" "nazwa-organizacji" --project backend/PRCockpit.Api
dotnet user-secrets set "AzureDevOps:Pat" "TWÓJ_PAT" --project backend/PRCockpit.Api
```

`Organization` to sama nazwa z `https://dev.azure.com/<organization>`, bez adresu URL. Alternatywnie ustaw zmienne środowiskowe `AzureDevOps__Organization` i `AzureDevOps__Pat` (nazwy w [.env.example](.env.example)). Plik `.env` nie jest ładowany automatycznie i jest ignorowany przez Git. Nie wpisuj PAT do `appsettings.json` ani do frontendu.

PAT jest używany tylko przez lokalny backend. To wariant do developmentu; przed udostępnieniem aplikacji innym użytkownikom potrzebne będą logowanie Microsoft Entra, autoryzacja endpointów i obsługa tokenów użytkowników.

## Uruchomienie

W dwóch terminalach, z katalogu głównego:

```powershell
dotnet restore PRCockpit.slnx
dotnet run --project backend/PRCockpit.Api --launch-profile http
```

```powershell
cd frontend
npm ci
npm run dev
```

Otwórz adres podany przez Vite (zwykle `http://localhost:5173`). Vite przekazuje `/api` do backendu na `http://localhost:5164`. Endpoint kontrolny: `http://localhost:5164/api/health`.

## Budowanie i testy

```powershell
dotnet build PRCockpit.slnx
dotnet test PRCockpit.slnx
cd frontend
npm ci
npm run build
```

Testy backendu korzystają z atrap HTTP; nie wymagają konta Azure DevOps ani PAT.

## Struktura

- `backend/PRCockpit.Api` — API ASP.NET Core i klient Azure DevOps
- `frontend` — Vue 3, TypeScript i Vite
- `tests/PRCockpit.Api.Tests` — testy mapowania i pobierania danych
- `docs` — kontekst produktu i decyzje architektoniczne

Obecny zakres nie zapisuje danych, dlatego SQLite pojawi się wraz z pierwszą funkcją wymagającą trwałego stanu. API pobiera dane na żądanie. Lista pokazuje datę utworzenia PR; Azure DevOps nie zwraca pola ostatniej aktualizacji PR w użytym endpointcie listy.
