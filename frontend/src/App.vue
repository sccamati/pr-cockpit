<script setup lang="ts">
// The shell: creates every feature once, hands them to the child components through
// cockpit.ts, and owns what crosses features — resetting a pull request, stepping through
// files and the keyboard. Each feature lives in its own folder under features/.
import { onMounted, provide, ref } from 'vue'
import { api } from './api'
import { cockpitKey } from './cockpit'
import { scrollBehavior } from '@/lib/format'
import { useComments } from '@/features/comments/useComments'
import ContextRail from '@/features/context/ContextRail.vue'
import { useChecklist } from '@/features/context/useChecklist'
import { useSummary } from '@/features/context/useSummary'
import DiffPanel from '@/features/diff/DiffPanel.vue'
import { useDiff } from '@/features/diff/useDiff'
import { useFileAi } from '@/features/file-ai/useFileAi'
import FileListPane from '@/features/file-tree/FileListPane.vue'
import { useFileList } from '@/features/file-tree/useFileList'
import PrHeader from '@/features/pull-requests/PrHeader.vue'
import PullRequestList from '@/features/pull-requests/PullRequestList.vue'
import PullRequestPicker from '@/features/pull-requests/PullRequestPicker.vue'
import { usePullRequests } from '@/features/pull-requests/usePullRequests'
import ShortcutHelp from '@/features/shortcuts/ShortcutHelp.vue'
import { useShortcuts } from '@/features/shortcuts/useShortcuts'
import WalkDone from '@/features/walkthrough/WalkDone.vue'
import WalkEntry from '@/features/walkthrough/WalkEntry.vue'
import { useReviewProgress } from '@/features/walkthrough/useReviewProgress'
import { useWalkthrough } from '@/features/walkthrough/useWalkthrough'

const focusMode = ref(false)
const helpDialog = ref<InstanceType<typeof ShortcutHelp> | null>(null)

// --- The features. Some need each other both ways; those links are getters, resolved
// only once everything below exists. ---
const pullRequests = usePullRequests({
  onReset: resetPullRequest,
  onOpened: (project, repository, id) => {
    // US-P3: a pull request big enough to get lost in opens on the proposal, not on the
    // tree. The tree is one click away and everything in it still works.
    if (walk.walkWorthwhile.value) walk.view.value = 'entry'
    void loadChecklist(project, repository, id)
    void loadSavedSummary(project, repository, id)
    void loadFileReviews(project, repository, id)
    void loadThreads(project, repository, id)
  },
})
const { details, projectId, repositoryId, loading, error, backToList } = pullRequests
const scope = { details, projectId, repositoryId }
const summaryState = useSummary(scope)
const checklistState = useChecklist(scope)
const review = useReviewProgress({ ...scope, onLoaded: () => walk.afterReviewsLoaded() })
const diff = useDiff({
  ...scope,
  defaultSince: () => fileList.filterIteration.value,
  onLeave: () => comments.leaveFile(),
  onOpened: (pullRequestId, path, request) => fileAi.fileOpened(pullRequestId, path, request),
})
const { selectedFilePath, fileDiff, diffPanel, lastFilePath, sideBySide, diffView, openFile, resetDiff, goToDiff } = diff
const fileList = useFileList({
  ...scope, selectedFilePath, summary: summaryState, review, threadsByFile: () => comments.threadsByFile.value,
})
const { orderedPaths, lastIteration, nextUnreviewedPath, resetFileList } = fileList
const comments = useComments({ ...scope, selectedFilePath, fileDiff, orderedPaths, lastIteration, diffView, openFile })
const walk = useWalkthrough({
  details, summary: summaryState, review, orderedPaths, lastIteration, nextUnreviewedPath, selectedFilePath, openFile,
})
const fileAi = useFileAi({ ...scope, selectedFilePath, diffRequest: diff.diffRequest })

const { resetSummary, loadSavedSummary } = summaryState
const { resetChecklist, loadChecklist } = checklistState
const { walkPosition, isReviewed, setFileReviewed, resetFileReviews, loadFileReviews } = review
const {
  commentsOpen, draft, editing, deleting, commentError, commentsEnabled, inlineThreadId,
  resetThreads, loadThreads, toggleComments, closeInlineComments,
} = comments
const { view, leaveWalkthrough, goToWalkIndex, walkAdvance, resetWalkthrough } = walk
const { questionOpen, resetExplanation, resetFileAi, explainFile, openQuestions } = fileAi

provide(cockpitKey, {
  ...scope, selectedFilePath, pullRequests, diff, fileList,
  summary: summaryState, checklist: checklistState, review, walk, comments, fileAi,
  openFile, openCriticalFile, backToList, stepFile, showBriefing, toggleReviewed,
})

function openCriticalFile(path: string) {
  void openFile(path)
  diffPanel.value?.scrollIntoView?.({ behavior: scrollBehavior(), block: 'start' })
}

function openNextUnreviewed() {
  if (nextUnreviewedPath.value) void openFile(nextUnreviewedPath.value)
}

function toggleReviewed() {
  if (!selectedFilePath.value || (!fileDiff.value && !isReviewed(selectedFilePath.value))) return
  const path = selectedFilePath.value
  // A stale marker counts as unread, so ticking the box restamps it rather than clearing it.
  void setFileReviewed(path, !isReviewed(path))
}

// Leaving the file also clears what was asked about it and the tree's filters.
function leaveFileAndFilters() {
  resetDiff()
  resetExplanation()
  resetFileList()
}

// Everything that belongs to one pull request. Each reset bumps that feature's request id,
// which is what makes a late answer about the previous pull request land nowhere.
function resetPullRequest() {
  leaveFileAndFilters()
  resetSummary()
  resetChecklist()
  resetFileReviews()
  resetWalkthrough()
  resetThreads()
  resetFileAi()
}

// Leaving a file for the description has to be reversible, so the path is kept and the
// briefing offers a way back. The reset clears it first, hence the order here.
function showBriefing() {
  const path = selectedFilePath.value
  leaveFileAndFilters()
  lastFilePath.value = path
}

function toggleBriefing() {
  if (selectedFilePath.value) showBriefing()
  else if (lastFilePath.value) void openFile(lastFilePath.value)
}

function focusSearch() {
  const input = document.getElementById('file-search') as HTMLInputElement | null
  input?.focus()
  input?.select()
}

function stepFile(offset: 1 | -1) {
  // In the walkthrough j/k move along the path, not along the tree — same keys, the list
  // under them is the one on screen.
  if (view.value === 'walk') {
    void goToWalkIndex(walkPosition.value + offset)
    return
  }
  const paths = orderedPaths.value
  if (paths.length === 0) return
  const index = paths.indexOf(selectedFilePath.value)
  if (index < 0) {
    void openFile(paths[0]!)
    return
  }
  const target = index + offset
  if (target < 0 || target >= paths.length) return
  void openFile(paths[target]!)
}

// toggleReviewed writes synchronously, so the computed below it re-evaluates against
// the new state on the next line. That ordering is what the keyboard test pins.
function markAndAdvance() {
  if (view.value === 'walk') {
    void walkAdvance(true)
    return
  }
  if (!selectedFilePath.value) return
  if (!fileDiff.value && !isReviewed(selectedFilePath.value)) return
  if (!isReviewed(selectedFilePath.value)) toggleReviewed()
  openNextUnreviewed()
}

// Esc peels one layer at a time. Leaving the pull request is the last of them — one key
// that drops the edit, the draft, the conversation and the whole PR at once is a trap.
function escapeLayer() {
  if (helpDialog.value?.isOpen()) helpDialog.value.close()
  else if (questionOpen.value) questionOpen.value = false
  else if (deleting.value !== null) deleting.value = null
  else if (editing.value) editing.value = null
  else if (draft.value) { draft.value = null; commentError.value = '' }
  else if (inlineThreadId.value !== null) closeInlineComments()
  else if (commentsOpen.value) commentsOpen.value = false
  else if (focusMode.value) focusMode.value = false
  else if (view.value === 'walk' || view.value === 'done') leaveWalkthrough()
  else if (view.value === 'entry') view.value = 'tree'
  else backToList()
}

function toggleHelp() {
  helpDialog.value?.toggle()
}

useShortcuts({
  enabled: () => !!details.value,
  onEscape: escapeLayer,
  onHelp: toggleHelp,
  shortcuts: {
    j: () => stepFile(1),
    n: () => stepFile(1),
    k: () => stepFile(-1),
    p: () => stepFile(-1),
    m: markAndAdvance,
    '.': () => goToDiff('next'),
    ']': () => goToDiff('next'),
    ',': () => goToDiff('previous'),
    '[': () => goToDiff('previous'),
    '/': focusSearch,
    s: () => { sideBySide.value = !sideBySide.value },
    f: () => { focusMode.value = !focusMode.value },
    g: () => diffView.value?.focusEditor(),
    o: toggleBriefing,
    e: explainFile,
    a: () => void openQuestions(),
    c: toggleComments,
  },
})

onMounted(pullRequests.loadProjects)
onMounted(async () => {
  // Swallows everything: the backend enforces the switch either way, so a failed probe
  // must not cost more than an offer to write that then fails.
  try { commentsEnabled.value = (await api.config()).commentsEnabled } catch { /* leave it on */ }
})
</script>

<template>
  <div class="shell">
    <header class="topbar" :class="{ 'topbar--details': details }">
      <div class="brand"><span class="brand-mark">PR</span><span>Cockpit</span></div>
    </header>

    <main :class="{ 'main--details': details }">
      <PullRequestPicker v-if="!details" />

      <p v-if="error" class="notice error" role="alert">{{ error }}</p>
      <p v-if="loading" class="notice" role="status">Pobieranie danych…</p>

      <section v-if="details" class="details">
        <PrHeader @help="toggleHelp" />

        <WalkEntry v-if="view === 'entry'" />
        <WalkDone v-else-if="view === 'done'" />

        <!-- Writing switched off in the backend: the actions are not disabled but absent,
             because a row of five greyed-out buttons is noise, not information. -->
        <div v-else class="pr-workspace" :class="{ 'pr-workspace--focus': focusMode, 'pr-workspace--walk': view === 'walk', 'pr-workspace--readonly': !commentsEnabled }">
          <FileListPane />
          <DiffPanel />
          <ContextRail />
        </div>
      </section>

      <PullRequestList v-else-if="repositoryId && !loading" />
      <section v-else-if="!loading" class="card empty">Wybierz projekt i repozytorium, aby rozpocząć.</section>
    </main>

    <ShortcutHelp ref="helpDialog" />
  </div>
</template>

<style scoped>
.shell { min-height: 100vh; }
.topbar { height: var(--chrome-height); padding: 0 max(28px, calc((100vw - 1140px) / 2)); display: flex; align-items: center; justify-content: space-between; background: var(--topbar); color: var(--topbar-text); }
.topbar--details { padding-inline: max(28px, calc((100vw - 1920px) / 2 + 28px)); }
.brand { display: flex; align-items: center; gap: 10px; font-size: 20px; font-weight: 700; letter-spacing: -.03em; }
.brand-mark { display: grid; place-items: center; width: 32px; height: 32px; border-radius: 7px; background: var(--brand-mark); color: #142437; font-size: 12px; font-weight: 800; }
main { max-width: 1140px; margin: 0 auto; padding: 48px 28px 80px; }
main.main--details { max-width: 1920px; padding: 12px 28px; }
.details { display: flex; flex-direction: column; gap: 12px; }
.pr-workspace { display: grid; gap: 14px;
  grid-template-columns: clamp(240px, 19vw, 360px) minmax(0, 1fr) clamp(280px, 21vw, 360px);
  height: calc(100vh - var(--chrome-height) - var(--pr-header-height) - 40px); }
.pr-workspace--focus { grid-template-columns: clamp(240px, 19vw, 360px) minmax(0, 1fr); }
.pr-workspace--focus :deep(.context-rail) { display: none; }
/* Writing is off in the backend, so the actions that can only fail are gone rather than
   greyed out. The guards in useComments are what actually stop them; this removes the offer. */
.pr-workspace--readonly :deep(.thread-actions),
.pr-workspace--readonly :deep(.comment-own-actions),
.pr-workspace--readonly :deep(.thread-new),
.pr-workspace--readonly :deep(.comment-line-button),
.pr-workspace--readonly :deep(.comment-add-glyph::before) { display: none; }
/* One file at a time, full width: the tree and the rail are the thing it is a relief from. */
.pr-workspace--walk { grid-template-columns: minmax(0, 1fr); }
.pr-workspace--walk :deep(.file-list-pane),
.pr-workspace--walk :deep(.context-rail) { display: none; }
/* Pasek przejścia stoi nad paskiem pliku i mówi to samo: krok, nazwę i rolę. Pozycja w
   liście Azure DevOps i druga nazwa pliku byłyby tu dwoma licznikami o tym samym
   mianowniku i tą samą ścieżką dwa razy. */
.pr-workspace--walk :deep(.diff-position), .pr-workspace--walk :deep(.diff-toolbar-title) { display: none; }

@media (max-width: 720px) {
  main { padding: 28px 16px 50px; }
  main.main--details { padding: 12px 16px; }
  .topbar--details { padding-inline: 16px; }
}

@media (prefers-color-scheme: dark) {
  .brand-mark { color: #0b1219; }
}
</style>
