<script setup lang="ts">
// The quiz that closes a review. One panel, two homes: the walkthrough's closing screen and
// the rail. A file under a question leads back into the code — into the walkthrough when the
// file is on its path, into the tree otherwise.
import { useCockpit } from '@/cockpit'
import { fileDirectory, fileName } from '@/lib/format'
import { quizKindLabels } from './useQuiz'

const { quiz: quizState, walk, openCriticalFile } = useCockpit()
const {
  quiz, quizLoading, quizComposing, quizError, answering, answerError, drafts, quizStale, quizPassed,
  composeQuiz, answerQuestion,
} = quizState
const { view, walkPaths, returnToWalkFile, leaveWalkthrough } = walk

function openQuestionFile(path: string) {
  if (view.value === 'done' && walkPaths.value.includes(path)) return returnToWalkFile(path)
  if (view.value !== 'tree') leaveWalkthrough()
  openCriticalFile(path)
}
</script>

<template>
  <div class="quiz">
    <p v-if="quizLoading" class="muted" role="status">Wczytywanie quizu…</p>
    <p v-else-if="quizComposing" class="notice" role="status">Quiz układa się w tle. Możesz czytać dalej — pojawi się tutaj.</p>
    <p v-if="quizError" class="notice error" role="alert">{{ quizError }}</p>

    <template v-if="!quiz && !quizLoading && !quizComposing">
      <p class="muted">Kilka pytań o to, co zmienia ten PR i jak to zrobiono. PR jest zakończony dopiero po zaliczeniu wszystkich.</p>
      <button type="button" class="quiz-compose" @click="composeQuiz()">Ułóż quiz (uruchomi AI)</button>
    </template>

    <template v-else-if="quiz">
      <p v-if="quizStale" class="notice quiz-stale" role="status">PR zmienił się od ułożenia tego quizu, więc nie da się go już zaliczyć.
        <button type="button" class="retry" :disabled="quizComposing" @click="composeQuiz(true)">Ułóż nowy (uruchomi AI)</button>
      </p>
      <p v-else-if="quizPassed" class="quiz-status quiz-status--passed" role="status">PR zakończony ✓ Wszystkie pytania zaliczone.</p>
      <p v-else class="quiz-status" role="status">Zaliczone {{ quiz.correct }} z {{ quiz.total }}. PR będzie zakończony po zaliczeniu wszystkich.</p>

      <ol class="quiz-questions">
        <li v-for="(question, index) in quiz.questions" :key="question.id" class="quiz-question"
          :class="{ 'quiz-question--passed': question.passed }">
          <!-- A passed question folds away, so a long quiz shows what is still open. -->
          <details v-if="question.passed">
            <summary><span class="quiz-mark">✓</span> {{ index + 1 }}/{{ quiz.total }} · {{ quizKindLabels[question.kind] }} · {{ question.question }}</summary>
            <p v-if="question.lastAttempt" class="quiz-answer"><strong>Twoja odpowiedź:</strong> {{ question.lastAttempt.answer }}</p>
            <p v-for="(sentence, line) in question.lastAttempt?.feedback ?? []" :key="line" class="quiz-feedback">{{ sentence }}</p>
            <div v-if="question.expectedPoints" class="quiz-expected">
              <strong>Wzorcowo:</strong>
              <ul><li v-for="(point, line) in question.expectedPoints" :key="line">{{ point }}</li></ul>
            </div>
          </details>
          <template v-else>
            <p class="quiz-kind">Pytanie {{ index + 1 }} z {{ quiz.total }} · {{ quizKindLabels[question.kind] }}</p>
            <p class="quiz-text">{{ question.question }}</p>
            <ul class="walk-file-list quiz-files" aria-label="Gdzie szukać odpowiedzi">
              <li v-for="path in question.files" :key="path">
                <button type="button" class="critical-open" :title="path" @click="openQuestionFile(path)">{{ fileName(path) }}<small>{{ fileDirectory(path) }}</small></button>
              </li>
            </ul>
            <div v-if="question.lastAttempt && !question.lastAttempt.correct" class="notice quiz-wrong" role="status">
              <strong>✗ Jeszcze nie.</strong>
              <p v-for="(sentence, line) in question.lastAttempt.feedback" :key="line">{{ sentence }}</p>
            </div>
            <label class="debug-label" :for="`quiz-${question.id}`">Twoja odpowiedź</label>
            <textarea :id="`quiz-${question.id}`" v-model="drafts[question.id]" class="debug-answer" rows="3"
              :maxlength="2000" :disabled="answering === question.id || quizStale"
              placeholder="Jedno do trzech zdań, własnymi słowami."></textarea>
            <div class="debug-actions">
              <button type="button" class="quiz-check" :disabled="answering !== null || quizStale || !drafts[question.id]?.trim()"
                @click="answerQuestion(question.id)">{{ answering === question.id ? 'Sprawdzam…' : 'Sprawdź (uruchomi AI)' }}</button>
              <span v-if="question.attemptCount > 0" class="muted">Próby: {{ question.attemptCount }}</span>
            </div>
            <p v-if="answerError?.id === question.id" class="notice error" role="alert">{{ answerError.text }}</p>
          </template>
        </li>
      </ol>

      <!-- No skip, by decision: the way out of a bad quiz is a new one, never a pass for free. -->
      <button v-if="!quizStale" type="button" class="quiz-regenerate" :disabled="quizComposing || answering !== null"
        @click="composeQuiz(true)">Ułóż quiz od nowa (uruchomi AI)</button>
    </template>
  </div>
</template>

<style scoped>
.quiz { display: flex; flex-direction: column; gap: 10px; margin-top: 8px; }
.quiz .notice, .quiz .muted { margin: 0; }
.quiz-compose, .quiz-check { align-self: flex-start; font-weight: 600; }
.quiz-status { margin: 0; font-size: 13px; font-weight: 600; color: var(--text-strong); }
.quiz-status--passed { padding: 8px 10px; border: 1px solid var(--ok-line); border-radius: 6px; background: var(--ok-bg); color: var(--ok); }
.quiz-stale { border-color: var(--warn-line); background: var(--warn-bg); color: var(--warn-text); }
.quiz-questions { display: flex; flex-direction: column; gap: 12px; margin: 0; padding: 0; list-style: none; }
.quiz-question { padding: 10px 12px; border: 1px solid var(--line-mid); border-radius: 8px; background: var(--surface); }
.quiz-question--passed { padding: 6px 12px; border-color: var(--ok-line); background: var(--ok-bg); }
.quiz-question--passed summary { cursor: pointer; font-size: 13px; color: var(--text-soft); }
.quiz-mark { color: var(--ok); font-weight: 700; }
.quiz-kind { margin: 0; font-size: 12px; font-weight: 700; color: var(--accent); }
.quiz-text { margin: 4px 0 0; font-size: 14px; font-weight: 600; color: var(--text); }
.quiz-files { margin: 8px 0; }
.quiz-wrong { margin: 0 0 8px; border-color: var(--error-line); background: var(--error-bg); color: var(--error-text); font-size: 13px; }
.quiz-wrong p { margin: 4px 0 0; }
.quiz-answer, .quiz-feedback { margin: 8px 0 0; font-size: 13px; }
.quiz-expected { margin-top: 8px; font-size: 13px; }
.quiz-expected ul { margin: 4px 0 0; padding-left: 20px; }
.quiz-regenerate { align-self: flex-start; font-size: 12px; }
</style>
