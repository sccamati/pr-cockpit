# PR Cockpit

## 1. Problem

Przy intensywnym korzystaniu z AI kod powstaje szybciej, niż developer jest w stanie go dokładnie przeczytać i zrozumieć.

Obecny problem nie polega głównie na jakości kodu — PR-y mogą przechodzić testy i AI review.

Problemem jest **utrata kontekstu aplikacji**:

* duże PR-y zawierają dziesiątki lub setki plików,
* developer bardziej „przegląda” diff niż go rozumie,
* po merge nie zawsze wiadomo, gdzie znajduje się konkretna logika,
* po kilku tygodniach trudno odtworzyć, dlaczego system działa w określony sposób,
* AI review wskazuje błędy, ale nie buduje modelu mentalnego aplikacji.

### Problem do rozwiązania

> Jak pozwolić developerowi korzystać z szybkości AI, jednocześnie zachowując kontrolę i zrozumienie rozwijanego systemu?

---

# 2. Cel produktu

PR Cockpit ma pomagać developerowi odpowiedzieć po każdym PR:

* Co się zmieniło?
* Dlaczego?
* Jak wygląda główny flow?
* Które pliki są naprawdę ważne?
* Jakie elementy systemu zostały dotknięte?
* Czy są potencjalne problemy jakościowe?
* Gdzie zacząć debugowanie?
* Czy zmiana wpływa na architekturę?

### Główna zasada

**Nie zastępujemy code review.**

Istniejące narzędzia i agenci mogą nadal szukać błędów.

PR Cockpit odpowiada przede wszystkim za:

> **comprehension + project context**

---

# 3. Użytkownik

Pierwsza wersja jest narzędziem osobistym dla developera pracującego z:

* Azure DevOps,
* dużą liczbą PR,
* AI-generated code,
* kilkoma projektami/repozytoriami.

Na początku nie projektujemy systemu multi-tenant ani produktu SaaS.

---

# 4. Główna wartość

Zamiast:

```text
PR
↓
83 files changed
↓
scrollowanie diffu
↓
approve
```

otrzymujemy:

```text
PR
↓
co się zmieniło?
↓
jak wygląda flow?
↓
które 5–10 plików muszę przeczytać?
↓
co jest ryzykowne?
↓
rozumiem zmianę
↓
merge
```

---

# 5. Workflow użytkownika

## Dashboard

Użytkownik widzi swoje aktywne PR-y.

Przykład:

```text
PR #1842   KSeF integration       4/6
PR #1845   Customer details       6/6
PR #1848   Authorization          2/6
```

Kliknięcie otwiera PR Cockpit.

---

## PR Overview

Pokazujemy:

* tytuł,
* autora,
* branch,
* liczbę zmienionych plików,
* liczbę zmian,
* status,
* powiązany Work Item,
* krótki opis zmiany.

Dodatkowo analiza:

```text
Affected areas

Billing
Database
KSeF
Background processing
```

oraz:

```text
Risk

LOW / MEDIUM / HIGH
```

Risk nie blokuje merge — pomaga jedynie określić, gdzie warto poświęcić więcej uwagi.

---

# 6. Checklista PR

Każdy PR ma prostą checklistę:

* [ ] AI Review
* [ ] Quality
* [ ] Understand
* [ ] Architecture
* [ ] Debug
* [ ] Ready

Status checklisty zapisujemy w aplikacji.

---

# 7. Understand PR

Najważniejsza funkcja MVP.

AI analizuje PR i repozytorium.

Generuje:

## Summary

2–5 zdań opisujących faktyczną zmianę.

## Main flow

Przykład:

```text
InvoicePage
    ↓
POST /invoice/{id}/send
    ↓
SendInvoiceEndpoint
    ↓
SendInvoiceHandler
    ↓
Invoice.MarkForSending()
    ↓
Outbox
    ↓
KsefWorker
    ↓
KSeF API
```

## Critical files

AI wybiera maksymalnie około 5–10 najważniejszych plików.

Dla każdego:

```text
SendInvoiceEndpoint.cs

Rola:
Entry point dla wysłania faktury.

Dlaczego warto przeczytać:
Pokazuje rozpoczęcie całego flow.
```

Pozostałe pliki mogą być sklasyfikowane jako:

* tests,
* DTO,
* mappings,
* configuration,
* generated,
* infrastructure,
* secondary changes.

Celem nie jest przeczytanie całego PR.

Celem jest znalezienie **minimalnego zestawu kodu potrzebnego do zrozumienia zmiany**.

---

# 8. Quality Review

Quality Review jest oddzielne od klasycznego AI Code Review.

Szukamy przede wszystkim kodu, który:

> działa, ale może być zrobiony wyraźnie lepiej.

Kategorie:

* Database queries
* N+1
* nadmierne pobieranie danych
* brak projekcji
* brak sensownego indeksu
* zbędne round-trip'y
* performance
* allocations
* async
* concurrency
* idempotency
* transactions
* security
* authorization
* niepotrzebna złożoność

Każdy finding:

```text
Database

CustomerRepository.cs

Potential issue:
Filtering happens after materialization.

Impact:
Possible unnecessary data transfer.

[Explain]
[Accepted]
[Resolved]
[Not relevant]
```

Quality Review nie powinien generować dziesiątek kosmetycznych uwag.

Priorytet:

**mało uwag, wysoka wartość.**

---

# 9. Debug Check

Prosty test:

> Czy wiem, gdzie zacząłbym szukać, gdyby ten feature nie działał?

Aplikacja generuje 1–3 przykładowe scenariusze.

Przykład:

```text
Invoice remains in Sending state.

Where would you start debugging?
```

Użytkownik może:

* odpowiedzieć,
* wybrać komponent,
* kliknąć `Show me`.

Nie budujemy systemu egzaminowania developera.

Funkcja ma wymusić kilka sekund aktywnego myślenia.

---

# 10. Architecture Check

Po analizie PR aplikacja określa:

```text
Architecture impact detected
```

lub:

```text
No architecture impact
```

Jeżeli wykryto zmianę:

```text
Billing
   ↓
KsefSender
   ↓
KSeF API
```

Użytkownik wybiera:

* Accept
* Edit
* Ignore

AI nigdy nie aktualizuje automatycznie wiedzy architektonicznej jako faktu bez akceptacji użytkownika.

---

# 11. Project Memory

Nie jest wymagane do pierwszego działającego MVP, ale model danych powinien umożliwić późniejsze dodanie tej funkcji.

Docelowo aplikacja przechowuje zaakceptowaną wiedzę:

```text
Project
├── Components
├── Modules
├── Integrations
├── Critical flows
├── Hotspots
└── PR history
```

Dzięki temu można później zapytać:

```text
Jak działa wysyłanie faktury?

Co ostatnio zmieniło się w Authz?

Które PR-y zmieniały CRM Sync?

Jakie są najważniejsze flow Billingu?
```

---

# 12. Integracja Azure DevOps

Azure DevOps jest głównym źródłem danych.

Potrzebujemy:

* projekty,
* repositories,
* pull requests,
* PR details,
* changed files,
* commits,
* diff,
* reviewers,
* comments,
* linked Work Items.

Nie synchronizujemy na początku całego Azure DevOps.

Pobieramy dane **on demand**.

---

# 13. AI

## Założenie MVP

Brak Claude API nie blokuje projektu.

Pierwsza wersja może działać lokalnie.

```text
PR Cockpit
    ↓
Local backend
    ↓
local repository
    ↓
AI CLI
```

AI musi mieć możliwość:

* przeczytania diffu,
* przeszukania repo,
* znalezienia zależności,
* przeczytania dodatkowych plików,
* zwrócenia wyniku w ustrukturyzowanym JSON.

### Ważne

Codex będzie wykorzystywany do **implementacji PR Cockpit**.

Nie oznacza to automatycznie, że Codex musi być modelem używanym przez sam PR Cockpit.

Runtime AI traktujemy jako wymienny adapter:

```text
IAiAnalyzer
```

Dzięki temu później może istnieć:

```text
ClaudeCodeAnalyzer
CodexAnalyzer
OpenAiApiAnalyzer
OtherAnalyzer
```

---

# 14. Proponowana architektura

```text
Vue
PR Cockpit UI
      │
      ▼
.NET API
      │
      ├── AzureDevOps
      │
      ├── PullRequests
      │
      ├── Analysis
      │
      ├── Checklist
      │
      └── ProjectKnowledge
                │
                ▼
           AI Adapter
                │
                ▼
            Local Repo
```

Baza:

```text
SQLite
```

w MVP.

Nie potrzebujemy na początku:

* Azure SQL,
* Redis,
* Service Bus,
* Kubernetes,
* mikroserwisów.

---

# 15. Minimalny model danych

## Project

```text
Id
AzureDevOpsProjectId
Name
```

## Repository

```text
Id
ProjectId
AzureDevOpsRepositoryId
Name
LocalPath
```

## PullRequest

```text
Id
RepositoryId
AzureDevOpsPullRequestId
Title
SourceBranch
TargetBranch
Status
LastSyncAt
```

## PullRequestAnalysis

```text
PullRequestId
Summary
MainFlow
Risk
AffectedAreas
CreatedAt
```

## CriticalFile

```text
AnalysisId
Path
Role
Reason
Order
```

## QualityFinding

```text
AnalysisId
Category
File
Description
Impact
Status
```

## Checklist

```text
PullRequestId

AiReview
Quality
Understand
Architecture
Debug
Ready
```

---

# 16. MVP

Pierwsza wersja musi obsługiwać tylko:

Stan: działa pierwszy vertical slice (Azure DevOps → PR → lista plików → diff w Monaco), ale nie całe MVP z tej sekcji. `[x]` oznacza zaimplementowaną funkcję. Użytkownik potwierdził działanie listy PR, widoku szczegółów, opisu, listy plików i diffu na rzeczywistej organizacji Azure DevOps. Lista commitów i nowe sterowanie review przeszły testy interakcji z atrapą API, lecz czekają na potwierdzenie na rzeczywistym PR i w wąskim układzie; szczegóły są w [docs/BACKLOG.md](docs/BACKLOG.md).

### Azure DevOps

* [x] konfiguracja organizacji (User Secrets lub zmienne środowiskowe)
* [x] wybór projektu
* [x] wybór repository
* [x] lista aktywnych PR
* [x] szczegóły PR
* [x] changed files (lista ścieżek i typów zmian)
* [x] diff jednego wybranego pliku tekstowego na żądanie w Monaco Diff Editor, tylko do odczytu, z kolorowaniem składni (pliki binarne i zbyt duże pokazują komunikat; użytkownik potwierdził działanie diffu na prawdziwym Azure DevOps)

### Local repository

* [ ] konfiguracja lokalnej ścieżki repo
* [ ] sprawdzenie poprawności repo
* [ ] checkout odpowiedniego brancha lub worktree

### AI

* [ ] uruchomienie analizy
* [ ] structured JSON result
* [ ] summary
* [ ] main flow
* [ ] critical files
* [ ] affected areas
* [ ] quality findings

### UI

* [x] Dashboard PR (lista aktywnych PR pokazuje postęp zapisanej checklisty jako `x/6`)
* [ ] PR Overview (podstawowe szczegóły są; brak affected areas i risk z sekcji 5)
* [ ] Understand
* [ ] Quality
* [x] Checklist — sześć ręcznych pól w widoku PR

### Persistence

* [x] zapis checklist w lokalnym SQLite
* [ ] zapis analizy
* [ ] ponowne otwarcie wcześniej przeanalizowanego PR

---

# 17. Poza MVP

Na początku NIE robimy:

* automatycznych Azure DevOps comments,
* automatycznych approvals,
* zespołów i użytkowników,
* SaaS,
* multi-tenancy,
* billing,
* vector database,
* embeddings,
* pełnego RAG,
* automatycznego generowania diagramów całego repo,
* statycznej analizy całej organizacji,
* real-time Service Hooks,
* rozbudowanych statystyk,
* IDE extension.

Najpierw potwierdzamy, że podstawowy workflow daje wartość.

---

# 18. Backlog implementacyjny

Kolejność najbliższych, małych zadań i warunki ich zakończenia są w [docs/BACKLOG.md](docs/BACKLOG.md). Poniższe epiki opisują szerszy plan produktu.

## EPIC 1 — Foundation

* [x] Utworzyć solution .NET
* [x] Utworzyć Vue frontend
* [x] Dodać SQLite dla checklisty
* [x] Przygotować podstawowy layout
* [x] Przygotować konfigurację aplikacji

---

## EPIC 2 — Azure DevOps

* [x] Klient Azure DevOps
* [x] Pobieranie projektów
* [x] Pobieranie repositories
* [x] Pobieranie aktywnych PR
* [x] Pobieranie szczegółów PR
* [x] Pobieranie changed files
* [x] Pobieranie diffu jednego pliku na żądanie (użytkownik potwierdził podstawowe działanie na prawdziwym Azure DevOps)
* [x] Pobieranie listy commitów PR z podstawowymi metadanymi i stronicowaniem
* [x] Pobieranie linked Work Item (powiązanie i ID; bez pełnych danych Work Item)

### Wynik

Można otworzyć PR z Azure DevOps w PR Cockpit.

---

## EPIC 3 — Local repository

* [ ] Przypisanie repo ADO → lokalny folder
* [ ] Walidacja Git repo
* [ ] Pobranie aktualnego brancha
* [ ] możliwość analizy plików
* [ ] bezpieczne przygotowanie wersji kodu odpowiadającej PR

### Wynik

Backend ma dostęp zarówno do diffu, jak i pełnego kontekstu repo.

---

## EPIC 4 — AI abstraction

Stworzyć:

```text
IAiAnalyzer
```

Przykładowa operacja:

```text
AnalyzePullRequest()
```

Result:

```text
summary
affectedAreas
risk
mainFlow
criticalFiles[]
qualityFindings[]
architectureImpact
debugHints[]
```

* [ ] model JSON
* [ ] walidacja odpowiedzi
* [ ] obsługa błędnej odpowiedzi
* [ ] pierwszy adapter AI

---

## EPIC 5 — Understand

* [ ] generowanie Summary
* [ ] generowanie Main Flow
* [ ] identyfikacja Critical Files
* [ ] opis roli plików
* [ ] UI flow
* [ ] UI critical files

### Wynik

Zamiast czytać 100 plików, użytkownik dostaje ścieżkę prowadzącą przez najważniejsze elementy zmiany.

---

## EPIC 6 — Quality

* [ ] quality prompt
* [ ] kategorie findings
* [ ] severity
* [ ] explain
* [ ] accepted
* [ ] resolved
* [ ] not relevant

### Wynik

Aplikacja wskazuje istotne techniczne niuanse poza podstawowym code review.

---

## EPIC 7 — Checklist

* [x] AI Review — ręczne oznaczenie
* [x] Quality — ręczne oznaczenie
* [x] Understand — ręczne oznaczenie
* [x] Architecture — ręczne oznaczenie
* [x] Debug — ręczne oznaczenie
* [x] Ready — ręczne oznaczenie
* [x] zapis statusu

### Wynik

Każdy PR ma jasny postęp review.

---

## EPIC 8 — Architecture

* [ ] wykrycie architecture impact
* [ ] propozycja zmiany
* [ ] Accept
* [ ] Ignore
* [ ] podstawowy Project Knowledge

Nie trzeba jeszcze tworzyć rozbudowanej wizualnej mapy.

---

# 19. Kolejność implementacji

Najpierw:

```text
ADO → PR → diff
```

potem:

```text
PR → AI → Summary
```

potem:

```text
Summary
+ Flow
+ Critical Files
```

dopiero później:

```text
Quality
```

następnie:

```text
Checklist
```

a na końcu MVP:

```text
Architecture / Project Memory
```

---

# 20. Definition of MVP Done

MVP jest gotowe, jeżeli mogę:

1. [x] uruchomić aplikację,
2. [x] wybrać swój projekt Azure DevOps,
3. [x] zobaczyć aktywne PR-y,
4. [x] otworzyć PR,
5. [ ] uruchomić analizę,
6. [ ] zobaczyć krótkie podsumowanie,
7. [ ] zobaczyć główny flow,
8. [ ] dostać listę najważniejszych plików,
9. [ ] dostać sensowne Quality Findings,
10. [x] odklikać checklistę,
11. [ ] wrócić później i nadal widzieć wynik.

---

# 21. Kryterium sukcesu produktu

Najważniejsze pytanie po 2–4 tygodniach używania:

> **Czy dzięki PR Cockpit lepiej wiem, co faktycznie znajduje się w aplikacji?**

Pomocnicze:

* Czy czytam mniej nieistotnych plików?
* Czy szybciej znajduję główny flow?
* Czy częściej wykrywam rzeczy, których wcześniej bym nie zauważył?
* Czy łatwiej wracam do kodu po kilku tygodniach?
* Czy mniej razy pojawia się sytuacja „nie wiem, dlaczego to tak działa”?

Jeżeli odpowiedź jest „tak”, rozwijamy Project Memory.

Jeżeli nie, nie dokładamy kolejnych funkcji — poprawiamy `Understand PR`.

---

# 22. Wizja dalszego rozwoju

```text
MVP
PR comprehension
       ↓
V2
Project Memory
       ↓
V3
Architecture knowledge
       ↓
V4
Developer knowledge assistant
```

Docelowe pytanie, na które produkt powinien odpowiadać:

> **Co dzieje się w tym systemie, gdzie to jest i dlaczego zostało zrobione w ten sposób?**
