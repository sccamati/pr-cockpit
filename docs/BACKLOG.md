# Backlog operacyjny PR Cockpit

Stan: 2026-09-19. Długoterminowy cel i epiki są w [PRODUCT.md](../PRODUCT.md). Ten plik wskazuje kolejność najbliższych prac i warunki ich zakończenia. Po każdym zadaniu aktualizujemy jego status; nowe pomysły trafiają do sekcji „Później”, dopóki nie wybierzemy ich do realizacji.

## Potwierdzone

### B-01 — Potwierdzić diff na prawdziwym Azure DevOps

Status: użytkownik potwierdził działanie diffu na Azure DevOps. Nie przekazał osobnych wyników dla wszystkich przypadków brzegowych wymienionych poniżej.

**Dlaczego:** lista PR i plików była sprawdzona z rzeczywistą organizacją, ale pobieranie obu wersji pliku i Monaco tylko testami z atrapą HTTP oraz buildem. To największa niewiadoma przed rozwijaniem widoku review.

**Zakres:** na dostępnym PR sprawdzić zmianę tekstową, dodanie, usunięcie i przeniesienie pliku; sprawdzić komunikat dla pliku binarnego i przekraczającego limit, jeśli są dostępne; otworzyć kilka plików po kolei i wrócić do listy PR. Sprawdzić zachowanie panelu przy szerokości około 768 px i 375 px oraz czy samo otwarcie pliku nie zaznacza „Obejrzałem”. Poprawić wykryte błędy.

**Gotowe, gdy:** obie strony diffu odpowiadają zawartości PR, Monaco działa po zmianie pliku, układ jest czytelny na wąskim ekranie, a wynik weryfikacji jest zapisany tutaj. Przypadki niedostępne w testowanym PR trzeba oznaczyć jako niesprawdzone, nie jako zaliczone.

## Wdrożone — do sprawdzenia w aplikacji

### B-02 — Nawigacja po plikach do obejrzenia

Status: zaimplementowane. Logika działa z wyszukiwaniem, build frontendu przechodzi; układ na wąskim ekranie wymaga wizualnego potwierdzenia.

**Dlaczego:** przy dużym PR licznik „Obejrzałem” pomaga, ale nadal trzeba ręcznie szukać kolejnego nieprzeczytanego pliku.

**Zakres:** filtr „Wszystkie / Nieobejrzane”, akcja „Następny nieobejrzany”, czytelny postęp `obejrzane / wszystkie`. Zachować istniejące wyszukiwanie i ręczny checkbox. Stan pozostaje w pamięci karty.

**Gotowe, gdy:** filtr współdziała z wyszukiwaniem, nawigacja nie oznacza pliku automatycznie, a po obejrzeniu ostatniego pliku użytkownik dostaje jasny komunikat. Sprawdzić także działanie na wąskim ekranie.

### B-03 — Kontekst commitów w widoku PR

Status: zaimplementowane. Stronicowanie i pusta lista są pokryte testami HTTP; lista commitów wymaga potwierdzenia na rzeczywistym PR.

**Dlaczego:** widok pokazuje dziś tylko liczbę commitów. Ich tytuły i autorzy pomagają zrozumieć kolejność oraz zamiar zmian bez opuszczania PR Cockpit.

**Zakres:** rozwinąć istniejące pobieranie commitów o podstawowe metadane i pokazać listę w panelu PR. Obsłużyć stronicowanie oraz pustą listę, bez pobierania pełnej zawartości repozytorium.

**Gotowe, gdy:** lista odpowiada commitom bieżącego PR, działa dla więcej niż jednej strony wyników i ma testy klienta HTTP bez połączenia z Azure DevOps.

## Później — do osobnej decyzji

- **Understand PR:** podsumowanie, główny flow i ważne pliki. Przed implementacją wybrać źródło kontekstu i sposób uruchamiania analizy.
- **Quality, Architecture i checklisty:** zgodnie z [PRODUCT.md](../PRODUCT.md), po potwierdzeniu podstawowego workflow.
- **Lokalne repozytorium, AI i trwały zapis:** nie są częścią zadań B-01–B-03; wymagają osobnego ustalenia zakresu.
