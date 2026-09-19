# Architektura

```text
Vue 3 / Vite → ASP.NET Core API → Azure DevOps REST API
```

Frontend wybiera projekt i repozytorium, wyświetla aktywne PR-y i szczegóły. W developmentcie Vite przekazuje żądania `/api` do backendu. Backend jest pojedynczą aplikacją; `AzureDevOpsClient` izoluje wywołania HTTP, a `AzureDevOpsMapper` mapuje odpowiedzi na prosty kontrakt API. To wystarcza na obecny zakres bez dodatkowych projektów domenowych czy wzorców pośredniczących.

Backend czyta organizację i PAT z konfiguracji .NET (User Secrets lub zmienne środowiskowe). Frontend nie otrzymuje PAT. Integracja używa Azure DevOps REST API 7.1 i pobiera dane na żądanie. Dla szczegółów PR pobiera ostatnią iterację, liczbę zmienionych plików, commity i powiązane Work Items. Liczenie plików korzysta z porównania ostatniej iteracji z bazą PR i obsługuje strony zmian.

Nie ma jeszcze bazy danych: w tym vertical slice nie ma stanu do zapisu. SQLite można dodać przy checklistach lub zapisanych analizach. Planowane obszary to `Analysis`, `Checklist` i `ProjectKnowledge`, ale nie istnieją jeszcze w kodzie.

PAT jest rozwiązaniem lokalnym, krótkoterminowym. Przed udostępnieniem aplikacji poza własną maszynę trzeba dodać Microsoft Entra i autoryzację API. Obecny profil uruchomieniowy nasłuchuje na localhost. Testy klienta używają atrapy `HttpMessageHandler`.
