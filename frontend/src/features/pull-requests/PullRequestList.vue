<script setup lang="ts">
// The active pull requests of the chosen repository, with quiz and file progress. A pull
// request is done when its quiz is passed — nothing else marks it.
import { useCockpit } from '@/cockpit'
import { formatDate } from '@/lib/format'

const {
  pullRequests, quizProgress, progressLoading, progressError, fileReviewProgress, openPullRequest, retryQuizProgress,
} = useCockpit().pullRequests

// ponytail: the list does not know a pull request's head, so a quiz passed before a push still
// shows as done here until the pull request is opened (inside, the quiz shows as out of date).
function quizLabel(id: number): string {
  if (progressLoading.value) return '…'
  if (progressError.value) return '—'
  const progress = quizProgress.value[id]
  if (!progress) return 'bez quizu'
  return progress.correct === progress.total ? '✓ zakończony' : `quiz ${progress.correct}/${progress.total}`
}
</script>

<template>
  <section class="card list-card">
    <div class="list-heading"><h2>Pull Requesty</h2><span>{{ pullRequests.length }} aktywnych</span></div>
    <p v-if="progressLoading" class="notice" role="status">Wczytywanie postępu quizów…</p>
    <div v-if="progressError" class="notice error" role="alert">Nie udało się wczytać postępu quizów: {{ progressError }} <button class="retry" type="button" @click="retryQuizProgress">Spróbuj ponownie</button></div>
    <p v-if="pullRequests.length === 0" class="empty">W tym repozytorium nie ma aktywnych Pull Requestów.</p>
    <button v-for="pr in pullRequests" :key="pr.id" class="pr-row" type="button" @click="openPullRequest(pr.id)">
      <span class="pr-number">#{{ pr.id }}</span>
      <span class="pr-title"><strong>{{ pr.title }}</strong><small>{{ pr.author }} · {{ pr.repository }}</small></span>
      <span class="status">{{ pr.status }}</span>
      <span class="pr-progress" :class="{ 'pr-progress--done': quizLabel(pr.id) === '✓ zakończony', 'pr-progress--none': quizLabel(pr.id) === 'bez quizu' }"
        :aria-label="`Quiz: ${quizLabel(pr.id)}`">{{ quizLabel(pr.id) }}</span>
      <span class="pr-files">{{ fileReviewProgress[pr.id] ? `${fileReviewProgress[pr.id]!.reviewed}/${fileReviewProgress[pr.id]!.total} plików` : '' }}</span>
      <span class="pr-date">Utworzono {{ formatDate(pr.createdAt) }}</span>
      <span class="row-arrow">→</span>
    </button>
  </section>
</template>

<style scoped>
.list-heading { display: flex; justify-content: space-between; align-items: center; gap: 20px; padding: 22px 24px; border-bottom: 1px solid var(--line); }
.list-heading span { color: var(--text-muted); font-size: 13px; }
.pr-row { display: flex; width: 100%; gap: 18px; align-items: center; min-height: 84px; padding: 16px 24px; background: transparent; border: 0; border-bottom: 1px solid var(--line); text-align: left; color: inherit; }
.pr-row:last-child { border-bottom: 0; }
.pr-row:hover { background: var(--surface-hover); }
.pr-number { color: var(--accent); font-weight: 800; min-width: 54px; }
.pr-title { display: flex; flex-direction: column; gap: 6px; flex: 1; min-width: 0; }
.pr-title strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pr-title small { color: var(--text-muted); }
.pr-progress { min-width: 96px; color: var(--accent); font-size: 13px; font-weight: 800; white-space: nowrap; text-align: center; }
.pr-progress--done { color: var(--ok); }
.pr-progress--none { color: var(--text-faint); font-weight: 600; }
.pr-files { min-width: 58px; color: var(--text-muted); font-size: 12px; font-weight: 700; white-space: nowrap; text-align: center; }
.pr-date { min-width: 166px; text-align: right; color: var(--text-muted); font-size: 12px; }
.row-arrow { color: var(--text-faint); font-size: 22px; }

@media (max-width: 720px) {
  .pr-row { flex-wrap: wrap; gap: 10px; padding: 16px; }
  .pr-title { flex-basis: calc(100% - 70px); }
  .pr-date { min-width: 0; text-align: left; }
  .row-arrow { margin-left: auto; }
}
</style>
