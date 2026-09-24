<script setup lang="ts">
// The body of one conversation — its comments, your own edit/delete, the "code moved" note
// and the reply box — wherever a thread is shown: the comments view, the block docked above
// a diff without an editor, and a Monaco zone between the lines. The heading stays with the
// caller, because each of those places heads a thread differently.
import { useCockpit } from './cockpit'
import { renderComment } from './description'
import { formatDate } from './format'
import { isResolved, type ReadableThread } from './useComments'

defineProps<{
  thread: ReadableThread
  // The comments view: the reply box names its thread, and "won't fix" is offered.
  full?: boolean
  // A zone already names the first comment's author in its foldable header.
  hideFirstAuthor?: boolean
}>()

const {
  draft, editing, deleting, commentSaving, lastIteration, movedSinceComment,
  startDraft, sendDraft, setThreadStatus, startEdit, sendEdit, confirmDelete, showChangesSinceComment,
} = useCockpit().comments
</script>

<template>
  <div v-for="(comment, index) in thread.comments" :key="comment.id" class="thread-comment">
    <template v-if="!hideFirstAuthor || index > 0">
      <span class="thread-author">{{ comment.author ?? 'Nieznany autor' }}</span><time v-if="comment.publishedAt" class="thread-date" :datetime="comment.publishedAt">{{ formatDate(comment.publishedAt) }}</time>
    </template>
    <div v-if="editing && editing.commentId === comment.id && editing.threadId === thread.id" class="comment-draft">
      <textarea v-model="editing.text" class="debug-answer" rows="3" :maxlength="10000" aria-label="Edycja komentarza"></textarea>
      <div class="debug-actions">
        <button type="button" class="comment-send" :disabled="commentSaving || !editing.text.trim()" @click="sendEdit">{{ commentSaving ? 'Zapisywanie…' : 'Zapisz zmianę' }}</button>
        <button type="button" :disabled="commentSaving" @click="editing = null">Anuluj</button>
      </div>
    </div>
    <template v-else>
      <div class="thread-content markdown-body" v-html="renderComment(comment.content)" />
      <div v-if="comment.isMine" class="comment-own-actions">
        <template v-if="deleting === comment.id">
          <span class="muted">Usunąć na stałe?</span>
          <button type="button" class="comment-delete" :disabled="commentSaving" @click="confirmDelete(thread.id, comment.id)">Tak, usuń</button>
          <button type="button" :disabled="commentSaving" @click="deleting = null">Nie</button>
        </template>
        <template v-else>
          <button type="button" @click="startEdit(thread.id, comment.id, comment.content)">Edytuj</button>
          <button type="button" class="comment-remove" @click="deleting = comment.id; editing = null">Usuń</button>
        </template>
      </div>
    </template>
  </div>
  <p v-if="movedSinceComment(thread)" class="thread-moved">
    Kod zmienił się po tym komentarzu (iteracja {{ thread.iterationId }} → {{ lastIteration }}).
    <button v-if="thread.filePath" type="button" class="thread-since" @click="showChangesSinceComment(thread)">Zobacz, co się zmieniło</button>
  </p>
  <div v-if="draft?.target === String(thread.id)" class="comment-draft">
    <textarea v-model="draft.text" class="debug-answer" rows="2" :maxlength="10000"
      :aria-label="full ? `Odpowiedź w wątku ${thread.id}` : 'Odpowiedź w wątku'"></textarea>
    <div class="debug-actions">
      <button type="button" class="comment-send" :disabled="commentSaving || !draft.text.trim()" @click="sendDraft()">{{ commentSaving ? 'Wysyłanie…' : 'Wyślij odpowiedź' }}</button>
      <button v-if="!isResolved(thread)" type="button" :disabled="commentSaving || !draft.text.trim()" @click="sendDraft(true)">Odpowiedz i rozwiąż</button>
      <button type="button" :disabled="commentSaving" @click="draft = null">Anuluj</button>
    </div>
  </div>
  <div v-else class="thread-actions">
    <button type="button" @click="startDraft(String(thread.id))">Odpowiedz</button>
    <button v-if="!isResolved(thread)" type="button" class="thread-resolve" :disabled="commentSaving" @click="setThreadStatus(thread.id, 'fixed')">Rozwiąż</button>
    <button v-else type="button" :disabled="commentSaving" @click="setThreadStatus(thread.id, 'active')">Otwórz ponownie</button>
    <button v-if="full && !isResolved(thread)" type="button" :disabled="commentSaving" @click="setThreadStatus(thread.id, 'wontFix')">Nie naprawimy</button>
  </div>
</template>
