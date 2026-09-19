<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, type Project, type Repository, type PullRequestDetails, type PullRequestSummary } from './api'

const projects = ref<Project[]>([])
const repositories = ref<Repository[]>([])
const pullRequests = ref<PullRequestSummary[]>([])
const details = ref<PullRequestDetails | null>(null)
const projectId = ref('')
const repositoryId = ref('')
const loading = ref(false)
const error = ref('')
let requestId = 0

function message(cause: unknown): string {
  return cause instanceof Error ? cause.message : 'Wystąpił nieoczekiwany błąd.'
}

async function loadProjects() {
  const current = ++requestId
  loading.value = true
  error.value = ''
  try {
    projects.value = await api.projects()
    if (current !== requestId) return
    if (projects.value.length === 1) {
      projectId.value = projects.value[0]!.id
      await loadRepositories()
    }
  } catch (cause) {
    if (current === requestId) error.value = message(cause)
  } finally {
    if (current === requestId) loading.value = false
  }
}

async function loadRepositories() {
  const current = ++requestId
  repositories.value = []
  repositoryId.value = ''
  pullRequests.value = []
  details.value = null
  error.value = ''
  if (!projectId.value) return
  loading.value = true
  try {
    repositories.value = await api.repositories(projectId.value)
    if (current !== requestId) return
    if (repositories.value.length === 1) {
      repositoryId.value = repositories.value[0]!.id
      await loadPullRequests()
    }
  } catch (cause) {
    if (current === requestId) error.value = message(cause)
  } finally {
    if (current === requestId) loading.value = false
  }
}

async function loadPullRequests() {
  const current = ++requestId
  pullRequests.value = []
  details.value = null
  error.value = ''
  if (!repositoryId.value) return
  loading.value = true
  try {
    pullRequests.value = await api.pullRequests(projectId.value, repositoryId.value)
  } catch (cause) {
    if (current === requestId) error.value = message(cause)
  } finally {
    if (current === requestId) loading.value = false
  }
}

async function openPullRequest(id: number) {
  const current = ++requestId
  details.value = null
  error.value = ''
  loading.value = true
  try {
    const result = await api.pullRequest(projectId.value, repositoryId.value, id)
    if (current === requestId) details.value = result
  } catch (cause) {
    if (current === requestId) error.value = message(cause)
  } finally {
    if (current === requestId) loading.value = false
  }
}

function backToList() {
  ++requestId
  details.value = null
  error.value = ''
  loading.value = false
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat('pl-PL', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function reviewerVote(vote: number): string {
  if (vote >= 5) return 'Zatwierdzono'
  if (vote < 0) return 'Zmiany wymagane'
  return 'Bez decyzji'
}

const changeLabels: Record<string, string> = {
  add: 'Dodano',
  edit: 'Zmieniono',
  delete: 'Usunięto',
  rename: 'Przeniesiono',
}

function changeLabel(changeType: string): string {
  return changeLabels[changeType.toLowerCase()] ?? changeType
}

onMounted(loadProjects)
</script>

<template>
  <div class="shell">
    <header class="topbar">
      <div class="brand"><span class="brand-mark">PR</span><span>Cockpit</span></div>
      <span class="topbar-caption">Azure DevOps / Pull Requests</span>
    </header>

    <main>
      <div class="intro">
        <div>
          <p class="eyebrow">PULL REQUESTS</p>
          <h1>{{ details ? `PR #${details.id}` : 'Aktywne Pull Requesty' }}</h1>
          <p class="subtitle">Wybierz projekt i repozytorium, aby zobaczyć bieżące zmiany.</p>
        </div>
        <button v-if="details" class="back-button" type="button" @click="backToList">← Wróć do listy</button>
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

      <p v-if="error" class="notice error" role="alert">{{ error }}</p>
      <p v-if="loading" class="notice" role="status">Pobieranie danych…</p>

      <section v-if="details" class="details card">
        <div class="details-heading">
          <div><p class="eyebrow">PR #{{ details.id }}</p><h2>{{ details.title }}</h2></div>
          <span class="status">{{ details.status }}</span>
        </div>
        <div class="metadata">
          <div><span>Autor</span><strong>{{ details.author }}</strong></div>
          <div><span>Repozytorium</span><strong>{{ details.repository }}</strong></div>
          <div><span>Utworzono</span><strong>{{ formatDate(details.createdAt) }}</strong></div>
          <div><span>Gałąź źródłowa</span><strong>{{ details.sourceBranch }}</strong></div>
          <div><span>Gałąź docelowa</span><strong>{{ details.targetBranch }}</strong></div>
          <div><span>Zmienione pliki</span><strong>{{ details.changedFilesCount }}</strong></div>
          <div><span>Commity</span><strong>{{ details.commitsCount }}</strong></div>
        </div>
        <div class="details-section"><h3>Opis</h3><p class="description">{{ details.description || 'Brak opisu.' }}</p></div>
        <div class="details-section"><h3>Zmienione pliki ({{ details.changedFilesCount }})</h3>
          <p v-if="details.changedFiles.length === 0" class="muted">Brak zmienionych plików.</p>
          <ul v-else class="changed-files">
            <li v-for="(file, index) in details.changedFiles" :key="index">
              <span class="file-path">{{ file.path }}</span>
              <span v-if="file.originalPath && file.originalPath !== file.path" class="previous-path">z {{ file.originalPath }}</span>
              <span class="change-type">{{ changeLabel(file.changeType) }}</span>
            </li>
          </ul>
        </div>
        <div class="details-section"><h3>Reviewerzy</h3>
          <p v-if="details.reviewers.length === 0" class="muted">Brak reviewerów.</p>
          <ul v-else class="plain-list"><li v-for="reviewer in details.reviewers" :key="reviewer.name">{{ reviewer.name }} <span class="muted">· {{ reviewerVote(reviewer.vote) }}</span></li></ul>
        </div>
        <div class="details-section"><h3>Powiązane Work Items</h3>
          <p v-if="details.workItems.length === 0" class="muted">Brak powiązanych Work Items.</p>
          <ul v-else class="plain-list"><li v-for="item in details.workItems" :key="item.id">#{{ item.id }}</li></ul>
        </div>
      </section>

      <section v-else-if="repositoryId && !loading" class="card list-card">
        <div class="list-heading"><h2>Pull Requesty</h2><span>{{ pullRequests.length }} aktywnych</span></div>
        <p v-if="pullRequests.length === 0" class="empty">W tym repozytorium nie ma aktywnych Pull Requestów.</p>
        <button v-for="pr in pullRequests" :key="pr.id" class="pr-row" type="button" @click="openPullRequest(pr.id)">
          <span class="pr-number">#{{ pr.id }}</span>
          <span class="pr-title"><strong>{{ pr.title }}</strong><small>{{ pr.author }} · {{ pr.repository }}</small></span>
          <span class="status">{{ pr.status }}</span>
          <span class="pr-date">Utworzono {{ formatDate(pr.createdAt) }}</span>
          <span class="row-arrow">→</span>
        </button>
      </section>
      <section v-else-if="!loading" class="card empty">Wybierz projekt i repozytorium, aby rozpocząć.</section>
    </main>
  </div>
</template>
