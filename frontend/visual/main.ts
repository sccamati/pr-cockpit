// The real App with fetch answered from fixtures, driven into one screen per `?s=` scenario.
// shots.mjs waits for window.__ready and takes the screenshot. When the API changes shape,
// update the routes below — an unmocked call is listed in window.__unmocked and reported.
import { createApp } from 'vue'
import App from '@/App.vue'
import '@/style.css'

const cs = (n: number) => Array.from({ length: 40 }, (_, i) =>
  i === 3 ? '    public decimal Total(Order order) => order.Lines.Sum(line => line.Price);' :
  i === 10 ? `    // change ${n}` : `    // line ${i + 1} of the order service`).join('\n')
const original = 'namespace Shop;\n\npublic class OrderService\n{\n' + cs(0) + '\n}\n'
const modified = 'namespace Shop;\n\npublic class OrderService\n{\n' + cs(1).replace('line 20 of', 'line 20 (edited) of') + '\n}\n'
const smallFiles = [
  { path: '/src/Orders/OrderService.cs', changeType: 'edit', originalPath: null },
  { path: '/src/Orders/OrderValidator.cs', changeType: 'add', originalPath: null },
  { path: '/src/Shared/Money.cs', changeType: 'rename', originalPath: '/src/Common/Money.cs' },
  { path: '/assets/logo.png', changeType: 'edit', originalPath: null },
  { path: '/package-lock.json', changeType: 'edit', originalPath: null, category: 'lockFile' },
]
const bigFiles = Array.from({ length: 9 }, (_, i) => ({ path: `/src/Billing/Step${i + 1}.cs`, changeType: i % 3 ? 'edit' : 'add', originalPath: null }))
const base = {
  author: 'Anna Nowak', repository: 'Shop', status: 'active', createdAt: '2026-09-01T12:00:00Z',
  sourceBranch: 'feature/orders-total', targetBranch: 'main', reviewers: [{ name: 'Jan Kowalski', vote: 10 }],
  commits: [
    { id: 'aaaaaaaaa1', message: 'Compute order total\nBody', author: 'Anna Nowak', authoredAt: '2026-09-01T12:00:00Z' },
    { id: 'bbbbbbbbb2', message: 'Validate orders', author: 'Jan Kowalski', authoredAt: null },
  ],
  commitsCount: 2, workItems: [{ id: '1234', url: 'https://example.invalid/1234' }],
  iterations: [{ id: 1, sourceCommitSha: 'aaaaaaaaa1' }, { id: 2, sourceCommitSha: 'bbbbbbbbb2' }],
  baseCommitSha: 'base', headCommitSha: 'head',
}
const prs: Record<number, any> = {
  123: { ...base, id: 123, title: 'Liczenie sumy zamówienia i walidacja', changedFiles: smallFiles, changedFilesCount: smallFiles.length,
    description: '## Co\nLiczy **sumę** zamówienia. Dotyczy #1234.\n\n- punkt pierwszy\n- punkt `drugi`\n\n```cs\nvar x = 1;\n```\n\n> cytat' },
  // More than five code files, so it opens on the walkthrough entry.
  200: { ...base, id: 200, title: 'Duży refaktor rozliczeń', changedFiles: bigFiles, changedFilesCount: bigFiles.length, description: null },
}
const comment = (id: number, content: string, author = 'Jan Kowalski', isMine = false) =>
  ({ id, author, content, commentType: 'text', publishedAt: '2026-09-02T09:30:00Z', authorId: author, isMine })
const threads = [
  { id: 1, status: 'active', filePath: '/src/Orders/OrderService.cs', rightLine: 8, leftLine: null, isSystem: false, iterationId: 1,
    comments: [comment(1, 'Czy **suma** uwzględnia rabaty?\n\n```cs\norder.Discount\n```'), comment(2, 'Tak, w następnym commicie.', 'Anna Nowak', true)] },
  { id: 2, status: 'fixed', filePath: '/src/Orders/OrderService.cs', rightLine: 15, leftLine: null, isSystem: false, iterationId: 2,
    comments: [comment(1, 'Literówka w komentarzu.')] },
  { id: 3, status: 'active', filePath: '/assets/logo.png', rightLine: null, leftLine: null, isSystem: false, iterationId: 2,
    comments: [comment(1, 'Nowe logo jest za ciemne.')] },
  { id: 4, status: 'active', filePath: null, rightLine: null, leftLine: null, isSystem: false, iterationId: 2,
    comments: [comment(1, 'Ogólna uwaga do całego PR.', 'Ewa')] },
]
const summaryFor = (pr: any) => ({
  schemaVersion: 1, summary: 'x',
  sentences: ['Zmiana liczy sumę zamówienia.', 'Dodaje walidator zamówień.', 'Przenosi typ Money.'],
  criticalFiles: pr.changedFiles.slice(0, 2).map((f: any) => ({ path: f.path, role: 'Rola pliku ' + f.path.split('/').pop(), why: 'Tu jest reguła biznesowa.' })),
  readingOrder: pr.changedFiles.map((f: any) => f.path),
  baseCommitSha: 'base', headCommitSha: 'head',
  contextReport: { changedFiles: pr.changedFiles.length, includedFiles: pr.changedFiles.length - 1, includedDiffCharacters: 1200, wasLimited: true,
    omittedFiles: [{ path: '/package-lock.json', reason: 'noise' }] },
})
const params = new URLSearchParams(location.search)
const routes: [RegExp, (m: RegExpMatchArray, body: any) => unknown][] = [
  [/^\/config$/, () => ({ commentsEnabled: !params.has('ro') })],
  [/^\/projects$/, () => [{ id: 'p', name: 'Sklep' }]],
  [/^\/projects\/p\/repositories$/, () => [{ id: 'r', name: 'Shop' }]],
  [/\/pull-requests$/, () => [prs[123], prs[200]]],
  [/\/pull-requests\/checklist-progress$/, () => [{ pullRequestId: 123, completedCount: 2 }]],
  [/\/pull-requests\/file-review-progress$/, () => [{ pullRequestId: 123, reviewedCount: 1, changedFilesCount: 5 }]],
  [/\/pull-requests\/(\d+)$/, m => prs[+m[1]!]],
  [/\/pull-requests\/\d+\/diff\?path=([^&]+)/, m => {
    const path = decodeURIComponent(m[1]!)
    return path.endsWith('.png')
      ? { kind: 'binary', path, originalPath: null, originalText: null, modifiedText: null }
      : { kind: 'text', path, originalPath: null, originalText: original, modifiedText: modified }
  }],
  [/\/pull-requests\/\d+\/threads$/, () => threads],
  [/\/pull-requests\/(\d+)\/summary$/, m => ({ stored: { result: summaryFor(prs[+m[1]!]), savedAt: '2026-09-02T10:00:00Z' } })],
  [/\/pull-requests\/\d+\/summary\/file$/, (_, body) => ({ schemaVersion: 1, path: body.path, headCommitSha: 'head', sentences: ['Plik liczy sumę.', 'Korzysta z Money.'] })],
  [/\/pull-requests\/\d+\/summary\/ask\?/, () => [{ schemaVersion: 1, path: '/assets/logo.png', question: 'Co robi Total?', selection: 'order.Lines.Sum()',
    sentences: ['Sumuje ceny linii.'], blobId: null, headCommitSha: 'head', askedAt: '2026-09-02T10:00:00Z' }]],
  [/\/pull-requests\/\d+\/checklist$/, () => ({ aiReview: true, quality: true, understand: false, architecture: false, debug: false, ready: false, updatedAt: null, debugNote: null })],
  [/\/pull-requests\/\d+\/file-reviews$/, () => ({ files: [{ path: '/src/Shared/Money.cs', blobId: null, headSha: 'head', updatedAt: '2026-09-02T10:00:00Z' }],
    readingPath: { paths: [], position: 0, headCommitSha: null, updatedAt: null }, updatedAt: null })],
  [/\/pull-requests\/\d+\/changed-paths/, () => ['/src/Orders/OrderService.cs']],
  [/\/pull-requests\/\d+\/usages$/, () => ({ mode: 'semantic', skippedFiles: 0, declarations: [{ line: 3, startColumn: 14, endColumn: 26, name: 'OrderService',
    usages: [{ path: '/src/Api/Program.cs', line: 12, startColumn: 5, endColumn: 17 }, { path: '/src/Api/Orders.cs', line: 4, startColumn: 9, endColumn: 21 }] }] })],
  [/\/reading-path$/, (_, body) => ({ paths: body.paths, position: body.position ?? 0, headCommitSha: body.headCommitSha ?? null, updatedAt: null })],
  [/^\/csharp\/hovers$/, () => ({ original: [], modified: [], originalTokens: [], modifiedTokens: [] })],
]
const unmocked: string[] = ((window as any).__unmocked = [])
window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input).replace(/^\/api/, '')
  const body = init?.body ? JSON.parse(String(init.body)) : undefined
  for (const [pattern, answer] of routes) {
    const match = url.match(pattern)
    if (match) return new Response(JSON.stringify(answer(match, body)), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  unmocked.push(`${init?.method ?? 'GET'} ${url}`)
  return new Response(JSON.stringify({ detail: 'unmocked ' + url }), { status: 500 })
}

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms))
async function waitFor(selector: string, timeout = 15000) {
  const start = performance.now()
  while (performance.now() - start < timeout) {
    const el = document.querySelector(selector) as HTMLElement | null
    if (el) return el
    await sleep(50)
  }
  throw new Error('timeout waiting for ' + selector)
}
const key = (k: string) => window.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }))
async function clickFile(name: string) {
  await waitFor('.file-button')
  ;[...document.querySelectorAll<HTMLElement>('.file-button')].find(b => b.textContent?.includes(name))!.click()
}
async function openPr(index: number) { await waitFor('.pr-row'); document.querySelectorAll<HTMLElement>('.pr-row')[index]!.click() }
// Monaco needs a moment after the zones appear before its layout settles.
const steps: Record<string, () => Promise<void>> = {
  list: async () => { await waitFor('.pr-progress'); await sleep(300) },
  // PR 123 opens on its first unread file by itself.
  opened: async () => { await openPr(0); await waitFor('.summary-text'); await waitFor('.zone-card'); await sleep(2500) },
  briefing: async () => { await steps.opened!(); key('o'); await waitFor('.pr-briefing') },
  binary: async () => { await steps.briefing!(); await clickFile('logo.png'); (await waitFor('.file-thread-chip')).click(); await waitFor('.inline-comments') },
  explain: async () => { await steps.binary!(); key('e'); await waitFor('.file-explanation') },
  chat: async () => { await steps.binary!(); key('a'); await waitFor('.file-chat-turn') },
  comments: async () => { await steps.briefing!(); key('c'); await waitFor('.comments-view .thread') },
  focus: async () => { await steps.binary!(); key('f'); await sleep(100) },
  help: async () => { await steps.briefing!(); key('?'); await sleep(100) },
  readonly: async () => { await steps.opened!() },
  sidebyside: async () => { await steps.opened!(); key('s'); await sleep(2000) },
  entry: async () => { await openPr(1); await waitFor('.walk-entry'); await sleep(200) },
  walk: async () => { await steps.entry!(); (await waitFor('.walk-entry .walk-primary')).click(); await waitFor('.walk-bar'); await sleep(2500) },
  done: async () => {
    await steps.walk!()
    for (let i = 0; i < 20 && !document.querySelector('.walk-done'); i++) {
      document.querySelector<HTMLElement>('.walk-actions--sticky .walk-primary')?.click()
      await sleep(400)
    }
    await waitFor('.walk-done')
  },
  rail: async () => { await steps.binary!(); document.querySelectorAll<HTMLDetailsElement>('.context-rail details').forEach(d => { d.open = true }); await sleep(100) },
}
;(window as any).__scenarios = Object.keys(steps)

createApp(App).mount('#app')
const scenario = params.get('s') ?? 'list'
steps[scenario]!().then(() => sleep(300)).then(
  () => { (window as any).__ready = 'ok' },
  (e: Error) => { (window as any).__ready = 'error: ' + e.message })
