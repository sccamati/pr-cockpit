<script setup lang="ts">
// US-P6. The walkthrough has an end, and the quiz is what makes it the end of the review: the
// pull request counts as done only once every question is passed.
import { useCockpit } from '@/cockpit'
import { fileDirectory, fileName } from '@/lib/format'
import QuizPanel from '@/features/quiz/QuizPanel.vue'

const { quiz, walk, backToList } = useCockpit()
const { quizPassed } = quiz
const { walkPaths, walkReadCount, walkSkippedPaths, walkOutsideCount, returnToWalkFile, leaveWalkthrough } = walk
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
          <button type="button" class="critical-open" :title="path" @click="returnToWalkFile(path)">{{ fileName(path) }}<small>{{ fileDirectory(path) }}</small></button>
        </li>
      </ul>
    </div>
    <section class="walk-quiz" aria-label="Quiz">
      <h4>Quiz</h4>
      <QuizPanel />
    </section>
    <div class="walk-actions">
      <button type="button" class="walk-primary" @click="leaveWalkthrough">Zejdź do pozostałych plików</button>
      <button type="button" class="walk-finish" @click="backToList">{{ quizPassed ? 'Zakończ ten PR ✓' : 'Wróć do listy (PR niezakończony)' }}</button>
    </div>
  </section>
</template>

<style scoped>
.walk-done { max-width: 900px; margin: 0 auto; padding: 18px 20px 24px; border: 1px solid var(--line); border-radius: 10px; background: var(--surface); }
.walk-done h3 { margin: 0 0 8px; }
.walk-done-counts { margin: 0; font-size: 14px; }
.walk-skipped { margin-top: 14px; }
.walk-quiz { margin-top: 16px; }
.walk-quiz h4 { margin: 0; }
</style>
