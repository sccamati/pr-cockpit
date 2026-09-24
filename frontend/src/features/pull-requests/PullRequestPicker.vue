<script setup lang="ts">
// The start screen's heading and the project and repository selects.
import { useCockpit } from '@/cockpit'

const { projects, repositories, projectId, repositoryId, loading, loadRepositories, loadPullRequests } = useCockpit().pullRequests
</script>

<template>
  <div class="intro">
    <div>
      <p class="eyebrow">PULL REQUESTS</p>
      <h1>Aktywne Pull Requesty</h1>
      <p class="subtitle">Wybierz projekt i repozytorium, aby zobaczyć bieżące zmiany.</p>
    </div>
  </div>

  <section class="filters" aria-label="Wybór źródła">
    <label>
      <span>Projekt</span>
      <select v-model="projectId" :disabled="loading && projects.length === 0" @change="loadRepositories">
        <option value="">Wybierz projekt</option>
        <option v-for="project in projects" :key="project.id" :value="project.id">{{ project.name }}</option>
      </select>
    </label>
    <label>
      <span>Repozytorium</span>
      <select v-model="repositoryId" :disabled="!projectId" @change="loadPullRequests">
        <option value="">Wybierz repozytorium</option>
        <option v-for="repository in repositories" :key="repository.id" :value="repository.id">{{ repository.name }}</option>
      </select>
    </label>
    <button class="refresh-button" type="button" :disabled="!repositoryId || loading" @click="loadPullRequests">Odśwież</button>
  </section>
</template>

<style scoped>
.intro { display: flex; justify-content: space-between; gap: 20px; align-items: center; margin-bottom: 32px; }
.eyebrow { margin: 0 0 8px; color: var(--accent); font-size: 11px; letter-spacing: .13em; font-weight: 800; }
.subtitle { margin-bottom: 0; color: var(--text-muted); font-size: 15px; }
.filters { display: flex; align-items: end; gap: 16px; margin-bottom: 26px; }
.filters label { display: flex; flex-direction: column; gap: 8px; flex: 1; max-width: 390px; color: var(--text-label); font-size: 12px; font-weight: 700; }

@media (max-width: 720px) {
  .intro { align-items: start; flex-direction: column; }
  .filters { align-items: stretch; flex-direction: column; }
  .filters label { max-width: none; }
}
</style>
