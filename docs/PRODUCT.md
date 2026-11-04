# PR Cockpit — kontekst produktu

AI może tworzyć kod szybciej, niż developer jest w stanie przyswoić zmiany. Powstaje **context debt**: po review i merge trudniej wyjaśnić przepływ wykonania, wskazać ważne pliki, ocenić wpływ na architekturę i rozpocząć debugowanie.

PR Cockpit ma budować zrozumienie zmian i utrzymywać kontekst projektu. Nie zastępuje klasycznego code review.

Docelowe obszary:

- **Understand PR:** podsumowanie zmiany, główny flow, ważne pliki i obszary systemu.
- **Quality:** niewiele istotnych uwag o jakości, wydajności i bezpieczeństwie.
- **Quiz:** na koniec przeglądu pytania o to, co zmienia PR i jak; PR jest zakończony dopiero po zaliczeniu.
- **Architecture:** wykrywanie wpływu zmian i akceptacja wiedzy przez developera.
- **Project Memory:** trwała, zaakceptowana wiedza o komponentach, przepływach i historii decyzji.

Obecny zakres obejmuje wybór projektu i repozytorium Azure DevOps, listę aktywnych PR-ów, ich szczegóły i diff plików w Monaco. AI, quiz i trwały zapis danych są późniejszymi etapami. Rozwinięty opis produktu pozostaje w [PRODUCT.md](../PRODUCT.md), a najbliższe zadania w [BACKLOG.md](BACKLOG.md).
