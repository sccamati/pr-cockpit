<script setup lang="ts">
// The right-hand rail of an open pull request: Summary, checklist, threads at a glance, Debug
// Check, the reading path and the plain PR metadata. Hidden in focus mode and in the
// walkthrough, which is why the header carries the checklist and comment counters too.
import { computed } from 'vue'
import { useCockpit } from './cockpit'
import { commentPreview } from './description'
import { commitTitle, fileDirectory, fileName, formatDate, omissionLabel, reviewerVote, threadStatusLabels } from './format'
import { checklistItems } from './useChecklist'
import { threadLocation } from './useComments'

const { details, projectId, repositoryId, summary: summaryState, checklist: checklistState, review, comments, openCriticalFile } = useCockpit()
const {
  summary, summaryLoading, summaryError, summaryReadLoading, summaryReadError, summarySavedAt, summaryFreshness,
  criticalProposal, proposalDismissed, generateSummary, loadSavedSummary,
} = summaryState
const {
  checklist, checklistLoading, checklistError, checklistSaving, debugAnswer, debugSaving, debugSaved, showDebugHint,
  loadChecklist, setChecklistItem, saveDebugAnswer,
} = checklistState
const { remainingCount, criticalPaths, manualCriticalLimit, moveCritical, toggleCritical, saveReadingPath } = review
const { threads, threadsLoading, threadsError, activeThreadCount, movedSinceComment, loadThreads, openThread, toggleComments } = comments

const showProposal = computed(() =>
  criticalProposal.value.length > 0 && !proposalDismissed.value && criticalPaths.value.length === 0)
// ponytail: a flat cap on the rows the rail draws. The rail is a summary of the path;
// the walkthrough is where a long one is read.
const railPathPreview = 15
const railCriticalPaths = computed(() => criticalPaths.value.slice(0, railPathPreview))

function acceptProposal() {
  if (!showProposal.value) return
  saveReadingPath(criticalProposal.value.map(file => file.path))
  proposalDismissed.value = true
}
</script>

<template>
  <aside class="context-rail" aria-label="Kontekst pull requesta">
    <div class="details-section summary-section">
      <div class="summary-heading"><div><h3>Summary</h3><p class="muted">Analiza korzysta z ograniczonego kontekstu PR i uruchamia się tylko po kliknięciu.</p></div>
        <button class="summary-button" type="button" :disabled="summaryLoading" @click="generateSummary">{{ summaryLoading ? 'Generowanie…' : summary ? 'Generuj ponownie' : 'Generuj Summary' }}</button>
      </div>
      <p v-if="summaryReadLoading" class="notice" role="status">Wczytywanie zapisanego Summary…</p>
      <div v-if="summaryReadError" class="notice error" role="alert">Nie udało się wczytać zapisanego Summary: {{ summaryReadError }} <button class="checklist-retry" type="button" :disabled="summaryLoading" @click="loadSavedSummary(projectId, repositoryId, details!.id)">Spróbuj ponownie</button></div>
      <p v-if="summaryLoading" class="notice" role="status">Generowanie Summary…</p>
      <p v-if="summaryError" class="notice error" role="alert">{{ summaryError }}</p>
      <template v-if="summary">
        <div class="summary-meta"><span class="summary-saved">Zapisano lokalnie<template v-if="summarySavedAt"> · {{ formatDate(summarySavedAt) }}</template></span><span v-if="summaryFreshness === 'current'" class="summary-current">Aktualne dla tego PR</span></div>
        <p v-if="summaryFreshness === 'stale'" class="notice summary-stale" role="status">PR zmienił się od zapisania tego Summary. Wygeneruj je ponownie, aby uwzględnić aktualny commit.</p>
        <p v-else-if="summaryFreshness === 'unknown'" class="notice summary-stale" role="status">Nie można potwierdzić aktualności Summary, ponieważ brakuje SHA commita.</p>
        <div v-if="summary.sentences?.length" class="summary-text" aria-label="Podsumowanie PR"><p v-for="(sentence, index) in summary.sentences" :key="index" :class="{ 'summary-lead': index === 0 }">{{ sentence }}</p></div>
        <p v-else class="summary-text">{{ summary.summary }}</p>
        <p class="summary-report">Kontekst: {{ summary.contextReport.includedFiles }} / {{ summary.contextReport.changedFiles }} plików z diffem · {{ summary.contextReport.includedDiffCharacters }} znaków diffu<span v-if="summary.headCommitSha"> · commit {{ summary.headCommitSha.slice(0, 8) }}</span></p>
        <details v-if="summary.contextReport.wasLimited" class="summary-omissions">
          <summary>Pominięto treść {{ summary.contextReport.omittedFiles.length }} plików</summary>
          <ul><li v-for="file in summary.contextReport.omittedFiles" :key="file.path"><code>{{ file.path }}</code> — {{ omissionLabel(file.reason) }}</li></ul>
        </details>
      </template>
    </div>

    <details class="rail-block checklist-section" open>
      <summary>Checklista PR</summary>
      <p class="muted">Zaznaczaj ręcznie po wykonaniu każdego kroku. Stan zapisuje się lokalnie.</p>
      <p v-if="checklistLoading" class="notice" role="status">Wczytywanie checklisty…</p>
      <p v-if="checklistError" class="notice error" role="alert">{{ checklistError }}</p>
      <div v-if="checklist" class="checklist-items">
        <label v-for="item in checklistItems" :key="item.key" class="checklist-item" :class="{ 'checklist-item--done': checklist[item.key] }">
          <input type="checkbox" :checked="checklist[item.key]" :disabled="checklistSaving !== null" @change="setChecklistItem(item.key, ($event.target as HTMLInputElement).checked)">
          <span>{{ item.label }}</span>
        </label>
      </div>
      <button v-else-if="!checklistLoading" class="checklist-retry" type="button" @click="loadChecklist(projectId, repositoryId, details!.id)">Spróbuj ponownie</button>
    </details>

    <details class="rail-block threads-section" :open="threads.length > 0">
      <summary>Komentarze<span v-if="threads.length"> · {{ activeThreadCount }} nierozwiązanych z {{ threads.length }}</span></summary>
      <p v-if="threadsLoading" class="muted" role="status">Wczytywanie komentarzy…</p>
      <p v-else-if="threadsError" class="notice error" role="alert">{{ threadsError }}
        <button class="checklist-retry" type="button" @click="loadThreads(projectId, repositoryId, details!.id)">Spróbuj ponownie</button>
      </p>
      <template v-else>
        <p v-if="threads.length === 0" class="muted">Brak komentarzy w tym PR.</p>
        <!-- The rail stays a summary: one line per thread so you can see at a glance
             where the conversation is. Reading and writing happen in the full view. -->
        <ul v-else class="thread-list" aria-label="Komentarze PR">
          <li v-for="thread in threads" :key="thread.id" class="thread">
            <button class="thread-location" type="button" :title="thread.filePath ?? 'Cały PR'"
              :disabled="!thread.filePath" @click="openThread(thread)">{{ threadLocation(thread) }}</button>
            <span class="thread-author">{{ thread.comments[0]?.author ?? 'Nieznany autor' }}</span>
            <span v-if="thread.status && threadStatusLabels[thread.status]" class="thread-status">{{ threadStatusLabels[thread.status] }}</span>
            <span v-if="thread.comments.length > 1" class="thread-status">{{ thread.comments.length }} wpisy</span>
            <span v-if="movedSinceComment(thread)" class="thread-status thread-moved-flag">kod się zmienił</span>
            <p class="thread-content thread-preview">{{ commentPreview(thread.comments[0]!.content) }}</p>
          </li>
        </ul>
        <button type="button" class="comments-open" title="Widok komentarzy (c)" @click="toggleComments">Otwórz widok komentarzy</button>
      </template>
    </details>

    <details class="rail-block debug-check" :open="remainingCount === 0">
      <summary>Debug Check</summary>
      <p class="debug-question">Gdyby ta zmiana nie zadziałała, gdzie zacząłbyś szukać?</p>
      <p v-if="remainingCount > 0" class="muted debug-hint-later">Pytanie ma sens po przeczytaniu PR — zostało {{ remainingCount }} {{ remainingCount === 1 ? 'plik' : 'plików' }}.</p>
      <label class="debug-label" for="debug-answer">Twoja odpowiedź</label>
      <textarea id="debug-answer" v-model="debugAnswer" class="debug-answer" rows="3"
        :maxlength="2000" placeholder="Jedno zdanie wystarczy."></textarea>
      <div class="debug-actions">
        <button type="button" class="debug-save" :disabled="debugSaving" @click="saveDebugAnswer">{{ debugSaving ? 'Zapisywanie…' : 'Zapisz' }}</button>
        <button v-if="criticalProposal.length > 0" type="button" @click="showDebugHint = !showDebugHint">{{ showDebugHint ? 'Ukryj podpowiedź' : 'Pokaż, gdzie patrzeć' }}</button>
        <span v-if="debugSaved" class="debug-saved" role="status">Zapisano</span>
      </div>
      <!-- "Show me" reuses the ranking the Summary already returned — no second
           model run, and nothing here is scored against your answer. -->
      <ol v-if="showDebugHint" class="debug-hint" aria-label="Podpowiedź">
        <li v-for="(file, index) in criticalProposal" :key="file.path">
          <span class="debug-hint-order">{{ index + 1 }}</span>
          <span class="debug-hint-file" :title="file.path">{{ fileName(file.path) }}<small>{{ fileDirectory(file.path) }}</small></span>
          <span class="debug-hint-why">{{ file.why }}</span>
        </li>
      </ol>
    </details>

    <details class="rail-block critical-section" :open="criticalPaths.length > 0 || showProposal">
      <summary>Ścieżka kluczowych plików<span> · {{ criticalPaths.length }} / {{ manualCriticalLimit }}</span></summary>
      <p class="muted">Wybierz do {{ manualCriticalLimit }} plików i ustaw kolejność czytania. Zapisuje się lokalnie.</p>
      <div v-if="showProposal" class="critical-proposal">
        <p class="critical-proposal-heading">Propozycja AI ({{ criticalProposal.length }})</p>
        <ol class="critical-proposal-list" aria-label="Propozycja ścieżki czytania">
          <li v-for="file in criticalProposal" :key="file.path">
            <button class="critical-open" type="button" :title="file.path" @click="openCriticalFile(file.path)">{{ file.path }}</button>
            <p class="critical-proposal-why">{{ file.why }}</p>
          </li>
        </ol>
        <div class="critical-proposal-actions">
          <button type="button" class="critical-accept" @click="acceptProposal">Przyjmij ścieżkę</button>
          <button type="button" @click="proposalDismissed = true">Odrzuć</button>
        </div>
      </div>
      <p v-else-if="criticalPaths.length === 0" class="muted critical-empty">Dodaj pliki z listy zmian po lewej.</p>
      <!-- Po przejściu całego PR ścieżka ma tyle pozycji, ile PR ma plików. Szyna
           jest podsumowaniem, nie drugim widokiem przejścia — pokazuje początek. -->
      <ol v-else class="critical-list" aria-label="Ścieżka kluczowych plików">
        <li v-for="(path, index) in railCriticalPaths" :key="path">
          <button class="critical-open" type="button" :title="path" @click="openCriticalFile(path)"><span class="critical-order">{{ index + 1 }}</span><span>{{ path }}</span></button>
          <div class="critical-actions">
            <button type="button" :disabled="index === 0" :aria-label="`Przesuń ${path} w górę`" @click="moveCritical(path, -1)">↑</button>
            <button type="button" :disabled="index === criticalPaths.length - 1" :aria-label="`Przesuń ${path} w dół`" @click="moveCritical(path, 1)">↓</button>
            <button type="button" :aria-label="`Usuń ${path} ze ścieżki`" @click="toggleCritical(path)">Usuń</button>
          </div>
        </li>
      </ol>
      <p v-if="criticalPaths.length > railCriticalPaths.length" class="muted critical-more">
        …i {{ criticalPaths.length - railCriticalPaths.length }} dalszych plików — całą ścieżkę widać w przejściu.
      </p>
    </details>

    <details class="rail-block">
      <summary>Commity ({{ details!.commitsCount }})</summary>
      <p v-if="details!.commits.length === 0" class="muted">Brak commitów.</p>
      <ul v-else class="commit-list">
        <li v-for="commit in details!.commits" :key="commit.id">
          <code class="commit-id" :title="commit.id">{{ commit.id.slice(0, 8) }}</code>
          <span class="commit-info"><strong>{{ commitTitle(commit.message) }}</strong><small>{{ commit.author }}<template v-if="commit.authoredAt"> · {{ formatDate(commit.authoredAt) }}</template></small></span>
        </li>
      </ul>
    </details>

    <details class="rail-block">
      <summary>Szczegóły PR</summary>
      <div class="metadata">
        <div><span>Autor</span><strong>{{ details!.author }}</strong></div>
        <div><span>Repozytorium</span><strong>{{ details!.repository }}</strong></div>
        <div><span>Utworzono</span><strong>{{ formatDate(details!.createdAt) }}</strong></div>
        <div><span>Gałąź źródłowa</span><strong>{{ details!.sourceBranch }}</strong></div>
        <div><span>Gałąź docelowa</span><strong>{{ details!.targetBranch }}</strong></div>
        <div><span>Zmienione pliki</span><strong>{{ details!.changedFilesCount }}</strong></div>
      </div>
      <h4>Reviewerzy</h4>
      <p v-if="details!.reviewers.length === 0" class="muted">Brak reviewerów.</p>
      <ul v-else class="plain-list"><li v-for="reviewer in details!.reviewers" :key="reviewer.name">{{ reviewer.name }} <span class="muted">· {{ reviewerVote(reviewer.vote) }}</span></li></ul>
      <h4>Powiązane Work Items</h4>
      <p v-if="details!.workItems.length === 0" class="muted">Brak powiązanych Work Items.</p>
      <ul v-else class="plain-list"><li v-for="item in details!.workItems" :key="item.id">#{{ item.id }}</li></ul>
    </details>
  </aside>
</template>
