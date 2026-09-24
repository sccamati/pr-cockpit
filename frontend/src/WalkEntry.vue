<script setup lang="ts">
// US-P3. Opening a pull request of 83 files on a tree of 83 files is the problem this screen
// exists for: one sentence, eight files, one decision.
import { useCockpit } from './cockpit'
import { fileDirectory, fileName } from './format'

const { details, summary: summaryState, review, walk, comments } = useCockpit()
const { summary, summaryLoading, summaryReadLoading, summaryError, summaryFreshness, criticalProposal, fullProposal, roleByPath, generateSummary } = summaryState
const { stalePaths, walkPosition, reviewState } = review
const {
  view, walkMode, walkResumeDismissed, walkResumable, walkChangedSincePicked, walkPaths, roundAvailable, roundPaths,
  roundUnreadCount, walkPick, walkPickSet, walkCandidates, walkOutsideCount, walkOutsideNoiseCount, fullOrderMissing,
  setWalkMode, toggleWalkPick, startWalkthrough, resumeWalkthrough,
} = walk
const { myOpenThreads, openMyThreads } = comments
</script>

<template>
  <section class="walk-entry" aria-label="Wejście w przejście">
    <!-- Druga runda. Liczona z markerów, które już są w przeglądarce — bez AI,
         bez dodatkowego pytania do Azure DevOps. -->
    <div v-if="walkMode === 'round'" class="walk-round">
      <h3>Runda po poprawkach</h3>
      <p class="walk-round-counts">
        Od Twojego czytania zmieniło się <strong>{{ stalePaths.length }}</strong>
        {{ stalePaths.length === 1 ? 'plik' : 'plików' }}<template v-if="roundUnreadCount">,
        a <strong>{{ roundUnreadCount }}</strong> {{ roundUnreadCount === 1 ? 'pliku' : 'plików' }} nie widziałeś w ogóle</template>.
        Każdy otworzy się na zmianach od iteracji, na której go ostatnio oglądałeś.
      </p>
      <button v-if="myOpenThreads.length" type="button" class="walk-round-threads" @click="openMyThreads">
        {{ myOpenThreads.length }} {{ myOpenThreads.length === 1 ? 'Twój wątek czeka' : 'Twoich wątków czeka' }} na domknięcie
      </button>
    </div>
    <div class="walk-entry-summary">
      <h3>Co się zmieniło</h3>
      <p v-if="summaryLoading || summaryReadLoading" class="notice" role="status">Przygotowuję propozycję ścieżki…</p>
      <template v-else-if="summary">
        <div v-if="summary.sentences?.length" class="summary-text">
          <p v-for="(sentence, index) in summary.sentences" :key="index" :class="{ 'summary-lead': index === 0 }">{{ sentence }}</p>
        </div>
        <p v-else class="summary-text">{{ summary.summary }}</p>
        <p class="summary-report">Kontekst: {{ summary.contextReport.includedFiles }} / {{ summary.contextReport.changedFiles }} plików z diffem</p>
        <!-- Regenerating costs money too, so a stale ranking says so and waits. -->
        <p v-if="summaryFreshness === 'stale'" class="notice walk-stale" role="status">
          PR zmienił się od zapisania tego Summary — propozycja niżej pochodzi ze starszego commita.
          <button class="checklist-retry" type="button" :disabled="summaryLoading" @click="generateSummary">Przelicz (uruchomi AI)</button>
        </p>
      </template>
      <p v-else-if="summaryError" class="notice error" role="alert">
        Propozycja ścieżki jest niedostępna: {{ summaryError }}
        <button class="checklist-retry" type="button" :disabled="summaryLoading" @click="generateSummary">Spróbuj ponownie</button>
      </p>
      <!-- Nothing saved for this pull request: the model runs when asked, not when
           the screen opens. Every AI run is money, and it is the user's money. -->
      <div v-else-if="walkMode !== 'round'" class="walk-no-summary">
        <p class="muted">Dla tego PR nie ma jeszcze Summary, więc nie ma propozycji ścieżki.</p>
        <button type="button" class="walk-primary" :disabled="summaryLoading" @click="generateSummary">Zaproponuj ścieżkę (uruchomi AI)</button>
      </div>
    </div>

    <!-- US-P7: an interrupted walkthrough is offered back before anything is recomputed. -->
    <div v-if="walkResumable && !walkResumeDismissed" class="walk-resume">
      <p><strong>Przerwane przejście</strong> — krok {{ walkPosition + 1 }} z {{ walkPaths.length }}.</p>
      <p v-if="walkChangedSincePicked" class="notice walk-changed" role="status">
        PR zmienił się od czasu wyboru ścieżki. Możesz wznowić dotychczasową albo przeliczyć propozycję — nic nie kasuję bez Twojej decyzji.
      </p>
      <div class="walk-actions">
        <button type="button" class="walk-primary" @click="resumeWalkthrough">Wznów od kroku {{ walkPosition + 1 }}</button>
        <button type="button" @click="walkResumeDismissed = true">Przelicz propozycję</button>
        <button type="button" @click="view = 'tree'">Pełne drzewo plików</button>
      </div>
    </div>

    <template v-else>
      <div class="walk-entry-files">
        <!-- Dwa tryby tej samej kolejności: skrót i całość. Kolejność w obu układa AI. -->
        <div class="walk-mode" role="group" aria-label="Zakres przejścia">
          <button type="button" class="walk-mode-option" :class="{ 'walk-mode--on': walkMode === 'key' }"
            :aria-pressed="walkMode === 'key'" @click="setWalkMode('key')">
            Kluczowe pliki<small>{{ criticalProposal.length }}</small>
          </button>
          <button type="button" class="walk-mode-option" :class="{ 'walk-mode--on': walkMode === 'all' }"
            :aria-pressed="walkMode === 'all'" @click="setWalkMode('all')">
            Wszystkie pliki<small>{{ fullProposal.length || details?.changedFiles.length || 0 }}</small>
          </button>
          <button v-if="roundAvailable" type="button" class="walk-mode-option" :class="{ 'walk-mode--on': walkMode === 'round' }"
            :aria-pressed="walkMode === 'round'" @click="setWalkMode('round')">
            Od mojego przejścia<small>{{ roundPaths.length }}</small>
          </button>
        </div>
        <h3>{{ walkMode === 'round' ? 'Do przejrzenia w tej rundzie' : 'Proponowana ścieżka' }} ({{ walkPick.length }})</h3>
        <!-- Summary sprzed tej funkcji nie ma kolejności całego PR. Zmyślanie jej
             byłoby gorsze niż powiedzenie tego wprost i policzenie na żądanie. -->
        <p v-if="fullOrderMissing" class="notice" role="status">
          To Summary powstało, zanim doszła kolejność całego PR — mam ranking, ale nie mam ułożonych wszystkich plików.
          <button class="checklist-retry" type="button" :disabled="summaryLoading" @click="generateSummary">Przelicz (uruchomi AI)</button>
        </p>
        <p v-else-if="walkCandidates.length === 0" class="muted">
          Ranking nie wskazał plików. Wejdź w pełne drzewo albo dodaj pliki ręcznie ze ścieżki w prawej szynie.
        </p>
        <ul v-else class="walk-file-list">
          <li v-for="path in walkCandidates" :key="path" :class="{ 'walk-file--off': !walkPickSet.has(path) }">
            <label class="walk-file-pick">
              <input type="checkbox" :checked="walkPickSet.has(path)" @change="toggleWalkPick(path)">
              <span class="walk-file-path" :title="path">{{ fileName(path) }}<small>{{ fileDirectory(path) }}</small></span>
            </label>
            <span v-if="roleByPath.get(path)" class="walk-file-role">{{ roleByPath.get(path) }}</span>
            <span v-if="reviewState(path) === 'current'" class="walk-file-read">przeczytany</span>
          </li>
        </ul>
      </div>
      <p class="walk-entry-rest">
        Poza przejściem zostaje {{ walkOutsideCount }} {{ walkOutsideCount === 1 ? 'plik' : 'plików' }}<template v-if="walkOutsideNoiseCount">, w tym {{ walkOutsideNoiseCount }} {{ walkOutsideNoiseCount === 1 ? 'zaklasyfikowany' : 'zaklasyfikowanych' }} jako szum</template>.
      </p>
      <div class="walk-actions">
        <button type="button" class="walk-primary" :disabled="walkPick.length === 0" @click="startWalkthrough()">{{ walkMode === 'round' ? 'Rozpocznij rundę' : 'Rozpocznij przejście' }} ({{ walkPick.length }})</button>
        <button type="button" @click="view = 'tree'">Pełne drzewo plików</button>
      </div>
    </template>
  </section>
</template>
