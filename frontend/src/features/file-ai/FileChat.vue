<script setup lang="ts">
// A drawer on the right rather than a block above the diff: stacked, it pushed the code off
// the screen exactly while you were reading it. It overlays instead of reflowing, so opening
// it never moves a line of code.
import { useCockpit } from '@/cockpit'
import { fileName } from '@/lib/format'

const { selectedFilePath, fileAi } = useCockpit()
const { questionOpen, questionTurns, questionLoading, questionError, questionSelection, questionDraft, questionBox, askQuestion } = fileAi
</script>

<template>
  <aside class="file-chat" aria-label="Pytania o plik">
    <div class="file-chat-head">
      <strong>Pytania o plik</strong>
      <span class="muted">{{ fileName(selectedFilePath) }}</span>
      <button type="button" class="inline-close" title="Zamknij (a albo Esc)"
        @click="questionOpen = false">Zamknij</button>
    </div>
    <div class="file-chat-thread">
      <p v-if="!questionTurns.length" class="muted">Zapytaj o ten plik albo zaznacz kawałek kodu i wybierz „Zapytaj AI o zaznaczenie” z menu prawego przycisku.</p>
      <div v-for="(turn, index) in questionTurns" :key="index" class="file-chat-turn">
        <p class="file-chat-question">{{ turn.question }}</p>
        <pre v-if="turn.selection" class="file-chat-selection"><code>{{ turn.selection }}</code></pre>
        <p v-for="(sentence, line) in turn.sentences" :key="line">{{ sentence }}</p>
      </div>
      <p v-if="questionLoading" class="muted" role="status">Pytam…</p>
    </div>
    <div class="file-chat-draft">
      <p v-if="questionError" class="notice error" role="alert">{{ questionError }}</p>
      <p v-if="questionSelection" class="file-chat-attached muted">
        Dołączę zaznaczony fragment ({{ questionSelection.length }} znaków).
        <button type="button" class="explanation-dismiss" @click="questionSelection = ''">Odłącz</button>
      </p>
      <!-- The box belongs to useFileAi, which focuses it whenever the drawer opens. -->
      <textarea :ref="el => { questionBox = el as HTMLTextAreaElement | null }" v-model="questionDraft" class="debug-answer" rows="3" :maxlength="1000"
        placeholder="Po co jest ten kawałek kodu?" aria-label="Pytanie o ten plik"
        @keydown.ctrl.enter="askQuestion"></textarea>
      <div class="debug-actions">
        <button type="button" class="comment-send" :disabled="questionLoading || !questionDraft.trim()"
          @click="askQuestion">{{ questionLoading ? 'Pytam…' : 'Zapytaj (uruchomi AI)' }}</button>
        <span class="muted">Ctrl+Enter wysyła. Rozmowa zapisuje się lokalnie.</span>
      </div>
    </div>
  </aside>
</template>

<style scoped>
/* A drawer, not a block in the flow: it overlays the diff, so opening it never reflows the
   code you are reading. Fixed to the viewport, because the walkthrough and the tree view
   put the diff panel in different places. */
.file-chat { position: fixed; top: 0; right: 0; bottom: 0; z-index: 30; width: min(420px, 92vw);
  display: flex; flex-direction: column; background: var(--surface); border-left: 3px solid var(--accent);
  box-shadow: -8px 0 24px rgba(0, 0, 0, 0.28); animation: file-chat-in 120ms ease-out; }
@keyframes file-chat-in { from { transform: translateX(100%); } to { transform: translateX(0); } }
.file-chat-head { display: flex; align-items: baseline; gap: 8px; padding: 10px 12px; border-bottom: 1px solid var(--line-mid); }
.file-chat-head span { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 12px; }
/* The thread takes whatever height is left; the question box stays put at the bottom. */
.file-chat-thread { flex: 1; overflow-y: auto; padding: 10px 12px; }
.file-chat-draft { padding: 10px 12px; border-top: 1px solid var(--line-mid); }
.file-chat-draft .debug-answer { width: 100%; box-sizing: border-box; }
/* Each turn is a block you read as one thing, so it gets air and a rule under it rather
   than being one more paragraph in a wall of them. */
.file-chat-turn { margin: 0 0 16px; padding-bottom: 16px; border-bottom: 1px solid var(--line-mid); }
.file-chat-turn:last-child { margin-bottom: 0; padding-bottom: 0; border-bottom: 0; }
.file-chat-turn p { margin: 0 0 9px; font-size: 13px; line-height: 1.55; }
.file-chat-turn p:last-child { margin-bottom: 0; }
/* The question reads as the heading of its answer, not as its first sentence. */
.file-chat-question { font-weight: 700; color: var(--text-strong); border-left: 3px solid var(--accent);
  padding-left: 9px; margin-bottom: 11px !important; }
/* --surface is the drawer's own background, so the snippet needs the muted one to show at all. */
.file-chat-selection { margin: 0 0 11px; padding: 8px 10px; background: var(--surface-muted);
  border: 1px solid var(--line-mid); border-radius: 5px; font-size: 12px; line-height: 1.5;
  max-height: 180px; overflow: auto; }
.file-chat-attached { font-size: 12px; margin: 0 0 6px; }

@media (prefers-reduced-motion: reduce) {
  .file-chat { animation: none; }
}
</style>
