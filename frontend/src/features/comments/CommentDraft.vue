<script setup lang="ts">
// A new comment — on the whole pull request or on one line. Two steps, always: Enter inserts
// a newline and only the button sends, because a comment cannot be taken back.
import { useCockpit } from '@/cockpit'

defineProps<{
  label: string
  // With an id the label is visible above the box; without one it is the box's aria-label.
  inputId?: string
}>()
defineEmits<{ cancel: [] }>()

const { draft, commentSaving, sendDraft } = useCockpit().comments
</script>

<template>
  <div v-if="draft" class="comment-draft">
    <label v-if="inputId" class="debug-label" :for="inputId">{{ label }}</label>
    <textarea :id="inputId" v-model="draft.text" class="debug-answer" rows="3" :maxlength="10000"
      :aria-label="inputId ? undefined : label"></textarea>
    <div class="debug-actions">
      <button type="button" class="comment-send" :disabled="commentSaving || !draft.text.trim()" @click="sendDraft()">{{ commentSaving ? 'Wysyłanie…' : 'Wyślij do Azure DevOps' }}</button>
      <button type="button" :disabled="commentSaving" @click="$emit('cancel')">Anuluj</button>
      <span class="muted comment-warning">Wysłanego komentarza nie da się cofnąć.</span>
    </div>
  </div>
</template>

<style scoped>
.comment-warning { font-size: 12px; }
</style>
