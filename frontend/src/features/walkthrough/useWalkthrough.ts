import { computed, ref, type ComputedRef, type Ref } from 'vue'
import type { PullRequestDetails } from '@/api'
import { same } from '@/lib/format'
import type { useReviewProgress } from './useReviewProgress'
import type { useSummary } from '@/features/context/useSummary'

// ponytail: a named rule, not configuration — a one-person local tool has no settings file.
// The path grows more slowly than the ranking (criticalFileLimit in useSummary.ts), because
// a walkthrough with no visible end is the thing it replaces.
export function walkPathLength(changedFiles: number): number {
  return Math.min(12, Math.max(8, Math.ceil(changedFiles / 8)))
}

/**
 * Przejście, the guided walkthrough: the entry screen with its three scopes, walking the
 * accepted path, and the closing screen. The path itself and the position on it are
 * stored by useReviewProgress; this owns the screens and the choice of what to walk.
 */
export function useWalkthrough({
  details, summary, review, orderedPaths, lastIteration, nextUnreviewedPath, selectedFilePath, openFile,
}: {
  details: Ref<PullRequestDetails | null>
  summary: ReturnType<typeof useSummary>
  review: ReturnType<typeof useReviewProgress>
  // The rendered tree order, which is what "next" means everywhere else too.
  orderedPaths: ComputedRef<string[]>
  lastIteration: ComputedRef<number>
  nextUnreviewedPath: ComputedRef<string | null>
  selectedFilePath: Ref<string>
  openFile: (path: string, sinceIteration?: number | null) => Promise<void>
}) {
  const { criticalProposal, fullProposal } = summary
  const { fileReviews, walkPosition, walkHeadSha, criticalPaths, criticalPathSet, reviewedPaths, reviewState, isReviewed } = review

  // 'tree' is everything that existed before the walkthrough; the other three are its screens.
  const view = ref<'tree' | 'entry' | 'walk' | 'done'>('tree')
  // 'key' walks the shortlist, 'all' walks every file of the pull request. Both follow the
  // same AI ordering — the mode only decides how much of it the path holds. 'round' is the
  // second pass after the author pushed fixes: only what moved since you last read it, in
  // tree order, and without a single call to the model.
  const walkMode = ref<'key' | 'all' | 'round'>('key')
  const walkSkipped = ref<string[]>([])
  // The entry screen's own selection, before the path is written. Null means "not touched",
  // so the default follows the ranking as it arrives.
  const walkPicked = ref<string[] | null>(null)
  const walkResumeDismissed = ref(false)

  // Only code files count: a pull request that is five sources plus a lockfile is small.
  const codeFileCount = computed(() =>
    (details.value?.changedFiles ?? []).filter(file => !file.category).length)
  // US-P3: below this, the proposal screen would cost more than the wall of files it saves.
  const walkWorthwhile = computed(() => codeFileCount.value > 5)
  const walkPaths = criticalPaths
  const walkFilePath = computed(() => walkPaths.value[walkPosition.value] ?? '')
  const walkFinished = computed(() =>
    walkPaths.value.length > 0 && walkPosition.value >= walkPaths.value.length)
  // A path with no head SHA was built in the rail, not by a walkthrough, so nothing is
  // offered to resume for it.
  const walkResumable = computed(() =>
    walkHeadSha.value !== null && walkPaths.value.length > 0 && !walkFinished.value)
  const walkChangedSincePicked = computed(() =>
    walkHeadSha.value !== null && details.value?.headCommitSha != null &&
    !same(walkHeadSha.value, details.value.headCommitSha))
  const walkReadCount = computed(() => walkPaths.value.filter(path => isReviewed(path)).length)
  const walkSkippedPaths = computed(() => walkPaths.value.filter(path => walkSkipped.value.includes(path)))

  // The second pass. A file belongs to it when its marker went stale — positive evidence the
  // content moved — or when it has no marker at all, because an unread file is unread whichever
  // push brought it. Noise stays out of the default the way it stays out of the shortlist; it
  // can still be ticked on by hand. No AI here: the order is the tree's.
  const roundPaths = computed(() => {
    const order = new Map(orderedPaths.value.map((path, index) => [path, index]))
    return (details.value?.changedFiles ?? [])
      .filter(file => !file.category && reviewState(file.path) !== 'current')
      .map(file => file.path)
      // A file hidden by the search box or the iteration filter keeps its place at the end
      // instead of dropping out of the round — the round is about the pull request, not the
      // current view of it.
      .sort((a, b) => (order.get(a) ?? Number.MAX_SAFE_INTEGER) - (order.get(b) ?? Number.MAX_SAFE_INTEGER))
  })
  const roundUnreadCount = computed(() => roundPaths.value.filter(path => !fileReviews.value[path]).length)
  // Something was read at an earlier head, so the pull request moved since you were here. An
  // unfinished first pass has every marker at the current head and is not a second round.
  const readAtOlderHead = computed(() => {
    const head = details.value?.headCommitSha
    if (!head) return false
    return Object.values(fileReviews.value).some(entry => entry.headSha && !same(entry.headSha, head))
  })
  const roundAvailable = computed(() => readAtOlderHead.value && roundPaths.value.length > 0)

  // The iteration this file was last seen at, so the second pass opens on what arrived after
  // it instead of the whole diff again. Null asks for the whole diff: no marker, a head SHA
  // belonging to no iteration of this pull request, or the newest iteration, where the
  // comparison would be with itself and show nothing.
  // ponytail: the baseline is the marker, so a file you never marked has nothing to measure
  // from and gets the full diff. A per-PR round stamp in the database would close that.
  function roundSince(path: string): number | null {
    const saved = fileReviews.value[path]?.headSha
    if (!saved) return null
    const match = (details.value?.iterations ?? []).find(item =>
      item.sourceCommitSha && same(item.sourceCommitSha, saved))
    if (!match || match.id >= lastIteration.value) return null
    return match.id
  }

  // The entry screen's list: in 'key' mode the ranking, minus what has already been read, cut
  // to the configured length; in 'all' mode the whole pull request, because leaving files out
  // is the one thing that mode is not for; in 'round' mode whatever moved since your last
  // pass. Once the user touches it, their version is the list.
  const walkDefaultPick = computed(() => {
    if (walkMode.value === 'round') return roundPaths.value
    if (walkMode.value === 'all') return fullProposal.value
    return criticalProposal.value
      .filter(file => reviewState(file.path) !== 'current')
      .slice(0, walkPathLength(details.value?.changedFiles.length ?? 0))
      .map(file => file.path)
  })
  const walkPick = computed(() => walkPicked.value ?? walkDefaultPick.value)
  const walkPickSet = computed(() => new Set(walkPick.value))
  // On the entry screen "the rest" is measured against what is selected, because the path
  // is not written yet; everywhere after that, against the path itself.
  const walkInsideSet = computed(() => view.value === 'entry' ? walkPickSet.value : criticalPathSet.value)
  const walkOutsideCount = computed(() =>
    Math.max(0, (details.value?.changedFiles.length ?? 0) - walkInsideSet.value.size))
  const walkOutsideNoiseCount = computed(() =>
    (details.value?.changedFiles ?? []).filter(file => file.category && !walkInsideSet.value.has(file.path)).length)
  // Every file the entry screen can offer: the proposal for this mode plus anything already
  // in the path, so unticking a file leaves the row on screen instead of making it vanish.
  const walkCandidates = computed(() => {
    const proposed = walkMode.value === 'round'
      ? roundPaths.value
      : walkMode.value === 'all'
        ? fullProposal.value
        : criticalProposal.value.map(file => file.path)
    return [...new Set([...proposed, ...walkPick.value])]
  })
  // 'all' mode needs an order the model produced; a Summary from before this feature has
  // none, and inventing one would hide that. The user is offered the recompute instead.
  const fullOrderMissing = computed(() =>
    walkMode.value === 'all' && summary.summary.value !== null && fullProposal.value.length === 0)

  function toggleWalkPick(path: string) {
    const current = walkPick.value
    walkPicked.value = current.includes(path)
      ? current.filter(item => item !== path)
      : [...current, path]
  }

  function enterWalkthrough() {
    // A second pass is worth the screen whatever the size of the pull request — the whole
    // point is that it is shorter than the first one.
    if (!walkWorthwhile.value && !roundAvailable.value) return
    walkPicked.value = null
    walkMode.value = roundAvailable.value ? 'round' : 'key'
    walkResumeDismissed.value = false
    view.value = 'entry'
  }

  // Switching mode drops the hand-edited selection, because it was a selection of the other
  // list. Staying on the mode you are already on changes nothing.
  function setWalkMode(mode: 'key' | 'all' | 'round') {
    if (walkMode.value === mode) return
    walkMode.value = mode
    walkPicked.value = null
  }

  function leaveWalkthrough() {
    view.value = 'tree'
  }

  // In the second pass a file opens on the changes that arrived after you last saw it, so
  // what you read is the fix and not the whole file again. The bar above the diff says so and
  // offers the whole diff back. Every other mode keeps opening the full diff.
  function openWalkFile(path: string) {
    return walkMode.value === 'round' ? openFile(path, roundSince(path)) : openFile(path)
  }

  // US-P3: accepting is what turns a proposal into a reading path, and the path is what the
  // walkthrough walks. Nothing here happens without the click.
  async function startWalkthrough(paths: string[] = walkPick.value) {
    if (paths.length === 0) return
    walkSkipped.value = []
    walkPosition.value = 0
    walkHeadSha.value = details.value?.headCommitSha ?? null
    view.value = 'walk'
    await review.saveReadingPath(paths, 0)
    const first = walkPaths.value[0]
    if (first) await openWalkFile(first)
  }

  function resumeWalkthrough() {
    view.value = 'walk'
    const path = walkFilePath.value
    if (path) void openWalkFile(path)
  }

  async function goToWalkIndex(index: number) {
    const paths = walkPaths.value
    const next = Math.min(Math.max(index, 0), paths.length)
    walkPosition.value = next
    void review.saveReadingPath(paths, next)
    if (next >= paths.length) {
      view.value = 'done'
      return
    }
    await openWalkFile(paths[next]!)
  }

  // The main action of the walkthrough: this file is read, show me the next one. Marking is
  // local, so a broken connection to Azure DevOps cannot lose it.
  async function walkAdvance(markRead: boolean) {
    const path = walkFilePath.value
    if (!path) return
    if (markRead) {
      if (!isReviewed(path)) await review.setFileReviewed(path, true)
      walkSkipped.value = walkSkipped.value.filter(item => item !== path)
    } else if (!walkSkipped.value.includes(path)) {
      walkSkipped.value = [...walkSkipped.value, path]
    }
    await goToWalkIndex(walkPosition.value + 1)
  }

  function walkBack() {
    if (walkPosition.value === 0) return
    void goToWalkIndex(walkPosition.value - 1)
  }

  // US-P6: the skipped files are the reason to come back, so one action returns to one.
  function returnToSkipped(path: string) {
    const index = walkPaths.value.indexOf(path)
    if (index < 0) return
    view.value = 'walk'
    void goToWalkIndex(index)
  }

  function resetWalkthrough() {
    view.value = 'tree'
    walkSkipped.value = []
    walkPicked.value = null
    walkResumeDismissed.value = false
  }

  // Called once the markers of a freshly opened pull request are in.
  function afterReviewsLoaded() {
    // US-P7: a walkthrough taken to its end is done with. Reopening the pull request lands
    // on the ordinary screen, with nothing offered to resume.
    if (walkHeadSha.value !== null && walkFinished.value) view.value = 'tree'
    // The second pass can only be recognised from the markers, and openPullRequest picks the
    // screen before they arrive — so the decision belongs here. A walkthrough already running
    // is left alone.
    if ((view.value === 'tree' || view.value === 'entry') && roundAvailable.value) {
      walkPicked.value = null
      walkMode.value = 'round'
      view.value = 'entry'
    }
    resumeAtFirstUnread()
  }

  // Picks up where the last session stopped instead of opening on an empty panel.
  function resumeAtFirstUnread() {
    // The entry screen is the start of the pull request when there is one; opening a file
    // behind it would only mean the user finds a diff waiting when they leave it.
    if (view.value !== 'tree') return
    if (selectedFilePath.value || reviewedPaths.value.length === 0) return
    const next = nextUnreviewedPath.value
    if (next) void openFile(next)
  }

  return {
    view, walkMode, walkSkipped, walkPicked, walkResumeDismissed,
    walkWorthwhile, walkPaths, walkFilePath, walkFinished, walkResumable, walkChangedSincePicked,
    walkReadCount, walkSkippedPaths, roundPaths, roundUnreadCount, roundAvailable,
    walkPick, walkPickSet, walkOutsideCount, walkOutsideNoiseCount, walkCandidates, fullOrderMissing,
    toggleWalkPick, enterWalkthrough, setWalkMode, leaveWalkthrough, startWalkthrough, resumeWalkthrough,
    goToWalkIndex, walkAdvance, walkBack, returnToSkipped, resetWalkthrough, afterReviewsLoaded,
  }
}
