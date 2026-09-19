<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as monaco from 'monaco-editor'
import EditorWorker from 'monaco-editor/editor/editor.worker?worker'
import JsonWorker from 'monaco-editor/languages/features/json/json.worker?worker'
import CssWorker from 'monaco-editor/languages/features/css/css.worker?worker'
import HtmlWorker from 'monaco-editor/languages/features/html/html.worker?worker'
import TsWorker from 'monaco-editor/languages/features/typescript/ts.worker?worker'
import { api, type CSharpHoverEntry, type CSharpSemanticToken } from './api'

const props = defineProps<{
  path: string
  originalPath: string | null
  originalText: string
  modifiedText: string
  sideBySide?: boolean
}>()
const container = ref<HTMLElement | null>(null)
let editor: monaco.editor.IStandaloneDiffEditor | null = null
let originalModel: monaco.editor.ITextModel | null = null
let modifiedModel: monaco.editor.ITextModel | null = null
let resizeObserver: ResizeObserver | null = null
let hoverRegistration: monaco.IDisposable | null = null
let semanticRegistration: monaco.IDisposable | null = null
let hoverAbort: AbortController | null = null
let firstDiffListener: monaco.IDisposable | null = null

const semanticTokenTypes = [
  'namespace', 'class', 'interface', 'struct', 'enum', 'delegate', 'typeParameter',
  'method', 'property', 'field', 'event', 'variable', 'parameter', 'enumMember',
]
const semanticLegend = { tokenTypes: semanticTokenTypes, tokenModifiers: [] }
let codeThemeConfigured = false

function encodeSemanticTokens(tokens: CSharpSemanticToken[]): Uint32Array {
  const data: number[] = []
  let previousLine = 0
  let previousColumn = 0
  for (const token of [...tokens].sort((a, b) => a.line - b.line || a.startColumn - b.startColumn)) {
    const type = semanticTokenTypes.indexOf(token.kind)
    if (type < 0 || token.endColumn <= token.startColumn) continue
    const line = token.line - 1
    const column = token.startColumn - 1
    data.push(line - previousLine, line === previousLine ? column - previousColumn : column,
      token.endColumn - token.startColumn, type, 0)
    previousLine = line
    previousColumn = column
  }
  return new Uint32Array(data)
}

// Light and dark foregrounds for the same semantic token, defined once so the two
// themes cannot drift apart. Dark values follow VS Code Dark+ so C# reads familiar.
const codeTokens: { scope: string; light: string; dark: string }[] = [
  { scope: 'identifier.cs', light: '294257', dark: 'C3D2E0' },
  { scope: 'identifier.ts', light: '294257', dark: 'C3D2E0' },
  { scope: 'namespace', light: '466B8A', dark: '8FB3CE' },
  { scope: 'class', light: '087589', dark: '4EC9B0' },
  { scope: 'interface', light: '087589', dark: '4EC9B0' },
  { scope: 'struct', light: '087589', dark: '4EC9B0' },
  { scope: 'enum', light: '98540D', dark: 'D7A05A' },
  { scope: 'delegate', light: '087589', dark: '4EC9B0' },
  { scope: 'typeParameter', light: '087589', dark: '4EC9B0' },
  { scope: 'method', light: '79522A', dark: 'DCC08A' },
  { scope: 'property', light: '174F8A', dark: '8FC0EE' },
  { scope: 'field', light: '174F8A', dark: '8FC0EE' },
  { scope: 'event', light: '79522A', dark: 'DCC08A' },
  { scope: 'variable', light: '243B53', dark: 'CBD7E4' },
  { scope: 'parameter', light: '36536B', dark: 'A9BED0' },
  { scope: 'enumMember', light: '98540D', dark: 'D7A05A' },
  { scope: 'type.identifier.ts', light: '087589', dark: '4EC9B0' },
]

// ponytail: the component owns theme detection instead of taking it as a prop, so App.vue
// never has to listen on a media query. Ceiling: no manual light/dark override.
const darkQuery = typeof window.matchMedia === 'function'
  ? window.matchMedia('(prefers-color-scheme: dark)')
  : null

function themeName(): string {
  return darkQuery?.matches ? 'pr-cockpit-code-dark' : 'pr-cockpit-code'
}

function applyTheme() {
  monaco.editor.setTheme(themeName())
}

function configureCodeThemes() {
  if (codeThemeConfigured) return
  monaco.editor.defineTheme('pr-cockpit-code', {
    base: 'vs',
    inherit: true,
    rules: codeTokens.map(entry => ({ token: entry.scope, foreground: entry.light })),
    colors: {},
  })
  monaco.editor.defineTheme('pr-cockpit-code-dark', {
    base: 'vs-dark',
    inherit: true,
    rules: codeTokens.map(entry => ({ token: entry.scope, foreground: entry.dark })),
    colors: {},
  })
  codeThemeConfigured = true
}

Object.assign(self, {
  MonacoEnvironment: {
    getWorker(_id: string, label: string): Worker {
      if (label === 'json') return new JsonWorker()
      if (['css', 'scss', 'less'].includes(label)) return new CssWorker()
      if (['html', 'handlebars', 'razor'].includes(label)) return new HtmlWorker()
      if (label === 'typescript' || label === 'javascript') return new TsWorker()
      return new EditorWorker()
    },
  },
})

function languageForPath(path: string): string {
  const name = path.split('/').pop()?.toLowerCase() ?? ''
  if (name.endsWith('.vue')) return 'html'
  const extension = name.includes('.') ? `.${name.split('.').pop()}` : ''
  const language = monaco.languages.getLanguages().find(entry =>
    entry.filenames?.some(filename => filename.toLowerCase() === name) ||
    entry.extensions?.some(candidate => candidate.toLowerCase() === extension))
  return language?.id ?? 'plaintext'
}

async function loadCSharpHovers() {
  if (!originalModel || !modifiedModel) return
  const original = originalModel
  const modified = modifiedModel
  hoverAbort = new AbortController()
  try {
    const hovers = await api.csharpHovers(props.originalText, props.modifiedText, hoverAbort.signal)
    if (hoverAbort.signal.aborted) return
    const byModel = new Map([
      [original.uri.toString(), hovers.original],
      [modified.uri.toString(), hovers.modified],
    ])
    const tokensByModel = new Map([
      [original.uri.toString(), hovers.originalTokens],
      [modified.uri.toString(), hovers.modifiedTokens],
    ])
    hoverRegistration = monaco.languages.registerHoverProvider('csharp', {
      provideHover(model, position) {
        const entries = byModel.get(model.uri.toString())
        const entry = entries?.find((item: CSharpHoverEntry) =>
          item.startLine === position.lineNumber &&
          item.startColumn <= position.column && position.column < item.endColumn)
        if (!entry) return null
        return {
          range: new monaco.Range(entry.startLine, entry.startColumn, entry.endLine, entry.endColumn),
          contents: [{ value: `\`\`\`csharp\n${entry.signature}\n\`\`\`` }],
        }
      },
    })
    semanticRegistration = monaco.languages.registerDocumentSemanticTokensProvider('csharp', {
      getLegend: () => semanticLegend,
      provideDocumentSemanticTokens(model) {
        return { data: encodeSemanticTokens(tokensByModel.get(model.uri.toString()) ?? []) }
      },
      releaseDocumentSemanticTokens() {},
    })
  } catch (error) {
    if (!(error instanceof DOMException && error.name === 'AbortError')) {
      console.error('Nie udało się pobrać podpowiedzi C#.', error)
    }
  }
}

onMounted(() => {
  if (!container.value) return
  const language = languageForPath(props.path)
  const originalLanguage = languageForPath(props.originalPath ?? props.path)
  originalModel = monaco.editor.createModel(props.originalText, originalLanguage)
  modifiedModel = monaco.editor.createModel(props.modifiedText, language)
  configureCodeThemes()
  editor = monaco.editor.createDiffEditor(container.value, {
    theme: themeName(),
    readOnly: true,
    originalEditable: false,
    renderSideBySide: props.sideBySide ?? false,
    diffWordWrap: 'on',
    scrollBeyondLastLine: false,
    minimap: { enabled: false },
    fontSize: 14,
    lineHeight: 22,
    lineNumbersMinChars: 3,
    // Collapses the untouched stretches of a large file down to its actual hunks.
    hideUnchangedRegions: { enabled: true, revealLineCount: 20, minimumLineCount: 6, contextLineCount: 3 },
    renderWhitespace: 'selection',
    // The gutter menu only offers revert/stage, which a read-only viewer cannot do.
    renderGutterMenu: false,
    // ponytail: experimental by name — first thing to drop if moved blocks render oddly.
    experimental: { showMoves: true },
    // Lets the page keep scrolling once the editor reaches its end (stacked layout).
    scrollbar: { alwaysConsumeMouseWheel: false },
  })
  editor.setModel({ original: originalModel, modified: modifiedModel })
  editor.getOriginalEditor().updateOptions({ 'semanticHighlighting.enabled': true })
  editor.getModifiedEditor().updateOptions({ 'semanticHighlighting.enabled': true })
  resizeObserver = new ResizeObserver(() => editor?.layout())
  resizeObserver.observe(container.value)
  darkQuery?.addEventListener?.('change', applyTheme)
  // Open on the first change rather than an unchanged prologue. Disposed after one
  // shot so later recomputes never yank the view away from where the reader is.
  firstDiffListener = editor.onDidUpdateDiff(() => {
    firstDiffListener?.dispose()
    firstDiffListener = null
    editor?.revealFirstDiff?.()
  })
  if (language === 'csharp' || originalLanguage === 'csharp') void loadCSharpHovers()
})

watch(() => props.sideBySide, value => editor?.updateOptions({ renderSideBySide: value ?? false }))

defineExpose({
  goToDiff: (target: 'next' | 'previous') => editor?.goToDiff(target),
  focusEditor: () => editor?.getModifiedEditor().focus(),
})

onBeforeUnmount(() => {
  hoverAbort?.abort()
  firstDiffListener?.dispose()
  darkQuery?.removeEventListener?.('change', applyTheme)
  hoverRegistration?.dispose()
  semanticRegistration?.dispose()
  resizeObserver?.disconnect()
  editor?.dispose()
  originalModel?.dispose()
  modifiedModel?.dispose()
})
</script>

<template>
  <div ref="container" class="monaco-diff" role="region" :aria-label="`Zmiany w pliku ${path}`" />
</template>
