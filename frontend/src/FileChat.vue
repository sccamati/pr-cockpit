<script setup lang="ts">
// A drawer on the right rather than a block above the diff: stacked, it pushed the code off
// the screen exactly while you were reading it. It overlays instead of reflowing, so opening
// it never moves a line of code.
import { useCockpit } from './cockpit'
import { fileName } from './format'

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
