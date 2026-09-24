import { inject, type InjectionKey, type Ref } from 'vue'
import type { PullRequestDetails } from './api'
import type { useChecklist } from '@/features/context/useChecklist'
import type { useComments } from '@/features/comments/useComments'
import type { useDiff } from '@/features/diff/useDiff'
import type { useFileAi } from '@/features/file-ai/useFileAi'
import type { useFileList } from '@/features/file-tree/useFileList'
import type { usePullRequests } from '@/features/pull-requests/usePullRequests'
import type { useReviewProgress } from '@/features/walkthrough/useReviewProgress'
import type { useSummary } from '@/features/context/useSummary'
import type { useWalkthrough } from '@/features/walkthrough/useWalkthrough'

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
  pullRequests: ReturnType<typeof usePullRequests>
  diff: ReturnType<typeof useDiff>
  fileList: ReturnType<typeof useFileList>
  summary: ReturnType<typeof useSummary>
  checklist: ReturnType<typeof useChecklist>
  review: ReturnType<typeof useReviewProgress>
  walk: ReturnType<typeof useWalkthrough>
  comments: ReturnType<typeof useComments>
  fileAi: ReturnType<typeof useFileAi>
  openFile: (path: string, sinceIteration?: number | null) => Promise<void>
  openCriticalFile: (path: string) => void
  backToList: () => void
  // Keyboard actions that the toolbar offers as buttons too.
  stepFile: (offset: 1 | -1) => void
  showBriefing: () => void
  toggleReviewed: () => void
}

export const cockpitKey: InjectionKey<Cockpit> = Symbol('cockpit')

export function useCockpit(): Cockpit {
  const cockpit = inject(cockpitKey)
  if (!cockpit) throw new Error('PR Cockpit components render only inside App.vue.')
  return cockpit
}
