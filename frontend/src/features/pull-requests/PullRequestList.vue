<script setup lang="ts">
// The active pull requests of the chosen repository, with checklist and file progress.
import { useCockpit } from '@/cockpit'
import { formatDate } from '@/lib/format'

const {
  pullRequests, checklistProgress, progressLoading, progressError, fileReviewProgress, openPullRequest, retryChecklistProgress,
} = useCockpit().pullRequests
</script>

<template>
  <section class="card list-card">
    <div class="list-heading"><h2>Pull Requesty</h2><span>{{ pullRequests.length }} aktywnych</span></div>
    <p v-if="progressLoading" class="notice" role="status">Wczytywanie postępu checklist…</p>
    <div v-if="progressError" class="notice error" role="alert">Nie udało się wczytać postępu checklist: {{ progressError }} <button class="checklist-retry" type="button" @click="retryChecklistProgress">Spróbuj ponownie</button></div>
    <p v-if="pullRequests.length === 0" class="empty">W tym repozytorium nie ma aktywnych Pull Requestów.</p>
    <button v-for="pr in pullRequests" :key="pr.id" class="pr-row" type="button" @click="openPullRequest(pr.id)">
      <span class="pr-number">#{{ pr.id }}</span>
      <span class="pr-title"><strong>{{ pr.title }}</strong><small>{{ pr.author }} · {{ pr.repository }}</small></span>
      <span class="status">{{ pr.status }}</span>
      <span class="pr-progress" :aria-label="`Postęp checklisty: ${progressLoading ? 'wczytywanie' : progressError ? 'błąd wczytywania' : `${checklistProgress[pr.id] ?? 0} z 6`}`">{{ progressLoading ? '…/6' : progressError ? '—/6' : `${checklistProgress[pr.id] ?? 0}/6` }}</span>
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
.pr-progress { min-width: 40px; color: var(--accent); font-size: 14px; font-weight: 800; white-space: nowrap; text-align: center; }
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
