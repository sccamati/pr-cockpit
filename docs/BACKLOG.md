# Backlog operacyjny PR Cockpit

Stan: 2026-09-20. Długoterminowy cel i epiki są w [PRODUCT.md](../PRODUCT.md), a plan najbliższych etapów w [PLAN.md](PLAN.md). Ten plik wskazuje kolejność najbliższych prac i warunki ich zakończenia. Po każdym zadaniu aktualizujemy jego status; nowe pomysły trafiają do sekcji „Później”, dopóki nie wybierzemy ich do realizacji.

## Potwierdzone

### B-01 — Potwierdzić diff na prawdziwym Azure DevOps

Status: użytkownik potwierdził działanie diffu na Azure DevOps. Nie przekazał osobnych wyników dla wszystkich przypadków brzegowych wymienionych poniżej.

**Dlaczego:** lista PR i plików była sprawdzona z rzeczywistą organizacją, ale pobieranie obu wersji pliku i Monaco tylko testami z atrapą HTTP oraz buildem. To największa niewiadoma przed rozwijaniem widoku review.

**Zakres:** na dostępnym PR sprawdzić zmianę tekstową, dodanie, usunięcie i przeniesienie pliku; sprawdzić komunikat dla pliku binarnego i przekraczającego limit, jeśli są dostępne; otworzyć kilka plików po kolei i wrócić do listy PR. Sprawdzić zachowanie panelu przy szerokości około 768 px i 375 px oraz czy samo otwarcie pliku nie zaznacza „Obejrzałem”. Poprawić wykryte błędy.

**Gotowe, gdy:** obie strony diffu odpowiadają zawartości PR, Monaco działa po zmianie pliku, układ jest czytelny na wąskim ekranie, a wynik weryfikacji jest zapisany tutaj. Przypadki niedostępne w testowanym PR trzeba oznaczyć jako niesprawdzone, nie jako zaliczone.

## Wdrożone — do potwierdzenia na prawdziwym PR

### B-02 — Nawigacja po plikach do obejrzenia

Status: zaimplementowane. Test interakcji frontendu potwierdza współdziałanie filtra z wyszukiwaniem, ręczne oznaczanie, przejście do kolejnego pliku bez automatycznego oznaczenia oraz komunikat po zakończeniu. Układ na wąskim ekranie wymaga wizualnego potwierdzenia.

**Dlaczego:** przy dużym PR licznik „Obejrzałem” pomaga, ale nadal trzeba ręcznie szukać kolejnego nieprzeczytanego pliku.

**Zakres:** filtr „Wszystkie / Nieobejrzane”, akcja „Następny nieobejrzany”, czytelny postęp `obejrzane / wszystkie`. Zachować istniejące wyszukiwanie i ręczny checkbox. Stan pozostaje w pamięci karty.

**Gotowe, gdy:** filtr współdziała z wyszukiwaniem, nawigacja nie oznacza pliku automatycznie, a po obejrzeniu ostatniego pliku użytkownik dostaje jasny komunikat. Sprawdzić także działanie na wąskim ekranie.

### B-03 — Kontekst commitów w widoku PR

Status: zaimplementowane. Stronicowanie i pusta lista są pokryte testami HTTP, a renderowanie tytułu i autora testem interakcji frontendu. Lista commitów wymaga potwierdzenia na rzeczywistym PR.

**Dlaczego:** widok pokazuje dziś tylko liczbę commitów. Ich tytuły i autorzy pomagają zrozumieć kolejność oraz zamiar zmian bez opuszczania PR Cockpit.

**Zakres:** rozwinąć istniejące pobieranie commitów o podstawowe metadane i pokazać listę w panelu PR. Obsłużyć stronicowanie oraz pustą listę, bez pobierania pełnej zawartości repozytorium.

**Gotowe, gdy:** lista odpowiada commitom bieżącego PR, działa dla więcej niż jednej strony wyników i ma testy klienta HTTP bez połączenia z Azure DevOps.

## Wynik weryfikacji 2026-09-19

- `dotnet test PRCockpit.slnx`: 16 testów zaliczonych. Obejmują między innymi dwie strony commitów, pustą listę oraz przypadki diffu z atrapą Azure DevOps.
- `npm test`: 2 testy interakcji zaliczone. Obejmują wyświetlenie commitów, filtr nieobejrzanych z wyszukiwaniem, nawigację, ręczne oznaczanie i ochronę przed spóźnioną odpowiedzią po zmianie repozytorium. `npm run build` przechodzi.
- Poprawiono nadpisywanie listy PR przez spóźnioną odpowiedź ze starego repozytorium i zawijanie długiego tytułu PR w wąskim układzie.
- **Niesprawdzone w tej sesji:** B-02 i B-03 na rzeczywistym PR oraz wygląd przy około 768 px i 375 px. Środowisko nie ma konfiguracji Azure DevOps (organizacji i PAT), a przeglądarka aplikacji jest niedostępna. Testy z atrapą i przegląd kodu CSS nie potwierdzają wyglądu ani zgodności commitów z prawdziwym PR. Nie ma też potwierdzenia paginacji commitów na realnym PR przekraczającym jedną stronę.
- Do zamknięcia B-02/B-03: otworzyć rzeczywisty PR, porównać listę commitów z Azure DevOps, sprawdzić wyszukiwanie i filtr po ręcznym oznaczeniu, przejść przez ostatni plik, a następnie obejrzeć panel przy 768 px i 375 px. Dla B-03 sprawdzić PR z więcej niż 1000 commitów, jeżeli taki jest dostępny; w przeciwnym razie pozostawić ten przypadek jako niepotwierdzony na żywo.

## Wdrożone — fundament AI Summary

### B-04 — Ograniczony pakiet kontekstu PR

Status: zaimplementowane w backendzie. Wymaga potwierdzenia na rzeczywistym PR.

**Zakres:** endpoint `/context` zwraca metadane PR (w tym SHA bazy i głowy z ostatniej iteracji), tytuły commitów i wszystkie zmienione pliki. Dla plików tekstowych dołącza obie strony istniejącego diffu. Limit wynosi 20 000 znaków na plik i 100 000 znaków łącznie dla tekstów diffów. Bez obcinania treści: pominięty plik zachowuje ścieżkę, typ zmiany i powód. Pomijane są lockfile, snapshoty, wygenerowane pliki, zminifikowane zasoby, build output oraz pliki binarne lub przekraczające istniejący limit diffu. Pobieranie diffów korzysta z jednorazowo pobranej iteracji i listy zmian PR.

**Weryfikacja:** testy bez Azure DevOps obejmują limity na plik i PR, granicę limitu, zachowanie metadanych pominiętych plików, typy pominięć, binarny i zbyt duży diff oraz ponowne użycie listy zmian przy wielu diffach.

## Wdrożone — do sprawdzenia na rzeczywistym PR i CLI

### B-05 — Summary na żądanie przez wymienny adapter AI

Status: zaimplementowano endpoint POST, adapter procesu CLI, walidację wyniku i panel UI. Testy z atrapą adaptera i API nie sprawdzają jakości odpowiedzi prawdziwego modelu.

**Zakres:** dodać interfejs adaptera AI przyjmujący przygotowany `PrContext` i zwracający ustrukturyzowany wynik `Summary` ze zdefiniowaną wersją schematu. Uruchomienie następuje tylko po świadomej akcji użytkownika dla aktualnego PR. Nie uruchamiać analizy przy otwarciu listy lub szczegółów PR. Adapter i model wybierane są w konfiguracji backendu, bez nazw dostawców w modelu domenowym. Treści PR i kodu traktować jako dane, nie instrukcje dla modelu.

**Gotowe, gdy:** użytkownik może uruchomić Summary dla wybranego PR i zobaczyć 2–5 zdań albo czytelny błąd; wynik jest walidowany przed pokazaniem, a ograniczenia pakietu kontekstu są widoczne obok wyniku. Testy z fake adapterem potwierdzają uruchomienie wyłącznie na żądanie, przekazanie dokładnie przygotowanego pakietu, obsługę błędnej odpowiedzi i brak ponownego pobierania kontekstu w samym adapterze. Nie wymaga to jeszcze lokalnego repozytorium ani persistence.

Do potwierdzenia na żywo: skonfigurować program CLI zgodny z protokołem z README, uruchomić Summary na rzeczywistym PR, porównać wynik z diffem i sprawdzić komunikat przy pominiętych plikach. Gdy CLI nie jest skonfigurowane, UI pokazuje błąd konfiguracji po kliknięciu.

## Wdrożone — ręczna ścieżka czytania

### B-06 — Kluczowe pliki wybrane przez użytkownika

Status: zaimplementowane w widoku PR. Użytkownik może dodać do ścieżki maksymalnie 10 plików z listy zmian, zmienić ich kolejność, otworzyć diff i usunąć pozycję. Ścieżka działa niezależnie od znacznika „Obejrzałem” i pozostaje w pamięci bieżącej karty. Po ponownym pobraniu szczegółów PR z listy znikają pliki, których nie ma już w zmianach. Nie ma automatycznego rankingu ani zapisu w bazie.

## Wdrożone — ręczna checklista PR

### B-07 — Sześć kroków z lokalnym zapisem

Status: zaimplementowano API, SQLite i panel w szczegółach PR. Każdy krok (`AI Review`, `Quality`, `Understand`, `Architecture`, `Debug`, `Ready`) jest zaznaczany ręcznie i zapisywany osobno. Stan jest oddzielony według organizacji, projektu, repozytorium i PR; po ponownym otwarciu PR aplikacja pobiera go z bazy. `Ready` nie zmienia się automatycznie. Do potwierdzenia w aplikacji: przejście między dwoma PR, odświeżenie strony oraz zachowanie na wąskim ekranie.

### B-08 — Postęp checklisty na liście aktywnych PR

Status: zaimplementowane. Lista pokazuje liczbę zapisanych kroków jako `x/6`, a PR bez wiersza w SQLite jako `0/6`. Backend odczytuje liczniki dla repozytorium jednym zapytaniem do bazy; frontend pobiera je jednym żądaniem dla całej listy. Podczas ładowania pokazuje stan oczekiwania, a przy błędzie komunikat i możliwość ponowienia. Odpowiedź ze starego projektu lub repozytorium nie zmienia bieżącej listy. Po powrocie ze szczegółów postęp jest odświeżany, aby uwzględnić ręczne zmiany checklisty.

Weryfikacja w tej sesji: build backendu i frontendu. Testów nie uruchamiano zgodnie z prośbą. Działanie na rzeczywistym Azure DevOps pozostaje do potwierdzenia.

### B-09 — Zapis i czytelny widok Summary

Status: zaimplementowane. Wynik Summary jest zapisywany w lokalnym SQLite wraz z SHA głowy PR i czasem zapisu. Po otwarciu szczegółów aplikacja odczytuje zapis bez uruchamiania AI, pokazuje nieaktualność przy zmianie SHA i pozwala wygenerować nowy wynik ręcznie. Odpowiedź zachowuje 2–5 zdań osobno, a UI pokazuje je w oddzielnych akapitach zamiast jednego bloku tekstu. Nieudana ponowna analiza nie usuwa poprzedniego wyniku.

Weryfikacja: `dotnet build PRCockpit.slnx` i `npm run build` przeszły. Testów nie uruchamiano.

Do potwierdzenia w aplikacji: ponowne otwarcie PR, zmiana SHA, brak SHA oraz odczyt i ponowne generowanie przy błędzie zapisu.

## Wdrożone — tryb czytania

### B-10 — Tryb czytania: układ, skróty, motyw i render opisu

Status: zaimplementowane we frontendzie. Backend nietknięty.

**Dlaczego:** widok PR był jedną długą stroną z dziewięcioma sekcjami, w której diff — jedyna rzecz faktycznie czytana — był siódmy w kolejności i dostawał okienko `clamp(500px, 76vh, 960px)` między listą commitów a reviewerami. Nie było żadnego skrótu klawiszowego. Przy PR-ach generowanych przez AI oznaczało to przewijanie zamiast czytania.

**Zakres:** trzypanelowy układ na wysokość okna (lista plików / diff / szyna kontekstu w `<details>`), nagłówek PR z powrotem i postępem checklisty, tryb skupienia. Skróty `j/k/n/p`, `m` (obejrzałem i dalej), `.`/`,` (skok po zmianach), `/`, `s`, `f`, `g`, `o`, `Esc`, `?` plus natywny `<dialog>` z pomocą. Monaco: `hideUnchangedRegions`, przełącznik widoku obok siebie, otwieranie na pierwszej zmianie, `showMoves`. Tokeny CSS i tryb ciemny wraz z drugim motywem Monaka. Render opisu PR z Markdowna (`markdown-it` + DOMPurify), z linkami do Work Itemów i zamianą załączników-obrazków na linki.

**Zweryfikowane:** `npm test` — 22 testy w 3 plikach. Cztery istniejące testy `App.test.ts` przeszły po przebudowie układu **bez żadnej edycji**, co było punktem kontrolnym kontraktu nazw klas i napisów. Nowe testy pokrywają: `m` oznacza i przechodzi dalej, `m` w polu wyszukiwania jest ignorowane (test montuje drzewo przez `attachTo`, bo bez tego zdarzenie nie dociera do nasłuchu na `window` i test przechodziłby pusto), `j`/`k`, brak przecieku nasłuchu po odmontowaniu, opcje `hideUnchangedRegions` i przełącznik kolumn, definicja obu motywów, oraz osiem testów renderu opisu — w tym sanityzacja `<script>`, atrybutów `on*` i linków `javascript:`. `npm run build` przechodzi.

**Niesprawdzone — do potwierdzenia w przeglądarce na rzeczywistym PR:**
- wygląd układu trójpanelowego, zwłaszcza między 900 a 1200 px, gdzie diff dostaje około 550 px; wyjściem awaryjnym jest tryb skupienia (`f`);
- czy nasłuch w fazie przechwytywania faktycznie wygrywa z obsługą klawiszy Monaka — wcisnąć `s`, `m`, `.` z kursorem w edytorze;
- kontrast palety ciemnej (dobrana ręcznie, nie mierzona);
- zachowanie `hideUnchangedRegions` i `experimental.showMoves` na prawdziwym, dużym diffie;
- wygląd wyrenderowanego opisu na rzeczywistych PR-ach, w szczególności czy Azure DevOps nie zapisuje wzmianek o osobach jako `@<GUID>`;
- `revealFirstDiff` jest w typach Monaka 0.56 oznaczone jako `unknown` i nieudokumentowane — wywołanie jest osłonięte `?.`, ale zachowanie wymaga obejrzenia.

**Doszło po pierwszej informacji zwrotnej:** lista plików jest drzewem folderów ze zwijaniem (`fileTree.ts` + `FileTree.vue`), bo przy 44 plikach powtarzana i ucięta ścieżka w każdym wierszu nie niosła informacji, a wiersz pliku zajmował cztery linie. Nawigacja `j`/`k` i „następny nieobejrzany” chodzą teraz w kolejności drzewa, nie w kolejności zwróconej przez Azure DevOps. Przejście do opisu PR jest odwracalne: zapamiętuje otwarty plik, `o` działa w obie strony, a na ekranie startowym jest widoczny przycisk powrotu.

**Zależności:** dodano `markdown-it` (MIT, 15.0.2). `dompurify` (MPL-2.0 OR Apache-2.0, 3.4.15) był już w drzewie przez monaco-editor i został awansowany do zależności bezpośredniej; przypięcie w `overrides` zmieniono na `$dompurify`, żeby zachować wymuszenie wersji w całym drzewie bez duplikowania numeru.

## Wdrożone — trwała pamięć czytania

### B-11 — Trwały postęp czytania i ścieżka czytania

Status: zaimplementowane po obu stronach.

**Dlaczego:** znacznik „Obejrzałem” i ścieżka kluczowych plików żyły wyłącznie w `ref`-ach Vue i ginęły po odświeżeniu. To była techniczna przyczyna zgłoszenia „nie pamiętam, co czytałem”.

**Zakres:** `ReviewProgressStore` na wzór `ChecklistStore`, reużywający `ChecklistException`, więc `Execute` w `Program.cs` nie wymagał zmiany. Cztery nowe trasy (odczyt stanu, zapis pliku, zapis ścieżki, zbiorczy postęp dla repozytorium). `ChangedFile` niesie teraz `objectId`, przechwycony z odpowiedzi, którą klient i tak pobierał. Front: stan per PR zamiast słowników kluczowanych po PR, zapis optymistyczny z wycofaniem, licznik `x/y plików` na liście PR, odznaka „zmienione” i wznowienie sesji na pierwszym nieprzeczytanym pliku.

**Reguła nieaktualności:** znacznik nigdy nie jest kasowany przez backend. Staje się nieaktualny tylko przy pozytywnym dowodzie zmiany — niezgodny blob ID, a w jego braku niezgodne SHA głowy. Odrzucono kasowanie znaczników przy nowym commicie (utrata danych wpisanych ręcznie) oraz milczące utrzymywanie ich (kłamstwo o jedynej rzeczy, do której narzędzie istnieje).

**Zweryfikowane:** 48 testów backendu, w tym 16 nowych testów magazynu na tymczasowym pliku SQLite: obieg oznacz→odznacz, przestemplowanie tożsamości, rozdział per PR i repozytorium, odrzucenie złej ścieżki, złego blob ID, braku flagi, ścieżki czytania ponad limit i z duplikatem, oraz limit 2000 wierszy wraz z tym, że istniejący wiersz nadal da się zaktualizować po jego osiągnięciu. Frontend: 33 testy, build przechodzi.

**Niesprawdzone:** działanie na rzeczywistym PR w Azure DevOps — w szczególności czy `item.objectId` faktycznie przychodzi dla wszystkich typów zmian (dodanie, usunięcie, przeniesienie) i czy znacznik poprawnie przeżywa dorzucenie commita do PR.

## Wdrożone — Entity Framework Core i SQL Server

### B-12 — Persystencja na EF Core i lokalnym SQL Server

Status: zaimplementowane. Decyzja użytkownika po przedstawieniu kompromisów.

**Dlaczego:** ręczne `CREATE TABLE IF NOT EXISTS` przy każdej operacji nie potrafiło rozwinąć schematu — dołożenie kolumny do istniejącej bazy po cichu nic by nie zrobiło, a zapytania przestałyby działać. Nie było żadnej ścieżki migracji. EF to naprawia migracjami.

**Zakres:** `PrCockpitContext` z czterema encjami w `Persistence/Entities`, przepisane `ChecklistStore`, `SummaryStore` i `ReviewProgressStore`, migracja `InitialSchema`, stosowanie migracji przy starcie, rejestracja magazynów jako scoped, nowe gałęzie `SqlException` i `DbUpdateException` w `Execute`. Usunięto `LocalDatabase` i `Microsoft.Data.Sqlite` z aplikacji.

**Zweryfikowane na żywo:** migracja zastosowana na rzeczywistej instancji SQL Server i zarejestrowana w `__EFMigrationsHistory`; aplikacja wstaje i wykonuje realny zapis oraz odczyt przez API (checklista, oznaczenie pliku, ścieżka czytania, zbiorczy postęp). 49 testów backendu przechodzi na SQLite w pamięci, 33 testy frontendu i build przechodzą.

**Pułapka warta zapamiętania:** pierwszy connection string nie zawierał `Initial Catalog`, więc EF utworzył tabele w bazie `master`. Wykryte, tabele z `master` usunięte, baza `PRCockpit` założona poprawnie, a wymóg jawnego `Initial Catalog` zapisany w README i `.env.example`.

**Dług:** do projektu API trzeba było dołożyć `Microsoft.CodeAnalysis.Workspaces.Common` i `Microsoft.CodeAnalysis.CSharp.Workspaces` w wersji 5.9.0, bo generator migracji EF zderzał się z Roslynem używanym przez podpowiedzi C#. Po rozdzieleniu na projekty warstwowe infrastruktura nie będzie zależeć od Roslyna i te dwa pakiety powinny zniknąć.

**Niesprawdzone:** zachowanie przy równoległych zapisach z dwóch okien oraz to, czy dotychczasowe dane z pliku SQLite mają zostać przeniesione — obecnie **nie są**, baza SQL Server startuje pusta.

## Wdrożone — podział na warstwy

### B-13 — Clean Architecture w osobnych projektach

Status: zaimplementowane. Decyzja użytkownika, podjęta po przedstawieniu zastrzeżenia, że zapis w `CLAUDE.md` tego repo mówił, iż narzędzie jednoosobowe nie zarabia na warstwy.

**Zakres:** backend rozbity z jednego projektu na cztery — `PRCockpit.Domain`, `PRCockpit.Application`, `PRCockpit.Infrastructure`, `PRCockpit.Api` — z zależnościami skierowanymi do środka. Modele i reguły trafiły do domeny, porty i przypadki użycia do aplikacji, adaptery (Azure DevOps, Roslyn, EF Core, magazyny) do infrastruktury, a host z trasami i mapowaniem błędów został w API. Orkiestracja „pobierz szczegóły → zbuduj kontekst → uruchom AI → zapisz”, która siedziała w ciele endpointu, dostała własnego właściciela w `SummaryService`.

**Najważniejsze:** doszedł `ArchitectureTests`, który czyta faktyczne referencje zestawów i wywraca build, gdy warstwa zostanie złamana — domena nie zna EF, SqlClient, Roslyna, ASP.NET ani HTTP; aplikacja nie zna infrastruktury; każdy port ma adapter. Bez tego katalogi byłyby tylko nazwami.

**Spłacony dług:** projekt API nie zależy już od Roslyna, bo podpowiedzi C# przeniosły się do infrastruktury. Pozostało jednak jawne przypięcie `Microsoft.CodeAnalysis.Common` i `CSharp.Workspaces` w wersji 5.9.0 w API — samo `EntityFrameworkCore.Design` ciąga niespójny zestaw Roslyna (`CSharp 5.9.0` obok `CSharp.Workspaces 5.0.0`) i bez przypięcia NuGet odmawia przywrócenia pakietów.

**Zweryfikowane:** 62 testy backendu (49 wcześniejszych plus 13 reguł architektonicznych) i 33 testy frontendu przechodzą; aplikacja wstaje po przebudowie i wykonuje realne zapisy oraz odczyty przez API na żywej bazie SQL Server.

## Wdrożone — odszumienie listy plików

### B-14 — Podział drzewa na kod i szum

Status: zaimplementowane. Pierwszy krok etapu 3 z [PLAN.md](PLAN.md) — jedyny, który nie dotyka AI.

**Dlaczego:** `PrContextBuilder` od B-04 rozpoznawał lockfile'e, snapshoty, pliki generowane, zminifikowane i build output, ale wyłącznie po to, żeby nie wysyłać ich do modelu. Drzewo plików w UI nic o tym nie wiedziało, więc PR z dwunastoma takimi plikami otwierał się na dwunastu wierszach, których nikt nie czyta.

**Zakres:** reguła przeniesiona z `PrContextBuilder.ExcludedType` do `FileCategory.Of` w domenie — jedna implementacja, dwóch odbiorców. `ChangedFile` dostał właściwość **wyliczaną** `Category` zamiast parametru konstruktora, więc ani mapper, ani miejsca konstrukcji, ani DTO, ani baza nie wymagały zmiany. Frontend dzieli przefiltrowane pliki na dwa drzewa i renderuje szum w zwiniętym `<details>` „Szum (N)”. Pliki szumu zostają widoczne i oznaczalne, ale w `orderedPaths` lądują na końcu.

**Świadoma decyzja:** liczniki obejrzanych plików i „Wszystkie pliki obejrzane” nadal obejmują szum. Zmiana jest czysto prezentacyjna — ukrycie pliku z rachunku byłoby twierdzeniem, że nie ma go w PR.

**Zweryfikowane:** 68 testów backendu (doszedł `[Theory]` pilnujący, że `ChangedFile.Category` zgadza się z tym, co pomija `PrContextBuilder`) i 34 testy frontendu, w tym nowy test sprawdzający, że szum trafia do własnej zwiniętej grupy i że „następny nieobejrzany” przerabia najpierw kod. `npm run build` przechodzi.

**Niesprawdzone:** wygląd bloku „Szum” na rzeczywistym PR — `max-height: 40%` w szynie plików dobrane na oko, nie zmierzone na długiej liście.

## Wdrożone — ranking plików od AI

### B-15 — Schemat Summary v2 i propozycja ścieżki czytania

Status: zaimplementowane. Etap 3, punkty 3.1 i 3.3 z [PLAN.md](PLAN.md), zrobione jako jedna zmiana — sam punkt 3.1 nie dałby nic widocznego, więc nie dałoby się go ocenić.

**Zakres backendu:** `SummaryDraft` i `SummaryResponse` dostały `criticalFiles` (`path`, `role`, `why`). `SummaryRunner` waliduje je równie twardo jak zdania: maksymalnie 10 pozycji, każda ścieżka **musi** należeć do listy plików tego PR, bez duplikatów, `role` i `why` niepuste i do 200 znaków. Cokolwiek poza kontraktem → 502. Limity zebrane w `SummaryContract`, z którego korzysta też magazyn i prompt. Instrukcja w `CliSummaryAnalyzer` rozszerzona; podział „stała instrukcja vs. niezaufane dane w `context`” nietknięty. Adapter referencyjny tylko przepuszcza treść, więc zmienił się w nim wyłącznie komentarz.

**Decyzja użytkownika — bez zgodności wstecz.** Schemat v1 nie jest już przyjmowany. Żeby stary zapis nie wywracał całego PR-a, `SummaryStore.GetAsync` traktuje niezgodną **wersję** jako brak Summary (UI proponuje wygenerowanie), a nie jako błąd; uszkodzony JSON nadal daje 503.

**Zakres frontendu:** ranking pokazuje się jako **propozycja**, nie jako zapis. Blok „Propozycja AI” pojawia się w szynie tylko wtedy, gdy ścieżka czytania jest pusta, i nic nie zapisuje, dopóki nie klikniesz „Przyjmij ścieżkę”; „Odrzuć” chowa go do czasu przełączenia PR. Wiersze drzewa plików dostają etykiety ról z rankingu niezależnie od akceptacji — etykieta informuje, niczego nie nadpisuje. To wprost wymóg [PRODUCT.md](../PRODUCT.md) §10.

**Zweryfikowane:** 71 testów backendu (doszły trzy: pełny kontrakt v2, pusta i brakująca lista, oraz odrzucenie wymyślonej ścieżki, duplikatu, pustej roli, przekroczonej długości i jedenastej pozycji) i 36 testów frontendu (doszły dwa: propozycja nic nie zapisuje dopóki nie zostanie przyjęta i zachowuje kolejność modelu; istniejąca ścieżka użytkownika nie jest ruszana). `npm run build` przechodzi.

**Niesprawdzone:** jakość samego rankingu na rzeczywistym PR — czy model trafia w te 5–10 plików i czy `why` jest warte czytania. Tego nie da się orzec z testów.

## Wdrożone — wyjaśnienie pliku i Debug Check

### B-16 — Wyjaśnienie pojedynczego pliku na żądanie

Status: zaimplementowane. Etap 3, punkt 3.2 z [PLAN.md](PLAN.md).

**Zakres:** drugi tryb tego samego adaptera (`task: "file"`), a nie drugi port — ten sam plik wykonywalny, ta sama konfiguracja i ten sam kontrakt, więc osobny interfejs kupowałby tylko kolejną rejestrację w DI. `CliSummaryAnalyzer` ma teraz **jedną** ścieżkę uruchomienia procesu dla obu zadań; duplikowanie jej oznaczałoby duplikowanie timeoutu, ograniczonych odczytów i zabijania procesu, czyli całego ryzyka w tej klasie. Kontekst zawęża opcjonalny parametr `onlyPath` w `PrContextBuilder` — budżety zostają dokładnie te same, więc pojedynczy plik nie jest traktowany łagodniej niż ten sam plik w pakiecie całego PR. Walidacja: 1–3 zdania, `schemaVersion == 2`, ścieżka bierze się z kontekstu, nigdy z odpowiedzi modelu.

**Endpoint** `POST .../summary/file` przyjmuje ścieżkę **w ciele** i sprawdza ją względem listy plików tego PR, zanim cokolwiek trafi do Azure DevOps albo do modelu. Zapis w nowej tabeli `pr_file_explanations`, klucz to PR + ścieżka, a SHA głowy jest **kolumną**: wiersz ze starej głowy jest nadpisywany zamiast puchnąć o wiersz na iterację.

**W UI:** przycisk „Wyjaśnij ten plik” w pasku czytania i skrót `e`. Wyjaśnienie pojawia się **nad** diffem, nie zamiast niego. Ładowanie korzysta z licznika `diffRequestId`, więc odpowiedź dla pliku, z którego już wyszedłeś, jest odrzucana — ten sam wzorzec co spóźniony diff.

**Zweryfikowane:** 83 testy backendu (doszły m.in. zawężenie kontekstu do jednego pliku bez pobierania pozostałych, utrzymanie budżetu przy jednym pliku, odrzucenie 0 i 4 zdań oraz odczyt wyjaśnienia tylko dla tej głowy, z której powstało) i 38 testów frontendu (doszedł test, że spóźnione wyjaśnienie nie pojawia się nad kolejnym plikiem).

### B-17 — Debug Check

Status: zaimplementowane w wersji minimalnej. Etap 3, punkt 3.5 z [PLAN.md](PLAN.md), [PRODUCT.md](../PRODUCT.md) §9.

**Zakres:** blok w szynie z jednym pytaniem — „Gdyby ta zmiana nie zadziałała, gdzie zacząłbyś szukać?” — polem na odpowiedź i zapisem w kolumnie `DebugNote` tabeli `pr_checklists`. Rozwija się sam, gdy wszystkie pliki są obejrzane; wcześniej mówi wprost, ile zostało. „Pokaż, gdzie patrzeć” odsłania ranking, który Summary już zwróciło — **żadnego drugiego wywołania modelu**. Zapis odpowiedzi **nie** zaznacza kroku „Debug” w checkliście; sześć pól zostaje decyzją użytkownika, tak jak od B-07.

**Świadome uproszczenie (`ponytail`):** PRODUCT.md §9 mówi o 1–3 generowanych scenariuszach awarii; tu pytanie jest stałe. Cel z §9 brzmi „wymusić kilka sekund aktywnego myślenia”, a to stałe pytanie robi bez trzeciej ścieżki AI i bez schematu v3. Sufit: jeśli stałe pytanie okaże się za słabe, scenariusze dochodzą jako `scenarios` w schemacie Summary.

**Zweryfikowane:** przechowywanie odpowiedzi, czyszczenie pustą treścią, limit 2000 znaków (400) i to, że zapis nie zaznacza kroku „Debug” — testy magazynu na SQLite w pamięci. Frontend: zapis pokazuje to, co zapisał serwer, i nie rusza checklisty.

**Niesprawdzone:** czy pytanie faktycznie zmienia sposób czytania PR. To ocena użytkownika po kilku prawdziwych PR-ach, nie test.

## Wdrożone — wątki Azure DevOps do odczytu

### B-18 — Komentarze PR, tylko odczyt

Status: zaimplementowane. Etap 4, podetap 4A z [PLAN.md](PLAN.md). **Zakres PAT bez zmian** — listowanie wątków mieści się w `vso.code`, który PAT już ma, a `SendAsync` nadal wysyła wyłącznie `GET`.

**Zakres:** `GET .../pull-requests/{id}/threads` przez `IAzureDevOpsClient.GetCommentThreadsAsync`. Backend odfiltrowuje wątki systemowe, zanim cokolwiek wyjdzie do frontendu. W szynie kontekstu doszedł blok „Komentarze” z lokalizacją (`plik:linia` albo „Cały PR”), statusem i treścią; kliknięcie lokalizacji otwiera plik. Ładowanie ma własny licznik `threadsRequestId`, zgodnie z regułą stale-guard.

**Mapper jest celowo inny niż pozostałe:** surowy wyłącznie na `id`, tolerancyjny na resztę. Azure DevOps emituje wątki systemowe bez pola `status` i komentarze miękko skasowane bez `content` — jeden taki wpis w liście zamieniłby całą listę w 500. Wątek uznajemy za systemowy, gdy wszystkie jego komentarze mają `commentType: "system"` albo gdy nie ma w nim żadnego komentarza.

**Treść komentarzy renderujemy jako zwykły tekst**, nie Markdown. To proza pisana przez inne osoby; render byłby kolejną powierzchnią do sanityzacji, a niczego tu nie dodaje.

**Nadal obowiązuje [PLAN.md](PLAN.md) §4.7:** wątki **nie** trafiają do kontekstu AI. `PrContext` jedzie do adaptera w całości, a komentarze to najbardziej podatny na wstrzyknięcie tekst w systemie.

**Zweryfikowane:** 85 testów backendu (doszły dwa: lista z wątkiem systemowym i skasowanym komentarzem nie wywraca odczytu, oraz wątek bez `status` i bez `threadContext` mapuje się poprawnie) i 39 testów frontendu (doszedł jeden: lista wątków, wątek bez pliku nie da się kliknąć, kliknięcie lokalizacji otwiera plik).

**Niesprawdzone — do potwierdzenia na prawdziwym PR:** semantyka `offset`, czy `pullRequestThreadContext.changeTrackingId` w ogóle przychodzi, `iterationContext` przy diffie względem bazy scalenia oraz czy wykrywanie wątków systemowych po `commentType` wystarcza. Właśnie po to 4A idzie przed 4B — te niewiadome rozstrzygną się przez **czytanie**, zanim cokolwiek zostanie wysłane.

## Wdrożone — pisanie komentarzy i widok komentarzy

### B-19 — Zapis komentarzy do Azure DevOps

Status: zaimplementowane. Etap 4, podetap 4B z [PLAN.md](PLAN.md). **Wymaga rozszerzenia PAT** o pozycję „PR threads (read & write)”; Code zostaje na Read.

**Wyłącznik:** `AzureDevOps:AllowComments`, domyślnie **false**. Sprawdzany w `RequireCommentsEnabled` zanim cokolwiek opuści maszynę — fail closed, bo komentarz widzi cały zespół i nie da się go cofnąć.

**Jedna ścieżka wysyłki:** `SendAsync` dostał opcjonalne `method` i `body` plus `method ?? HttpMethod.Get`. Bloku walidacji organizacji i PAT **nie** zduplikowano. Mapowanie błędów dla zapisu różni się w trzech miejscach: 401/403 → **503** z komunikatem nazywającym brakujący zakres, 400 → **400**, 409 → **409**.

**Zakres v1:** założenie wątku (z kotwicą na plik i linię albo bez), odpowiedź, zmiana statusu, a po prośbie użytkownika także **edycja i usunięcie własnego komentarza** (`PATCH`/`DELETE .../threads/{id}/comments/{id}`). Azure DevOps pozwala ruszać wyłącznie własne komentarze, więc backend pyta raz na proces o `_apis/connectionData` i oznacza każdy komentarz `isMine` — przycisk, który na pewno dostałby odmowę, w ogóle się nie pokazuje. Gdy zapytanie o tożsamość padnie, lista wątków i tak działa, tylko bez tych przycisków. `403` przy edycji i usuwaniu mapuje się na **403** z komunikatem o własności, nie na 503 o zakresie PAT. Usunięcie jest miękkie — komentarz zostaje w wątku bez treści — i wymaga potwierdzenia w UI. Kotwica zawsze po prawej stronie, `offset: 1` — tak jak w przykładzie z dokumentacji Azure DevOps. Statusy wysyłamy liczbą (`active 1, fixed 2, wontFix 3, closed 4`), wracają napisem.

**Żadnego zapisu optymistycznego.** Przycisk się blokuje, żądanie leci, wątki są pobierane ponownie. Szkic i potwierdzenie to zawsze dwa kroki, Enter nie wysyła, a pod przyciskiem stoi zdanie „Wysłanego komentarza nie da się cofnąć”.

**Zweryfikowane:** 95 testów backendu. Doszły m.in.: przy wyłączniku off żadne z trzech żądań nie opuszcza maszyny, nowy wątek jedzie z `rightFileStart` i bez `leftFileStart`, status leci liczbą i wraca nazwą, nieznany status i pusta treść kończą się 400 bez kontaktu z Azure DevOps, mapowanie 403/400/409 na 503/400/409, oraz **test antyregresyjny: każda ścieżka odczytu nadal wysyła `GET` i nie ma treści**.

### B-20 — Widok komentarzy i znaczniki w diffie

Status: zaimplementowane. Podetap 4C plus widok, o który poprosił użytkownik.

**Widok komentarzy** (skrót `c`) zajmuje środkowy panel obok diffu i opisu PR. Jest w nim wyszukiwarka po treści, autorze i ścieżce, filtr „Aktywne / Wszystkie”, licznik „N aktywnych z M” i grupowanie **po pliku w kolejności drzewa plików** — czyli w tej, w której czyta się PR — a w grupie po numerze linii. Stamtąd się odpowiada, zmienia status i zakłada nowy wątek. Szyna kontekstu została podsumowaniem: jedna linia na wątek plus wejście do widoku.

**Znaczniki przy linii:** `createDecorationsCollection` z `linesDecorationsClassName` na edytorze **zmodyfikowanym** (pierwotnie `glyphMargin`, ale ten dokłada ~26 px pustej rynny i wypychał kod w prawo — przy diffie w linii lewa strona i tak wydaje dwie kolumny na numery; `lineDecorationsWidth: 18` wystarcza na ikonę) — przy `renderSideBySide: false` linie usunięte są strefami widoku bez adresowalnej pozycji, więc prawa strona jest jedyną, na której znacznik może stać. Kliknięcie w margines otwiera rozmowę dla tej linii, a linia bez wątku otwiera szkic zakotwiczony w niej. Przycisk „Skomentuj linię” bierze linię z kursora (`getPosition()` działa mimo `readOnly`). Nasłuch i kolekcja dekoracji dołączyły do istniejącego bloku zwalniania w `onBeforeUnmount`.

**Doszło po informacji zwrotnej:** komentarz zakłada się **kliknięciem w linię w diffie** — inaczej funkcja jest nie do użycia. Najechanie na linię pokazuje `+` na marginesie, a kliknięcie w margines **albo w numer linii** otwiera rozmowę dla tej linii; ikona o szerokości kilkunastu pikseli to zły cel kliknięcia. Rozmowa i szkic otwierają się **nad diffem**, a nie w widoku komentarzy: przełączenie panelu zabierałoby z ekranu dokładnie ten kod, którego komentarz dotyczy. Linia, która już ma wątek, nie dostaje `+` — ma swój znacznik.

**Widoku view zones świadomie nie budujemy** — PLAN.md §4.5 wariant C konkurowałby ze strefami, których diff w linii już używa na linie usunięte.

### B-21 — „Co się zmieniło po tym komentarzu”

Status: zaimplementowane. Prośba użytkownika: żeby po poprawce łatwo było zobaczyć, co się przy komentarzu zmieniło.

**Jak to działa:** wątek niesie iterację, na której go napisano (`pullRequestThreadContext.iterationContext.secondComparingIteration`, z odwrotem na `first…`). `PullRequestDetails` niesie teraz listę iteracji z ich commitami. Jeśli iteracja wątku jest starsza niż ostatnia, wątek dostaje etykietę „Kod zmienił się po tym komentarzu (iteracja N → M)”, a w szynie kropkę. Przycisk „Zobacz, co się zmieniło” otwiera diff pliku **między commitem tamtej iteracji a głową** — to samo, co w Azure DevOps robi widok „update N”. Nad diffem stoi pasek mówiący wprost, że to nie jest pełna zmiana, z powrotem do pełnego diffu.

**SHA nie wychodzą do przeglądarki jako parametr.** Frontend podaje `sinceIteration=N`, a backend sam rozwiązuje commit z listy iteracji tego PR — inaczej byłaby to dowolna para commitów podana przez klienta.

**Świadome uproszczenie:** przy takim porównaniu plik dostaje `ChangeType = "edit"`. Plik dodany po tamtej iteracji pokazałby wtedy pustą stronę źródłową zamiast zostać oznaczony jako dodany; odwrotnie byłoby gorzej, bo „add” ukryłoby starą wersję, czyli dokładnie to, po co się tu przychodzi.

**Zweryfikowane:** 95 testów backendu (odczyt iteracji wątku i jej brak przy wątku bez kontekstu diffu) i 45 testów frontendu (oflagowany jest tylko wątek ze starszej iteracji; szkic z kliknięcia w linię nie opuszcza diffu i wysyła dopiero na drugi krok; `+` pojawia się na linii pod kursorem, ale nie tam, gdzie wątek już jest; „Zobacz, co się zmieniło” woła diff z `sinceIteration`, a powrót bez niego; pisanie komentarza wymaga dwóch kroków i czyta wątki ponownie; szukanie po treści i grupowanie po pliku).

**Niesprawdzone — całe 4B i 4C na żywym PR.** Nic z tego nie było uruchomione przeciwko prawdziwemu Azure DevOps: ani zapis z rozszerzonym PAT, ani semantyka `offset`, ani to, czy 409 w ogóle występuje, ani wygląd znaczników na marginesie. Wyłącznik jest domyślnie wyłączony właśnie dlatego.

## Wdrożone — widoczność komentarzy i resolve

### B-22 — Komentarze widoczne w pliku, oznaczone w drzewie, z resolve

Status: zaimplementowane. Prośba użytkownika po obejrzeniu B-20.

**W drzewie plików** plik z komentarzami dostaje odznakę `💬 N`, wyróżnioną kolorem akcentu, gdy któryś jest nierozwiązany. Foldery sumują to przez wszystkie poziomy, więc zwinięty folder nadal mówi, że w środku coś czeka. Folder w całości przeczytany **nie** zwija się, dopóki został w nim nierozwiązany komentarz — inaczej zniknąłby razem z nim.

**Nad diffem** stoi pasek ze wszystkimi wątkami tego pliku jako żetony (`💬 linia 42`, `✓ linia 20`). Komentarz jest widoczny od razu po otwarciu pliku, zamiast czekać, aż ktoś trafi w znacznik na marginesie. Kliknięcie żetonu otwiera rozmowę pod nim.

**Rozwiązane a nierozwiązane.** „Rozwiązany” znaczy to, co w Azure DevOps: `fixed`, `wontFix` albo `closed`. Wątek bez statusu liczy się jako czekający. Rozwiązany wątek ma wyblakły znacznik `💬` na marginesie, wyblakły żeton z `✓` i wyblakłą kartę w widoku komentarzy; `+` na hoverze nie pojawia się na linii, która już ma wątek — rozwiązany czy nie.

**Resolve** jest osobną akcją „Rozwiąż” (ustawia `fixed`) w widoku komentarzy **i** w bloku nad diffem, obok „Nie naprawimy” i „Otwórz ponownie”. Do tego **„Odpowiedz i rozwiąż”** — dwa żądania po kolei, bo Azure DevOps nie ma jednego łączonego; status zmienia się dopiero, gdy odpowiedź jest zapisana, bo to odpowiedź jest tu rzeczą ważną.

**Zweryfikowane:** 48 testów frontendu. Doszły cztery: liczenie komentarzy przez poziomy drzewa i nierozwinięty folder trzymany otwarty przez nierozwiązany wątek; odznaka `💬 2` na pliku; podział znaczników na rozwiązane i nie; „Rozwiąż” z bloku nad diffem woła status `fixed` i przeładowuje wątki; „Odpowiedz i rozwiąż” wykonuje odpowiedź **przed** zmianą statusu.

**Doszło po informacji zwrotnej:** w widoku komentarzy sama ścieżka i numer linii nic nie mówią, więc każdy wątek pokazuje **kod, którego dotyczy** — trzy linie kontekstu plus linia zakotwiczenia, podświetlona i z numerami. Pobieramy **jeden diff na plik**, nie na wątek, i trzymamy go, dopóki PR jest otwarty; plik binarny albo za duży po prostu nie ma wycinka. Komentarz na linii usuniętej czyta wersję sprzed zmiany, bo tam ta linia istnieje. Limit 20 plików z komentarzami (`ponytail`).

**Odczyt potwierdzony na żywym Azure DevOps (2026-09-20):** PR 1904 w `SmartIT_Monorepo` zwraca 8 wątków z plikami, liniami i iteracjami; wątki systemowe są odfiltrowane. Wcześniejszy brak komentarzy wynikał z tego, że działał proces backendu sprzed etapu 4 — trasa `/threads` zwracała 404.

**Czytelność i układ, po informacji zwrotnej:**
- Treść komentarza to **Markdown**, renderowany tym samym sanityzowanym torem co opis PR (`markdown-it` z `html:false` + DOMPurify). Surowy tekst zamieniał każdy pogrubiony nagłówek i fragment kodu w szum. Znaczniki narzędziowe w rodzaju `<!--review-swarm-->` są usuwane przed renderem, bo przy `html:false` markdown-it by je wypisał. W szynie zostaje jedna linia podglądu ze zdjętą interpunkcją Markdowna.
- Otwarcie komentarza **przewija diff do jego linii** (`setPosition` + `revealLineInCenter`), inaczej rozmowa zajmowała miejsce, w którym był kod, a linia znikała pod zgięciem.
- **Komentarze renderują się pomiędzy liniami kodu** (Monaco *view zones*), tak jak w Azure DevOps. Kontener tworzy `MonacoDiff` i oddaje go przez zdarzenie `zones`; treść wjeżdża tam Vue'owym `<Teleport>`, więc odpowiadanie i resolve to zwykły Vue, a nie ręcznie budowany DOM. Wysokość strefy bierze `ResizeObserver` z kontenera. Strefy dodajemy i usuwamy pojedynczo, nigdy hurtowym resetem, i odtwarzamy je po `onDidUpdateDiff`, bo diff editor przelicza wtedy własne strefy (wyrównanie i linie usunięte w widoku w linii). To **odwrócenie zalecenia z PLAN.md §4.5 wariant C** — na prośbę użytkownika, świadomie.
- **Blok był nieklikalny.** Monaco dokłada `.view-lines` **po** `.view-zones` (`view.js`), a warstwa linii to bezwzględnie pozycjonowany prostokąt na całą treść — malowała się na komentarzu i przechwytywała każde kliknięcie, stąd kursor tekstowy nad przyciskami. Strefa i tak jest `position:absolute`, więc wystarczyło `z-index: 2` na `.comment-zone`. Testy mockują Monaco, więc **tego nie potwierdza żaden test** — dowodem jest kolejność `appendChild` w źródle Monaco i sprawdzenie w przeglądarce.
- Blok jest wyrównany do kodu, nie wcięty w prawo. Zwija się kliknięciem w **cały nagłówek** (caret to podpowiedź, nie jedyny cel), a przełącznik „Ukryj komentarze” w pasku wyjmuje je z kodu na całą sesję — wracasz do nich przełącznikiem albo żetonem nad diffem. Każdy blok zwija się osobno do jednej linii z podglądem, więc plik z czterema długimi komentarzami nie zamienia się w ścianę tekstu. Plik bez diffu tekstowego (binarny, za duży) nie ma edytora, więc tam zostaje blok dokowany nad diffem.
- Wyjaśnienie AI pliku jest teraz **zwijane** (`<details>`) i ma „Ukryj” — czyta się je raz, a trzymało górę panelu przez cały plik.
- **Nic nigdzie nie jest ucinane.** Blok rozmowy nad diffem ma własny pasek przewijania i natywne `resize: vertical`. Przycinanie z „Pokaż całość” w widoku listy komentarzy zostało usunięte — rozwijanie każdego wątku z osobna kosztowało więcej niż oszczędzało (informacja zwrotna użytkownika, 20 wrz 2026).
- Przy kilku komentarzach w pliku blok ma nawigację `‹ ›` i licznik „n z m”, a żetony nad diffem przewijają do swojej linii.

**Zapis włączony, nadal niesprawdzony na żywym PR:** `AzureDevOps:AllowComments=true` ustawione w User Secrets 20 wrz 2026 — bez niego CSS `.pr-workspace--readonly` chowa „Odpowiedz”, „Rozwiąż”, „Edytuj” i „Usuń”, co wyglądało jak brak funkcji. Samo wysłanie wątku, odpowiedzi i zmiany statusu do Azure DevOps nadal nie zostało potwierdzone przeciw prawdziwemu PR.

## Wdrożone — pięć poprawek z przeglądu UX

### B-23 — Martwy klik, ukryte liczby, daty, `Esc` i wyłącznik

Status: zaimplementowane we frontendzie. Wynik przeglądu UX na prośbę użytkownika; wybrał pięć pozycji z listy znalezisk.

**Kliknięcie lokalizacji w widoku komentarzy nic nie robiło.** `openThread` wołało `openFile`, ale widok komentarzy zajmuje **ten sam** panel co diff, więc plik wczytywał się za nim. `showChangesSinceComment` robiło to poprawnie od początku — różnica była jedną linią. Teraz widok się zamyka, a wątek otwiera się przy swoim kodzie.

**Szyna liczyła wszystkie wątki, reszta aplikacji nierozwiązane.** Szyna mówi teraz „N nierozwiązanych z M", tak samo jak widok. Do nagłówka PR doszedł licznik `💬 N` wchodzący w widok komentarzy — nagłówek jest jedynym paskiem, który zostaje na ekranie w trybie skupienia, więc bez tego nierozwiązany wątek znikał razem z szyną.

**Daty komentarzy były pobierane od B-18 i nigdy nie pokazywane.** `publishedDate` szło przez mapper aż do typu `PrComment` we froncie i kończyło w niczym. Przy etykiecie „kod zmienił się po tym komentarzu" wiek wpisu jest drugą połową tej informacji.

**`Esc` wychodził z całego PR-a jednym naciśnięciem** — razem z otwartą edycją, szkicem i rozmową. Teraz odkleja po jednej warstwie: pomoc → potwierdzenie usunięcia → edycja → szkic → rozmowa → widok komentarzy → tryb skupienia → lista. Klawisz działa też **wewnątrz pola szkicu**, bo tam się po niego sięga; w każdym innym polu tylko zdejmuje focus.

**`AzureDevOps:AllowComments` poznawało się z 503 po napisaniu komentarza.** Doszło `GET /api/config` z jedną flagą. Gdy pisanie jest wyłączone, akcje zapisu **znikają** zamiast się wyszarzać (wraz z `+` na marginesie), a nad listą stoi zdanie mówiące, czego brakuje. Sonda nie może wywrócić aplikacji: każdy błąd zostawia pisanie włączone, bo prawdziwą bramką jest backend.

**Zweryfikowane:** 59 testów frontendu (doszły trzy: otwarcie pliku z widoku komentarzy zamyka widok i pokazuje diff wraz z datą i licznikiem w nagłówku; `Esc` kasuje szkic i zostaje w PR, a drugi wychodzi; przy wyłączonym pisaniu nie ma żadnej akcji zapisu, a czytanie działa). `npm run build` przechodzi.

**Niesprawdzone:** backendu nie przebudowano w tej zmianie — działał proces `PRCockpit.Api`, który trzyma DLL-e, a kod backendu i tak nie był ruszany. Wygląd licznika w nagłówku i dat przy komentarzach nie był oglądany w przeglądarce.

## Wdrożone — przejście prowadzone przez PR (epik US-P0…US-P7)

Źródło: brief BA/PO „Przejście prowadzone przez PR" z 20 wrz 2026, wersja 1.0. Decyzje
ramowe D-01…D-05 przyjęte bez zmian. Kolejność implementacji zgodna z §2 briefu.

### US-P0 — blokada nasłuchu poza localhostem

Status: zaimplementowane, pokryte testami. Narzędzie nie ma uwierzytelniania (D-01), więc
adres nasłuchu jest jedyną rzeczą trzymającą token przy jednej maszynie. `LocalOnly` czyta
`urls`, `ASPNETCORE_URLS` i `Kestrel:Endpoints:*:Url`; cokolwiek innego niż pętla zwrotna
zatrzymuje start z komunikatem. `*`, `+` i `0.0.0.0` liczą się jako adres zewnętrzny, bo
`Uri` ich nie parsuje, a to właśnie one otwierają port na sieć. Świadome wyłączenie:
`Security:AllowRemoteAccess=true` — wtedy start przechodzi z ostrzeżeniem.

### US-P1 — Summary generowane przy otwarciu PR — **odrzucone przez użytkownika**

Status: zaimplementowane 20 wrz 2026, **wycofane tego samego dnia** na wyraźne polecenie
użytkownika. Powód: każde uruchomienie modelu to pieniądze i decyzja o wydaniu ma należeć
do człowieka, a nie do ekranu, który się otworzył. To nadpisuje US-P1 z briefu — brief
zakładał, że automat jest tym, co usuwa tarcie; właściciel budżetu uznał tarcie za tańsze.

Co zostało zamiast automatu:
- Zapisane Summary wczytuje się jak dotąd i **nic nie kosztuje**, więc drugie otwarcie PR
  pokazuje propozycję ścieżki od razu.
- Brak zapisanego Summary → ekran wejścia mówi, że propozycji nie ma, i daje jeden przycisk
  „Zaproponuj ścieżkę (uruchomi AI)". Wyjście do pełnego drzewa jest obok, od razu.
- Summary z wcześniejszego commita → propozycja jest pokazywana (to nadal ranking), z
  informacją, że pochodzi ze starszego commita, i przyciskiem „Przelicz (uruchomi AI)".
  Przeliczanie też kosztuje, więc też czeka na kliknięcie.
- Raport pokrycia kontekstu („Kontekst: a / b plików") jest widoczny także na ekranie
  wejścia — przy dużym PR mówi, na ilu plikach ranking naprawdę powstał.

Decyzja `[wymagana decyzja]` o bardzo dużych PR jest przez to bezprzedmiotowa: model rusza
tylko na kliknięcie, niezależnie od rozmiaru PR.

### US-P2 — wyjaśnienie pliku traci ważność po zmianie tego pliku (NIESP-04)

Status: zaimplementowane, pokryte testami. `pr_file_explanations` dostało kolumnę `BlobId`,
a decyzję podejmuje `ContentFreshness` w warstwie Domain: identyfikator treści, a gdy go nie
ma — commit głowy, dokładnie jak znacznik „Obejrzałem". Jeden dorzucony commit nie kasuje
już wyjaśnień plików, których nikt nie dotknął — co przy prefetchu (US-P5) było różnicą
między jednym a ośmioma uruchomieniami modelu.

**Odstępstwo od briefu, świadome:** „jedno miejsce w kodzie" nie wyszło dosłownie. Reguła
znacznika „Obejrzałem" liczy się w przeglądarce (`reviewState` w `App.vue`) na danych, które
front i tak ma; reguła wyjaśnienia liczy się w backendzie. To ta sama reguła w dwóch
językach, z odsyłaczem w komentarzu po obu stronach. Ujednolicenie wymagałoby endpointu
tylko po to, żeby front zapytał backend o coś, co już wie.

### US-P3…US-P7 — ekran wejścia, tryb przejścia, prefetch, domknięcie, wznowienie

Status: zaimplementowane, pokryte testami (`frontend/tests/Walkthrough.test.ts`).

- **Wejście (US-P3).** PR powyżej pięciu plików kodu otwiera się na propozycji, nie na
  drzewie. Domyślnie osiem plików z rankingu (dolna granica 3, ranking daje do 10), bez
  plików już przeczytanych i **bez szumu** — szum można dołożyć ręcznie, ale AI go nie
  proponuje. Lista jest edytowalna przed startem i dopiero „Rozpocznij przejście" zapisuje
  ścieżkę czytania; do tego czasu nic nie leci do bazy. Brak rankingu → zdanie, że
  propozycja jest niedostępna, i wyjście do drzewa.
- **Przejście (US-P4).** Pełna szerokość, drzewo i szyna schowane, pasek z pozycją
  w **ścieżce** (nie w liście z Azure DevOps — NIESP-05 zostaje w widoku drzewa i nie jest
  tu naprawiany). Akcje: przeczytane/dalej, pomiń, wstecz, wyjście — przyklejone na dole,
  więc dostępne bez przewijania diffu. `m`, `j`, `k` robią w przejściu to samo co w drzewie,
  tylko po ścieżce; nowych skrótów nie ma. Licznik nierozwiązanych komentarzy stoi
  w nagłówku PR, który zostaje na ekranie.
- **Prefetch (US-P5).** Po akceptacji ścieżki wyjaśnienia powstają w tle, w kolejności
  ścieżki, po dwa naraz, dla całej ścieżki. Wynik ląduje w pamięci przeglądarki, więc
  wejście na plik nie ma stanu oczekiwania; jeśli użytkownik wyprzedzi prefetch, diff i tak
  renderuje się natychmiast, a zdania dojeżdżają bez przeładowania. Błąd na jednym pliku
  zostaje przy tym pliku, z ponowieniem. Wyjście z PR albo z przejścia przerywa prefetch
  (`AbortController`).
- **Domknięcie (US-P6).** Liczby (przeczytane / pominięte / poza ścieżką), lista pominiętych
  z powrotem jednym kliknięciem, pytanie Debug Check i dwa wyjścia. Żaden krok checklisty
  nie zaznacza się sam.
- **Wznowienie (US-P7).** `pr_reading_paths` dostało `Position` i `HeadCommitSha`. Ścieżka
  bez `HeadCommitSha` pochodzi z szyny, nie z przejścia, i nie jest proponowana do wznowienia.
  Inny commit głowy niż w chwili wyboru → informacja i wybór: wznów albo przelicz; nic nie
  jest kasowane bez decyzji. Pozycja jest przycinana do długości ścieżki przy odczycie,
  a plik, który zniknął z PR, wypada ze ścieżki, więc przejście nie prowadzi donikąd.

**Parametry (`[wymagana decyzja]` z briefu).** Wszystkie na wartościach domyślnych:
długość ścieżki 8, prefetch 2 równolegle, prefetch całej ścieżki. Siedzą jako nazwane stałe
`walkDefaultLength` i `walkParallelPrefetch` na górze `App.vue`, z komentarzem
`// ponytail:` — jednoosobowe narzędzie lokalne nie ma pliku ustawień, a zakładanie go dla
dwóch liczb byłoby tym, czego brief kazał unikać. Ścieżka wyjścia: ekran ustawień, jeśli
liczby zaczną się zmieniać.

**Zweryfikowane:** 117 testów backendu, 77 testów frontendu (doszło 18 w `Walkthrough.test.ts`
i 4 w backendzie), `npm run build` i `dotnet build` przechodzą. Testy pilnują też tego, że
otwarcie PR **nie** uruchamia modelu.

Migracje `AddExplanationBlobId` i `AddWalkthroughPosition` zastosowały się na lokalnej bazie
przy starcie backendu (`dotnet ef migrations list` pokazuje obie jako wykonane). Blokada
nasłuchu została sprawdzona na żywym procesie: `--urls http://0.0.0.0:5199` zatrzymuje start
z komunikatem, profil `http` na localhoście startuje normalnie.

**Niesprawdzone:** całości nie oglądano w przeglądarce ani na prawdziwym PR — layout ekranu
wejścia, paska przejścia i ekranu domknięcia nie był widziany. Nie sprawdzono też, czy
prefetch dwóch wyjaśnień naraz jest znośny dla realnego adaptera AI: testy używają atrapy,
która odpowiada natychmiast.

**Poza zakresem, świadomie (za briefem §5):** NIESP-05 i NIESP-06 zostają otwarte, Project
Memory i Quality Review nietknięte, żadnego uwierzytelniania ani grywalizacji. Kontrakt
wyniku AI bez zmian — ranking wystarczył taki, jaki jest.

## Wdrożone — „AI CLI returned invalid JSON" da się teraz zdiagnozować

Status: zaimplementowane, pokryte testami. Zgłoszenie użytkownika 20 wrz 2026.

**Czego nie udało się odtworzyć.** Adapter uruchomiony ręcznie na trzech kontekstach —
sztucznym jednoplikowym, prawdziwym PR 1906 (44 pliki) i PR 1900 (208 plików) — za każdym
razem zwrócił poprawny JSON. Dwa równoległe wyjaśnienia plików (to, co wprowadził prefetch
z US-P5) też przeszły. Błąd jest więc **niedeterministyczny** i przyczyna pozostaje
nieustalona.

**Dlaczego nie dało się jej ustalić.** Backend wyrzucał wszystkie dowody: stderr adaptera
był świadomie pomijany (`_ = await errorTask`), a nieparsowalne stdout nie trafiało nigdzie.
Zostawał 502 bez śladu. To była prawdziwa luka — diagnostyka jednego zgłoszenia kosztowała
kilkanaście minut i trzy uruchomienia modelu zamiast zajrzenia do logu.

Dwie zmiany:

- **`CliSummaryAnalyzer` mówi, co przyszło.** Przy nieudanym parsowaniu i przy kodzie wyjścia
  różnym od zera loguje pierwsze 400 znaków stdout i stderr na poziomie Warning. Do klienta
  nadal nie idzie nic z tej treści — to wyjście cudzego procesu, nie nasze do przekazywania —
  ale operatorem narzędzia jednoosobowego jest ta sama osoba, która patrzy w konsolę.
- **Adapter wyjmuje JSON zamiast ufać modelowi.** `scripts/extract-json.mjs` bierze zewnętrzny
  obiekt `{…}`, licząc klamry poza literałami tekstowymi. Radzi sobie z płotem ```json,
  zdaniem przed odpowiedzią i po niej oraz z notką CLI na tym samym strumieniu — czyli
  z najczęstszymi powodami, dla których wyjście modelu w trybie tekstowym nie jest czystym
  JSON-em. Czego nie rozumie, oddaje bez zmian, żeby log pokazał prawdę, a nie ucięty domysł.

**Zweryfikowane:** 117 testów backendu, 87 frontendu (doszło 10 na `extractJson`,
uruchamianych tym samym `npm test`). Adapter sprawdzony end-to-end po zmianie — czysty JSON
przechodzi nietknięty.

**Uzupełnienie tego samego dnia.** Przy okazji pytania o model wyszło, że CLI pisze własną
prozę na stdout (odrzucony `--model` drukuje tam „There's an issue with the selected
model…"), a taki komunikat potrafi nieść obiekt JSON, który **nie jest** odpowiedzią —
`[claude-code:unrecognized_model] {"model":"…","query_source":"sdk"}`. Pierwsza wersja
`extractJson` brała pierwszy napotkany obiekt, więc podałaby backendowi właśnie ten: 502
„AI returned an invalid Summary" i **ani linii w logu**, bo logowanie dodaliśmy przy błędzie
parsowania, a nie przy nieudanej walidacji. Teraz kandydat liczy się tylko wtedy, gdy ma
kształt odpowiedzi (pole `sentences`); reszta idzie dalej nietknięta, a skaner szuka kolejnego
obiektu. Uwaga na uczciwość zapisu: **nieudane uruchomienie CLI kończy się kodem wyjścia 1**,
nie 0 — backend łapie je wcześniej na kodzie wyjścia, więc ten konkretny komunikat i tak by
tu nie dotarł. Zabezpieczenie dotyczy tego, co CLI wypisze obok **udanego** przebiegu.
(Pierwotnie zapisano tu, że CLI wychodzi z zerem; to była pomyłka w odczycie — kod wyjścia
pochodził z `head` w potoku.)

**Wciąż nieszczelne:** nieudana **walidacja** odpowiedzi (`SummaryRunner`) nadal nie loguje
tego, co przyszło — `SummaryRunner` żyje w warstwie Application i nie ma loggera. Jeśli 502
będzie brzmiało „AI returned an invalid Summary", log nadal nic nie powie.

**Niesprawdzone:** ponieważ pierwotnego błędu nie udało się odtworzyć, **nie wiadomo, czy to go naprawia**.
Jeśli wróci, w logu backendu stanie teraz linia Warning z treścią odpowiedzi i to ona
powie, co się dzieje. Gdyby przyczyną okazały się dwa oddzielne obiekty JSON w jednej
odpowiedzi albo limit 16 384 znaków wyjścia, `extractJson` tego nie załatwia.

## Wdrożone — dwa zakresy przejścia i kolejność czytania całego PR

Status: zaimplementowane, pokryte testami. Prośba użytkownika z 21 wrz 2026.

**Ranking układał ważnością, nie kolejnością czytania.** „Most important first" w instrukcji
dawało listę od najważniejszego, a to co innego niż kolejność, w której zmiana daje się
zrozumieć. Instrukcja prosi teraz o kolejność, która idzie za samą zmianą: od miejsca, gdzie
się zaczyna, przez to, czego dotyka, do skutków. Kierunek wybiera model z tego PR — zmiana
backendowa zwykle domena → dostęp do danych → endpoint → UI, zmiana zaczynająca się od guzika
odwrotnie. Świadomie **nie** ma tu sztywnej listy warstw po nazwach katalogów: recenzowane
repozytoria mają różne układy i heurystyka po ścieżkach zgadywałaby tam, gdzie kontekst wie.

**Nie było trybu na cały PR.** Alternatywą dla ścieżki było drzewo, czyli ta sama ściana
osiemdziesięciu plików. Summary zwraca teraz drugie pole, `readingOrder` — każdy zmieniony
plik dokładnie raz, w tej samej logice narracyjnej, szum na końcu. Ekran wejścia dostał
przełącznik dwóch zakresów: „Kluczowe pliki" (jak dotąd) i „Wszystkie pliki". Oba czytają
tę samą odpowiedź modelu, więc przełączenie nie kosztuje ani jednego wywołania.

**`readingOrder` jest polem wersji 2, nie wersją 3.** Bump unieważniłby każde zapisane
wyjaśnienie pliku w każdym PR — oba magazyny odrzucają zapis o innej wersji schematu —
a każde z nich to opłacone wywołanie modelu. Ceną jest to, że Summary sprzed tej zmiany nie
ma kolejności całego PR; tryb „wszystkie pliki" mówi to wprost i proponuje przeliczenie,
zamiast pokazać kolejność, której nie dostał.

**Prefetch dostał okno.** Wyjaśnienia ścieżki powstawały dla całej ścieżki naraz — przy
ścieżce na cały PR znaczyłoby to osiemdziesiąt wywołań modelu przy starcie przejścia, które
porzuca się po dziesiątym pliku. Teraz kupowane jest okno dziesięciu plików przed kursorem,
przesuwane przy każdym kroku. Przesunięcie okna nie przerywa żądania w locie (jest opłacone,
ma dolecieć do pamięci); abort został tam, gdzie wyniku nikt nie przeczyta — przy wyjściu
z PR albo z przejścia. Ścieżka kluczowych plików jest krótsza od okna, więc jej to nie dotyka.

**Przy okazji, bo ścieżka urosła:** `MaxReadingPath` w magazynie podniesiony z 25 do 2000
(ta sama strona listy zmian, której używa klient Azure DevOps), trzy zaszyte „10" w szynie
zamienione na `criticalFileLimit`, a lista ścieżki w szynie rysuje pierwsze 15 pozycji
i mówi, ile zostało — po przejściu całego PR miałaby ich tyle, ile PR ma plików.

**Zweryfikowane:** 133 testy backendu (doszły 4: kolejność modelu zachowana, luki dopisane,
szum na końcu, odrzucenia poza kontraktem, ścieżka dłuższa od skrótu), 99 frontendu (doszło 5:
pełna lista w kolejności, zapis pełnej ścieżki, powrót do skrótu, brak kolejności → prośba
o przeliczenie, okno prefetchu). Oba buildy przechodzą.

**Niesprawdzone:** nic z tego nie było widziane w przeglądarce ani na prawdziwym PR.
W szczególności nie wiadomo, czy model faktycznie zwraca **wszystkie** ścieżki przy PR o 80+
plikach i czy jego kolejność jest lepsza od kolejności listy zmian — walidacja pilnuje
kontraktu, nie jakości. Nie zmierzono też, ile tokenów dokłada `readingOrder` do odpowiedzi.
## Wdrożone — czytelniejsza podpowiedź i ranking skalowany rozmiarem PR

Status: zaimplementowane, pokryte testami. Dwie uwagi użytkownika z 20 wrz 2026.

**„Pokaż, gdzie szukać" było nieczytelne.** Lista pokazywała pełną ścieżkę w `<code>`,
myślnik i powód — przy ścieżkach w rodzaju
`/apps/ekobill/server/Ekobill.Server/Modules/MasterData/Services/PpeService.cs` ścieżka
zjadała cały wiersz, zawijała się, a powód, po który się tam sięga, lądował na końcu
szarym drobnym drukiem. Teraz każdy wpis ma numer pozycji w kółku (kolejność rankingu to
informacja), nazwę pliku wytłuszczoną, katalog obok niej drobno i przygaszony, a powód
w osobnym wierszu normalnym kolorem tekstu. Pełna ścieżka została w `title`. Ten sam układ
w obu miejscach: w szynie i na ekranie domknięcia.

**Dziesięć plików to za mało przy dużym PR.** Ranking był na sztywno ucięty do 10 w czterech
miejscach (instrukcja dla modelu, walidacja odpowiedzi, magazyn, front). Przy PR o 80+
plikach dziesiątka przestaje być punktem startu — staje się próbką. `SummaryContract.CriticalFileLimit`
daje teraz mniej więcej jeden wskazany plik na cztery zmienione, z podłogą 10 i sufitem 25:
20 plików → 10, 44 → 11, 84 → 21, 200 → 25. Ta sama liczba idzie do instrukcji dla modelu
i do walidacji, więc model nie jest proszony o więcej, niż wolno mu zwrócić.

Domyślna długość ścieżki przejścia też rośnie, ale **wolniej** — jeden plik na osiem
zmienionych, między 8 a 12. To decyzja, nie przeoczenie: „widoczny koniec" był jednym z
trzech warunków powodzenia wycinka, a ścieżka na 21 plików przestaje go spełniać. Ranking
pokazuje wszystko, co model uznał za ważne; przejście domyślnie bierze z tego tyle, ile da
się przeczytać za jednym posiedzeniem, a resztę można dokliknąć na ekranie wejścia.

**Zweryfikowane:** 125 testów backendu (doszło 8, w tym tabelka progów), 89 frontendu.
Oba buildy przechodzą.

**Niesprawdzone:** nie widziałem nowej podpowiedzi ani dłuższej propozycji w przeglądarce,
i nie sprawdzono, czy model faktycznie zwraca 21 sensownych plików przy PR tej wielkości —
limit pozwala, ale o jakości listy decyduje model.

## Wdrożone — dwie pułapki w dokumentacji zamknięte (NIESP-09, NIESP-10)

Status: zaimplementowane. Dwie najtańsze pozycje z przeglądu 20 wrz 2026.

- **NIESP-09.** `README.md` kazał pisać własny adapter na `schemaVersion: 1` i pokazywał
  odpowiedź bez `criticalFiles`, a `SummaryContract.SchemaVersion` wynosi **2** i odrzuca
  taką odpowiedź jako „niepoprawny wynik”. Instrukcja opisuje teraz wersję 2, oba zadania
  (`summary` i `file`), listę `criticalFiles` z regułą liczebności (jeden plik na cztery
  zmienione, od 10 do 25) i mówi wprost, że wersja 1 jest odrzucana. Kontrakt **nie** został
  wydzielony jako wersjonowany załącznik — przy jednym adapterze referencyjnym nie ma czego
  wersjonować osobno.
- **NIESP-10.** `docs/ARCHITECTURE.md` twierdził „bez edycji i bez kasowania”, choć jedno
  i drugie działa dla własnych komentarzy. Zapis wymienia je w zakresie i mówi, że o pokazaniu
  akcji decyduje `isMine`.

Oba wpisy w `docs/specs/pr-cockpit.md` §15 są oznaczone jako zamknięte z rozstrzygnięciem.

## Wdrożone — filtr „co przyszło po aktualizacji N”

Status: zaimplementowane, pokryte testami. Prośba użytkownika: tak, jak to działa na Azure
DevOps. Domyka pętlę, której narzędzie dotąd nie obsługiwało — **drugiego przejścia po PR,
po poprawkach**: filtr zawęża drzewo do plików tkniętych po wybranej aktualizacji, a otwarty
z niego plik pokazuje wyłącznie to, co w nim dopisano.

**Per iteracja, nie per commit — świadomie.** Azure DevOps grupuje commity w iteracje (jedna
na push) i porównuje wyłącznie całe iteracje. Commita ze środka pushu nie da się odseparować
bez liczenia diffu commit-do-commita u siebie, czyli bez drugiego, własnego mechanizmu diffu.
Lista rozwijana pokazuje więc aktualizacje, opisane tytułem commita zamykającego każdą z nich
— a to i tak odpowiada na pytanie, dla którego się tam sięga („co przyszło od mojego ostatniego
przejścia”).

**Koszt: jeden parametr, jeden endpoint, jedna lista rozwijana.** `$compareTo` w wywołaniu
`/iterations/{id}/changes` było zaszyte na 0; teraz jest parametrem, a `0` nadal znaczy „cały
PR”. `GET .../changed-paths?sinceIteration=N` zwraca **same ścieżki** — przeglądarka ma już
wiersze ze zmianami i potrzebuje tylko wiedzieć, które zostawić. Diff od iteracji istniał od
B-21 („co się zmieniło po tym komentarzu”), więc `openFile` bierze numer iteracji z filtru
i nic nowego po stronie diffu nie powstało.

**Decyzje przy krawędziach:** wybór ostatniej iteracji zwraca pustą listę bez żądania o zmiany
(porównanie najnowszego pushu z samym sobą nie jest błędem wartym 404); iteracja spoza PR to
404, iteracja ≤ 0 to 400. Błąd filtru **kasuje filtr i mówi o tym** — filtr, który po cichu
pokazuje wszystko, kłamie gorzej niż jego brak. Spóźniona lista z poprzedniego PR jest
odrzucana własnym licznikiem żądań, jak reszta ładowań. Skok do wątku komentarza zawsze
otwiera pełny diff, bo wątek jest zakotwiczony w linii całego pliku, której w zawężonym diffie
może nie być.

**Zweryfikowane:** 129 testów backendu (doszły 4: `$compareTo` trafia do wywołania, ostatnia
iteracja nie wywołuje żądania o zmiany, dwa przypadki odmowy) i 94 frontendu (doszły 3:
zawężenie drzewa i diffu wraz z powrotem do pełnego diffu, błąd filtru, spóźniona odpowiedź).
`npm run build` przechodzi. Backend zbudowany i przetestowany z przekierowanym katalogiem
wyjściowym, bo działający `PRCockpit.Api` trzymał `backend/*/bin` — wynik ten sam, ale
`dotnet test PRCockpit.slnx` bez uruchomionego backendu nie był tu uruchomiony.

**Niesprawdzone:** nie widziane w przeglądarce ani na prawdziwym PR. W szczególności nie
sprawdzono, czy Azure DevOps zwraca dla `$compareTo=N` dokładnie to, czego oczekujemy, przy
PR z rebasem albo z wymuszonym pushem — iteracje przestają wtedy być prostym ciągiem.

## Później — do osobnej decyzji

- **Dalszy Understand PR:** główny flow i automatyczny wybór ważnych plików po sprawdzeniu Summary.
- **Komentarze Azure DevOps:** wątki PR, najpierw do odczytu (bez zmiany PAT), potem zapis za wyłącznikiem konfiguracyjnym.
- **Quality i Architecture:** zgodnie z [PRODUCT.md](../PRODUCT.md), po potwierdzeniu podstawowego workflow.
- **Lokalne repozytorium i zapis pozostałych analiz:** osobne etapy po Summary.
