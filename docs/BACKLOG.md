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

## Później — do osobnej decyzji

- **Dalszy Understand PR:** główny flow i automatyczny wybór ważnych plików po sprawdzeniu Summary.
- **Komentarze Azure DevOps:** wątki PR, najpierw do odczytu (bez zmiany PAT), potem zapis za wyłącznikiem konfiguracyjnym.
- **Quality i Architecture:** zgodnie z [PRODUCT.md](../PRODUCT.md), po potwierdzeniu podstawowego workflow.
- **Lokalne repozytorium i zapis pozostałych analiz:** osobne etapy po Summary.
