# Architektura

```text
Vue 3 / Vite → ASP.NET Core API → Azure DevOps REST API
```

Frontend wybiera projekt i repozytorium, wyświetla aktywne PR-y i szczegóły. W developmentcie Vite przekazuje żądania `/api` do backendu. Backend jest pojedynczą aplikacją; `AzureDevOpsClient` izoluje wywołania HTTP, a `AzureDevOpsMapper` mapuje odpowiedzi na prosty kontrakt API. To wystarcza na obecny zakres bez dodatkowych projektów domenowych czy wzorców pośredniczących.

Backend czyta organizację i PAT z konfiguracji .NET (User Secrets lub zmienne środowiskowe). Frontend nie otrzymuje PAT. Integracja używa Azure DevOps REST API 7.1 i pobiera dane na żądanie. Dla szczegółów PR pobiera ostatnią iterację, listę zmienionych plików, commity i powiązane Work Items. Lista plików korzysta z porównania ostatniej iteracji z bazą PR i obsługuje strony zmian; licznik wynika z długości listy.

Po kliknięciu pliku frontend wywołuje osobny endpoint diffu. Backend ponownie pobiera ostatnią iterację i listę zmian (`$compareTo=0`), aby zaakceptować wyłącznie plik należący do PR. Wersja przed zmianą pochodzi z `commonRefCommit` tej iteracji, a wersja po zmianie z `sourceRefCommit`; przy przeniesieniu pliku wersja bazowa używa `originalPath`. Dla dodania i usunięcia jedna strona jest pusta. [Dokumentacja iteracji](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-iterations/list?view=azure-devops-rest-7.1) i [porównania zmian](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-iteration-changes/get?view=azure-devops-rest-7.1) opisują te punkty odniesienia.

Backend pobiera metadane pliku z [Git Items Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/items/get?view=azure-devops-rest-7.1) dla konkretnego commita, potem strumień z [Git Blobs Get Blob](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/blobs/get-blob?view=azure-devops-rest-7.1). Odrzuca pliki binarne, symlinki, pliki ponad 256 KB na wersję i tekst przekraczający 4000 linii łącznie. Diff linii jest liczony w backendzie, a UI pokazuje go wewnątrz widoku PR. Nie przechowujemy zawartości plików. Ten przepływ jest sprawdzony testami HTTP bez prawdziwego Azure DevOps; użytkownik potwierdził na prawdziwym Azure DevOps tylko wcześniejszą listę PR, szczegóły, opis i listę plików.

Nie ma jeszcze bazy danych. Ręczny znacznik „Obejrzałem” jest przechowywany tylko w pamięci bieżącej karty i znika po odświeżeniu. SQLite można dodać przy checklistach lub zapisanych analizach. Planowane obszary to `Analysis`, `Checklist` i `ProjectKnowledge`, ale nie istnieją jeszcze w kodzie.

PAT jest rozwiązaniem lokalnym, krótkoterminowym. Przed udostępnieniem aplikacji poza własną maszynę trzeba dodać Microsoft Entra i autoryzację API. Obecny profil uruchomieniowy nasłuchuje na localhost. Testy klienta używają atrapy `HttpMessageHandler`.
