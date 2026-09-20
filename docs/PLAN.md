# Plan rozwoju — co dalej

Dokument przekazywany między sesjami. Stan i decyzje już podjęte są w [BACKLOG.md](BACKLOG.md), projekt techniczny w [ARCHITECTURE.md](ARCHITECTURE.md), a cel produktu w [PRODUCT.md](../PRODUCT.md). Tutaj jest tylko to, co **przed nami**.

## Skąd się wziął ten plan

Zgłoszenie użytkownika: *„nie pamiętam, co czytam w PR-kach, albo przelatuje to przeze mnie przez ilość kodu generowanego przez AI"*. PRODUCT.md opisuje dokładnie ten problem („developer bardziej »przegląda« diff niż go rozumie”), więc plan trzyma się jego sekcji, zamiast wymyślać równoległy produkt.

Decyzje użytkownika podjęte przy planowaniu i nadal obowiązujące:

- Komentarze do PR: **czytanie i pisanie** w diffie.
- Zapis do Azure DevOps: **komentarze tak, approve/vote nie**.
- AI: **jedno wywołanie na PR, głębiej na żądanie**.
- Persystencja: **EF Core na lokalnym SQL Server** (nie SQLite).
- Backend: **Clean Architecture w osobnych projektach**.

## Zrobione

| Etap | Commit | Co daje |
|---|---|---|
| 1 — Tryb czytania | `1ec881f` | Trójpanelowy układ, skróty klawiszowe, `hideUnchangedRegions`, tryb ciemny, drzewo plików, render opisu z Markdowna |
| 2 — Trwała pamięć czytania | `502ac4d` | Znaczniki „Obejrzałem” i ścieżka czytania przeżywają odświeżenie; nieaktualność po blob ID |
| EF Core + SQL Server | `2b7c632` | Migracje zamiast ręcznego tworzenia tabel |
| Warstwy | `68ff134` | Cztery projekty, porty, `ArchitectureTests` pilnujący kierunku zależności |

---

## Zanim dołożysz kolejną funkcję

PRODUCT.md §21 stawia sprawę wprost: jeżeli narzędzie nie poprawiło rozumienia PR-ów, **nie dokładamy funkcji, tylko poprawiamy `Understand`**. Etapy 1 i 2 zostały przejrzane w przeglądarce na prawdziwym PR i przyjęte przez użytkownika (potwierdzenie ustne, 2026-09-20), więc etap 3 ruszył.

Lista kontrolna, z której to wyszło — zachowana dla historii:

- układ trójpanelowy, zwłaszcza między 900 a 1200 px, gdzie diff dostaje ~550 px (`f` chowa szynę boczną);
- czy nasłuch klawiatury w fazie przechwytywania wygrywa z Monakiem — wcisnąć `s`, `m`, `.` z kursorem w edytorze;
- kontrast palety ciemnej (dobrana ręcznie, nie mierzona);
- `hideUnchangedRegions` i `experimental.showMoves` na dużym, prawdziwym diffie;
- wyrenderowany opis PR — czy Azure DevOps nie wypisuje wzmianek o osobach jako `@<GUID>`;
- czy `item.objectId` przychodzi dla dodania, usunięcia i przeniesienia pliku, oraz czy znacznik „Obejrzałem” poprawnie przeżywa dorzucenie commita do PR.

---

# Etap 3 — Understand: AI prowadzi przez PR

Funkcja, którą PRODUCT.md §7 nazywa najważniejszą w MVP i która wciąż nie istnieje. Odpowiada na „które 5–10 plików muszę przeczytać”.

### 3.1 Schemat AI w wersji 2 — ZROBIONE (B-15)

Dziś `SummaryRunner` w [PRCockpit.Application/Analysis/SummaryRunner.cs](../backend/PRCockpit.Application/Analysis/SummaryRunner.cs) waliduje jeden kształt: `schemaVersion == 1` i 2–5 zdań. Rozszerzamy o ranking plików:

```jsonc
{ "schemaVersion": 2,
  "sentences": ["...", "..."],           // jak dziś, 2-5
  "criticalFiles": [                      // 0-10 pozycji
    { "path": "/src/SendInvoiceEndpoint.cs",
      "role": "Wejście do wysyłki faktury.",      // ≤200 znaków
      "why": "Pokazuje początek całego flow." }   // ≤200 znaków
  ] }
```

Walidacja równie twarda jak obecna: maks. 10 pozycji, `path` **musi** należeć do listy zmienionych plików tego PR (odrzucamy halucynacje ścieżek), bez duplikatów, limity długości. Wynik poza kontraktem → 502.

Zgodność wstecz **odrzucona przez użytkownika** — robimy docelowo. Przyjmujemy wyłącznie v2, a zapis w starej wersji `SummaryStore.GetAsync` raportuje jako brak Summary (`null`), nie jako błąd, żeby przedawniony wiersz nie blokował całego PR-a.

Instrukcja w [CliSummaryAnalyzer.cs](../backend/PRCockpit.Infrastructure/Analysis/CliSummaryAnalyzer.cs) rozszerzona o polecenie wskazania plików, z zachowaniem rozdziału „stała instrukcja vs. niezaufane dane w `context`”.

### 3.2 Wyjaśnienie pojedynczego pliku na żądanie — ZROBIONE (B-16)

Drugi tryb tego samego adaptera: `task: "file"`, kontekst zawężony do jednego pliku (obie wersje, w istniejącym budżecie 20 000 znaków). Zwraca 1–3 zdania: co ten plik robi i co się w nim zmieniło.

Decyzja przy implementacji: **rozszerzenie** `IAiSummaryAnalyzer`, nie nowy port — ten sam plik wykonywalny i ta sama konfiguracja. Endpoint `POST .../summary/file`, ze ścieżką **w ciele** i tą samą listą dozwolonych ścieżek co diff. Zapis w nowej tabeli EF pod kluczem PR + ścieżka + SHA głowy, więc płacisz raz.

W UI: przycisk „Wyjaśnij ten plik” w pasku czytania plus skrót `e`. Wyjaśnienie nad diffem, nie zamiast niego.

### 3.3 Ścieżka czytania: propozycja AI zamiast pustej listy — ZROBIONE (B-15)

Ścieżka kluczowych plików jest dziś pusta, dopóki sam nie wyklikasz pozycji. Po etapie 3 zostaje **wypełniona propozycją AI**, którą nadal można edytować, przestawiać i usuwać — mechanika i zapis bez zmian, zmienia się punkt startowy. Wiersze w drzewie plików dostają etykiety ról z rankingu.

> PRODUCT.md §10: AI nigdy nie zapisuje niczego jako faktu bez akceptacji użytkownika. Ranking jest propozycją, nie decyzją.

### 3.4 Odszumienie listy plików — ZROBIONE (B-14)

`PrContextBuilder.ExcludedType` w [PrContextBuilder.cs](../backend/PRCockpit.Application/PullRequests/PrContextBuilder.cs) **już** klasyfikuje lockfile'y, snapshoty, pliki generowane, zminifikowane i build output — ale tylko na potrzeby pakietu dla AI. Drzewo plików w UI tego nie widzi.

Wystawione jako właściwość **wyliczana** `Category` (nie parametr konstruktora — zero zmian w mapperze, DTO i bazie), reguła przeniesiona do `FileCategory.Of` w domenie. Drzewo dzieli się na kod i zwinięty blok „Szum (N)”. Szczegóły w [BACKLOG.md](BACKLOG.md) B-14.

### 3.5 Debug Check — ZROBIONE w wersji minimalnej (B-17)

PRODUCT.md §9: jedno pytanie kontrolne po przeczytaniu PR, *„gdyby ten feature nie działał, gdzie zacząłbyś szukać?”*. Cel nazwany tam wprost: **wymusić kilka sekund aktywnego myślenia**, nie egzaminować. Robić dopiero, jeśli etapy 1–3 faktycznie pomogły.

---

# Etap 4 — Komentarze Azure DevOps

Trzy podetapy o wyraźnie różnym ryzyku.

### 4.1 Zakres PAT — sprawdzone, węższe niż się wydaje

Listowanie wątków mieści się w `vso.code`, który PAT **już ma**. Zapis wymaga `vso.threads_full`, czyli pozycji **„PR threads (read & write)”** — nie `vso.code_write`, który dodatkowo pozwalałby pushować kod i kasować referencje. **Code zostaje na Read.** Zapisy wątków zwracają `200 OK`, nie `201`.

### 4.2 Odblokowanie zapisu

`SendAsync` w [AzureDevOpsClient.cs](../backend/PRCockpit.Infrastructure/AzureDevOps/AzureDevOpsClient.cs) ma na sztywno `HttpMethod.Get`. Cała zmiana to opcjonalne parametry metody i treści plus `method ??= HttpMethod.Get`. **Nie** tworzymy drugiej ścieżki wysyłki — blok walidacji organizacji i PAT jest krytyczny i nie wolno go duplikować. Dochodzi test antyregresyjny: każde żądanie ze ścieżek odczytu musi być `GET`.

Mapowanie błędów dla zapisu różni się w trzech miejscach: 401/403 → **503** (brak zakresu PAT to wada konfiguracji, komunikat musi nazwać zakres), 400 → **400** (serwer odrzucił nasze ciało), 409 → **409** (wątek zmieniony równolegle).

### 4.3 Co pewne, a co do sprawdzenia na żywo

**Pewne:** `GET/POST .../pullRequests/{id}/threads`, `POST .../threads/{threadId}/comments`, `PATCH .../threads/{threadId}`. `CommentThreadStatus` = `active 1, fixed 2, wontFix 3, closed 4`; `CommentType` = `text 1, system 3`. Żądania wysyłają liczby, odpowiedzi wracają napisami. `filePath` względem korzenia repo, zaczyna się od `/` — jak `ChangedFile.Path`. `line` liczony od 1.

**Do sprawdzenia:** semantyka `offset` (dokumentacja mówi „od 0”, jej własny przykład wysyła `1`); czy `pullRequestThreadContext.changeTrackingId` jest wymagane; `iterationContext` przy diffie względem bazy scalenia; czy 409 w ogóle występuje.

### 4.4 Wątki systemowe

Azure DevOps emituje wątki systemowe (głosy, próby scalenia, zmiany reviewerów) **bez pola `status`** oraz komentarze miękko skasowane **bez `content`**. Mapper wątków musi być surowy **wyłącznie** na `id` i tolerancyjny na resztę — inaczej jeden wątek systemowy zamieni się w 500 dla całej listy. To odstępstwo od stylu pozostałych mapperów i wymaga komentarza wyjaśniającego dlaczego. Backend domyślnie odfiltrowuje wątki systemowe.

### 4.5 Pozycjonowanie w Monaco — etapami

Przy `renderSideBySide: false` widoczny jest edytor zmodyfikowany, a linie usunięte to strefy widoku bez adresowalnej pozycji. Stąd: **kotwiczymy zawsze po prawej stronie** (`getModifiedEditor().getPosition()`). Kursor działa mimo `readOnly: true`.

- **A — sam panel boczny, zero zmian w edytorze.** ~80% wartości przy minimalnym ryzyku.
- **B — znaczniki na marginesie.** `glyphMargin: true`, `createDecorationsCollection`, `onMouseDown` na `GUTTER_GLYPH_MARGIN`. Dołączyć do istniejącego bloku zwalniania w `onBeforeUnmount`.
- **C — wątki w treści diffu przez view zones. Odradzane** — konkuruje ze strefami, których inline diff już używa na linie usunięte.

### 4.6 Bezpieczeństwo — akcja nieodwracalna i widoczna dla zespołu

- **Wyłącznik** `AzureDevOps:AllowComments`, domyślnie **false**.
- **Szkic i potwierdzenie, zawsze dwa kroki.** Enter nie wysyła.
- **Żadnego zapisu optymistycznego** — to jedyne miejsce, gdzie wzorca z checklisty kopiować NIE wolno. Wycofanie komentarza jest niemożliwe. Zamiast tego: blokada przycisku → wysyłka → ponowne pobranie wątków.
- **Zakres v1:** założenie wątku, odpowiedź, zmiana statusu. Bez edycji i bez kasowania.

### 4.7 Wątki a AI — rekomendacja: nie włączać

`PrContext` jest serializowany do adaptera w całości. Wątki byłyby najbardziej podatnym na wstrzyknięcie tekstem w systemie: proza pisana przez inne osoby, kierowana do recenzenta, naturalnie sformułowana jak polecenia. Do tego kontrakt Summary brzmi „2–5 zdań o zmianie”, a komentarze są o recenzji, nie o zmianie.

### Kolejność i ryzyko

| Podetap | Zmiana PAT | Ryzyko |
|---|---|---|
| 4A — wątki tylko do odczytu, panel boczny | **brak** | niskie |
| 4B — zapis, wyłącznik, szkic z potwierdzeniem | + PR threads (read & write) | średnie — na zewnątrz, nieodwracalne |
| 4C — znaczniki na marginesie | brak | niskie |

Podział 4A/4B jest najważniejszy: niepewności co do `threadContext` rozstrzygną się przez **czytanie** prawdziwych wątków, zanim cokolwiek zostanie wysłane.

---

## Dług i rzeczy otwarte

- **Dane z SQLite nie zostały przeniesione.** Stare checklisty i Summary zostały w `%LOCALAPPDATA%\PRCockpit\checklist.db`; baza SQL Server wystartowała pusta.
- **Dwa przypięcia Roslyna w projekcie API** (`Microsoft.CodeAnalysis.Common` i `CSharp.Workspaces` 5.9.0) — `EntityFrameworkCore.Design` rozwiązuje niespójny zestaw i bez nich NuGet odmawia przywrócenia.
- **`CLAUDE.md` nie jest śledzony przez git**, choć kieruje pracą w tym repo.
- **Równoległe zapisy z dwóch okien** nie były sprawdzane.

## Zasady, które obowiązują

- Teksty UI po polsku, kod i identyfikatory po angielsku.
- `docs/BACKLOG.md` i `docs/ARCHITECTURE.md` aktualizowane **w tej samej zmianie co kod**.
- Kierunek zależności pilnuje `ArchitectureTests` — nowy kod trafia do właściwej warstwy albo build pada.
- Wzorzec stale-guard (liczniki `requestId`) obowiązuje każde nowe ładowanie asynchroniczne we frontendzie.
- Kontrakt testów frontendu: klasy CSS i napisy wymienione w BACKLOG B-10 są zamrożone.
- Skróty oznaczamy komentarzem `// ponytail:` z sufitem i ścieżką wyjścia.
- Czego nie dało się potwierdzić na prawdziwym PR — zapisujemy jako **niesprawdzone**, nie jako zaliczone.

## Wskazówka praktyczna

Działająca aplikacja blokuje `bin/`, więc `dotnet build` i `dotnet test` padają na kopiowaniu pliku. Obejście bez zatrzymywania aplikacji:

```powershell
dotnet test PRCockpit.slnx -p:BaseOutputPath="$env:TEMP\prcockpit-build\"
```
