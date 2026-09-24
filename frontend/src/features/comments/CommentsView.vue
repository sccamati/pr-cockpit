<script setup lang="ts">
// The third mode of the middle panel: every thread of the pull request, grouped by file in
// tree order, each with the lines it is about.
import CommentDraft from './CommentDraft.vue'
import CommentThread from './CommentThread.vue'
import { useCockpit } from '@/cockpit'
import { threadStatusLabels } from '@/lib/format'
import { isResolved, threadLocation } from './useComments'

const {
  threads, threadsLoading, threadSearch, threadFilter, draft, commentError, commentsEnabled,
  activeThreadCount, mineKnown, visibleThreads, threadGroups, snippetsLoading, threadSnippet,
  toggleComments, startDraft, openThread,
} = useCockpit().comments
</script>

<template>
  <section class="comments-view" aria-label="Komentarze pull requesta">
    <div class="comments-head">
      <h3>Komentarze</h3>
      <span class="comments-counts">{{ activeThreadCount }} aktywnych z {{ threads.length }}</span>
      <button type="button" class="comments-close" title="Zamknij widok komentarzy (c)" @click="toggleComments">Zamknij</button>
    </div>
    <div class="comments-toolbar">
      <label class="visually-hidden" for="thread-search">Szukaj w komentarzach</label>
      <input id="thread-search" v-model="threadSearch" class="thread-search" type="search"
        placeholder="Szukaj w treści, autorze lub ścieżce" autocomplete="off">
      <div class="file-filter" role="group" aria-label="Filtr komentarzy">
        <button type="button" :aria-pressed="threadFilter === 'all'" :class="{ active: threadFilter === 'all' }" @click="threadFilter = 'all'">Wszystkie</button>
        <button type="button" :aria-pressed="threadFilter === 'active'" :class="{ active: threadFilter === 'active' }" @click="threadFilter = 'active'">Aktywne</button>
        <button v-if="mineKnown" type="button" :aria-pressed="threadFilter === 'mine'" :class="{ active: threadFilter === 'mine' }" @click="threadFilter = 'mine'">Moje nierozwiązane</button>
      </div>
      <button type="button" class="thread-new" :disabled="!commentsEnabled" @click="startDraft('new')">Nowy komentarz do PR</button>
    </div>
    <p v-if="commentError" class="notice error" role="alert">{{ commentError }}</p>
    <p v-if="!commentsEnabled" class="muted comments-off">
      Pisanie komentarzy jest wyłączone w backendzie (<code>AzureDevOps:AllowComments</code>). Czytanie działa normalnie.
    </p>

    <CommentDraft v-if="draft?.target === 'new'" label="Treść komentarza" input-id="thread-draft" @cancel="draft = null" />

    <p v-if="threadsLoading" class="muted" role="status">Wczytywanie komentarzy…</p>
    <p v-else-if="threads.length === 0" class="muted">Brak komentarzy w tym PR.</p>
    <p v-else-if="visibleThreads.length === 0" class="muted">Nic nie pasuje do filtra.</p>
    <div v-for="group in threadGroups" :key="group.path || 'none'" class="thread-group">
      <h4 class="thread-group-head" :title="group.path">{{ group.label }}</h4>
      <article v-for="thread in group.threads" :key="thread.id" class="thread thread--full"
        :class="{ 'thread--resolved': isResolved(thread) }">
        <header class="thread-head">
          <button class="thread-location" type="button" :disabled="!thread.filePath" @click="openThread(thread)">{{ threadLocation(thread) }}</button>
          <span v-if="thread.status && threadStatusLabels[thread.status]" class="thread-status">{{ threadStatusLabels[thread.status] }}</span>
        </header>
        <div v-if="threadSnippet(thread)" class="thread-snippet">
          <pre><code><span v-for="row in threadSnippet(thread)!.lines" :key="row.number"
            class="snippet-line" :class="{ 'snippet-line--anchor': row.anchor }"><span class="snippet-number">{{ row.number }}</span>{{ row.text }}
</span></code></pre>
          <span class="snippet-side">{{ threadSnippet(thread)!.side }}</span>
        </div>
        <p v-else-if="thread.filePath && snippetsLoading" class="muted snippet-loading">Wczytywanie kodu…</p>
        <CommentThread :thread="thread" full />
      </article>
    </div>
  </section>
</template>

<style scoped>
.comments-off { margin: 8px 0 0; font-size: 12px; }
.comments-off code { font-family: Consolas, 'Courier New', monospace; }
.comments-view { display: flex; flex-direction: column; gap: 10px; padding: 14px 16px; overflow-y: auto; }
.comments-head { display: flex; align-items: baseline; gap: 10px; }
.comments-head h3 { margin: 0; }
.comments-counts { font-size: 12px; color: var(--text-muted); }
.comments-close { margin-left: auto; }
.comments-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.thread-search { flex: 1 1 220px; font: inherit; font-size: 13px; padding: 6px 8px; border: 1px solid var(--line); border-radius: 6px; background: var(--surface); color: inherit; }
/* Spacing carries the hierarchy: the gap between threads is clearly wider than any gap
   inside one, and the gap between files wider still. */
.thread-group { display: flex; flex-direction: column; gap: 20px; }
.thread-group + .thread-group { margin-top: 18px; }
/* The file a block of threads belongs to stays pinned while its threads scroll past. */
.thread-group-head { position: sticky; top: -14px; z-index: 3; margin: 8px -16px 0; padding: 8px 16px; border-bottom: 1px solid var(--border-control); background: var(--surface-muted); color: var(--text-strong); font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; font-size: 13px; font-weight: 700; overflow-wrap: anywhere; }
/* Each thread is a card lifted off the pane by a shadow; the accent edge is kept for
   active threads only, so teal stays a signal instead of decoration. */
.thread--full { border: 1px solid var(--border-control); border-left: 4px solid var(--accent); border-radius: 0 8px 8px 0; padding: 0 0 12px; background: var(--surface); box-shadow: var(--card-shadow); }
.thread--full.thread--resolved { border-left-color: var(--border-control); }
/* Three zones — header, conversation, actions — split by hairlines. */
.thread--full > .thread-head { padding: 8px 14px; border-bottom: 1px solid var(--line-mid); }
.thread--full > :deep(:not(.thread-head)) { margin-left: 14px; margin-right: 14px; }
.thread--full > .thread-snippet { margin-top: 10px; }
.thread--full > :deep(.thread-comment:first-of-type) { margin-top: 12px; }
.thread--full > :deep(.thread-actions), .thread--full > :deep(.comment-draft:last-child) { margin: 12px 0 -12px; padding: 8px 14px; border-top: 1px solid var(--line-mid); background: var(--surface-muted); border-radius: 0 0 8px 0; }
.thread--full :deep(.thread-actions button), .thread--full :deep(.comment-draft button) { font-size: 12px; padding: 3px 10px; }
.thread--full .thread-status { margin-left: auto; padding: 1px 8px; border-radius: 999px; background: var(--accent-soft); color: var(--accent); font-weight: 700; }
.thread--full.thread--resolved .thread-status { background: var(--surface-chip); color: var(--text-muted); }
.thread--full :deep(.thread-author) { font-size: 13px; }
/* A quote inside a comment must not wear the same teal bar as the thread itself. */
.thread--full :deep(.markdown-body blockquote) { border-left-color: var(--text-faint); background: var(--surface-muted); }
.thread-head { display: flex; align-items: baseline; gap: 8px; }
.thread--resolved { opacity: .65; }
.thread-snippet { position: relative; margin: 6px 0; border: 1px solid var(--line); border-radius: 6px; background: var(--surface); overflow: hidden; }
.thread-snippet pre { margin: 0; padding: 6px 8px; overflow-x: auto; }
.thread-snippet code { font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; font-size: 12px; line-height: 18px; }
.snippet-line { display: block; white-space: pre; }
.snippet-line--anchor { background: var(--accent-soft); font-weight: 600; }
.snippet-number { display: inline-block; width: 44px; margin-right: 8px; text-align: right; color: var(--text-muted); font-weight: 400; user-select: none; }
.snippet-side { position: absolute; top: 4px; right: 6px; font-size: 10px; color: var(--text-muted); background: var(--surface); padding: 0 4px; }
.snippet-loading { font-size: 12px; margin: 4px 0; }
</style>
