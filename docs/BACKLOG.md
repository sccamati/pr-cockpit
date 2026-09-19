# Backlog operacyjny PR Cockpit

Stan: 2026-09-20. Długoterminowy cel i epiki są w [PRODUCT.md](../PRODUCT.md). Ten plik wskazuje kolejność najbliższych prac i warunki ich zakończenia. Po każdym zadaniu aktualizujemy jego status; nowe pomysły trafiają do sekcji „Później”, dopóki nie wybierzemy ich do realizacji.

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

## Później — do osobnej decyzji

- **Dalszy Understand PR:** główny flow i automatyczny wybór ważnych plików po sprawdzeniu Summary.
- **Quality i Architecture:** zgodnie z [PRODUCT.md](../PRODUCT.md), po potwierdzeniu podstawowego workflow.
- **Lokalne repozytorium i zapis pozostałych analiz:** osobne etapy po Summary.
