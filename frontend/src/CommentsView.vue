<script setup lang="ts">
// The third mode of the middle panel: every thread of the pull request, grouped by file in
// tree order, each with the lines it is about.
import CommentDraft from './CommentDraft.vue'
import CommentThread from './CommentThread.vue'
import { useCockpit } from './cockpit'
import { threadStatusLabels } from './format'
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
