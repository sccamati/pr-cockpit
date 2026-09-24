import { nextTick, ref, type Ref } from 'vue'
import { api, type FileExplanation, type FileQuestionTurn } from '@/api'
import type { PrScope } from '@/cockpit'
import { message } from '@/lib/format'

/**
 * The two per-file AI paths: "Wyjaśnij ten plik" and the conversation about the open file.
 * Both ride the diff request id: switching files or leaving the pull request invalidates an
 * answer still in flight, exactly like a late diff response.
 */
export function useFileAi({ details, projectId, repositoryId, selectedFilePath, diffRequest }: PrScope & {
  selectedFilePath: Ref<string>
  // The current value of App's diff request counter.
  diffRequest: () => number
}) {
  const explanation = ref<FileExplanation | null>(null)
  const explanationLoading = ref(false)
  const explanationError = ref('')
  const explanationOpen = ref(true)
  // Every explanation asked for, kept for as long as the pull request is open, so coming
  // back to a file shows it again without paying for it twice.
  const explanationCache = ref<Record<string, FileExplanation>>({})
  // The conversation about the open file. Kept in the backend, so this is only what is on
  // screen; the draft and the captured snippet live here until they are sent.
  const questionTurns = ref<FileQuestionTurn[]>([])
  const questionDraft = ref('')
  const questionSelection = ref('')
  const questionLoading = ref(false)
  const questionError = ref('')
  const questionOpen = ref(false)
  const questionBox = ref<HTMLTextAreaElement | null>(null)

  function resetExplanation() {
    explanation.value = null
    explanationLoading.value = false
    explanationError.value = ''
    explanationOpen.value = true
  }

  function resetQuestions() {
    questionTurns.value = []
    questionDraft.value = ''
    questionSelection.value = ''
    questionError.value = ''
    questionLoading.value = false
    questionOpen.value = false
  }

  // Leaving the pull request: the cache belongs to it.
  function resetFileAi() {
    resetExplanation()
    resetQuestions()
    explanationCache.value = {}
  }

  // A file was opened under diff request `current`.
  function fileOpened(pullRequestId: number, path: string, current: number) {
    resetExplanation()
    // Already asked for once, so it comes back without paying for it again.
    const cached = explanationCache.value[path]
    if (cached) explanation.value = cached
    resetQuestions()
    void loadQuestions(pullRequestId, path, current)
  }

  async function explainFile() {
    if (!details.value || !selectedFilePath.value || explanationLoading.value) return
    const current = diffRequest()
    const pullRequestId = details.value.id
    const path = selectedFilePath.value
    explanationError.value = ''
    explanationLoading.value = true
    try {
      const result = await api.explainFile(projectId.value, repositoryId.value, pullRequestId, path)
      if (current !== diffRequest()) return
      explanation.value = result
      explanationCache.value = { ...explanationCache.value, [path]: result }
    } catch (cause) {
      if (current === diffRequest()) explanationError.value = message(cause)
    } finally {
      if (current === diffRequest()) explanationLoading.value = false
    }
  }

  async function loadQuestions(pullRequestId: number, path: string, current: number) {
    try {
      const turns = await api.fileQuestions(projectId.value, repositoryId.value, pullRequestId, path)
      if (current !== diffRequest()) return
      // The thread loads, the drawer does not open itself: it overlays the code, and a file
      // you asked about yesterday must not cover the diff when you come back to it. The count
      // on the toolbar button is what says there is something to read.
      questionTurns.value = turns
    } catch {
      // A conversation that cannot be read is not worth an alarm over the file: the box still
      // works, and the first answer will say so if the backend is really down.
    }
  }

  async function askQuestion() {
    if (!details.value || !selectedFilePath.value || questionLoading.value) return
    const asked = questionDraft.value.trim()
    if (!asked) return
    const current = diffRequest()
    const pullRequestId = details.value.id
    const path = selectedFilePath.value
    const selection = questionSelection.value || null
    questionError.value = ''
    questionLoading.value = true
    // The question goes into the thread and the box empties at once, the way a chat behaves;
    // the answer fills in when it arrives. ponytail: matched by index, because turns are only
    // ever appended and a file switch is already caught by the diff request id below.
    const index = questionTurns.value.length
    questionTurns.value = [...questionTurns.value, {
      schemaVersion: 0, path, question: asked, selection, sentences: [],
      blobId: null, headCommitSha: null, askedAt: new Date().toISOString(),
    }]
    questionDraft.value = ''
    questionSelection.value = ''
    try {
      const turn = await api.askAboutFile(projectId.value, repositoryId.value, pullRequestId, path, asked, selection)
      if (current !== diffRequest()) return
      questionTurns.value = questionTurns.value.map((existing, at) => at === index ? turn : existing)
    } catch (cause) {
      if (current !== diffRequest()) return
      // Nothing was stored, so the question leaves the thread and goes back into the box
      // with its snippet — one error line beats a turn that looks answered and is not.
      questionTurns.value = questionTurns.value.filter((_, at) => at !== index)
      questionDraft.value = asked
      questionSelection.value = selection ?? ''
      questionError.value = message(cause)
    } finally {
      if (current === diffRequest()) questionLoading.value = false
    }
  }

  // Opening the drawer is also how the right-click action lands: the snippet is already
  // captured, the cursor goes where the question gets typed. A selection always opens —
  // you just asked for it — while the button and the key toggle, because that is the way
  // back out of a drawer that covers the code.
  async function openQuestions(selection = '') {
    if (!selectedFilePath.value) return
    if (selection) questionSelection.value = selection
    else if (questionOpen.value) { questionOpen.value = false; return }
    questionOpen.value = true
    await nextTick()
    questionBox.value?.focus()
  }

  return {
    explanation, explanationLoading, explanationError, explanationOpen,
    questionTurns, questionDraft, questionSelection, questionLoading, questionError, questionOpen, questionBox,
    resetExplanation, resetFileAi, fileOpened, explainFile, askQuestion, openQuestions,
  }
}
