<script setup lang="ts">
// Recursive: a folder renders FileTree for each of its subfolders. Collapsing is native
// <details>, so there is no open/closed state to keep anywhere.
import type { TreeFolder } from './fileTree'

defineProps<{ node: TreeFolder; showRatio: boolean }>()
defineEmits<{ open: [path: string]; toggleCritical: [path: string] }>()
</script>

<template>
  <component :is="node.label ? 'details' : 'div'" class="tree-folder" :open="node.label ? node.open : undefined">
    <summary v-if="node.label" class="tree-summary">
      <span class="tree-label" :title="node.label">{{ node.label }}</span>
      <!-- Under the "unreviewed only" filter every visible file is unread by definition,
           so a 0/N ratio would claim nothing was read. Show the plain count instead. -->
      <span v-if="node.commentCount > 0" class="tree-comments"
        :class="{ 'tree-comments--open': node.unresolvedCount > 0 }"
        :title="`${node.commentCount} komentarzy, ${node.unresolvedCount} nierozwiązanych`">💬 {{ node.commentCount }}</span>
      <span class="tree-count" :class="{ 'tree-count--done': showRatio && node.reviewedCount === node.total }"
        :title="showRatio ? `Obejrzane ${node.reviewedCount} z ${node.total} plików` : `${node.total} plików`">
        {{ showRatio ? `${node.reviewedCount}/${node.total}` : node.total }}
      </span>
    </summary>

    <FileTree v-for="folder in node.folders" :key="folder.key" :node="folder" :show-ratio="showRatio"
      @open="$emit('open', $event)" @toggle-critical="$emit('toggleCritical', $event)" />

    <ul v-if="node.files.length" class="tree-files">
      <li v-for="file in node.files" :key="file.path">
        <button class="file-button" :class="{ selected: file.selected, reviewed: file.reviewed }" type="button"
          :aria-current="file.selected ? 'true' : undefined" :title="file.path" @click="$emit('open', file.path)">
          <span class="file-name">{{ file.name }}</span>
          <span class="file-badges">
            <!-- Jedna litera, całe słowo w title: przy dziewięćdziesięciu plikach ta
                 szerokość jest potrzebna nazwie pliku, nie odznace. -->
            <span class="change-type" :title="file.changeType" :aria-label="file.changeType">{{ file.changeType.slice(0, 1) }}</span>
            <span v-if="file.comments > 0" class="tree-comments"
              :class="{ 'tree-comments--open': file.unresolvedComments > 0 }"
              :title="file.unresolvedComments > 0 ? `${file.unresolvedComments} nierozwiązanych z ${file.comments}` : `${file.comments} rozwiązanych`">💬 {{ file.comments }}</span>
            <span v-if="file.reviewed" class="reviewed-badge">✓</span>
            <span v-else-if="file.stale" class="reviewed-badge reviewed-badge--stale" title="Plik zmienił się od czasu przeczytania">✓ zmienione</span>
          </span>
        </button>
        <button class="critical-toggle" :class="{ active: file.critical }" type="button"
          :aria-pressed="file.critical" :disabled="file.criticalDisabled"
          :title="file.critical ? 'Usuń ze ścieżki czytania' : 'Dodaj do ścieżki czytania'"
          :aria-label="file.critical ? `Usuń ${file.name} ze ścieżki czytania` : `Dodaj ${file.name} do ścieżki czytania`"
          @click="$emit('toggleCritical', file.path)">{{ file.critical ? '★' : '☆' }}</button>
        <p v-if="file.role" class="file-role" :title="file.role">{{ file.role }}</p>
        <p v-if="file.originalPath && file.originalPath !== file.path" class="previous-path">z {{ file.originalPath }}</p>
      </li>
    </ul>
  </component>
</template>

<style scoped>
.reviewed-badge--stale { padding: 2px 6px; border-radius: 5px; background: var(--warn-bg); color: var(--warn-text); }
/* Opis ma tylko część plików i łamie się na dwie linie, więc w drzewie podwajał liczbę
   wierszy. Zostaje przy pliku, na którym stoisz. */
.file-role { display: none; margin: 0 0 2px 10px; font-size: 12px; color: var(--text-muted); font-style: italic; }
.tree-files li:has(.file-button.selected) .file-role { display: block; }
.tree-comments { font-size: 11px; color: var(--text-muted); }
.tree-comments--open { color: var(--accent); font-weight: 600; }
/* One indent step per nesting level; compacted chains keep this shallow. */
.tree-folder .tree-folder, .tree-folder > .tree-files { padding-left: 9px; }
.tree-summary { display: flex; align-items: center; gap: 8px; padding: 6px 10px; border-bottom: 1px solid var(--line); background: var(--surface-muted); cursor: pointer; }
.tree-summary:hover { background: var(--surface-hover); }
.tree-summary:focus-visible { outline: 3px solid var(--focus-ring); outline-offset: -3px; }
/* Wraps instead of truncating: only the top compacted chain is long, and cutting it
   would hide the most specific folder, which is the informative part. */
.tree-label { flex: 1; min-width: 0; overflow-wrap: anywhere; color: var(--text-label); font-family: Consolas, 'Courier New', monospace; font-size: 11.5px; font-weight: 700; }
.tree-count { flex: none; color: var(--text-muted); font-size: 11px; font-weight: 700; font-variant-numeric: tabular-nums; }
.tree-count--done { color: var(--ok); }
.tree-files { margin: 0; padding: 0; list-style: none; }
.tree-files li { display: flex; flex-wrap: wrap; align-items: center; border-bottom: 1px solid var(--line); }
.tree-files li:last-child { border-bottom: 0; }
.file-button { display: flex; align-items: center; gap: 8px; flex: 1; min-width: 0; padding: 7px 4px 7px 12px; border: 0; background: transparent; color: inherit; text-align: left; }
.file-button:hover { background: var(--surface-hover); }
.file-button:focus-visible { outline: 3px solid var(--focus-ring); outline-offset: -3px; }
.file-button.selected { background: var(--accent-soft); box-shadow: inset 3px 0 var(--accent); }
.file-button.selected .file-name { color: var(--accent); }
.file-name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: Consolas, 'Courier New', monospace; font-size: 12.5px; font-weight: 600; }
.file-badges { display: flex; flex: none; gap: 5px; align-items: center; }
/* Jedna litera, całe słowo w title: przy 93 plikach nazwa potrzebuje tej szerokości. */
.change-type { flex: none; min-width: 17px; padding: 2px 4px; border-radius: 5px; background: var(--surface-chip); color: var(--text-label); font-size: 11px; font-weight: 700; text-align: center; }
/* Obejrzany plik gaśnie, zamiast zapalać kolejną zieleń — w drzewie zieleń zostaje
   zaznaczeniu. Woła tylko wariant --stale, bo tam jest co zrobić. */
.reviewed-badge { flex: none; color: var(--text-muted); font-size: 11px; font-weight: 700; }
.file-button.reviewed .file-name { color: var(--text-muted); font-weight: 500; }
.critical-toggle { flex: none; width: 28px; height: 28px; margin: 0 6px 0 0; padding: 0; border: 0; border-radius: 5px; background: transparent; color: var(--text-muted); font-size: 15px; line-height: 1; }
.critical-toggle:hover:not(:disabled) { color: var(--accent); background: var(--accent-soft); }
.critical-toggle.active { color: var(--accent); }
.previous-path { flex-basis: 100%; margin: 0; padding: 0 12px 6px 12px; color: var(--text-muted); overflow-wrap: anywhere; font-size: 11px; }
</style>
