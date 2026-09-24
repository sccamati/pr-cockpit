<script setup lang="ts">
// The left pane of an open pull request: reading progress, search, filters and the tree.
import { useCockpit } from '@/cockpit'
import FileTree from './FileTree.vue'

const { details, fileList, review, checklist, walk, openFile } = useCockpit()
const {
  fileSearch, onlyUnreviewed, filterIteration, filterPaths, filterLoading, filterError, iterationChoices,
  matchingFiles, unreviewedMatches, codeFiles, noiseFiles, fileTree, noiseTree, expandAll, nextUnreviewedPath, setFilterIteration,
} = fileList
const { reviewedPaths, stalePaths, fileReviewError, remainingCount, toggleCritical } = review
const { remainingChecklist } = checklist
const { walkWorthwhile, enterWalkthrough } = walk

function openNextUnreviewed() {
  if (nextUnreviewedPath.value) void openFile(nextUnreviewedPath.value)
}
</script>

<template>
  <div v-if="details" class="file-list-pane">
    <div class="file-list-head">
      <div class="file-review-heading">
        <h3>Zmienione pliki ({{ details.changedFilesCount }})</h3>
        <span>{{ reviewedPaths.length }} / {{ details.changedFiles.length }} obejrzanych</span>
      </div>
      <progress v-if="details.changedFiles.length" class="file-progress" :value="reviewedPaths.length"
        :max="details.changedFiles.length" aria-label="Postęp przeglądania plików" />
      <p v-if="stalePaths.length" class="file-stale-note">
        {{ stalePaths.length }} {{ stalePaths.length === 1 ? 'plik zmienił się' : 'plików zmieniło się' }} od czasu przeczytania.
      </p>
      <p v-if="fileReviewError" class="notice error file-review-error" role="alert">{{ fileReviewError }}</p>
      <p v-if="details.changedFiles.length && remainingCount === 0" class="review-complete" role="status">
        Wszystkie pliki obejrzane.<template v-if="remainingChecklist.length"> Zostało w checkliście: {{ remainingChecklist.join(', ') }}.</template>
      </p>
    </div>
    <p v-if="details.changedFiles.length === 0" class="file-list-empty">Brak zmienionych plików.</p>
    <template v-else>
      <label class="file-search-label" for="file-search">Szukaj pliku</label>
      <input id="file-search" v-model="fileSearch" class="file-search" type="search" placeholder="Nazwa lub ścieżka" autocomplete="off">
      <div class="file-filter" role="group" aria-label="Filtr plików">
        <button type="button" :aria-pressed="!onlyUnreviewed" :class="{ active: !onlyUnreviewed }" @click="onlyUnreviewed = false">Wszystkie</button>
        <button type="button" :aria-pressed="onlyUnreviewed" :class="{ active: onlyUnreviewed }" @click="onlyUnreviewed = true">Nieobejrzane</button>
      </div>
      <template v-if="iterationChoices.length">
        <label class="file-search-label" for="iteration-filter">Pokaż zmiany</label>
        <select id="iteration-filter" class="iteration-filter" :value="filterIteration ?? ''"
          :disabled="filterLoading"
          @change="setFilterIteration(($event.target as HTMLSelectElement).value === '' ? null : Number(($event.target as HTMLSelectElement).value))">
          <option value="">Z całego PR</option>
          <option v-for="choice in iterationChoices" :key="choice.id" :value="choice.id">{{ choice.label }}</option>
        </select>
        <p v-if="filterError" class="notice error" role="alert">{{ filterError }}</p>
        <p v-else-if="filterLoading" class="file-list-hint" role="status">Sprawdzam, co się zmieniło…</p>
        <p v-else-if="filterPaths" class="file-list-hint" role="status">
          {{ filterPaths.length === 0 ? 'Po tej aktualizacji nic się nie zmieniło.' : `Zmienione po aktualizacji ${filterIteration}: ${filterPaths.length} z ${details.changedFiles.length}. Diff pokazuje tylko te zmiany.` }}
        </p>
      </template>
      <button v-if="walkWorthwhile" class="walk-enter-button" type="button" @click="enterWalkthrough">Prowadź mnie przez PR</button>
      <button class="next-file-button" type="button" :disabled="!nextUnreviewedPath" @click="openNextUnreviewed">Następny nieobejrzany →</button>
      <p v-if="matchingFiles.length === 0" class="file-list-empty">Nie znaleziono plików.</p>
      <p v-else-if="!nextUnreviewedPath && remainingCount > 0" class="file-list-hint">{{ unreviewedMatches.length === 0 ? 'Brak nieobejrzanych plików w wynikach wyszukiwania.' : 'To ostatni nieobejrzany plik. Oznacz go po przejrzeniu.' }}</p>
      <div v-if="codeFiles.length > 0" class="changed-files" aria-label="Zmienione pliki">
        <FileTree :node="fileTree" :show-ratio="!onlyUnreviewed" @open="openFile" @toggle-critical="toggleCritical" />
      </div>
      <details v-if="noiseFiles.length > 0" class="noise-section" :open="expandAll || noiseTree.containsSelected">
        <summary class="noise-summary">Szum ({{ noiseFiles.length }})</summary>
        <FileTree :node="noiseTree" :show-ratio="!onlyUnreviewed" @open="openFile" @toggle-critical="toggleCritical" />
      </details>
    </template>
  </div>
</template>

<style scoped>
.file-review-heading { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; }
.file-review-heading span { color: var(--accent); font-size: 13px; font-weight: 700; }
.file-progress { display: block; width: 100%; height: 7px; margin: 0; accent-color: var(--accent); }
.file-stale-note { margin: 8px 0 0; color: var(--warn-text); font-size: 12px; font-weight: 700; }
.file-review-error { margin: 8px 0 0; font-size: 12px; }
.review-complete { margin: 0 0 14px; color: var(--ok); font-size: 13px; font-weight: 700; }
.file-list-pane { min-width: 0; min-height: 0; border: 1px solid var(--line-strong); border-radius: var(--radius); overflow: hidden; }
.file-list-pane { display: flex; flex-direction: column; padding: 12px 0 0; background: var(--surface); }
.file-search-label { padding: 0 12px 6px; color: var(--text-label); font-size: 12px; font-weight: 700; }
.file-search { height: 38px; min-width: 0; margin: 0 12px 12px; padding: 0 10px; border: 1px solid var(--border-control); border-radius: 6px; background: var(--surface); color: var(--text); font: inherit; }
.file-search:focus-visible { outline: 3px solid var(--focus-ring); outline-offset: 2px; }
/* min-width: 0, bo bez niego szerokość najdłuższej opcji rozpycha select poza panel,
   a panel ma overflow: hidden — strzałka znikała za krawędzią. */
.iteration-filter { height: 38px; min-width: 0; margin: 0 12px 10px; padding: 0 8px; border: 1px solid var(--border-control); border-radius: 6px; background: var(--surface); color: var(--text); font: inherit; font-size: 12px; }
.iteration-filter:focus-visible { outline: 3px solid var(--focus-ring); outline-offset: 2px; }
/* Prymarne CTA kolumny to "Prowadź mnie przez PR"; ten jest skrótem, nie drugim CTA. */
.next-file-button { margin: 0 12px 10px; padding: 6px 8px; border: 0; border-radius: 6px; background: transparent; color: var(--accent); font-size: 12px; font-weight: 700; text-align: left; }
.next-file-button:hover:not(:disabled) { background: var(--accent-soft); }
.next-file-button:disabled { color: var(--text-muted); cursor: not-allowed; }
.file-list-hint { margin: 0 12px 10px; color: var(--text-muted); font-size: 12px; }
.file-list-empty { margin: 0; padding: 12px; color: var(--text-muted); }
.file-list-head { padding: 0 12px 10px; border-bottom: 1px solid var(--line); }
.file-list-head h3 { margin: 0; font-size: 13px; }
.file-list-head .file-review-heading span { font-size: 12px; }
.changed-files { flex: 1; min-height: 0; overflow-y: auto; border-top: 1px solid var(--line); }
.noise-section { flex: 0 0 auto; max-height: 40%; overflow-y: auto; border-top: 1px solid var(--line); }
.noise-summary { padding: 6px 10px; font-size: 13px; color: var(--text-muted); background: var(--surface-muted); border-bottom: 1px solid var(--line); cursor: pointer; }
.noise-summary:hover { background: var(--surface-hover); }
.noise-summary:focus-visible { outline: 3px solid var(--focus-ring); outline-offset: -3px; }
.walk-enter-button { width: 100%; margin-bottom: 6px; border-color: var(--accent); background: var(--accent-soft); color: var(--accent); font-weight: 700; }

@media (max-width: 900px) {
  .file-list-pane { height: 340px; flex: none; }
}

@media (max-width: 720px) {
  .file-review-heading { align-items: flex-start; flex-direction: column; }
}
</style>
