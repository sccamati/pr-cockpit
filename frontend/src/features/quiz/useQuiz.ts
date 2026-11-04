import { computed, ref } from 'vue'
import { api, type Quiz, type QuizKind } from '@/api'
import type { PrScope } from '@/cockpit'
import { message, same } from '@/lib/format'

export const quizKindLabels: Record<QuizKind, string> = {
  intent: 'Cel',
  mechanism: 'Mechanizm',
  domain: 'Biznes',
  runtime: 'Wdrożenie',
  design: 'Wzorzec',
  impact: 'Wpływ',
  edge: 'Przypadek brzegowy',
  debug: 'Diagnoza',
  free: 'Inne',
}

// A compose still running, per pull request. Module-level on purpose: the request outlives
// leaving the pull request (the backend finishes and saves), so coming back while the model
// still works joins that request instead of paying for a second run.
const composing = new Map<string, Promise<Quiz>>()

/**
 * The quiz that closes a review. Composed in the background when a walkthrough starts — a
 * click, so opening a pull request still costs nothing (US-P1) — and graded one answer at
 * a time. The pull request counts as done only when every question is passed for its head.
 */
export function useQuiz({ details, projectId, repositoryId }: PrScope) {
  const quiz = ref<Quiz | null>(null)
  const quizLoading = ref(false)
  const quizComposing = ref(false)
  const quizError = ref('')
  const answering = ref<string | null>(null)
  const answerError = ref<{ id: string; text: string } | null>(null)
  // What the reviewer typed per question, kept after a wrong answer so it can be corrected.
  const drafts = ref<Record<string, string>>({})
  let quizRequestId = 0

  const quizStale = computed(() => {
    const saved = quiz.value?.headCommitSha
    const head = details.value?.headCommitSha
    return !!saved && !!head && !same(saved, head)
  })
  const quizPassed = computed(() => !!quiz.value?.passed && !quizStale.value)

  const key = (project: string, repository: string, id: number) => `${project}/${repository}/${id}`

  function resetQuiz() {
    ++quizRequestId
    quiz.value = null
    quizLoading.value = false
    quizComposing.value = false
    quizError.value = ''
    answering.value = null
    answerError.value = null
    drafts.value = {}
  }

  async function loadQuiz(project: string, repository: string, id: number) {
    const current = ++quizRequestId
    const pending = composing.get(key(project, repository, id))
    if (pending) return awaitCompose(pending, current)
    quizLoading.value = true
    quizError.value = ''
    try {
      const result = await api.savedQuiz(project, repository, id)
      if (current === quizRequestId) quiz.value = result
    } catch (cause) {
      if (current === quizRequestId) quizError.value = message(cause)
    } finally {
      if (current === quizRequestId) quizLoading.value = false
    }
  }

  async function awaitCompose(pending: Promise<Quiz>, current: number) {
    quizComposing.value = true
    quizError.value = ''
    try {
      const result = await pending
      if (current === quizRequestId) quiz.value = result
    } catch (cause) {
      if (current === quizRequestId) quizError.value = message(cause)
    } finally {
      if (current === quizRequestId) quizComposing.value = false
    }
  }

  // A quiz for this head is already here, so a walkthrough started again costs nothing.
  // The backend applies the same rule; this saves the round trip.
  function composeQuiz(regenerate = false) {
    if (!details.value) return Promise.resolve()
    if (!regenerate && quiz.value && !quizStale.value) return Promise.resolve()
    const project = projectId.value
    const repository = repositoryId.value
    const id = details.value.id
    const k = key(project, repository, id)
    let pending = composing.get(k)
    if (!pending) {
      pending = api.composeQuiz(project, repository, id, regenerate).finally(() => composing.delete(k))
      composing.set(k, pending)
    }
    return awaitCompose(pending, quizRequestId)
  }

  // One answer at a time: each is a model run, and the quiz is one row the backend rewrites.
  async function answerQuestion(questionId: string) {
    const answer = (drafts.value[questionId] ?? '').trim()
    if (!details.value || !answer || answering.value) return
    const current = quizRequestId
    const project = projectId.value
    const repository = repositoryId.value
    const id = details.value.id
    answering.value = questionId
    answerError.value = null
    try {
      const result = await api.answerQuiz(project, repository, id, questionId, answer)
      if (current === quizRequestId) quiz.value = result
    } catch (cause) {
      if (current === quizRequestId) answerError.value = { id: questionId, text: message(cause) }
    } finally {
      if (current === quizRequestId) answering.value = null
    }
  }

  return {
    quiz, quizLoading, quizComposing, quizError, answering, answerError, drafts, quizStale, quizPassed,
    resetQuiz, loadQuiz, composeQuiz, answerQuestion,
  }
}
