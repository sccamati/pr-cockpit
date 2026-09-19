# PR Cockpit — kontekst produktu

AI może tworzyć kod szybciej, niż developer jest w stanie przyswoić zmiany. Powstaje **context debt**: po review i merge trudniej wyjaśnić przepływ wykonania, wskazać ważne pliki, ocenić wpływ na architekturę i rozpocząć debugowanie.

PR Cockpit ma budować zrozumienie zmian i utrzymywać kontekst projektu. Nie zastępuje klasycznego code review.

Docelowe obszary:

- **Understand PR:** podsumowanie zmiany, główny flow, ważne pliki i obszary systemu.
- **Quality:** niewiele istotnych uwag o jakości, wydajności i bezpieczeństwie.
- **Checklist:** świadome przejście przez kroki review.
- **Architecture:** wykrywanie wpływu zmian i akceptacja wiedzy przez developera.
- **Project Memory:** trwała, zaakceptowana wiedza o komponentach, przepływach i historii decyzji.

Pierwszy vertical slice obejmuje jedynie wybór projektu i repozytorium Azure DevOps, listę aktywnych PR-ów oraz ich szczegóły. AI, checklista i trwały zapis danych są kolejnymi etapami. Rozwinięty pierwotny opis produktu pozostaje w [PRODUCT.md](../PRODUCT.md).
