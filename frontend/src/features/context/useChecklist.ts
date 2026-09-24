import { computed, ref } from 'vue'
import { api, type ChecklistItem, type ChecklistState } from '@/api'
import type { PrScope } from '@/cockpit'
import { message } from '@/lib/format'

export const checklistItems: { key: ChecklistItem; label: string }[] = [
  // Kolejnosc jak w PRODUCT.md par. 6; klucze ida do API i bazy, wiec zmieniaja sie tylko napisy.
  { key: 'aiReview', label: 'Review AI' },
  { key: 'quality', label: 'Jakość' },
  { key: 'understand', label: 'Rozumiem zmianę' },
  { key: 'architecture', label: 'Architektura' },
  { key: 'debug', label: 'Diagnoza' },
  { key: 'ready', label: 'Gotowe' },
]

/** The manual six-item checklist and the Debug Check answer stored in the same row. */
export function useChecklist({ details, projectId, repositoryId }: PrScope) {
  const checklist = ref<ChecklistState | null>(null)
  const checklistLoading = ref(false)
  const checklistSaving = ref<ChecklistItem | null>(null)
  const checklistError = ref('')
  // PRODUCT.md §9: the point is a few seconds of active thinking, not a grade. Nothing here
  // checks the answer, and leaving it empty costs nothing.
  // ponytail: one fixed question instead of the 1-3 generated failure scenarios §9 describes
  // — it forces the same few seconds without a third AI path. Ceiling: if the fixed question
  // turns out to be too weak, scenarios join the Summary schema.
  const debugAnswer = ref('')
  const debugSaving = ref(false)
  const debugSaved = ref(false)
  const showDebugHint = ref(false)
  let checklistRequestId = 0

  const checklistCompleted = computed(() => checklist.value
    ? checklistItems.filter(item => checklist.value![item.key]).length
    : 0)
  const remainingChecklist = computed(() => checklist.value
    ? checklistItems.filter(item => !checklist.value![item.key]).map(item => item.label)
    : [])

  function resetChecklist() {
    ++checklistRequestId
    checklist.value = null
    checklistLoading.value = false
    checklistSaving.value = null
    checklistError.value = ''
    debugAnswer.value = ''
    debugSaving.value = false
    debugSaved.value = false
    showDebugHint.value = false
  }

  async function loadChecklist(project: string, repository: string, id: number) {
    const current = ++checklistRequestId
    checklistLoading.value = true
    checklistError.value = ''
    try {
      const result = await api.checklist(project, repository, id)
      if (current === checklistRequestId) {
        checklist.value = result
        debugAnswer.value = result.debugNote ?? ''
      }
    } catch (cause) {
      if (current === checklistRequestId) checklistError.value = message(cause)
    } finally {
      if (current === checklistRequestId) checklistLoading.value = false
    }
  }

  // No optimistic write: this is the reviewer's own sentence, so what the field shows after
  // a save is what the server stored, not what we hoped it stored.
  async function saveDebugAnswer() {
    if (!details.value || debugSaving.value) return
    const current = checklistRequestId
    const project = projectId.value
    const repository = repositoryId.value
    const id = details.value.id
    debugSaving.value = true
    debugSaved.value = false
    checklistError.value = ''
    try {
      const result = await api.setDebugNote(project, repository, id, debugAnswer.value)
      if (current === checklistRequestId) {
        checklist.value = result
        debugAnswer.value = result.debugNote ?? ''
        debugSaved.value = true
      }
    } catch (cause) {
      if (current === checklistRequestId) checklistError.value = message(cause)
    } finally {
      if (current === checklistRequestId) debugSaving.value = false
    }
  }

  async function setChecklistItem(item: ChecklistItem, completed: boolean) {
    if (!details.value || !checklist.value || checklistSaving.value) return
    const current = checklistRequestId
    const project = projectId.value
    const repository = repositoryId.value
    const id = details.value.id
    const previous = checklist.value
    checklist.value = { ...previous, [item]: completed }
    checklistSaving.value = item
    checklistError.value = ''
    try {
      const result = await api.setChecklistItem(project, repository, id, item, completed)
      if (current === checklistRequestId) checklist.value = result
    } catch (cause) {
      if (current === checklistRequestId) {
        checklist.value = previous
        checklistError.value = message(cause)
      }
    } finally {
      if (current === checklistRequestId) checklistSaving.value = null
    }
  }

  return {
    checklist, checklistLoading, checklistSaving, checklistError,
    debugAnswer, debugSaving, debugSaved, showDebugHint, checklistCompleted, remainingChecklist,
    resetChecklist, loadChecklist, saveDebugAnswer, setChecklistItem,
  }
}
