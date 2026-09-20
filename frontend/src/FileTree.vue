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
      <span class="tree-count" :class="{ 'tree-count--done': showRatio && node.reviewedCount === node.total }">
        {{ showRatio ? `${node.reviewedCount}/${node.total}` : node.total }}
      </span>
    </summary>

    <FileTree v-for="folder in node.folders" :key="folder.key" :node="folder" :show-ratio="showRatio"
      @open="$emit('open', $event)" @toggle-critical="$emit('toggleCritical', $event)" />

    <ul v-if="node.files.length" class="tree-files">
      <li v-for="file in node.files" :key="file.path">
        <button class="file-button" :class="{ selected: file.selected }" type="button"
          :aria-current="file.selected ? 'true' : undefined" :title="file.path" @click="$emit('open', file.path)">
          <span class="file-name">{{ file.name }}</span>
          <span class="file-badges">
            <span class="change-type">{{ file.changeType }}</span>
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
