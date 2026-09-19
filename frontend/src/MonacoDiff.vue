<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import * as monaco from 'monaco-editor'
import EditorWorker from 'monaco-editor/editor/editor.worker?worker'
import JsonWorker from 'monaco-editor/languages/features/json/json.worker?worker'
import CssWorker from 'monaco-editor/languages/features/css/css.worker?worker'
import HtmlWorker from 'monaco-editor/languages/features/html/html.worker?worker'
import TsWorker from 'monaco-editor/languages/features/typescript/ts.worker?worker'
import { api, type CSharpHoverEntry, type CSharpSemanticToken } from './api'

const props = defineProps<{ path: string; originalPath: string | null; originalText: string; modifiedText: string }>()
const container = ref<HTMLElement | null>(null)
let editor: monaco.editor.IStandaloneDiffEditor | null = null
let originalModel: monaco.editor.ITextModel | null = null
let modifiedModel: monaco.editor.ITextModel | null = null
let resizeObserver: ResizeObserver | null = null
let hoverRegistration: monaco.IDisposable | null = null
let semanticRegistration: monaco.IDisposable | null = null
let hoverAbort: AbortController | null = null

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

function configureCodeTheme() {
  if (codeThemeConfigured) return
  monaco.editor.defineTheme('pr-cockpit-code', {
    base: 'vs',
    inherit: true,
    rules: [
      { token: 'identifier.cs', foreground: '294257' },
      { token: 'identifier.ts', foreground: '294257' },
      { token: 'namespace', foreground: '466B8A' },
      { token: 'class', foreground: '087589' },
      { token: 'interface', foreground: '087589' },
      { token: 'struct', foreground: '087589' },
      { token: 'enum', foreground: '98540D' },
      { token: 'delegate', foreground: '087589' },
      { token: 'typeParameter', foreground: '087589' },
      { token: 'method', foreground: '79522A' },
      { token: 'property', foreground: '174F8A' },
      { token: 'field', foreground: '174F8A' },
      { token: 'event', foreground: '79522A' },
      { token: 'variable', foreground: '243B53' },
      { token: 'parameter', foreground: '36536B' },
      { token: 'enumMember', foreground: '98540D' },
      { token: 'type.identifier.ts', foreground: '087589' },
    ],
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
  configureCodeTheme()
  editor = monaco.editor.createDiffEditor(container.value, {
    theme: 'pr-cockpit-code',
    readOnly: true,
    originalEditable: false,
    renderSideBySide: false,
    diffWordWrap: 'on',
    scrollBeyondLastLine: false,
    minimap: { enabled: false },
    fontSize: 14,
    lineHeight: 22,
    lineNumbersMinChars: 3,
  })
  editor.setModel({ original: originalModel, modified: modifiedModel })
  editor.getOriginalEditor().updateOptions({ 'semanticHighlighting.enabled': true })
  editor.getModifiedEditor().updateOptions({ 'semanticHighlighting.enabled': true })
  resizeObserver = new ResizeObserver(() => editor?.layout())
  resizeObserver.observe(container.value)
  if (language === 'csharp' || originalLanguage === 'csharp') void loadCSharpHovers()
})

onBeforeUnmount(() => {
  hoverAbort?.abort()
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
