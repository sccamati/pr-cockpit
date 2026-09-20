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

**Zakres v1:** założenie wątku (z kotwicą na plik i linię albo bez), odpowiedź, zmiana statusu. Bez edycji i bez kasowania. Kotwica zawsze po prawej stronie, `offset: 1` — tak jak w przykładzie z dokumentacji Azure DevOps. Statusy wysyłamy liczbą (`active 1, fixed 2, wontFix 3, closed 4`), wracają napisem.

**Żadnego zapisu optymistycznego.** Przycisk się blokuje, żądanie leci, wątki są pobierane ponownie. Szkic i potwierdzenie to zawsze dwa kroki, Enter nie wysyła, a pod przyciskiem stoi zdanie „Wysłanego komentarza nie da się cofnąć”.

**Zweryfikowane:** 95 testów backendu. Doszły m.in.: przy wyłączniku off żadne z trzech żądań nie opuszcza maszyny, nowy wątek jedzie z `rightFileStart` i bez `leftFileStart`, status leci liczbą i wraca nazwą, nieznany status i pusta treść kończą się 400 bez kontaktu z Azure DevOps, mapowanie 403/400/409 na 503/400/409, oraz **test antyregresyjny: każda ścieżka odczytu nadal wysyła `GET` i nie ma treści**.

### B-20 — Widok komentarzy i znaczniki w diffie

Status: zaimplementowane. Podetap 4C plus widok, o który poprosił użytkownik.

**Widok komentarzy** (skrót `c`) zajmuje środkowy panel obok diffu i opisu PR. Jest w nim wyszukiwarka po treści, autorze i ścieżce, filtr „Aktywne / Wszystkie”, licznik „N aktywnych z M” i grupowanie **po pliku w kolejności drzewa plików** — czyli w tej, w której czyta się PR — a w grupie po numerze linii. Stamtąd się odpowiada, zmienia status i zakłada nowy wątek. Szyna kontekstu została podsumowaniem: jedna linia na wątek plus wejście do widoku.

**Znaczniki na marginesie:** `glyphMargin: true` i `createDecorationsCollection` na edytorze **zmodyfikowanym** — przy `renderSideBySide: false` linie usunięte są strefami widoku bez adresowalnej pozycji, więc prawa strona jest jedyną, na której znacznik może stać. Kliknięcie w margines otwiera rozmowę dla tej linii, a linia bez wątku otwiera szkic zakotwiczony w niej. Przycisk „Skomentuj linię” bierze linię z kursora (`getPosition()` działa mimo `readOnly`). Nasłuch i kolekcja dekoracji dołączyły do istniejącego bloku zwalniania w `onBeforeUnmount`.

**Widoku view zones świadomie nie budujemy** — PLAN.md §4.5 wariant C konkurowałby ze strefami, których diff w linii już używa na linie usunięte.

### B-21 — „Co się zmieniło po tym komentarzu”

Status: zaimplementowane. Prośba użytkownika: żeby po poprawce łatwo było zobaczyć, co się przy komentarzu zmieniło.

**Jak to działa:** wątek niesie iterację, na której go napisano (`pullRequestThreadContext.iterationContext.secondComparingIteration`, z odwrotem na `first…`). `PullRequestDetails` niesie teraz listę iteracji z ich commitami. Jeśli iteracja wątku jest starsza niż ostatnia, wątek dostaje etykietę „Kod zmienił się po tym komentarzu (iteracja N → M)”, a w szynie kropkę. Przycisk „Zobacz, co się zmieniło” otwiera diff pliku **między commitem tamtej iteracji a głową** — to samo, co w Azure DevOps robi widok „update N”. Nad diffem stoi pasek mówiący wprost, że to nie jest pełna zmiana, z powrotem do pełnego diffu.

**SHA nie wychodzą do przeglądarki jako parametr.** Frontend podaje `sinceIteration=N`, a backend sam rozwiązuje commit z listy iteracji tego PR — inaczej byłaby to dowolna para commitów podana przez klienta.

**Świadome uproszczenie:** przy takim porównaniu plik dostaje `ChangeType = "edit"`. Plik dodany po tamtej iteracji pokazałby wtedy pustą stronę źródłową zamiast zostać oznaczony jako dodany; odwrotnie byłoby gorzej, bo „add” ukryłoby starą wersję, czyli dokładnie to, po co się tu przychodzi.

**Zweryfikowane:** 95 testów backendu (odczyt iteracji wątku i jej brak przy wątku bez kontekstu diffu) i 43 testy frontendu (oflagowany jest tylko wątek ze starszej iteracji; „Zobacz, co się zmieniło” woła diff z `sinceIteration`, a powrót bez niego; pisanie komentarza wymaga dwóch kroków i czyta wątki ponownie; szukanie po treści i grupowanie po pliku).

**Niesprawdzone — całe 4B i 4C na żywym PR.** Nic z tego nie było uruchomione przeciwko prawdziwemu Azure DevOps: ani zapis z rozszerzonym PAT, ani semantyka `offset`, ani to, czy 409 w ogóle występuje, ani wygląd znaczników na marginesie. Wyłącznik jest domyślnie wyłączony właśnie dlatego.

## Później — do osobnej decyzji

- **Dalszy Understand PR:** główny flow i automatyczny wybór ważnych plików po sprawdzeniu Summary.
- **Komentarze Azure DevOps:** wątki PR, najpierw do odczytu (bez zmiany PAT), potem zapis za wyłącznikiem konfiguracyjnym.
- **Quality i Architecture:** zgodnie z [PRODUCT.md](../PRODUCT.md), po potwierdzeniu podstawowego workflow.
- **Lokalne repozytorium i zapis pozostałych analiz:** osobne etapy po Summary.
