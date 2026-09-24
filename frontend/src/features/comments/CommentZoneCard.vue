<script setup lang="ts">
// A conversation rendered between the lines of code, inside a container Monaco made.
// Ordinary Vue markup, so replying and resolving work the same as in the comments view.
import { useCockpit } from '@/cockpit'
import { commentPreview } from '@/lib/description'
import { formatDate, threadStatusLabels } from '@/lib/format'
import CommentDraft from './CommentDraft.vue'
import CommentThread from './CommentThread.vue'
import type { ReadableThread } from './useComments'

defineProps<{ line: number; thread: ReadableThread | null }>()

const { draft, commentError, inlineThreadId, collapsedZones, toggleZone, closeInlineComments } = useCockpit().comments
</script>

<template>
  <!-- The editor must not treat a click in the conversation as a click in the
       code, but it must not be prevented from happening either. -->
  <div class="zone-card" :class="{ 'zone-card--draft': !thread }" @mousedown.stop @click.stop>
    <template v-if="thread">
      <!-- The whole header folds the block; the caret is a hint, not the only target. -->
      <div class="zone-head" role="button" tabindex="0" :aria-expanded="!collapsedZones.has(line)"
        @click="toggleZone(line)" @keydown.enter.prevent="toggleZone(line)" @keydown.space.prevent="toggleZone(line)">
        <span class="zone-toggle">{{ collapsedZones.has(line) ? '▸' : '▾' }}</span>
        <span class="thread-author">{{ thread.comments[0]?.author ?? 'Nieznany autor' }}</span><time v-if="thread.comments[0]?.publishedAt" class="thread-date" :datetime="thread.comments[0].publishedAt">{{ formatDate(thread.comments[0].publishedAt) }}</time>
        <span v-if="thread.status && threadStatusLabels[thread.status]" class="thread-status">{{ threadStatusLabels[thread.status] }}</span>
        <span v-if="thread.comments.length > 1" class="thread-status">{{ thread.comments.length }} wpisy</span>
        <span v-if="collapsedZones.has(line)" class="zone-preview">{{ commentPreview(thread.comments[0]?.content ?? '') }}</span>
      </div>
      <template v-if="!collapsedZones.has(line)">
        <p v-if="commentError && inlineThreadId === thread.id" class="notice error" role="alert">{{ commentError }}</p>
        <CommentThread :thread="thread" hide-first-author />
      </template>
    </template>
    <template v-else-if="draft">
      <div class="zone-head"><strong>Nowy komentarz do linii {{ line }}</strong></div>
      <p v-if="commentError" class="notice error" role="alert">{{ commentError }}</p>
      <CommentDraft label="Treść komentarza do linii" @cancel="closeInlineComments" />
    </template>
  </div>
</template>

<style scoped>
.zone-card { margin: 4px 10px 6px 8px; padding: 8px 10px; border: 1px solid var(--line); border-left: 3px solid var(--accent); border-radius: 0 6px 6px 0; background: var(--surface-muted); font-family: system-ui, -apple-system, "Segoe UI", sans-serif; }
.zone-card--draft { border-left-style: dashed; }
.zone-head { display: flex; align-items: baseline; gap: 8px; min-width: 0; cursor: pointer; }
.zone-head:hover .zone-toggle { color: var(--accent); }
.zone-toggle { border: 0; background: none; padding: 0 2px; cursor: pointer; color: var(--text-muted); }
.zone-preview { font-size: 12px; color: var(--text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
