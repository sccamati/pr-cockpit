# Backlog operacyjny PR Cockpit

Stan: 2026-09-19. Długoterminowy cel i epiki są w [PRODUCT.md](../PRODUCT.md). Ten plik wskazuje kolejność najbliższych prac i warunki ich zakończenia. Po każdym zadaniu aktualizujemy jego status; nowe pomysły trafiają do sekcji „Później”, dopóki nie wybierzemy ich do realizacji.

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

## Proponowany kolejny mały etap — do decyzji

### B-04 — Ręczna ścieżka kluczowych plików

**Dlaczego:** samo odznaczenie przeczytanych plików mówi o postępie review, ale nie tworzy krótkiej ścieżki prowadzącej przez kod. Wybór kluczowych plików pozwoli sprawdzić użyteczność tej części „Understand PR” przed decyzją o automatycznej analizie.

**Zakres:** w widoku PR użytkownik może wskazać z listy zmian maksymalnie 10 kluczowych plików i ułożyć je w kolejności czytania. Osobny, krótki panel pokazuje wybrane ścieżki z możliwością otwarcia diffu i usunięcia pozycji. Wybór jest ręczny i istnieje tylko w pamięci karty, podobnie jak stan „Obejrzałem”. Nie proponujemy automatycznego rankingu, podsumowania ani głównego flow w tym etapie.

**Gotowe, gdy:** można zbudować i zmienić kolejność listy 1–10 plików, każdy link otwiera właściwy diff, usunięty lub już nieobecny w PR plik nie zostaje na liście, a zwykły znacznik „Obejrzałem” działa niezależnie. Panel jest czytelny na wąskim ekranie; test interakcji obejmuje dodanie, zmianę kolejności, usunięcie i ponowne otwarcie pliku. Przed rozpoczęciem tego etapu wracamy do nierozstrzygniętych sprawdzeń B-02/B-03 na realnym PR i małym ekranie.

## Później — do osobnej decyzji

- **Understand PR:** podsumowanie, główny flow i ważne pliki. Przed implementacją wybrać źródło kontekstu i sposób uruchamiania analizy.
- **Quality, Architecture i checklisty:** zgodnie z [PRODUCT.md](../PRODUCT.md), po potwierdzeniu podstawowego workflow.
- **Lokalne repozytorium, AI i trwały zapis:** nie są częścią zadań B-01–B-03; wymagają osobnego ustalenia zakresu.
