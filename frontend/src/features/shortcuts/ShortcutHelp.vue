<script setup lang="ts">
// The `?` dialog. The keys themselves are bound in App.vue; this is only their list.
import { ref } from 'vue'

const shortcutHelp = [
  { keys: 'j / n', label: 'Następny plik' },
  { keys: 'k / p', label: 'Poprzedni plik' },
  { keys: 'm', label: 'Obejrzałem i przejdź dalej' },
  { keys: '. / ]', label: 'Następna zmiana w pliku' },
  { keys: ', / [', label: 'Poprzednia zmiana w pliku' },
  { keys: '/', label: 'Szukaj pliku' },
  { keys: 's', label: 'Widok obok siebie / w linii' },
  { keys: 'f', label: 'Tryb skupienia' },
  { keys: 'g', label: 'Przejdź kursorem do kodu' },
  { keys: 'o', label: 'Opis PR i powrót do pliku' },
  { keys: 'e', label: 'Wyjaśnij ten plik' },
  { keys: 'a', label: 'Zapytaj o ten plik' },
  { keys: 'c', label: 'Widok komentarzy' },
  { keys: 'Esc', label: 'Zamknij pomoc albo wróć do listy' },
  { keys: '?', label: 'Ta pomoc' },
]

const dialog = ref<HTMLDialogElement | null>(null)

function toggle() {
  if (!dialog.value) return
  if (dialog.value.open) dialog.value.close?.()
  else dialog.value.showModal?.()
}

function close() {
  dialog.value?.close?.()
}

defineExpose({ toggle, close, isOpen: () => !!dialog.value?.open })
</script>

<template>
  <dialog ref="dialog" class="shortcut-help" aria-label="Skróty klawiszowe">
    <h3>Skróty klawiszowe</h3>
    <dl>
      <div v-for="shortcut in shortcutHelp" :key="shortcut.keys"><dt>{{ shortcut.keys }}</dt><dd>{{ shortcut.label }}</dd></div>
    </dl>
    <button type="button" class="refresh-button" @click="close">Zamknij</button>
  </dialog>
</template>

<style scoped>
.shortcut-help { max-width: 420px; padding: 22px 26px; border: 1px solid var(--line-strong); border-radius: 12px; background: var(--surface); color: var(--text); }
.shortcut-help::backdrop { background: rgba(10, 18, 26, .45); }
.shortcut-help dl { margin: 0 0 18px; }
.shortcut-help dl > div { display: flex; gap: 14px; align-items: baseline; padding: 5px 0; border-bottom: 1px solid var(--line); }
.shortcut-help dt { flex: none; min-width: 74px; color: var(--accent); font-family: Consolas, 'Courier New', monospace; font-size: 13px; font-weight: 700; }
.shortcut-help dd { margin: 0; font-size: 13px; }
</style>
