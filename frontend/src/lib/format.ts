// Small pure helpers shared by App.vue, the composables and the components. No state here.

export function message(cause: unknown): string {
  return cause instanceof Error ? cause.message : 'Wystąpił nieoczekiwany błąd.'
}

// SHAs and blob ids arrive in whatever case the API felt like.
export function same(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase()
}

export function fileName(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1)
}

export function fileDirectory(path: string): string {
  return path.slice(0, path.lastIndexOf('/') + 1)
}

export function formatDate(value: string): string {
  return new Intl.DateTimeFormat('pl-PL', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

export function commitTitle(message: string): string {
  return message.split(/\r?\n/, 1)[0]?.trim() || 'Bez opisu'
}

export function reviewerVote(vote: number): string {
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

export function changeLabel(changeType: string): string {
  return changeLabels[changeType.toLowerCase()] ?? changeType
}

const omissionLabels: Record<string, string> = {
  lockFile: 'plik zależności', snapshot: 'snapshot', generated: 'plik wygenerowany',
  minified: 'plik zminifikowany', buildOutput: 'wynik budowania', binary: 'plik binarny',
  sourceTooLarge: 'limit istniejącego diffu', fileCharacterLimit: 'limit na plik',
  pullRequestCharacterLimit: 'limit na PR',
}

export function omissionLabel(reason: string): string {
  return omissionLabels[reason] ?? reason
}

export const threadStatusLabels: Record<string, string> = {
  active: 'aktywny', fixed: 'naprawiony', wontFix: 'nie naprawimy',
  closed: 'zamknięty', pending: 'oczekuje', byDesign: 'zgodne z projektem', unknown: '',
}

// Polish needs three forms: 1 użycie, 2–4 użycia (but 12–14 użyć), 5+ użyć.
function usageNoun(count: number): string {
  if (count === 1) return 'użycie'
  const tens = count % 100
  const units = count % 10
  return units >= 2 && units <= 4 && (tens < 12 || tens > 14) ? 'użycia' : 'użyć'
}

// tests/, __tests__/, Billing.Tests/, UnitTests/, App.test.ts, x.spec.js, InvoiceTests.cs —
// but not Contest.cs or latest/.
const testPaths = [/(^|\/)(__)?tests?(__)?\/|[.\-_]tests?\/|\.(test|spec)\.[cm]?[jt]sx?$/i, /Tests?(\/|\.cs$)/]
const isTestPath = (path: string) => testPaths.some(pattern => pattern.test(path))

// The label over a declaration. "~" marks a count matched by name rather than resolved,
// and "tylko testy" is the case a reviewer is after: code that nothing but its tests calls.
export function usageLabel(usages: { path: string }[], mode: 'semantic' | 'name'): string {
  const count = `${mode === 'name' ? '~' : ''}${usages.length} ${usageNoun(usages.length)}`
  return usages.length > 0 && usages.every(usage => isTestPath(usage.path)) ? `${count} · tylko testy` : count
}

// A CSS prefers-reduced-motion block cannot override the JS `behavior` option, so the
// smooth-scroll call sites have to ask for themselves.
export function scrollBehavior(): ScrollBehavior {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth'
}
