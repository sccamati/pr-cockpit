# Specyfikacja funkcjonalna: PR Cockpit

**Obszar:** Narzędzia developerskie / przegląd zmian w kodzie (code comprehension)
**Data analizy:** 2026-09-20
**Status:** odtworzone z działającego prototypu — do walidacji przez PO / analityka / architekta

> **Jak czytać ten dokument.** Opisuje **zachowanie biznesowe**: co użytkownik widzi i robi,
> skąd biorą się dane, **jak dokładnie liczone są wskaźniki i limity** (§4), i gdzie są
> niespójności do rozstrzygnięcia (§15). Nie opisuje kodu ani architektury — docelową
> aplikację projektuje się na nowo.
>
> **Legenda:** **[FAKT]** = potwierdzone w prototypie · **[ZAŁOŻENIE]** = interpretacja do
> potwierdzenia · **[DEFEKT]** = błąd · **[RYZYKO]** = zagrożenie na przyszłość.

---

## 0. Streszczenie i mapa

PR Cockpit to **osobiste narzędzie jednego developera** do *rozumienia* pull requestów
w Azure DevOps — nie do ich zatwierdzania. Zakłada, że kod powstaje dziś szybciej (z pomocą
AI), niż człowiek jest w stanie go przeczytać, i że problemem nie jest jakość kodu, tylko
**utrata kontekstu aplikacji**. Narzędzie prowadzi więc przez PR jak przez lekturę: daje
krótkie streszczenie zmiany, proponuje 5–10 plików, od których warto zacząć, pamięta, które
pliki zostały już przeczytane (także po zamknięciu przeglądarki), pokazuje i pozwala pisać
komentarze wprost między liniami kodu i zmusza do jednej odpowiedzi na pytanie „gdzie
zacząłbym szukać, gdyby to nie zadziałało".

Wszystkie dane o PR są pobierane z Azure DevOps **na żądanie** — nie ma żadnej synchronizacji
ani kopii repozytorium. Lokalnie zapisywane są wyłącznie **własne decyzje użytkownika**
i wyniki analizy AI.

### Mapa ekranów

| Ekran / element | Co pokazuje | Jakie dane | Skąd dane |
|---|---|---|---|
| Wybór źródła | Lista projektów i repozytoriów | Nazwy projektów i repozytoriów | Azure DevOps, na żądanie |
| Lista PR | Aktywne pull requesty, postęp checklisty `x/6`, postęp czytania `x/y plików`, data utworzenia | Metadane PR + dwa liczniki lokalne | Azure DevOps + baza lokalna |
| Nagłówek PR | Numer, tytuł, status, gałęzie, autor, `Checklista x/6`, licznik nierozwiązanych komentarzy | Metadane PR + stan lokalny + wątki | Azure DevOps + baza lokalna |
| Panel lewy — lista zmienionych plików | Drzewo folderów, licznik przeczytanych, odznaki komentarzy, gwiazdka „ścieżka czytania", wydzielona sekcja „Szum" | Lista zmienionych plików ostatniej iteracji | Azure DevOps + baza lokalna |
| Panel środkowy — tryb 1: opis PR | Renderowany opis PR (ekran startowy) | Opis PR, powiązane Work Itemy | Azure DevOps |
| Panel środkowy — tryb 2: diff pliku | Porównanie obu wersji pliku, komentarze między liniami, wyjaśnienie AI pliku | Dwie pełne wersje pliku | Azure DevOps, na żądanie |
| Panel środkowy — tryb 3: widok komentarzy | Wszystkie wątki PR, pogrupowane po pliku, z fragmentem kodu | Wątki komentarzy | Azure DevOps |
| Szyna prawa — Summary | 2–5 zdań o PR, raport pokrycia kontekstu, informacja o aktualności | Wynik AI | Lokalny program AI + baza lokalna |
| Szyna prawa — Checklista PR | 6 ręcznych kroków | Stan lokalny | Baza lokalna |
| Szyna prawa — Komentarze | Skrót wątków, wejście do widoku komentarzy | Wątki | Azure DevOps |
| Szyna prawa — Debug Check | Jedno stałe pytanie + pole odpowiedzi + podpowiedź | Odpowiedź lokalna + ranking z Summary | Baza lokalna |
| Szyna prawa — Ścieżka kluczowych plików | Do 10 plików w wybranej kolejności, propozycja AI | Stan lokalny + ranking z Summary | Baza lokalna |
| Szyna prawa — Commity | Tytuł, autor, data każdego commita | Commity PR | Azure DevOps |
| Szyna prawa — Szczegóły PR | Autor, repozytorium, daty, gałęzie, liczba plików, reviewerzy, Work Itemy | Metadane PR | Azure DevOps |

### Co jest gotowe, a co nie

**Działa w pełni:** wybór projektu/repozytorium, lista aktywnych PR z dwoma licznikami
postępu, drzewo plików z odszumianiem, diff pliku z podpowiedziami typów dla plików C#,
trwała pamięć przeczytanych plików, ręczna ścieżka czytania, ręczna checklista 6 kroków,
Summary AI z rankingiem plików, wyjaśnienie AI pojedynczego pliku, pełna obsługa komentarzy
(czytanie, pisanie, odpowiedź, edycja i usunięcie własnych, zmiana statusu, komentarze
renderowane między liniami kodu), „co się zmieniło po tym komentarzu", skróty klawiszowe,
tryb skupienia, tryb ciemny.

**Zaplanowane, ale niezbudowane:** *Main flow* (graf przepływu przez zmienione komponenty),
*Quality Review* (findingi jakościowe z kategoriami i statusami), *Architecture Check*
(wykrycie wpływu na architekturę z akceptacją użytkownika), *Project Memory* (trwała wiedza
o projekcie: komponenty, moduły, integracje, hotspoty, historia PR), generowane scenariusze
awarii w Debug Check, dostęp do lokalnego klonu repozytorium. Szczegóły w §13.

**Świadomie poza zakresem:** wielu użytkowników, logowanie, uprawnienia, wdrożenie poza
maszyną developera. Patrz §2 i §9 — to najpoważniejsze ograniczenie całego prototypu.

### Top 3 do rozstrzygnięcia przed budową

1. **Czy produkt zostaje narzędziem jednoosobowym, czy staje się aplikacją zespołową?**
   Dziś nie ma żadnego uwierzytelniania, żadnej autoryzacji i żadnego pojęcia „użytkownika";
   cały dostęp opiera się na jednym tokenie technicznym trzymanym w konfiguracji serwera.
   Przejście na zespół to przebudowa modelu danych, nie dodanie ekranu logowania. (NIESP-01)
2. **Trzy różne definicje „nieaktualności" żyją obok siebie** — dla Summary, dla wyjaśnienia
   pliku i dla znacznika „Obejrzałem" — i dają sprzeczne odpowiedzi po dorzuceniu commita.
   (NIESP-04)
3. **Licznik „x/y plików" na liście PR ma mianownik z przeszłości** — zapamiętany przy
   ostatnim oznaczeniu pliku, a nie odczytany na bieżąco. (NIESP-06)

---

## 1. Cel i wartość biznesowa

**Problem.** Przy intensywnym korzystaniu z AI kod powstaje szybciej, niż developer jest
w stanie go przeczytać ze zrozumieniem. Duże PR-y mają dziesiątki lub setki plików;
developer „przegląda" diff, zamiast go rozumieć; po merge nie wiadomo, gdzie leży konkretna
logika; po kilku tygodniach trudno odtworzyć, dlaczego system działa tak, a nie inaczej.
Klasyczne AI review wskazuje błędy, ale **nie buduje modelu mentalnego aplikacji**.

**Cel.** Pozwolić developerowi korzystać z szybkości AI, zachowując kontrolę i zrozumienie
systemu. Po każdym PR użytkownik ma umieć odpowiedzieć: co się zmieniło, dlaczego, jak
wygląda główny przepływ, które pliki są naprawdę ważne, co zostało dotknięte, gdzie zacząć
debugowanie, czy zmiana rusza architekturę.

**Zasada nadrzędna:** *nie zastępujemy code review*. Inne narzędzia nadal szukają błędów.
PR Cockpit odpowiada za **zrozumienie i kontekst projektu** (comprehension + project context).

**Mierzalna wartość dzisiaj [FAKT]:** narzędzie zamienia „83 zmienione pliki → przewijanie
diffu → approve" na uporządkowaną lekturę z zapamiętanym postępem, w której nic nie wypada
z pamięci po zamknięciu karty przeglądarki i w której komentarz zespołu jest widoczny
dokładnie przy linii, której dotyczy.

---

## 2. Role i uprawnienia

**[FAKT] W systemie nie istnieje pojęcie użytkownika, roli ani uprawnienia.** Nie ma
logowania, sesji, kont ani autoryzacji jakiegokolwiek rodzaju. Aplikacja jest uruchamiana
lokalnie przez jedną osobę i nasłuchuje wyłącznie na tej maszynie.

Faktyczne „role" są dwie, obie techniczne:

| Rola | Kto/co | Co wolno |
|---|---|---|
| Użytkownik lokalny | Osoba uruchamiająca aplikację na swojej maszynie | Wszystko, co aplikacja potrafi — bez żadnych ograniczeń po stronie aplikacji |
| Tożsamość techniczna w Azure DevOps | Osobisty token dostępu wpisany do konfiguracji serwera | Zakres operacji na Azure DevOps ogranicza wyłącznie zakres tego tokenu |

**[FAKT] Uprawnienia do danych w Azure DevOps są w całości delegowane do tokenu.** Aplikacja
nie sprawdza, czy „ten użytkownik" może zobaczyć ten projekt czy PR — pokazuje wszystko, co
zwróci Azure DevOps dla tego tokenu. Zakres minimalny do czytania: odczyt kodu oraz odczyt
projektów i zespołów. Do pisania komentarzy dodatkowo: odczyt i zapis wątków PR (zakres
zapisu kodu **nie** jest używany i nie powinien być nadawany).

**[FAKT] Jedna bramka uprawnieniowa naprawdę istnieje: przełącznik zapisu komentarzy.**
W konfiguracji serwera jest wyłącznik, który **domyślnie blokuje wszystkie operacje zapisu
komentarzy** (założenie wątku, odpowiedź, edycja, usunięcie, zmiana statusu). Sprawdzenie
następuje po stronie serwera, zanim cokolwiek opuści maszynę. Uzasadnienie biznesowe:
komentarz jest widoczny dla całego zespołu i nie da się go cofnąć.

**[FAKT] Widoczność akcji zapisu jest spójna z tym przełącznikiem.** Interfejs pyta serwer
o stan wyłącznika raz przy starcie. Gdy zapis jest wyłączony, przyciski zapisu **znikają**
(nie są wyszarzone), a nad listą komentarzy stoi zdanie wyjaśniające. Gdy zapytanie o stan
wyłącznika się nie powiedzie, interfejs **zostawia pisanie włączone** — prawdziwą bramką
jest serwer, a przycisk zwróci wtedy czytelny błąd.

**[FAKT] Edycja i usunięcie dotyczą wyłącznie własnych komentarzy.** Przy każdym wczytaniu
listy wątków aplikacja pyta Azure DevOps, do kogo należy używany token, i oznacza komentarze
tej osoby. Przyciski „Edytuj" i „Usuń" pojawiają się tylko przy nich. Gdy pytanie o tożsamość
się nie powiedzie, **żaden** komentarz nie jest uznany za własny i obie akcje po prostu nie
istnieją w interfejsie — reszta widoku działa normalnie. Ostateczną decyzję i tak podejmuje
Azure DevOps; odmowa jest tłumaczona na komunikat „można edytować lub usuwać tylko własne
komentarze".

---

## 3. Przepływy użytkownika i powierzchnia ekranów

### 3.1 Ścieżka główna

1. **Wybór źródła.** Użytkownik wybiera projekt, potem repozytorium. **[FAKT]** Jeżeli
   dostępny jest dokładnie jeden projekt, jest wybierany automatycznie i od razu pobierana
   jest lista repozytoriów; analogicznie dla jednego repozytorium — od razu pobierana jest
   lista PR. Jest też przycisk „Odśwież".
2. **Lista aktywnych PR.** Każdy wiersz: numer, tytuł, autor, repozytorium, status, postęp
   checklisty `x/6`, postęp czytania `x/y plików` (tylko gdy cokolwiek już oznaczono), data
   utworzenia. **[FAKT]** Lista zawiera wyłącznie PR o statusie *aktywny*.
3. **Otwarcie PR.** Selektory projektu i repozytorium znikają. Ekran zmienia się w tryb
   czytania: nagłówek PR + trzy niezależnie przewijane panele o wysokości okna.
4. **Ekran startowy panelu środkowego** to **opis PR** — zanim wybrany zostanie jakikolwiek
   plik. **[FAKT]** Jeżeli poprzednia sesja zostawiła nieprzeczytane pliki, aplikacja
   automatycznie otwiera pierwszy z nich zamiast ekranu startowego (patrz §4.9).
5. **Czytanie plików.** Klik w plik w drzewie (albo klawisz nawigacji) wczytuje diff.
   Użytkownik przechodzi kolejno, oznaczając „Obejrzałem".
6. **Kontekst w szynie.** Równolegle użytkownik może wygenerować Summary, przyjąć
   proponowaną ścieżkę czytania, odhaczyć kroki checklisty i odpowiedzieć na pytanie
   Debug Check.
7. **Komentarze.** Najechanie na linię kodu pokazuje `+` na marginesie; klik w margines lub
   numer linii otwiera rozmowę albo szkic nowego komentarza — **bez opuszczania diffu**.
8. **Powrót.** Przycisk „← Wróć" albo klawisz wyjścia. Po powrocie liczniki postępu na
   liście PR są odświeżane.

### 3.2 Ścieżki alternatywne

- **Od komentarza do kodu.** W widoku komentarzy klik w lokalizację wątku zamyka widok
  komentarzy, otwiera plik i przewija edytor do linii wątku.
- **„Co się zmieniło po tym komentarzu".** Wątek niesie numer iteracji PR, na której powstał.
  Jeżeli PR ma nowszą iterację, przy wątku pojawia się etykieta i przycisk pokazujący
  **wyłącznie różnicę między tamtą iteracją a obecną głową** — zamiast całego diffu pliku.
  Pasek nad diffem mówi wtedy wprost, co jest pokazywane, i oferuje powrót do pełnego diffu.
- **Wyjaśnienie pliku.** Osobna akcja („Wyjaśnij ten plik") uruchamia AI dla jednego,
  otwartego pliku i zwraca 1–3 zdania. Blok jest zwijalny.
- **Tryb skupienia.** Ukrywa prawą szynę; wtedy jedynym miejscem, gdzie widać nierozwiązane
  komentarze, jest licznik w nagłówku PR.

### 3.3 Elementy ekranu — panel lewy (lista plików)

- Nagłówek `Zmienione pliki (N)` + `x / y obejrzanych` + pasek postępu.
- Zdanie o plikach, które zmieniły się od czasu przeczytania (gdy takie są).
- Komunikat „Wszystkie pliki obejrzane" wraz z wyliczeniem kroków checklisty, których
  jeszcze brakuje.
- Pole wyszukiwania pliku (po nazwie i po ścieżce, także po ścieżce sprzed przeniesienia).
- Przełącznik `Wszystkie / Nieobejrzane`.
- Przycisk `Następny nieobejrzany →`.
- **Drzewo folderów.** Łańcuchy folderów zawierające wyłącznie jeden podfolder są scalane
  w jedną etykietę. Każdy folder pokazuje licznik (`przeczytane/wszystkie` albo samą liczbę
  przy filtrze „Nieobejrzane") i sumę komentarzy w środku. Folder w całości przeczytany
  **zwija się sam**, chyba że trwa wyszukiwanie, zawiera otwarty plik albo zawiera
  nierozwiązany komentarz.
- **Wiersz pliku:** nazwa, etykieta typu zmiany, odznaka liczby komentarzy, znacznik `✓`
  (przeczytany) albo `✓ zmienione` (przeczytany, ale plik się zmienił), gwiazdka dodająca do
  ścieżki czytania, etykieta roli z rankingu AI, ścieżka sprzed przeniesienia.
- **Zwinięta sekcja „Szum (N)"** — osobne drzewo dla plików sklasyfikowanych jako szum (§4.2).

### 3.4 Elementy ekranu — panel środkowy (diff)

Pasek narzędzi nad diffem: nazwa i katalog pliku, `Plik X z Y`, strzałki „poprzedni/następny
plik", strzałki „poprzednia/następna zmiana w pliku", przełącznik `Obok siebie / W linii`,
`Opis PR`, `Pokaż/Ukryj komentarze (N)`, `Skomentuj linię`, `Wyjaśnij ten plik`, checkbox
`Obejrzałem`. Poniżej: pasek ze wszystkimi wątkami tego pliku (chipy), blok wyjaśnienia AI,
sam diff.

### 3.5 Skróty klawiszowe [FAKT]

| Klawisz | Działanie |
|---|---|
| `j` / `n` | Następny plik |
| `k` / `p` | Poprzedni plik |
| `m` | Oznacz jako obejrzany i przejdź do następnego nieobejrzanego |
| `.` / `]` | Następna zmiana w pliku |
| `,` / `[` | Poprzednia zmiana w pliku |
| `/` | Ustaw kursor w wyszukiwarce plików |
| `s` | Przełącz widok obok siebie / w linii |
| `f` | Tryb skupienia (chowa prawą szynę) |
| `o` | Opis PR ↔ powrót do ostatnio czytanego pliku |
| `e` | Wyjaśnij ten plik |
| `c` | Widok komentarzy |
| `g` | Przenieś kursor do kodu |
| `Esc` | Odklej jedną warstwę (patrz niżej) |
| `?` | Okno pomocy ze skrótami |

**[FAKT] Zasady działania skrótów.** Skrót nie działa, gdy wciśnięty jest jednocześnie
Ctrl/Alt/Meta ani gdy kursor stoi w polu tekstowym — z jednym wyjątkiem: `Esc` działa także
wewnątrz pola szkicu komentarza (tam się po niego sięga); w innych polach `Esc` tylko zdejmuje
fokus. Strzałki, PageUp/PageDown, Home/End i klawisze funkcyjne są **celowo nieobsługiwane**,
żeby przewijanie i wyszukiwanie wewnątrz edytora kodu zostały nietknięte.

**[FAKT] `Esc` odkleja dokładnie jedną warstwę na raz**, w kolejności: okno pomocy →
potwierdzenie usunięcia komentarza → edycja komentarza → szkic komentarza → otwarta rozmowa →
widok komentarzy → tryb skupienia → powrót do listy PR. Uzasadnienie: jeden klawisz, który
jednocześnie porzuca niewysłany szkic i wychodzi z PR, jest pułapką.

**[FAKT] Dostępność.** Ponieważ `j`, `k` i `m` nie przenoszą fokusu, pozycja aktualnego pliku
jest ogłaszana w ukrytym obszarze czytanym przez czytnik ekranu.

---

## 4. Reguły biznesowe i wyliczenia

### 4.1 Punkt odniesienia dla wszystkich danych o zmianach

**[FAKT]** Cały obraz zmian pochodzi z **ostatniej iteracji PR**. Dla PR pobierane są jej
dwa punkty: commit bazowy (wspólny przodek gałęzi) i commit głowy (szczyt gałęzi źródłowej).
Z nich wynika **wszystko**: lista zmienionych plików, treść obu wersji każdego pliku, pakiet
kontekstu dla AI i ocena aktualności zapisanych wyników.

**[FAKT] Lista plików tej iteracji jest listą dozwolonych ścieżek.** Żądanie diffu,
wyjaśnienia pliku ani niczego innego dla ścieżki spoza tej listy nie jest realizowane —
zwracany jest błąd „plik nie należy już do tego PR". Dotyczy to także ścieżek zwróconych
przez model AI (§4.7).

**[FAKT] Foldery są pomijane** przy budowaniu listy zmienionych plików. Licznik zmienionych
plików to po prostu długość tej listy, a licznik commitów — długość listy commitów.

### 4.2 Klasyfikacja pliku jako „szum" [FAKT]

Jedna reguła, licząca się z samej ścieżki pliku, obsługuje dwa zastosowania: wydzielenie
sekcji „Szum" w drzewie **oraz** pominięcie treści pliku w pakiecie dla AI. Kategorie,
sprawdzane w tej kolejności (pierwsze trafienie wygrywa):

| Kategoria | Reguła |
|---|---|
| plik zależności (lockfile) | nazwa to `package-lock.json`, `npm-shrinkwrap.json`, `yarn.lock` lub `pnpm-lock.yaml`, albo nazwa kończy się na `.lock` |
| snapshot | nazwa kończy się na `.snap` albo ścieżka zawiera segment `__snapshots__` |
| plik wygenerowany | nazwa zawiera `.generated.` albo kończy się na `.g.cs` |
| plik zminifikowany | nazwa kończy się na `.min.js` albo `.min.css` |
| wynik budowania | dowolny segment ścieżki to `node_modules`, `dist`, `bin` albo `obj` |
| *(brak)* | zwykły kod |

Porównania są nieczułe na wielkość liter. **Pliki szumu nie znikają** — nadal można je otworzyć
i oznaczyć — ale w kolejności czytania idą **na końcu**, więc „następny nieobejrzany"
przerabia najpierw kod.

### 4.3 Kiedy diff pliku jest pokazywany, a kiedy nie [FAKT]

Aplikacja pobiera **obie pełne wersje pliku** (nie różnicę tekstową) i porównuje je
w przeglądarce. Wersja „przed" pochodzi z commita bazowego, wersja „po" z commita głowy;
przy pliku przeniesionym wersja „przed" jest brana ze starej ścieżki. Dla pliku dodanego
strona „przed" jest pusta, dla usuniętego — strona „po".

Diff **nie jest pokazywany** w trzech przypadkach, każdy z własnym komunikatem:

| Wynik | Warunek |
|---|---|
| `plik binarny` | Azure DevOps oznacza go jako binarny, jest symlinkiem, jest folderem, treść zawiera bajt zerowy (poza plikami w kodowaniu UTF‑16 ze znacznikiem kolejności bajtów) albo nie daje się zdekodować jako tekst |
| `zbyt duży` | którakolwiek wersja przekracza **256 KB**, albo **suma linii obu wersji przekracza 4000** |
| *(błąd)* | Azure DevOps odmówił albo zwrócił niespójne dane |

**Reguła liczenia linii:** liczone są linie obu wersji łącznie; ciąg CR+LF liczy się jako
jeden koniec linii; **końcowy znak nowej linii nie tworzy pustej linii na końcu**; pusty tekst
to zero linii.

**[FAKT] Treść plików nie jest nigdzie przechowywana.** Każde otwarcie pliku to świeże
pobranie z Azure DevOps.

### 4.4 Podpowiedzi typów i kolorowanie dla plików C# [FAKT]

Dla pliku w języku C# obie już pobrane wersje są **raz** wysyłane do analizy po stronie
serwera. Wracają dwie rzeczy: podpowiedzi (sygnatura symbolu pokazywana po najechaniu) oraz
tokeny semantyczne, którym edytor nadaje kolory. Klasy, interfejsy, struktury, typy wyliczeniowe,
delegaty, metody, właściwości, pola, zdarzenia, parametry, zmienne i przestrzenie nazw mają
odrębne kolory, osobno dobrane dla motywu jasnego i ciemnego.

**Ograniczenie [FAKT]:** analiza widzi **tylko te dwa pliki**, bez reszty projektu i bez jego
zależności. Symbole zdefiniowane w innych plikach pozostają nierozstrzygnięte i są po cichu
pomijane — brak podpowiedzi dla takiego symbolu jest oczekiwany, nie jest błędem. Żądanie
podlega tym samym limitom co diff (256 KB na wersję, 4000 linii łącznie); przekroczenie kończy
się błędem walidacji. Przy zamknięciu pliku żądanie jest przerywane, a dostawcy podpowiedzi
i kolorów usuwani.

### 4.5 Pakiet kontekstu dla AI — budżet znaków [FAKT]

Pakiet wejściowy dla analizy AI powstaje **na żądanie** i zawsze od zera. Zawiera:
metadane PR (numer, tytuł, opis, autor, repozytorium, obie gałęzie, status, data utworzenia,
reviewerzy, Work Itemy, commit bazowy i commit głowy), **tytuły** commitów (pierwsza linia
wiadomości każdego commita) oraz każdy zmieniony plik ze ścieżką, typem zmiany i ścieżką
sprzed przeniesienia.

**Budżet tekstów:**

| Limit | Wartość | Co obejmuje |
|---|---|---|
| Na plik | **20 000 znaków** | suma długości obu wersji pliku |
| Na cały PR | **100 000 znaków** | suma wszystkich dołączonych tekstów |

**Reguła nadrzędna: tekst jest dołączany w całości albo w ogóle — nigdy ucinany.** Powód:
model nie powinien wnioskować o połowie pliku. Metadane i tytuły commitów **nie** wchodzą do
tego budżetu.

**Kolejność decyzji dla każdego pliku (w tej kolejności):**
1. plik sklasyfikowany jako szum (§4.2) → pominięty z powodem = nazwa kategorii, **bez
   pobierania treści**;
2. budżet PR już wyczerpany → pominięty z powodem `limit na PR`, **bez pobierania treści**;
3. pobranie treści; jeżeli nie jest tekstem → powód `plik binarny` albo `limit istniejącego
   diffu`;
4. suma długości obu wersji > 20 000 → powód `limit na plik`;
5. suma długości obu wersji > pozostały budżet PR → powód `limit na PR`;
6. w przeciwnym razie: tekst dołączony, zużycie budżetu rośnie o tę sumę.

Plik pominięty **zachowuje ścieżkę, typ zmiany i powód pominięcia**. Pakiet niesie też
informację, czy cokolwiek zostało pominięte, i łączną liczbę dołączonych znaków.

**[FAKT] Pakiet kontekstu nie zawiera komentarzy PR.** Komentarze nigdy nie trafiają do modelu.

### 4.6 Summary — uruchomienie, walidacja, treść [FAKT]

**Uruchomienie.** Summary powstaje **wyłącznie** po świadomym kliknięciu przycisku dla
otwartego PR. Nic nie uruchamia analizy przy wejściu na listę ani przy otwarciu PR.

**Przebieg.** Serwer buduje świeży pakiet kontekstu (§4.5), uruchamia skonfigurowany lokalny
program analizujący **bez powłoki systemowej**, podaje mu na wejście jeden obiekt danych
i odczytuje z wyjścia jeden obiekt danych.

**Granica bezpieczeństwa [FAKT]:** stała instrukcja dla modelu jest **oddzielnym polem** od
niezaufanych treści (opis PR, tytuły commitów, kod), które siedzą w polu kontekstu. Instrukcja
mówi modelowi wprost, żeby traktował te treści jako dane, nigdy jako polecenia. Program
analizujący **nie dostaje ścieżki do lokalnego repozytorium** ani żadnych narzędzi do kodu.

**Limity wykonania:** czas oczekiwania domyślnie **120 sekund** (konfigurowalny w zakresie
1–300 s; wartość spoza zakresu jest odrzucana jako błąd konfiguracji), odpowiedź najwyżej
**16 384 znaki**. Przekroczenie któregokolwiek limitu, niepoprawny format odpowiedzi, brak
konfiguracji programu albo jego niezerowy kod wyjścia → jawny błąd, **bez zapisu**.

**Walidacja wyniku — wszystko poniżej musi być spełnione, inaczej wynik jest odrzucany:**
- zgodna wersja kontraktu (aktualna: **2**);
- **2 do 5 zdań**, każde niepuste i nie dłuższe niż **500 znaków**;
- lista plików krytycznych: **najwyżej 10** pozycji, lista pusta jest dozwolona;
- każda ścieżka na tej liście **musi pochodzić z listy zmienionych plików tego PR** —
  ścieżka wymyślona przez model unieważnia cały wynik;
- **brak duplikatów** ścieżek;
- każda pozycja ma `rolę` i `dlaczego warto przeczytać`, obie niepuste i **nie dłuższe niż
  200 znaków**.

**Raport pokrycia.** Obok wyniku pokazywany jest raport zbudowany **z pakietu przygotowanego
przez serwer, nie z deklaracji modelu**: liczba plików z dołączonym diffem / liczba
zmienionych plików, liczba dołączonych znaków, skrót commita głowy oraz rozwijana lista
pominiętych plików z powodem.

**Etykiety powodów pominięcia widoczne dla użytkownika:** plik zależności, snapshot, plik
wygenerowany, plik zminifikowany, wynik budowania, plik binarny, limit istniejącego diffu,
limit na plik, limit na PR.

**Prezentacja.** Zdania są pokazywane jako osobne akapity (pierwsze wyróżnione), **bez
interpretowania formatowania tekstu** — to surowy tekst, nie dokument.

### 4.7 Aktualność Summary [FAKT]

Zapisany wynik niesie commit głowy, dla którego powstał. Po otwarciu PR aplikacja porównuje
go z commitem głowy bieżącej ostatniej iteracji (porównanie nieczułe na wielkość liter):

| Wynik porównania | Co widzi użytkownik |
|---|---|
| zgodne | „Aktualne dla tego PR" |
| różne | Wynik **pozostaje widoczny** + ostrzeżenie „PR zmienił się od zapisania tego Summary" |
| brakuje któregokolwiek commita | „Nie można potwierdzić aktualności Summary" |

**Nieaktualność niczego nie kasuje.** Ponowne wygenerowanie zawsze wymaga kliknięcia,
a zapis jest nadpisywany **dopiero po udanej i zwalidowanej analizie** — nieudana próba nigdy
nie niszczy poprzedniego wyniku.

**[FAKT] Zapis w starej wersji kontraktu jest traktowany jak brak zapisu**, nie jak błąd:
użytkownik dostaje ofertę wygenerowania nowego wyniku, a nie komunikat, że PR nie działa.
Zapis uszkodzony (nieczytelny albo z liczbą zdań poza 2–5) jest natomiast zgłaszany jako błąd.

### 4.8 Ranking plików: propozycja, nie decyzja [FAKT]

Lista plików krytycznych z Summary jest **propozycją**. Reguły:
- propozycja jest pokazywana tylko wtedy, gdy ścieżka czytania użytkownika jest **pusta**
  i propozycja nie została odrzucona;
- lista propozycji jest dodatkowo filtrowana do plików, które nadal są w PR, i obcinana do 10;
- **nic nie jest zapisywane do ścieżki czytania, dopóki użytkownik nie kliknie „Przyjmij
  ścieżkę"**; „Odrzuć" chowa propozycję do końca pracy z tym PR;
- etykiety ról przy plikach w drzewie **tylko opisują** wiersze — nie zmieniają kolejności,
  nie zaznaczają nic i nie wpływają na żaden licznik.

### 4.9 Ścieżka czytania (kluczowe pliki) [FAKT]

- Do **10 plików**, w kolejności ustalonej przez użytkownika (strzałki w górę/w dół).
- Dodawanie i usuwanie gwiazdką przy pliku w drzewie; po osiągnięciu 10 gwiazdki przy
  pozostałych plikach są nieaktywne.
- **Duplikaty są odrzucane** przez serwer, podobnie jak lista dłuższa niż 10 i ścieżka
  w niepoprawnym formacie.
- Przy wyświetlaniu ścieżka jest filtrowana do plików nadal obecnych w PR — pozycja usunięta
  z PR po prostu znika z listy.
- Zapis jest **optymistyczny z wycofaniem**: lista przestawia się natychmiast, a przy błędzie
  wraca do poprzedniego stanu i pokazuje komunikat.
- Odczyt zapisanej ścieżki jest **ponownie walidowany** — uszkodzony zapis jest zgłaszany jako
  błąd, a nie pokazywany jako prawdziwa ścieżka.

### 4.10 Znacznik „Obejrzałem" i reguła nieaktualności [FAKT] — **kluczowa reguła produktu**

Oznaczenie pliku zapisuje trzy rzeczy: ścieżkę, **identyfikator treści pliku** (identyfikator
obiektu, który Azure DevOps zwraca razem z listą zmian) oraz commit głowy PR jako wariant
zapasowy.

**Znacznik nigdy nie jest kasowany automatycznie.** Staje się „nieaktualny" **wyłącznie przy
pozytywnym dowodzie zmiany**:

| Sytuacja | Wynik |
|---|---|
| zapisany identyfikator treści **i** bieżący identyfikator są znane → różne | **nieaktualny** |
| zapisany identyfikator treści **i** bieżący identyfikator są znane → równe | przeczytany |
| brak identyfikatora po którejkolwiek stronie, ale znane są oba commity głowy → różne | **nieaktualny** |
| brak identyfikatora, commity głowy równe | przeczytany |
| nie ma czego porównać | **przeczytany** (świadomie optymistycznie) |

Uzasadnienie: identyfikator treści zmienia się wtedy i tylko wtedy, gdy zmieniła się treść
**tego** pliku — porównanie po samym commicie głowy unieważniałoby po jednym commicie
**wszystkie** znaczniki w PR. Nagabywanie bez powodu uznano za gorsze niż lekko optymistyczny
ptaszek. Odrzucono dwa warianty: kasowanie znaczników przy nowym commicie (utrata ręcznie
wprowadzonych danych) i milczące utrzymywanie ich (kłamstwo o jedynej rzeczy, do której
narzędzie istnieje).

**Skutki nieaktualności:**
- plik liczy się jako **nieprzeczytany** dla licznika, filtra „Nieobejrzane" i akcji
  „następny nieobejrzany";
- w drzewie renderuje **własną odznakę** `✓ zmienione` zamiast wracać do pustki;
- zaznaczenie checkboxa „przestempluje" znacznik nowym identyfikatorem, zamiast go zdjąć.

**Ograniczenia i limity:**
- **2000 oznaczonych plików na PR**; już istniejący wiersz zawsze da się zaktualizować, więc
  ponowne oznaczenie nigdy nie jest blokowane limitem;
- odznaczenie **usuwa** zapis (brak wiersza = nieprzeczytany);
- zapis jest optymistyczny z wycofaniem przy błędzie;
- **samo otwarcie pliku niczego nie oznacza** — to świadoma decyzja produktowa.

### 4.11 Liczniki i postęp — dokładne definicje [FAKT]

| Licznik | Gdzie | Dokładna definicja |
|---|---|---|
| `Checklista x / 6` | nagłówek PR, szyna | liczba zaznaczonych kroków z sześciu |
| `x/6` | lista PR | suma sześciu pól z zapisu lokalnego; PR bez zapisu pokazuje `0/6`; w trakcie ładowania `…/6`, przy błędzie `—/6` |
| `x / y obejrzanych` | panel plików | x = pliki w stanie *przeczytany* (nie *nieaktualny*), y = liczba zmienionych plików |
| `x/y plików` | lista PR | x = liczba zapisanych wierszy oznaczenia, y = **największa** liczba zmienionych plików zapamiętana przy którymkolwiek oznaczeniu (NIESP-06); wiersz nie pojawia się wcale, gdy nic nie oznaczono |
| licznik folderu | drzewo | `przeczytane/wszystkie` z całego poddrzewa; przy filtrze „Nieobejrzane" — **sama liczba** plików, bo proporcja `0/N` twierdziłaby, że nic nie przeczytano |
| odznaka `💬 N` folderu | drzewo | suma wszystkich wątków w poddrzewie; wyróżniona, gdy jest w nim choć jeden nierozwiązany |
| `N nierozwiązanych z M` | nagłówek, szyna, widok komentarzy | M = wszystkie wątki niesystemowe, N = te, które nie mają statusu *rozwiązany* (§4.13) |
| `Plik X z Y` | pasek diffu | X = pozycja pliku na liście z Azure DevOps (**nie** w kolejności drzewa — NIESP-05), Y = liczba zmienionych plików |
| `Kontekst: a / b plików z diffem` | Summary | a = pliki, których treść weszła do pakietu, b = wszystkie zmienione pliki |

**„Następny nieobejrzany" [FAKT]:** szukanie idzie w **kolejności renderowania drzewa**
(najpierw podfoldery, potem pliki leżące bezpośrednio w folderze; kod przed szumem), zaczynając
od pliku po aktualnym; jeżeli nic nie znajdzie, **zawija się na początek** i szuka wśród plików
przed aktualnym. Drzewo powstaje z plików już przefiltrowanych przez wyszukiwanie i przełącznik
„Nieobejrzane", więc „następny" zawsze znaczy „następny widoczny".

**Ocena reviewera [FAKT]:** głos ≥ 5 → „Zatwierdzono", głos < 0 → „Zmiany wymagane",
w pozostałych przypadkach → „Bez decyzji".

**Etykiety typu zmiany [FAKT]:** dodanie → „Dodano", edycja → „Zmieniono", usunięcie →
„Usunięto", przeniesienie → „Przeniesiono"; nieznany typ jest pokazywany dosłownie.

### 4.12 Checklista PR i Debug Check [FAKT]

**Sześć kroków, w stałej kolejności:** `AI Review`, `Quality`, `Understand`, `Architecture`,
`Debug`, `Ready`. Wszystkie są **wyłącznie ręczne**. Krok `Ready` **nie zmienia się sam** —
jest ostateczną decyzją użytkownika. Nazwa kroku spoza tej szóstki jest odrzucana przez serwer.
Zapisywany jest jeden krok na raz, a w odpowiedzi wraca cały stan checklisty. Zapis jest
optymistyczny z wycofaniem.

**Debug Check** to **jedno stałe pytanie**: *„Gdyby ta zmiana nie zadziałała, gdzie zacząłbyś
szukać?"* oraz pole odpowiedzi (do **2000 znaków**, pusty tekst czyści odpowiedź).

Reguły, które definiują tę funkcję:
- **nic nie ocenia odpowiedzi** — celem jest kilka sekund aktywnego myślenia, nie egzamin;
- **zapis odpowiedzi nie zaznacza kroku `Debug`** ani żadnego innego;
- sekcja **rozwija się sama dopiero wtedy**, gdy nie ma już nieprzeczytanych plików; wcześniej
  jest zwinięta i mówi, ile plików zostało („pytanie ma sens po przeczytaniu PR");
- przycisk **„Pokaż, gdzie patrzeć"** odsłania ranking, który **Summary już zwróciło** — nie
  uruchamia modelu ani razu;
- zapis **nie jest optymistyczny**: w polu zostaje to, co naprawdę zapisał serwer.

### 4.13 Komentarze — reguły [FAKT]

**Odczyt.** Pobierane są wszystkie wątki PR; **wątki systemowe są odfiltrowywane po stronie
serwera**. Wątek jest uznany za systemowy, gdy **wszystkie** jego komentarze są systemowe
albo gdy nie ma w nim żadnego komentarza. Odczyt wątków **nie wymaga** rozszerzonego zakresu
tokenu.

**Tolerancja mapowania.** Odczyt wątku jest surowy **tylko** wobec identyfikatora. Status,
treść, autor i data mogą być nieobecne, bo Azure DevOps naprawdę je pomija (wątek systemowy
nie ma statusu, komentarz usunięty nie ma treści). Surowość w pozostałych polach zamieniłaby
jeden taki wątek w błąd całej listy.

**Definicja „rozwiązany".** Wątek jest rozwiązany, gdy jego status to **naprawiony**,
**nie naprawimy** albo **zamknięty**. Wątek **bez statusu liczy się jako czekający**.

**Statusy możliwe do ustawienia:** aktywny, naprawiony, nie naprawimy, zamknięty. Inna wartość
jest odrzucana.

**Zapis — zawsze dwa kroki, nigdy optymistycznie.** Szkic jest pisany, potem potwierdzany
osobnym przyciskiem; klawisz Enter **nigdy** nie wysyła (wstawia nową linię). Przycisk blokuje
się na czas żądania, a po odpowiedzi **wątki są czytane ponownie z Azure DevOps** — na ekranie
widać to, co naprawdę zostało zapisane. Przy każdym szkicu stoi zdanie „Wysłanego komentarza
nie da się cofnąć". Treść: po obcięciu białych znaków musi być niepusta i mieć **najwyżej
10 000 znaków**.

**„Odpowiedz i rozwiąż"** to dwie operacje w ustalonej kolejności: najpierw odpowiedź, potem
zmiana statusu — bo odpowiedź jest tym, co się liczy, a status zmienia się dopiero wtedy, gdy
odpowiedź jest bezpiecznie zapisana.

**Usunięcie** to osobne dwa kroki z pytaniem „Usunąć na stałe?". Azure DevOps usuwa miękko:
komentarz zostaje w wątku, bez treści, i jest pokazywany jako „(komentarz usunięty)".

**Kotwiczenie.** Nowy komentarz do linii jest **zawsze** kotwiczony po stronie **po zmianie**.
Powód: przy diffie w jednej kolumnie usunięte linie nie mają adresowalnej pozycji, więc
interfejs i tak nie potrafiłby wskazać strony „przed". Numer linii musi mieścić się w zakresie
1–1 000 000; ścieżka pliku musi zaczynać się od ukośnika i nie zawierać spacji.

**Renderowanie treści.** Treść komentarza i opis PR są tekstem sformatowanym i przechodzą przez
**dwie niezależne warstwy sanityzacji** — to ta sama klasa niezaufanego tekstu, pisanego przez
inne osoby. Znaczniki-komentarze zostawiane przez narzędzia są usuwane, zanim tekst zostanie
wyświetlony.

**Zwijanie długich komentarzy.** Komentarz dłuższy niż **500 znaków** jest przycinany
z przyciskiem „Pokaż całość" — w widoku komentarzy. W bloku przy kodzie **nigdy nie jest
przycinany**, bo blok się przewija i ma uchwyt do zmiany rozmiaru.

**Fragment kodu przy wątku.** W widoku komentarzy każdy wątek pokazuje **3 linie kontekstu
przed linią zakotwiczenia, samą linię zakotwiczenia i 1 linię po niej**, z odpowiedniej strony
(„po zmianie" dla kotwicy prawostronnej, „przed zmianą" dla lewostronnej). Fragmenty są brane
**z jednego diffu na plik**, pobranego raz i trzymanego dopóki PR jest otwarty. **Limit: 20
plików z komentarzami**; powyżej tego wątek po prostu nie ma fragmentu. Plik binarny lub zbyt
duży też nie ma fragmentu — wątek nadal wyświetla się normalnie.

**Etykieta „kod zmienił się po tym komentarzu".** Wątek niesie numer iteracji PR, na której
powstał. Jeżeli bieżąca ostatnia iteracja ma **wyższy numer**, przy wątku pojawia się etykieta
z obydwoma numerami i przycisk pokazujący różnicę między commitem tamtej iteracji a commitem
głowy. **Z przeglądarki przychodzi numer iteracji, nigdy identyfikator commita** — commit
rozwiązuje serwer z listy iteracji tego PR. Dla tego porównania typ zmiany pliku jest zawsze
traktowany jak „edycja": potraktowanie go jako „dodanie" wygasiłoby stronę „przed" i ukryło
dokładnie to, co użytkownik przyszedł zobaczyć.

**Grupowanie w widoku komentarzy.** Wątki są grupowane po pliku, pliki idą **w kolejności
drzewa** (pliki nieobecne w drzewie oraz wątki bez pliku — na końcu, alfabetycznie), a w grupie
wątki są sortowane po numerze linii.

### 4.14 Renderowanie opisu PR [FAKT]

- Opis jest tekstem sformatowanym; pojedyncza nowa linia jest traktowana jako łamanie wiersza
  (tak jak robi to Azure DevOps). Adresy URL są automatycznie linkowane.
- Wszystkie linki dostają otwieranie w nowej karcie i zabezpieczenie przed dostępem do okna
  źródłowego.
- **Obrazki-załączniki są zamieniane na linki** z ikoną spinacza. Powód: załącznik wymaga
  uwierzytelnienia, więc obrazek i tak wyrenderowałby się jako zepsuty; link działa, bo
  przeglądarka ma sesję użytkownika w Azure DevOps.
- Wzmianka w formie `#1234` staje się linkiem **tylko wtedy**, gdy PR faktycznie ma powiązany
  Work Item o tym numerze. Wzmianki wewnątrz kodu i wewnątrz istniejących linków są pomijane.

---

## 4A. Prezentacja i sposób rysowania

> To opis **wyglądu prototypu**, nie wiążący projekt wizualny. Wiążąca jest **intencja**:
> tryb czytania, a nie długa strona. Źródłem prawdy dla kolorów i typografii w docelowej
> aplikacji powinien być system projektowy / brandbook.

**[FAKT] Układ.** Widok otwartego PR to nagłówek + trzy panele o wysokości okna, przewijane
niezależnie: lista plików | diff | szyna kontekstu. Szyna składa się z bloków zwijanych
natywnie. **Poniżej 900 px szerokości panele układają się w kolumnę, a diff idzie na górę**;
przy otwieraniu pliku na wąskim ekranie widok sam przewija się do panelu diffu.

**[FAKT] Edytor diffu.** Tylko do odczytu; język dobierany po rozszerzeniu pliku (fallback:
zwykły tekst); domyślnie **diff w jednej kolumnie**, z przełącznikiem na widok obok siebie;
niezmienione fragmenty pliku są automatycznie zwijane; po pierwszym przeliczeniu widok ustawia
się **na pierwszej zmianie** (i tylko raz — kolejne przeliczenia nie przesuwają już widoku
czytelnikowi); przeniesione bloki kodu są wyróżniane; brak minimapy; zawijanie długich linii
włączone.

**[FAKT] Komentarze w kodzie.** Renderowane **pomiędzy liniami kodu**, nie obok nich. Marker
komentarza stoi na wąskim pasku przy numerach linii (nie na szerokim marginesie ikon, bo ten
przesuwał kod w bok). Najechanie na linię bez komentarza pokazuje `+`. Każdy blok komentarza
zwija się osobno, a w stanie zwiniętym pokazuje podgląd pierwszego zdania. Jest też przełącznik
„Ukryj wszystkie komentarze" — **preferencja na całą sesję, nie na plik** (wyłącza się je po
to, żeby czytać kod). Plik bez diffu tekstowego nie ma edytora, więc jego wątki są pokazywane
w bloku **dokowanym nad diffem**.

**[FAKT] Motyw.** Jasny i ciemny, przełączane za ustawieniem systemu operacyjnego. Kolory
składni C# w motywie ciemnym celowo naśladują popularny motyw edytorski, żeby kod czytał się
znajomo. Bloki animacji i płynnego przewijania są wyłączane, gdy system sygnalizuje preferencję
ograniczenia ruchu — dotyczy to także przewijania sterowanego z kodu.

**[FAKT] Format daty i liczb:** lokalizacja polska, data średniej długości z godziną i minutą.
Identyfikatory commitów pokazywane są jako pierwsze **8 znaków**.

---

## 5. Stany i przejścia

### 5.1 Stan pliku w przeglądanym PR

```
nieprzeczytany ──(użytkownik zaznacza „Obejrzałem")──> przeczytany
przeczytany ──(odznaczenie)──> nieprzeczytany   [zapis jest usuwany]
przeczytany ──(zmieniła się treść pliku w PR)──> nieaktualny
nieaktualny ──(użytkownik zaznacza ponownie)──> przeczytany   [znacznik przestemplowany]
```
*Automatyczne jest wyłącznie przejście do stanu „nieaktualny" — i tylko przy pozytywnym
dowodzie zmiany (§4.10). Otwarcie pliku nie zmienia stanu.*

### 5.2 Stan wątku komentarzy

```
czekający (aktywny albo bez statusu)
   ├─(„Rozwiąż")──────────> naprawiony
   ├─(„Nie naprawimy")────> nie naprawimy
   └─(„Odpowiedz i rozwiąż")─> odpowiedź zapisana, potem naprawiony
naprawiony / nie naprawimy / zamknięty ──(„Otwórz ponownie")──> aktywny
```

### 5.3 Stan Summary dla PR

```
brak ──(klik „Generuj Summary")──> generowanie
generowanie ──(sukces i walidacja)──> zapisane / aktualne
generowanie ──(błąd)──> brak zmiany zapisu + komunikat błędu
zapisane ──(commit głowy PR się zmienił)──> zapisane / nieaktualne  [wynik wciąż widoczny]
zapisane ──(stara wersja kontraktu)──> traktowane jak „brak"
```

### 5.4 Stan szkicu komentarza

```
brak ──(klik w linię / „Nowy komentarz" / „Odpowiedz")──> szkic
szkic ──(„Wyślij")──> wysyłanie ──> zapisane w Azure DevOps + ponowny odczyt wątków
szkic ──(„Anuluj" albo Esc)──> brak
```
*Szkic ginie także przy zmianie pliku, przejściu do innego PR i przy opuszczeniu PR.*

---

## 6. Dane wejściowe i wyjściowe

### 6.1 Co wchodzi z Azure DevOps

Projekty (identyfikator, nazwa) · repozytoria (identyfikator, nazwa) · PR: numer, tytuł, opis,
autor, repozytorium, status, data utworzenia, gałąź źródłowa i docelowa (bez technicznego
przedrostka nazwy referencji), reviewerzy z głosem, powiązane Work Itemy · iteracje PR
(numer, commit) · lista zmienionych plików (ścieżka, typ zmiany, ścieżka sprzed przeniesienia,
identyfikator treści) · commity (identyfikator, wiadomość, autor, opcjonalna data) · treść obu
wersji pliku · wątki komentarzy (identyfikator, status, plik, linia po stronie „przed" i „po",
iteracja, komentarze: identyfikator, autor, identyfikator autora, treść, typ, data publikacji).

### 6.2 Co wychodzi do Azure DevOps

Wyłącznie operacje na komentarzach, i tylko przy włączonym przełączniku: założenie wątku
(treść + opcjonalna ścieżka i linia), odpowiedź w wątku (treść), edycja własnego komentarza
(nowa treść), usunięcie własnego komentarza, zmiana statusu wątku. **Nic innego nie jest
zapisywane do Azure DevOps** — w szczególności żaden kod, żadna gałąź, żaden głos reviewera
i żadna zmiana samego PR.

### 6.3 Co jest przechowywane lokalnie

| Dane | Klucz | Zawartość |
|---|---|---|
| Checklista | organizacja + projekt + repozytorium + numer PR | sześć znaczników, odpowiedź Debug Check, data aktualizacji |
| Summary | jw. | cały zwalidowany wynik, commit głowy, data zapisu |
| Wyjaśnienie pliku | jw. + ścieżka pliku | zdania, commit głowy, data zapisu |
| Znacznik „Obejrzałem" | jw. + ścieżka pliku | identyfikator treści, commit głowy, zapamiętana liczba zmienionych plików, data |
| Ścieżka czytania | jw. | uporządkowana lista do 10 ścieżek, data |

**[FAKT] Poza tym nie jest przechowywane nic** — w szczególności żadna treść plików, żaden
komentarz i żadna kopia danych PR.

---

## 7. Źródła danych, świeżość i model danych

**[FAKT] Jedno źródło zewnętrzne: Azure DevOps, wersja interfejsu 7.1.** Wszystko jest
pobierane **na żądanie**, przy każdym wejściu na ekran. **Nie ma synchronizacji, nie ma
harmonogramu, nie ma zadań w tle, nie ma pamięci podręcznej.** W konsekwencji **nie istnieje
pojęcie „opóźnienia danych" ani strefy czasowej odświeżania** — dane są zawsze tak świeże, jak
moment kliknięcia.

**[FAKT] Wyjątki od „zawsze na żądanie", czyli co jest trzymane w pamięci na czas pracy z PR:**
- **tożsamość właściciela tokenu** — potrzebna, żeby wiedzieć, które komentarze wolno edytować;
- **fragmenty kodu pod wątkami** — jeden diff na plik z komentarzem, do 20 plików, trzymany
  dopóki PR jest otwarty;
- **stan interfejsu** (rozwinięte komentarze, zwinięte bloki, tryb skupienia) — ginie przy
  zmianie PR.

**[FAKT] Stronicowanie i ciche limity — ważne dla dużych repozytoriów:**

| Zasób | Stronicowanie | Uwaga |
|---|---|---|
| Projekty | po 100, pełne przejście przez strony | — |
| Repozytoria | **jedno żądanie, bez stronicowania** | [RYZYKO] organizacja z bardzo dużą liczbą repozytoriów może zostać obcięta po stronie Azure DevOps |
| Pull requesty | po 100, pełne przejście przez strony | tylko status *aktywny* |
| Zmienione pliki | po 2000, pełne przejście przez strony | foldery są pomijane |
| Commity | po 1000, pełne przejście przez strony | — |
| Wątki komentarzy | **jedno żądanie** | [RYZYKO] PR z bardzo dużą liczbą wątków może zostać obcięty |

**[FAKT] Czas oczekiwania na Azure DevOps: 30 sekund na żądanie.** Przekroczenie kończy się
komunikatem o przekroczonym czasie.

**[FAKT] Idempotencja zapisu lokalnego.** Każdy zapis lokalny jest **nadpisaniem wiersza pod
tym samym kluczem**, nie dopisaniem nowego: powtórne wygenerowanie Summary zastępuje poprzednie,
powtórne wyjaśnienie pliku zastępuje poprzednie, ponowne oznaczenie pliku przestempluje istniejący
wiersz. Odznaczenie pliku **usuwa** wiersz. **Nie ma historii** — poprzednie wersje niczego nie
są zachowywane.

**[FAKT] Model danych jest w pełni zakresowany do jednego PR.** Każdy zapisany wiersz jest
kluczowany organizacją, projektem, repozytorium i numerem PR. Nazwa organizacji jest brana
z konfiguracji serwera — nie z przeglądarki.

**[RYZYKO] Na co model danych nie jest przygotowany:**
- **Brak pojęcia użytkownika.** Zmiana organizacji w konfiguracji serwera sprawia, że cała
  dotychczasowa historia czytania staje się niewidoczna (inny klucz); dwie osoby na tej samej
  bazie nadpisywałyby sobie nawzajem postęp bez ostrzeżenia.
- **Brak historii wersji.** Nie da się odtworzyć, jakie Summary widziało się tydzień temu ani
  kiedy dokładnie plik przestał być aktualny.
- **Pusty „magazyn na zapas" opisany w wizji produktu nie istnieje w modelu.** Wiedza o projekcie
  (komponenty, moduły, integracje, kluczowe przepływy, hotspoty, historia PR) nie ma żadnej
  reprezentacji — dodanie jej to nowy obszar danych, nie kolumna.
- **Przy wielu zakładkach przeglądarki** otwartych na tym samym PR ostatni zapis wygrywa; nie ma
  wykrywania konfliktu.

---

## 8. Powiązania i re-użycie danych

### 8.1 Producenci → ta aplikacja → konsumenci

| Producent | Co wytwarza | Konsumenci w aplikacji |
|---|---|---|
| Azure DevOps (ostatnia iteracja PR) | lista zmienionych plików, commit bazowy i głowy, identyfikatory treści plików | drzewo plików, diff, pakiet dla AI, reguła nieaktualności znacznika, reguła nieaktualności Summary |
| Azure DevOps (treść plików) | obie wersje każdego pliku | edytor diffu, analiza podpowiedzi C#, pakiet dla AI, fragmenty kodu pod wątkami |
| Azure DevOps (wątki) | komentarze, statusy, iteracje wątków | widok komentarzy, bloki między liniami kodu, odznaki w drzewie, licznik w nagłówku, szyna |
| Lokalny program AI | zdania Summary + ranking plików; zdania wyjaśnienia pliku | panel Summary, propozycja ścieżki czytania, etykiety ról w drzewie, podpowiedź w Debug Check, blok wyjaśnienia nad diffem |
| Użytkownik | checklista, odpowiedź Debug Check, znaczniki „Obejrzałem", ścieżka czytania | liczniki na liście PR i w panelu plików, kolejność czytania, komunikat „wszystkie pliki obejrzane" |

### 8.2 Re-użycie tej samej reguły w dwóch miejscach [FAKT]

- **Klasyfikacja „szum"** ma **jedną** definicję i **dwóch** odbiorców: wydzielenie sekcji
  w drzewie i pominięcie treści w pakiecie dla AI. To jest zamierzone i spójne.
- **Ranking plików z Summary** ma **trzech** odbiorców: propozycję ścieżki czytania, etykiety
  ról przy plikach w drzewie i podpowiedź „Pokaż, gdzie patrzeć" w Debug Check. Wszystkie trzy
  czytają **ten sam** wynik, żaden nie uruchamia modelu ponownie.
- **Kolejność renderowania drzewa** jest źródłem prawdy dla: nawigacji klawiszami, akcji
  „następny nieobejrzany" i kolejności grup w widoku komentarzy. **Ale nie dla licznika
  `Plik X z Y`** — patrz NIESP-05.

### 8.3 Ten sam wskaźnik policzony inaczej [FAKT]

- **„Nieaktualność" ma dwie definicje** (NIESP-04, zamknięte): per plik, po identyfikatorze
  treści — dla znacznika „Obejrzałem" i dla wyjaśnienia pliku; per PR, po commicie głowy —
  dla Summary, które opisuje cały PR. Wariantem zapasowym obu reguł per plik jest commit
  głowy, gdy identyfikatora treści nie da się ustalić.
- **Postęp czytania jest liczony dwa razy z dwóch źródeł** (NIESP-06): w otwartym PR —
  z bieżącej listy zmienionych plików; na liście PR — z liczby zapamiętanej w momencie
  ostatniego oznaczenia. Oba mogą pokazywać inny mianownik dla tego samego PR.

---

## 9. Ekspozycja zewnętrzna / API / model dostępu

**[FAKT] Model dostępu: brak.** Serwer aplikacji **nie wymaga żadnego uwierzytelnienia**.
Nie ma logowania, kluczy, nagłówków autoryzacyjnych ani limitów żądań. Każdy, kto dosięgnie
portu serwera, ma pełne uprawnienia aplikacji — w tym możliwość czytania kodu z Azure DevOps
i, przy włączonym przełączniku, pisania komentarzy w imieniu właściciela tokenu.

Jedyne, co ogranicza ryzyko dzisiaj, to fakt, że serwer nasłuchuje **na localhost**. Jest to
świadome, udokumentowane ograniczenie prototypu, nie przeoczenie.

**[FAKT] Token dostępu nigdy nie opuszcza serwera.** Przeglądarka nie widzi go w żadnej
postaci i rozmawia wyłącznie z własnym serwerem.

**[FAKT] Egzekwowanie reguł jest scentralizowane w czterech punktach** — to dobra wiadomość
dla przebudowy:
1. **Jedna droga wyjścia do Azure DevOps.** Odczyt i zapis idą tą samą ścieżką; walidacja
   nazwy organizacji i obecności tokenu istnieje w jednym egzemplarzu. Nazwa organizacji musi
   pasować do wzorca (litera lub cyfra, dalej litery, cyfry i myślniki) — inaczej żądanie nie
   wychodzi.
2. **Jeden przełącznik zapisu komentarzy**, sprawdzany przed każdą operacją zapisu.
3. **Jedno mapowanie błędów** dla całego interfejsu serwera (§11).
4. **Jedna lista dozwolonych ścieżek** — lista plików ostatniej iteracji PR — używana zarówno
   dla diffu, jak i dla wyjaśnienia pliku.

**[FAKT] Jeden punkt wyłamuje się z tego wzorca:** usługa analizy kodu C# (podpowiedzi
i kolorowanie) przyjmuje **dowolny tekst** od wywołującego, bez powiązania z jakimkolwiek PR,
projektem czy plikiem. To operacja obliczeniowa, nie dostęp do danych, ale jest jedynym
punktem, który wykonuje pracę na treści pochodzącej wprost od klienta (ograniczonej tylko
rozmiarem: 256 KB na wersję, 4000 linii łącznie). Patrz NIESP-02.

**[FAKT] Brak integracji wychodzących poza Azure DevOps i lokalnym programem AI.** Nie ma
webhooków, nie ma publicznego interfejsu, nie ma interfejsu dla asystentów AI, nie ma
eksportu danych.

**[FAKT] Model AI jest uruchamiany jako zewnętrzny program lokalny**, wskazany w konfiguracji
serwera. Granice tej integracji: uruchomienie **bez powłoki systemowej**, wejście i wyjście
przez strumienie (jeden obiekt danych w każdą stronę), ograniczony czas i rozmiar odpowiedzi,
wymuszone zabicie procesu wraz z jego procesami potomnymi po zakończeniu, oraz **odrzucenie
diagnostyki programu** — komunikaty z jego strumienia błędów nigdy nie są pokazywane
użytkownikowi. Ścieżka do programu i jego argumenty muszą pochodzić **wyłącznie z zaufanej
konfiguracji serwera**.

---

## 10. Powiadomienia i alerty

**[FAKT] Nie istnieją.** Nie ma e-maili, SMS-ów, powiadomień przeglądarki ani żadnego alertu
opartego na tych danych. Nie ma też żadnego harmonogramu — nic nie dzieje się bez kliknięcia
użytkownika.

Jedyne „powiadomienia" to komunikaty w interfejsie, opisane w §11, oraz trzy pasywne sygnały:
licznik nierozwiązanych komentarzy w nagłówku PR, zdanie o plikach zmienionych od czasu
przeczytania, ostrzeżenie o nieaktualnym Summary.

---

## 11. Walidacje i komunikaty

### 11.1 Walidacje danych wejściowych [FAKT]

| Co | Reguła | Skutek naruszenia |
|---|---|---|
| Numer PR | liczba dodatnia | odrzucenie, błąd walidacji |
| Nazwa projektu / repozytorium | niepusta, różna od `.` i `..` | odrzucenie, błąd walidacji |
| Nazwa organizacji | wzorzec: litera/cyfra + litery, cyfry, myślniki | odrzucenie, błąd konfiguracji |
| Ścieżka pliku | zaczyna się od ukośnika, bez znaku zerowego, w granicach długości | odrzucenie |
| Ścieżka pliku przy komentarzu | dodatkowo: bez spacji | odrzucenie |
| Numer linii komentarza | 1 – 1 000 000 | odrzucenie |
| Treść komentarza | po obcięciu białych znaków niepusta, ≤ 10 000 znaków | odrzucenie |
| Odpowiedź Debug Check | ≤ 2000 znaków; pusta czyści zapis | odrzucenie przy przekroczeniu |
| Ścieżka czytania | ≤ 10 pozycji, bez duplikatów, każda ścieżka poprawna | odrzucenie |
| Oznaczenie pliku | wymagana jawna wartość „przeczytany/nieprzeczytany"; identyfikator treści i commit muszą być 40-znakowymi ciągami szesnastkowymi | odrzucenie |
| Nazwa kroku checklisty | jedna z sześciu | odrzucenie |
| Status wątku | jeden z czterech | odrzucenie |
| Liczba oznaczonych plików w PR | ≤ 2000 dla **nowego** pliku; istniejący zawsze przechodzi | odrzucenie nowego |
| Identyfikatory commitów z Azure DevOps | 40 znaków szesnastkowych | uznane za błąd po stronie Azure DevOps |

### 11.2 Mapowanie błędów na komunikaty [FAKT]

Wszystkie błędy są tłumaczone w **jednym miejscu**. Reguły istotne biznesowo:

| Sytuacja | Komunikat dla użytkownika (sens) |
|---|---|
| Brak organizacji lub tokenu w konfiguracji | „Skonfiguruj organizację i token po stronie serwera" |
| Odmowa dostępu przy **odczycie** | „Azure DevOps odrzucił skonfigurowane poświadczenia lub dostęp" |
| Odmowa dostępu przy **zapisie** | „Token nie może pisać komentarzy. Dodaj zakres odczytu i zapisu wątków PR; dostęp do kodu zostaw na odczycie" — traktowane jako **błąd konfiguracji po naszej stronie**, nie jako awaria Azure DevOps |
| Odmowa przy edycji/usunięciu cudzego komentarza | „Można edytować lub usuwać tylko własne komentarze" |
| Zapis komentarza przy wyłączonym przełączniku | „Pisanie komentarzy jest wyłączone" + wskazanie, co włączyć |
| Konflikt przy zapisie | „Wątek zmienił się w Azure DevOps, gdy pisałeś. Wczytaj komentarze ponownie" |
| Przekroczony limit żądań Azure DevOps | „Osiągnięto limit żądań. Spróbuj później" |
| Nieosiągalny Azure DevOps / przekroczony czas | „Azure DevOps jest niedostępny" / „Żądanie przekroczyło czas" |
| Problem z bazą lokalną | „Lokalny magazyn jest niedostępny" |
| Brak konfiguracji programu AI | „Skonfiguruj program analizujący po stronie serwera" |
| Program AI nie wystartował / nie znaleziono | komunikat o niepoprawnej konfiguracji |
| Program AI przekroczył czas | „Program AI przekroczył czas" |
| Program AI zwrócił niepoprawne dane / niepoprawny wynik | „Program AI zwrócił niepoprawne dane" / „AI zwróciło niepoprawne Summary" |
| Plik spoza PR | „Plik nie należy już do tego pull requesta" |
| Iteracja niedostępna | „Ta iteracja nie jest już dostępna" |
| Uszkodzony zapis lokalny (Summary / ścieżka czytania / wyjaśnienie) | jawny komunikat o niepoprawnym zapisie |

### 11.3 Komunikaty informacyjne w interfejsie [FAKT]

„Pobieranie danych…", „Wczytywanie zapisanego Summary…", „Generowanie Summary…",
„Pobieranie diffu…", „Wczytywanie komentarzy…", „Wczytywanie kodu…", „Zapisywanie…",
„Zapisano", „Wszystkie pliki obejrzane" (+ lista brakujących kroków checklisty), „Brak
zmienionych plików", „Nie znaleziono plików", „Brak nieobejrzanych plików w wynikach
wyszukiwania", „To ostatni nieobejrzany plik. Oznacz go po przejrzeniu.", „Brak komentarzy
w tym PR", „Nic nie pasuje do filtra", „Plik binarny — diff tekstowy jest niedostępny",
„Plik jest zbyt duży, aby pokazać diff", „W tym repozytorium nie ma aktywnych Pull Requestów",
„Wysłanego komentarza nie da się cofnąć".

**[FAKT] Błąd odczytu nie jest przedstawiany jako zero.** Gdy nie uda się wczytać postępu
checklist, lista PR pokazuje `—/6`, a nie `0/6`, i wyświetla osobny komunikat z przyciskiem
ponowienia. To świadoma reguła: „nie wiem" nie może wyglądać jak „nic nie zrobiono".

---

## 12. Przypadki brzegowe i wyjątki

**[FAKT] Obsłużone świadomie:**

- **PR bez zmienionych plików** — komunikat „Brak zmienionych plików"; żądanie diffu dla
  takiego PR kończy się komunikatem „ten PR nie ma zmian w plikach".
- **PR bez commitów / bez reviewerów / bez Work Itemów / bez opisu** — każda sekcja ma własny
  komunikat pustego stanu.
- **Plik przeniesiony** — wersja „przed" jest brana ze starej ścieżki; w drzewie pokazywana
  jest poprzednia ścieżka; wyszukiwanie plików obejmuje także ją.
- **Plik dodany / usunięty** — jedna strona diffu jest pusta.
- **Komentarz usunięty** — wyświetlany jako „(komentarz usunięty)"; nie znika z wątku.
- **Wątek systemowy** — odfiltrowany, nigdy nie trafia do interfejsu.
- **Wątek bez statusu** — liczony jako **czekający**, nie jako rozwiązany.
- **Spóźniona odpowiedź sieciowa.** Każde asynchroniczne wczytanie niesie identyfikator
  pokolenia. Po zmianie projektu, repozytorium, PR albo pliku odpowiedź z poprzedniego
  pokolenia jest **odrzucana** i nie podmienia tego, co użytkownik widzi. Dotyczy to listy PR,
  szczegółów PR, checklisty, Summary, wyjaśnienia pliku, postępu czytania, diffu i wątków.
- **Blokada przycisków po wysyłce komentarza** jest zdejmowana **bezwarunkowo**, także gdy
  ponowny odczyt wątków zmienił pokolenie — inaczej przyciski komentarzy zostałyby zablokowane
  do końca pracy z PR.
- **Plik binarny lub zbyt duży w kontekście komentarzy** — wątek nadal działa, po prostu nie ma
  fragmentu kodu i nie ma bloku między liniami (blok jest wtedy dokowany nad diffem).
- **Plik tekstowy w kodowaniu UTF‑16 ze znacznikiem kolejności bajtów** — poprawnie dekodowany
  mimo obecności bajtów zerowych; znacznik jest usuwany.
- **Znacznik kolejności bajtów UTF‑8** — usuwany przed wyświetleniem.
- **Plik nie do zdekodowania jako tekst** — traktowany jako binarny.
- **Ranking AI wskazujący plik spoza PR** — cały wynik odrzucony; ranking wskazujący plik, który
  wypadł z PR między zapisem a odczytem — odfiltrowany przy wyświetlaniu.
- **Przerwanie analizy typów C# przy zamknięciu pliku** — żądanie przerywane, dostawcy
  podpowiedzi usuwani.
- **Ostatni nieobejrzany plik** — komunikat „To ostatni nieobejrzany plik", a „następny
  nieobejrzany" zawija się na początek listy.

**[FAKT] Nieobsłużone lub obsłużone tylko częściowo:**

- **Równoległa praca w dwóch zakładkach na tym samym PR** — brak wykrywania konfliktu, wygrywa
  ostatni zapis.
- **PR z liczbą wątków przekraczającą jedną stronę odpowiedzi** — patrz §7.
- **Organizacja z bardzo dużą liczbą repozytoriów** — patrz §7.
- **Wątek zakotwiczony wyłącznie po stronie „przed zmianą" w pliku tekstowym** — patrz
  NIESP-03.
- **Wątek zakotwiczony w pliku, którego nie ma już wśród zmienionych plików** — patrz NIESP-07.
- **Zmiana czasu / strefy czasowe** — daty są wyświetlane w strefie przeglądarki; nie ma
  żadnej logiki zależnej od pory dnia, więc zmiana czasu nie wywołuje skutków.

---

## 13. Funkcje zbudowane, lecz nieaktywne — i funkcje zaplanowane, lecz niezbudowane

### 13.1 Zaplanowane w dokumentacji produktowej, **niezbudowane w ogóle** [FAKT]

Wszystkie poniższe wymagają zbudowania od zera — w kodzie nie ma dla nich ani ekranu, ani
danych, ani ścieżki obliczeniowej.

| Funkcja | Co miała robić | Stan |
|---|---|---|
| **Main flow** | Graf przepływu przez zmienione komponenty (np. ekran → punkt wejścia → handler → model → kolejka → integracja) | Niezbudowane. Kontrakt wyniku AI nie ma dla tego miejsca. |
| **Quality Review** | Osobne od klasycznego review: findingi typu „działa, ale może być wyraźnie lepiej" w kategoriach (zapytania do bazy, N+1, wydajność, współbieżność, idempotencja, transakcje, bezpieczeństwo, autoryzacja, zbędna złożoność), każdy z opisem problemu, skutkiem i statusami *Zaakceptowany / Rozwiązany / Nieistotny*. Zasada: **mało uwag, wysoka wartość** | Niezbudowane. Istnieje **tylko** ręczny checkbox `Quality` w checkliście. |
| **Architecture Check** | Werdykt „wykryto wpływ na architekturę" / „brak wpływu" + propozycja fragmentu grafu architektury z akcjami *Przyjmij / Edytuj / Zignoruj*. Zasada: **AI nigdy nie aktualizuje wiedzy architektonicznej jako faktu bez akceptacji użytkownika** | Niezbudowane. Istnieje **tylko** ręczny checkbox `Architecture`. |
| **Project Memory** | Trwała, zaakceptowana wiedza o projekcie: komponenty, moduły, integracje, kluczowe przepływy, hotspoty, historia PR — po to, by móc później zapytać „jak działa wysyłanie faktury?" albo „które PR-y zmieniały ten obszar?" | Niezbudowane. **Brak jakiejkolwiek reprezentacji w modelu danych** — to nowy obszar, nie rozszerzenie istniejącego. |
| **Dostęp do lokalnego klonu repozytorium** | Analiza z pełnym kontekstem projektu, nie tylko diffu | Niezbudowane i **świadomie zablokowane**: program analizujący nigdy nie dostaje ścieżki do repozytorium. |
| **Klasyfikacja pozostałych plików** | Etykiety typu *testy / DTO / mapowania / konfiguracja / infrastruktura / zmiany wtórne* | Niezbudowane. Klasyfikacja obejmuje tylko „szum" vs. „kod" (§4.2). |

### 13.2 Zbudowane w uproszczonej formie — świadomy skrót z nazwanym sufitem [FAKT]

| Funkcja | Wizja | Co jest dzisiaj | Sufit / droga wyjścia |
|---|---|---|---|
| **Debug Check** | 1–3 **generowane** scenariusze awarii dla tego PR (np. „faktura zostaje w stanie *wysyłanie*") | **Jedno stałe pytanie** dla każdego PR, to samo za każdym razem | Wymusza te same kilka sekund myślenia bez trzeciej ścieżki AI. Jeśli stałe pytanie okaże się za słabe — scenariusze dołącza się do kontraktu Summary. |
| **Automatyczny wybór kluczowych plików** | AI wybiera i **ustawia** ścieżkę czytania | AI **proponuje**, użytkownik akceptuje | Świadoma decyzja produktowa, nie skrót — patrz §4.8. |
| **Obrazki w opisie PR** | Widoczne w miejscu | Zamienione na linki | Przekazywanie załączników przez serwer, który ma token. |
| **Fragmenty kodu pod wątkami** | Dla każdego wątku | Do 20 plików z komentarzami | Płaski limit zamiast stronicowania; do podniesienia, gdy pojawi się większy PR. |
| **Znacznik „Obejrzałem" przy braku identyfikatora treści** | Dokładne śledzenie per plik | Wariant zapasowy po commicie głowy PR — wtedy **dowolny nowy commit** oznacza plik jako nieaktualny | Dokładne śledzenie wymagałoby porównywania iteracji. |
| **Migracje bazy przy starcie** | Osobny, świadomy krok wdrożeniowy | Wykonywane automatycznie przy każdym uruchomieniu | Dobre dla jednej maszyny i jednego procesu; wdrożenie współdzielone wymagałoby osobnego kroku. |

### 13.3 Kod, który nie robi tego, co zapowiada [DEFEKT]

**[DEFEKT] Tożsamość właściciela tokenu jest pobierana częściej, niż zakłada projekt.**
Zamierzeniem było zapytać o nią **raz na czas życia procesu** (nie może się zmienić w trakcie
pracy). Faktycznie pamięć jest zakładana od nowa przy **każdym** żądaniu listy wątków, więc
każde wczytanie komentarzy kosztuje dodatkowe zapytanie do Azure DevOps. Skutek jest wyłącznie
wydajnościowy — wynik pozostaje poprawny. Patrz NIESP-08.

---

## 14. Założenia przyjęte w analizie

1. **[ZAŁOŻENIE]** Aplikacja jest przeznaczona dla **jednej osoby na jednej maszynie** i nie
   ma aspiracji do pracy zespołowej w obecnej formie. Wynika to z dokumentacji produktowej
   i z braku jakiegokolwiek pojęcia użytkownika, ale nie jest nigdzie powiedziane, czy docelowo
   ma tak zostać.
2. **[ZAŁOŻENIE]** Krok `Ready` w checkliście oznacza „przeczytałem i rozumiem", a **nie**
   „zatwierdzam PR" — aplikacja nigdy nie oddaje głosu reviewera do Azure DevOps. Intencja
   biznesowa nie jest nigdzie zapisana wprost.
3. **[ZAŁOŻENIE]** Pozostałe pięć kroków checklisty to **miejsca na funkcje, które powstaną**
   (`Quality`, `Architecture`) albo na działania poza aplikacją (`AI Review`). Dziś to sześć
   niezależnych pól bez żadnej logiki między nimi.
4. **[ZAŁOŻENIE]** Limity kontekstu (20 000 / 100 000 znaków) zostały dobrane pod okno
   kontekstowe modelu i koszt, a nie pod żadną regułę biznesową. Wymagają potwierdzenia
   przy wyborze modelu docelowego.
5. **[ZAŁOŻENIE]** Fragment kodu przy wątku (3 linie przed, 1 po) to dobrana ręcznie wartość,
   nie wynik badania.
6. **[ZAŁOŻENIE]** Limit 2000 oznaczonych plików na PR odzwierciedla rozmiar strony listy
   zmian z Azure DevOps, a nie oczekiwaną wielkość PR.
7. **[ZAŁOŻENIE]** Odpowiedzi modelu mają być po polsku (tak brzmi instrukcja), przy
   angielskich nazwach kroków checklisty i angielskim kodzie. Dokumentacja i interfejs są po
   polsku.

---

## 15. Rejestr niespójności i rozbieżności

### Bezpieczeństwo i dostęp

**NIESP-01 — Brak jakiegokolwiek modelu dostępu przy jednoczesnej możliwości pisania
w imieniu użytkownika.**
*Obserwacja:* serwer nie uwierzytelnia nikogo; jedyną ochroną jest nasłuchiwanie na localhost.
Przy włączonym przełączniku zapisu każdy, kto dosięgnie portu, może opublikować komentarz
widoczny dla całego zespołu, podpisany właścicielem tokenu.
*Skutek:* aplikacji nie wolno w obecnej formie uruchomić na żadnym współdzielonym hoście ani
za tunelem — a jest to naturalny następny krok, gdy narzędzie okaże się przydatne.
**→ Decyzja:** czy produkt zostaje narzędziem lokalnym (i wtedy potrzebna jest jawna,
egzekwowana blokada uruchomienia poza localhostem), czy przechodzi na uwierzytelnianie
użytkownika i token per osoba. To rozstrzygnięcie przesądza cały model danych (§7).

**NIESP-02 — Usługa analizy kodu przyjmuje dowolny tekst bez powiązania z PR.**
*Obserwacja:* wszystkie pozostałe operacje sprawdzają ścieżkę względem listy plików PR; ta
jedna przyjmuje treść wprost od klienta, ograniczoną tylko rozmiarem.
*Skutek:* przy dzisiejszym modelu (brak uwierzytelnienia) to punkt, który wykonuje pracę
obliczeniową na żądanie dowolnego wywołującego. Ryzyko jest ograniczone rozmiarem wejścia,
ale wzorzec jest niespójny z resztą systemu.
**→ Decyzja:** czy analiza ma być powiązana z konkretnym plikiem konkretnego PR (spójnie
z resztą), czy pozostaje usługą bezstanową — i czy wtedy dostaje własny limit częstotliwości.

### Wyliczenia i dane

**NIESP-03 — Wątek zakotwiczony wyłącznie po stronie „przed zmianą" jest niewidoczny w diffie
pliku tekstowego.** **[DEFEKT]**
*Obserwacja:* bloki komentarzy między liniami kodu powstają wyłącznie dla kotwicy po stronie
„po zmianie". Wątek z samą kotwicą lewostronną (komentarz do usuniętej linii) dostaje chip
w pasku nad diffem, ale kliknięcie chipa **nie pokazuje niczego** — blok dokowany renderuje się
tylko dla plików bez edytora (binarnych i zbyt dużych).
*Skutek:* komentarz do usuniętej linii jest czytelny **tylko** w osobnym widoku komentarzy;
w pliku wygląda na zepsuty przycisk.
**→ Decyzja:** czy dla takiego wątku pokazywać blok dokowany nad diffem (najmniejsza zmiana),
czy kotwiczyć go przy najbliższej linii po stronie „po zmianie", czy wyłączyć dla niego chip.

**NIESP-04 — ~~Trzy~~ dwie definicje „nieaktualności" w jednej aplikacji.** **[ZAMKNIĘTE
20 wrz 2026, US-P2]**
*Obserwacja (stan sprzed poprawki):*
- znacznik „Obejrzałem" → per plik, po **identyfikatorze treści pliku**;
- Summary → per PR, po **commicie głowy** (poprawne, bo Summary opisuje cały PR);
- wyjaśnienie pojedynczego pliku → per plik, ale porównywane po **commicie głowy PR**.
*Skutek:* po dorzuceniu commita zmieniającego jeden plik znaczniki „Obejrzałem" pozostawały
w mocy dla pozostałych plików (poprawnie), ale **wszystkie zapisane wyjaśnienia plików**
stawały się nieaktualne.
**→ Rozstrzygnięcie:** wyjaśnienie pliku zostało ujednolicone do identyfikatora treści, tą
samą regułą co znacznik (wariant zapasowy: commit głowy, gdy identyfikatora nie ma). Zostają
**dwie** definicje: per plik i per PR — ta druga tylko dla Summary, które opisuje cały PR.

**NIESP-05 — Licznik `Plik X z Y` liczy w innej kolejności niż nawigacja.** **[DEFEKT]**
*Obserwacja:* `X` to pozycja pliku na liście zwróconej przez Azure DevOps. Klawisze `j`/`k`,
strzałki w pasku i „następny nieobejrzany" chodzą natomiast w **kolejności drzewa** (kod przed
szumem, podfoldery przed plikami), a przy włączonym filtrze operują tylko na widocznych plikach.
*Skutek:* przechodzenie `j`, `j`, `j` daje skaczące numery („Plik 12 z 44", potem „Plik 3 z 44"),
a przy filtrze „Nieobejrzane" mianownik `Y` opisuje inny zbiór niż ten, po którym się nawiguje.
Licznik przestaje informować o postępie.
**→ Decyzja:** liczyć pozycję w kolejności drzewa i wobec aktualnie widocznego zbioru (`Plik 3
z 12 widocznych`), czy zrezygnować z licznika na rzecz samego paska postępu.

**NIESP-06 — Mianownik `x/y plików` na liście PR pochodzi z przeszłości.**
*Obserwacja:* liczba zmienionych plików jest zapamiętywana **w momencie oznaczania pliku**
i dosyłana z przeglądarki; na liście PR pokazywana jest **największa** zapamiętana wartość.
Lista PR z Azure DevOps nie zawiera liczby zmienionych plików, a dociąganie jej dla każdego
wiersza łamałoby zasadę pobierania na żądanie.
*Skutek:* PR, do którego dorzucono pliki i którego od tego czasu nie otwierano, pokazuje
`8/8 plików` mimo 14 zmienionych. Liczba jest **niedoszacowana**, czyli myli w najgorszą
stronę: sugeruje ukończenie.
**→ Decyzja:** czy zaakceptować przybliżenie i **oznaczyć je w interfejsie** (np. „≥"), czy
zapłacić jedno dodatkowe żądanie na PR, czy pokazywać samą liczbę przeczytanych bez mianownika.

**NIESP-07 — Martwy klik na wątku z pliku spoza listy zmian.** **[DEFEKT, drobny]**
*Obserwacja:* w szynie i w widoku komentarzy przycisk lokalizacji wątku jest aktywny, gdy wątek
ma **jakikolwiek** plik. Otwarcie pliku jest natomiast odmawiane, gdy pliku nie ma wśród zmian
ostatniej iteracji (np. komentarz do pliku, który wypadł z PR).
*Skutek:* kliknięcie nic nie robi i nie tłumaczy dlaczego.
**→ Decyzja:** wyłączyć przycisk i pokazać powód („plik nie jest już częścią tego PR"),
albo otwierać wtedy sam wątek bez kodu.

### Wydajność i spójność techniczna

**NIESP-08 — Tożsamość właściciela tokenu pobierana przy każdym odczycie komentarzy.**
*Obserwacja:* opisana w §13.3 — zamierzone „raz na proces", faktyczne „raz na żądanie".
*Skutek:* dodatkowe zapytanie do Azure DevOps przy każdym wczytaniu listy wątków (a wątki są
wczytywane po każdym zapisie komentarza). Wynik pozostaje poprawny.
**→ Decyzja:** czy tożsamość ma żyć przez cały czas pracy serwera (i co wtedy przy zmianie
tokenu bez restartu), czy zostawić obecne zachowanie jako świadome.

### Dokumentacja

**NIESP-09 — Dokumentacja opisuje starszą wersję kontraktu wyniku AI.** **[ZAMKNIĘTE
20 wrz 2026]**
*Obserwacja (stan sprzed poprawki):* instrukcja dla osoby pisującej własny program analizujący
podawała wersję kontraktu **1** i przykład odpowiedzi bez listy plików krytycznych; system
przyjmuje wyłącznie wersję **2** z tą listą.
*Skutek:* program napisany według tej instrukcji był odrzucany jako „niepoprawny wynik".
*Rozstrzygnięcie:* `README.md` opisuje teraz wersję **2**, oba zadania (`summary` i `file`),
listę `criticalFiles` wraz z regułą liczebności (jeden plik na cztery zmienione, od 10 do 25)
i mówi wprost, że wersja 1 jest odrzucana. Kontrakt nadal nie jest osobnym, wersjonowanym
załącznikiem — przy jednym adapterze referencyjnym w repozytorium nie ma czego wersjonować
osobno; wraca to, gdy powstanie drugi adapter pisany przez kogoś innego.

**NIESP-10 — Dokumentacja architektury opisuje zakres komentarzy sprzed edycji i usuwania.**
**[ZAMKNIĘTE 20 wrz 2026]**
*Obserwacja (stan sprzed poprawki):* zapis „zakres to założenie wątku, odpowiedź i status —
bez edycji i bez kasowania" nie odpowiadał stanowi aplikacji: edycja i usunięcie **własnych**
komentarzy działają.
*Skutek:* czytelnik dokumentacji budował błędny obraz zakresu produktu.
*Rozstrzygnięcie:* `docs/ARCHITECTURE.md` wymienia edycję i skasowanie własnego komentarza
w zakresie i mówi, że to `isMine` decyduje o pokazaniu akcji.

---

## 16. Pytania otwarte dla PO / analityka / architekta

1. **Model dostępu.** Czy produkt zostaje narzędziem jednoosobowym, czy ma być używany przez
   zespół? Od tej odpowiedzi zależy model danych, token, uprawnienia i sposób wdrożenia.
   *(NIESP-01)*
2. **Czy analiza kodu ma być powiązana z PR**, czy pozostaje bezstanową usługą pomocniczą?
   *(NIESP-02)*
3. **Jak długo wynik AI ma pozostawać ważny?** Jedna definicja „nieaktualności" dla Summary
   i wyjaśnienia pliku, czy dwie z jawnym uzasadnieniem? *(NIESP-04)*
4. **Czym jest „postęp czytania PR"** widziany z listy: liczbą przeczytanych plików wobec
   stanu na teraz (kosztem dodatkowego żądania na PR) czy przybliżeniem oznaczonym w interfejsie?
   *(NIESP-06)*
5. **Czy licznik pozycji pliku ma opisywać zbiór widoczny, czy cały PR?** *(NIESP-05)*
6. **Czy kontrakt wyniku AI ma być publicznym, wersjonowanym kontraktem** (pozwalającym komuś
   napisać własny program analizujący), czy szczegółem wewnętrznym? Dziś jest opisany
   w dokumentacji, ale w starej wersji. *(NIESP-09)*
7. **Komentarz do usuniętej linii** — czy jest przypadkiem wartym obsługi w widoku pliku?
   *(NIESP-03)*
8. **Które z czterech niezbudowanych funkcji mają realny priorytet?** *Quality Review*,
   *Architecture Check*, *Main flow*, *Project Memory* — dziś każda ma tylko ręczny checkbox
   albo nic. Warto rozstrzygnąć **przed** projektowaniem modelu danych, bo *Project Memory*
   jest jedyną z nich, która wymaga zupełnie nowego obszaru danych. *(§13.1)*
9. **Czy `Ready` ma pozostać wyłącznie prywatną notatką**, czy w przyszłości ma oddawać głos
   reviewera do Azure DevOps? Dziś aplikacja nigdy nie zmienia stanu PR. *(§14, założenie 2)*
10. **Czy Debug Check ma dostać generowane scenariusze awarii** zgodnie z pierwotną wizją,
    czy jedno stałe pytanie jest wystarczające? Jest to jedyna funkcja, gdzie prototyp świadomie
    odszedł od zapisanej wizji. *(§13.2)*
11. **Czy potrzebna jest obsługa PR-ów innych niż aktywne** (zamknięte, porzucone) — np. do
    nadrabiania zaległości albo do analizy historycznej?
12. **Czy limity kontekstu AI (20 000 / 100 000 znaków) są właściwe** dla docelowego modelu?
    Przy dużych PR-ach powodują, że Summary powstaje na podstawie części zmian — fakt ten jest
    uczciwie raportowany, ale wpływa na wartość wyniku. *(§14, założenie 4)*

---

## 17. Słowniczek pojęć

| Pojęcie | Gdzie użyte | Co znaczy | Kolizja / uwaga |
|---|---|---|---|
| **Obejrzałem / przeczytany** | drzewo plików, licznik, filtr | ręczna decyzja użytkownika, że przeczytał ten plik | **Nie** znaczy „otwarty" — otwarcie pliku niczego nie oznacza |
| **Nieaktualny** | znacznik pliku, Summary, wyjaśnienie pliku | „to, co zapisano, opisuje inną wersję kodu" | **Dwa** sposoby liczenia: per plik (znacznik, wyjaśnienie) i per PR (Summary) — NIESP-04 zamknięte |
| **Nieobejrzany** | filtr, „następny nieobejrzany", licznik | nieprzeczytany **albo** nieaktualny | Plik nieaktualny liczy się jako nieobejrzany, ale ma własną odznakę |
| **Kluczowy plik** | ścieżka czytania (ręczna) | plik wybrany przez użytkownika do ścieżki czytania | Inne niż „plik krytyczny" |
| **Plik krytyczny** | ranking z Summary | plik **zaproponowany** przez AI | **Propozycja**, nie decyzja; nie trafia do ścieżki czytania bez akceptacji |
| **Szum** | drzewo plików, pakiet dla AI | plik, którego reviewer prawie nigdy nie czyta | Ta sama reguła służy do dwóch różnych celów — świadomie |
| **Rozwiązany** (wątek) | widok komentarzy, liczniki, odznaki | status *naprawiony*, *nie naprawimy* albo *zamknięty* | Wątek **bez statusu** liczy się jako czekający, nie jako rozwiązany |
| **Iteracja** | etykieta „kod zmienił się po tym komentarzu", porównanie od iteracji | jedno wypchnięcie zmian do PR (pojęcie Azure DevOps) | Z przeglądarki przychodzi **numer** iteracji, nigdy identyfikator commita |
| **Commit głowy** | aktualność Summary, wariant zapasowy reguł per plik, znacznik czasu ścieżki | szczyt gałęzi źródłowej w ostatniej iteracji PR | Zmienia się przy każdym wypchnięciu — stąd NIESP-04 |
| **Identyfikator treści pliku** | reguła nieaktualności znacznika **i wyjaśnienia pliku** | identyfikator zawartości pliku w danej iteracji | Zmienia się **wtedy i tylko wtedy**, gdy zmieniła się treść tego pliku |
| **Przejście** | ekran wejścia, tryb przejścia, domknięcie | prowadzone czytanie ścieżki plik po pliku, z własnym licznikiem | **Dodatkowa** droga obok drzewa, nie zamiennik; licznik liczy po ścieżce, nie po liście z Azure DevOps |
| **Kontekst** | panel Summary („Kontekst: a / b plików") | pakiet danych przekazany modelowi, po zastosowaniu budżetów | Nie mylić z „kontekstem aplikacji" z opisu problemu produktowego |
| **Checklista** | nagłówek, szyna, lista PR | sześć ręcznych kroków, bez żadnej logiki między nimi | `Ready` **nie** zmienia się automatycznie i **nie** jest głosem reviewera |
| **Summary** | szyna, lista propozycji | 2–5 zdań o PR + ranking do 10 plików | Jedna z dwóch ścieżek AI; druga to wyjaśnienie pojedynczego pliku |
| **Debug Check** | szyna, checklista | jedno stałe pytanie + pole odpowiedzi | Krok `Debug` w checkliście jest **niezależny** — zapis odpowiedzi go nie zaznacza |
