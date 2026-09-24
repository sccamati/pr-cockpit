import { inject, type InjectionKey, type Ref } from 'vue'
import type { PullRequestDetails } from './api'
import type { useChecklist } from './useChecklist'
import type { useComments } from './useComments'
import type { useFileAi } from './useFileAi'
import type { useReviewProgress } from './useReviewProgress'
import type { useSummary } from './useSummary'
import type { useWalkthrough } from './useWalkthrough'

/** Which pull request a composable works on. Every write captures these before it awaits. */
export interface PrScope {
  details: Ref<PullRequestDetails | null>
  projectId: Ref<string>
  repositoryId: Ref<string>
}

/**
 * Everything App.vue owns, handed to its child components. App creates each feature once;
 * the components are pieces of App's screen, not reusable widgets, so they take the
 * features from here rather than through a prop per field.
 */
export interface Cockpit extends PrScope {
  selectedFilePath: Ref<string>
  summary: ReturnType<typeof useSummary>
  checklist: ReturnType<typeof useChecklist>
  review: ReturnType<typeof useReviewProgress>
  walk: ReturnType<typeof useWalkthrough>
  comments: ReturnType<typeof useComments>
  fileAi: ReturnType<typeof useFileAi>
  openFile: (path: string, sinceIteration?: number | null) => Promise<void>
  openCriticalFile: (path: string) => void
  backToList: () => void
}

export const cockpitKey: InjectionKey<Cockpit> = Symbol('cockpit')

export function useCockpit(): Cockpit {
  const cockpit = inject(cockpitKey)
  if (!cockpit) throw new Error('PR Cockpit components render only inside App.vue.')
  return cockpit
}
