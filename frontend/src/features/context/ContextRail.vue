<script setup lang="ts">
// The right-hand rail of an open pull request: Summary, checklist, threads at a glance, Debug
// Check, the reading path and the plain PR metadata. Hidden in focus mode and in the
// walkthrough, which is why the header carries the checklist and comment counters too.
import { computed } from 'vue'
import { useCockpit } from '@/cockpit'
import { commentPreview } from '@/lib/description'
import { commitTitle, fileDirectory, fileName, formatDate, omissionLabel, reviewerVote, threadStatusLabels } from '@/lib/format'
import { checklistItems } from './useChecklist'
import { threadLocation } from '@/features/comments/useComments'

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

<style scoped>
.metadata { display: grid; grid-template-columns: repeat(3, 1fr); gap: 24px; padding: 28px 0; }
.metadata div { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
.metadata span { color: var(--text-muted); font-size: 12px; }
.metadata strong { overflow-wrap: anywhere; font-size: 14px; }
.details-section { padding: 22px 0; border-top: 1px solid var(--line); }
.details-section:last-child { padding-bottom: 0; }
.checklist-items { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; margin-top: 16px; }
.checklist-item { display: flex; align-items: center; gap: 10px; min-height: 44px; padding: 9px 12px; border: 1px solid var(--line-strong); border-radius: var(--radius); color: var(--text-soft); cursor: pointer; }
.checklist-item--done { border-color: var(--ok-line); background: var(--ok-bg); color: var(--ok); }
.checklist-item:has(input:disabled) { opacity: .65; cursor: wait; }
.checklist-item input { width: 17px; height: 17px; margin: 0; accent-color: var(--accent); }
/* Szyna jest jednym przewijanym słupkiem: bez position: sticky tekst Summary zaczyna się
   w środku zdania, bez nagłówka nad nim. To samo niżej dla sekcji <details> szyny. */
.summary-heading { position: sticky; top: 0; z-index: 1; display: flex; align-items: center; justify-content: space-between; gap: 18px; padding: 8px 0; background: var(--surface); }
.summary-heading .muted { margin-bottom: 0; font-size: 13px; }
.summary-button { flex: none; min-height: 40px; padding: 0 16px; border: 0; border-radius: 7px; background: var(--accent); color: #ffffff; font-weight: 700; }
.summary-button:hover:not(:disabled) { background: var(--accent-strong); }
.summary-meta { display: flex; flex-wrap: wrap; gap: 8px 14px; align-items: center; margin-top: 18px; font-size: 12px; }
.summary-saved { color: var(--text-muted); }
.summary-current { color: var(--ok); font-weight: 700; }
.summary-stale { max-width: 80ch; margin: 12px 0 0; border-color: var(--warn-line); background: var(--warn-bg); color: var(--warn-text); }
.summary-omissions { margin-top: 12px; color: var(--text-soft); font-size: 13px; }
.summary-omissions summary { cursor: pointer; font-weight: 700; }
.summary-omissions ul { max-height: 180px; margin: 8px 0 0; padding-left: 20px; overflow: auto; }
.summary-omissions li { padding: 3px 0; overflow-wrap: anywhere; }
.critical-empty { margin: 14px 0 0; font-size: 13px; }
.critical-list { margin: 16px 0 0; padding: 0; list-style: none; border: 1px solid var(--line-mid); border-radius: var(--radius); overflow: hidden; }
.critical-list li { display: flex; align-items: center; gap: 12px; padding: 7px 10px; border-bottom: 1px solid var(--line); }
.critical-list li:last-child { border-bottom: 0; }
.critical-order { display: grid; place-items: center; flex: none; width: 24px; height: 24px; border-radius: 50%; background: var(--accent-soft); color: var(--accent); font: 700 12px Inter, 'Segoe UI', sans-serif; }
.critical-actions { display: flex; flex: none; gap: 6px; }
.critical-actions button { min-width: 32px; min-height: 30px; padding: 3px 7px; border: 1px solid var(--border-control); border-radius: 5px; background: var(--surface); color: var(--text-strong); font-size: 12px; }
.plain-list { margin: 0; padding-left: 20px; line-height: 1.9; }
.commit-list { max-height: 280px; margin: 0; padding: 0; list-style: none; overflow-y: auto; border: 1px solid var(--line); border-radius: var(--radius); }
.commit-list li { display: flex; align-items: baseline; gap: 14px; padding: 10px 12px; border-bottom: 1px solid var(--line); }
.commit-list li:last-child { border-bottom: 0; }
.commit-id { flex: none; color: var(--accent); font-size: 12px; }
.commit-info { display: flex; flex-direction: column; gap: 4px; min-width: 0; overflow-wrap: anywhere; }
.commit-info strong { font-size: 13px; font-weight: 600; }
.commit-info small { color: var(--text-muted); }
.details-section .muted { margin-bottom: 0; }
.context-rail { min-width: 0; min-height: 0; overflow-y: auto; padding: 0 14px 18px; border: 1px solid var(--line-strong); border-radius: var(--radius); background: var(--surface); }
.context-rail .details-section { padding-top: 14px; border-top: 0; }
.context-rail .checklist-items { grid-template-columns: repeat(2, minmax(0, 1fr)); }
.context-rail .metadata { grid-template-columns: 1fr; gap: 10px; padding: 10px 0; }
.context-rail h4 { margin: 16px 0 6px; font-size: 13px; }
.rail-block { padding: 12px 0; border-top: 1px solid var(--line); }
.rail-block > summary { position: sticky; top: 0; z-index: 1; padding: 2px 0; background: var(--surface); cursor: pointer; font-size: 13px; font-weight: 700; color: var(--text-strong); }
.rail-block > summary:focus-visible { outline: 3px solid var(--focus-ring); outline-offset: 2px; }
.rail-block > summary span { color: var(--accent); font-weight: 700; }
.rail-block .muted { margin: 8px 0 0; font-size: 12px; }
.critical-proposal { border: 1px dashed var(--line); border-radius: 6px; padding: 8px; margin-bottom: 8px; }
.critical-proposal-heading { margin: 0 0 6px; font-size: 13px; font-weight: 600; }
.critical-proposal-list { margin: 0; padding-left: 20px; display: flex; flex-direction: column; gap: 6px; }
.critical-proposal-why { margin: 2px 0 0; font-size: 12px; color: var(--text-muted); }
.critical-proposal-actions { display: flex; gap: 8px; margin-top: 8px; }
.critical-accept { font-weight: 600; }
.debug-question { margin: 0 0 6px; font-size: 13px; font-weight: 600; }
.debug-hint-later { font-size: 12px; margin: 0 0 6px; }
.debug-save { font-weight: 600; }
.debug-saved { font-size: 12px; color: var(--text-muted); }
.thread-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
.thread-preview { display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.comments-open { margin-top: 10px; width: 100%; }
.thread-moved-flag { color: var(--warn-text); }
/* Ścieżka po przejściu całego PR bywa długa; szyna pokazuje początek i tak mówi. */
.critical-more { margin: 8px 0 0; font-size: 12px; }

@media (max-width: 720px) {
  .checklist-items { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .critical-list li { align-items: stretch; flex-direction: column; gap: 4px; }
  .critical-actions { padding-left: 34px; }
  .summary-heading { align-items: stretch; flex-direction: column; }
  .commit-list li { flex-direction: column; gap: 4px; }
  .metadata { grid-template-columns: repeat(2, 1fr); }
}

@media (prefers-color-scheme: dark) {
  /* The accent is light-on-dark here, so the filled button needs dark text. */
  .summary-button { color: #0b1219; }
}
</style>
