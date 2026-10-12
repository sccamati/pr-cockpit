<script setup lang="ts">
// The strip above an open pull request: title, branches, author and the counters that must
// stay visible when the rail is hidden.
import { computed, ref } from 'vue'
import { useCockpit } from '@/cockpit'

defineEmits<{ help: [] }>()

const { details, checklist: checklistState, comments, pullRequests, backToList } = useCockpit()
const { checklist, checklistCompleted } = checklistState
const { threads, activeThreadCount, toggleComments, commentsEnabled } = comments
const { voteSaving, voteError, toggleWaitingForAuthor } = pullRequests
const waitingForAuthor = computed(() => details.value?.reviewers.some(reviewer => reviewer.isMine && reviewer.vote === -5) ?? false)

const copied = ref(false)
async function copyLink() {
  try {
    await navigator.clipboard.writeText(details.value!.webUrl!)
    copied.value = true
    setTimeout(() => { copied.value = false }, 1500)
  } catch {
    // ponytail: no fallback — the clipboard API works on localhost, which is the only place this runs.
  }
}
</script>

<template>
  <div v-if="details" class="pr-header">
    <button class="back-button" type="button" @click="backToList">← Wróć</button>
    <h2 :title="details.title"><span class="pr-header-number">#{{ details.id }}</span> {{ details.title }}</h2>
    <span class="status">{{ details.status }}</span>
    <span class="pr-header-meta pr-header-branch" :title="`${details.sourceBranch} → ${details.targetBranch}`">{{ details.sourceBranch }} → {{ details.targetBranch }}</span>
    <span class="pr-header-meta">{{ details.author }}</span>
    <span v-if="checklist" class="pr-header-progress">Checklista {{ checklistCompleted }} / 6</span>
    <span v-if="voteError" class="pr-header-error" role="alert" :title="voteError">{{ voteError }}</span>
    <!-- Like every other write, gone rather than greyed out when writing is off. -->
    <button v-if="commentsEnabled" type="button" class="pr-header-pill pr-header-vote"
      :class="{ 'pr-header-pill--on': waitingForAuthor }" :aria-pressed="waitingForAuthor" :disabled="voteSaving"
      aria-label="Czeka na autora"
      :title="waitingForAuthor ? 'Cofnij głos „Czeka na autora”' : 'Ustaw głos „Czeka na autora”'"
      @click="toggleWaitingForAuthor"><span aria-hidden="true">⏳</span><span class="pr-header-label"> Czeka na autora</span></button>
    <button v-if="details.webUrl" type="button" class="pr-header-pill pr-header-link" title="Kopiuj link do PR w Azure DevOps"
      :aria-label="copied ? 'Skopiowano link' : 'Kopiuj link do PR'"
      @click="copyLink"><span aria-hidden="true">{{ copied ? '✓' : '🔗' }}</span><span class="pr-header-label">{{ copied ? ' Skopiowano' : ' Link' }}</span></button>
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
.pr-header-error { flex: 0 1 auto; min-width: 0; max-width: 260px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--error-text); font-size: 12px; }
.pr-header-pill, .pr-header-comments { flex: none; padding: 4px 9px; border: 1px solid var(--border-button); border-radius: 999px; background: var(--surface); color: var(--text-muted); font-size: 12px; font-weight: 700; white-space: nowrap; }
.pr-header-pill--on, .pr-header-comments--open { border-color: var(--accent); background: var(--accent-soft); color: var(--accent); }
.pr-header .back-button { height: 36px; padding: 0 12px; font-size: 13px; }
.shortcut-button { flex: none; width: 32px; height: 32px; border: 1px solid var(--border-button); border-radius: 50%; background: var(--surface); color: var(--text-strong); font-weight: 800; }
/* The title is the only thing that gives way (flex-basis 0), so on a narrow screen the
   pills lose their words first and the branch goes next, before the title disappears. */
@media (max-width: 1200px) { .pr-header-label { display: none; } }
@media (max-width: 900px) { .pr-header-branch { display: none; } }
</style>
