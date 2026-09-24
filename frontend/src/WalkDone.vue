<script setup lang="ts">
// US-P6. One question, one decision, and the walkthrough has an end.
import { useCockpit } from './cockpit'
import { fileDirectory, fileName } from './format'

const { summary, checklist, walk, backToList } = useCockpit()
const { criticalProposal } = summary
const { debugAnswer, debugSaving, debugSaved, showDebugHint, saveDebugAnswer } = checklist
const { walkPaths, walkReadCount, walkSkippedPaths, walkOutsideCount, returnToSkipped, leaveWalkthrough } = walk
</script>

<template>
  <section class="walk-done" aria-label="Domknięcie przejścia">
    <h3>Przejście zamknięte</h3>
    <p class="walk-done-counts">
      Przeczytane: <strong>{{ walkReadCount }}</strong> z {{ walkPaths.length }} ·
      Pominięte: <strong>{{ walkSkippedPaths.length }}</strong> ·
      Poza ścieżką: <strong>{{ walkOutsideCount }}</strong>
    </p>
    <div v-if="walkSkippedPaths.length" class="walk-skipped">
      <h4>Pominięte pliki</h4>
      <ul class="walk-file-list">
        <li v-for="path in walkSkippedPaths" :key="path">
          <button type="button" class="critical-open" :title="path" @click="returnToSkipped(path)">{{ fileName(path) }}<small>{{ fileDirectory(path) }}</small></button>
        </li>
      </ul>
    </div>
    <div class="walk-debug">
      <label class="debug-label" for="walk-debug-answer">Gdzie zacząłbyś szukać, gdyby to nie zadziałało?</label>
      <textarea id="walk-debug-answer" v-model="debugAnswer" class="debug-answer" rows="3" :maxlength="2000"></textarea>
      <div class="debug-actions">
        <button type="button" :disabled="debugSaving" @click="saveDebugAnswer">{{ debugSaving ? 'Zapisywanie…' : 'Zapisz odpowiedź' }}</button>
        <span v-if="debugSaved" class="muted" role="status">Zapisano.</span>
        <button v-if="criticalProposal.length > 0" type="button" @click="showDebugHint = !showDebugHint">{{ showDebugHint ? 'Ukryj podpowiedź' : 'Pokaż, gdzie patrzeć' }}</button>
      </div>
      <ol v-if="showDebugHint" class="debug-hint" aria-label="Podpowiedź">
        <li v-for="(file, index) in criticalProposal" :key="file.path">
          <span class="debug-hint-order">{{ index + 1 }}</span>
          <span class="debug-hint-file" :title="file.path">{{ fileName(file.path) }}<small>{{ fileDirectory(file.path) }}</small></span>
          <span class="debug-hint-why">{{ file.why }}</span>
        </li>
      </ol>
    </div>
    <div class="walk-actions">
      <button type="button" class="walk-primary" @click="leaveWalkthrough">Zejdź do pozostałych plików</button>
      <button type="button" @click="backToList">Zakończ ten PR</button>
    </div>
  </section>
</template>
