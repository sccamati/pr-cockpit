<script setup lang="ts">
// The strip above an open pull request: title, branches, author and the counters that must
// stay visible when the rail is hidden.
import { useCockpit } from '@/cockpit'

defineEmits<{ help: [] }>()

const { details, checklist: checklistState, comments, backToList } = useCockpit()
const { checklist, checklistCompleted } = checklistState
const { threads, activeThreadCount, toggleComments } = comments
</script>

<template>
  <div v-if="details" class="pr-header">
    <button class="back-button" type="button" @click="backToList">← Wróć</button>
    <h2 :title="details.title"><span class="pr-header-number">#{{ details.id }}</span> {{ details.title }}</h2>
    <span class="status">{{ details.status }}</span>
    <span class="pr-header-meta" :title="`${details.sourceBranch} → ${details.targetBranch}`">{{ details.sourceBranch }} → {{ details.targetBranch }}</span>
    <span class="pr-header-meta">{{ details.author }}</span>
    <span v-if="checklist" class="pr-header-progress">Checklista {{ checklistCompleted }} / 6</span>
    <!-- The rail is hidden in focus mode, so the header is the only place an
         unresolved conversation can stay visible. -->
    <button v-if="threads.length" type="button" class="pr-header-comments"
      :class="{ 'pr-header-comments--open': activeThreadCount > 0 }"
      :title="`${activeThreadCount} nierozwiązanych z ${threads.length} · widok komentarzy (c)`"
      :aria-label="`Komentarze: ${activeThreadCount} nierozwiązanych z ${threads.length}`"
      @click="toggleComments">💬 {{ activeThreadCount }}</button>
    <button class="shortcut-button" type="button" aria-label="Skróty klawiszowe" title="Skróty klawiszowe (?)"
      @click="$emit('help')">?</button>
  </div>
</template>

<style scoped>
.back-button { flex: none; }
.pr-header { display: flex; align-items: center; gap: 14px; min-height: var(--pr-header-height); padding: 0 2px; border-bottom: 1px solid var(--line); }
.pr-header h2 { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 18px; }
.pr-header-number { color: var(--accent); font-weight: 800; }
.pr-header-meta { flex: none; max-width: 260px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text-muted); font-size: 12px; }
.pr-header-progress { flex: none; color: var(--accent); font-size: 12px; font-weight: 700; white-space: nowrap; }
.pr-header-comments { flex: none; padding: 4px 9px; border: 1px solid var(--border-button); border-radius: 999px; background: var(--surface); color: var(--text-muted); font-size: 12px; font-weight: 700; white-space: nowrap; }
.pr-header-comments--open { border-color: var(--accent); background: var(--accent-soft); color: var(--accent); }
.pr-header .back-button { height: 36px; padding: 0 12px; font-size: 13px; }
.shortcut-button { flex: none; width: 32px; height: 32px; border: 1px solid var(--border-button); border-radius: 50%; background: var(--surface); color: var(--text-strong); font-weight: 800; }
</style>
