// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { renderDescription } from '../src/description'

function render(text: string, workItems: { id: string; url: string }[] = []) {
  const host = document.createElement('div')
  host.innerHTML = renderDescription(text, workItems)
  return host
}

describe('render opisu PR', () => {
  it('turns Azure DevOps markdown into real elements', () => {
    const host = render('## Tytuł\n\n- pierwszy\n- drugi\n\nTekst z [linkiem](https://example.com).')

    expect(host.querySelector('h2')?.textContent).toBe('Tytuł')
    expect(host.querySelectorAll('li')).toHaveLength(2)
    expect(host.querySelector('a')?.getAttribute('href')).toBe('https://example.com')
  })

  it('keeps a single newline as a line break, the way Azure DevOps does', () => {
    expect(render('pierwsza\ndruga').querySelectorAll('br')).toHaveLength(1)
  })

  it('renders fenced code without treating it as markup', () => {
    const host = render('```\nvar x = "<b>nie tag</b>";\n```')

    expect(host.querySelector('pre code')?.textContent).toContain('<b>nie tag</b>')
    expect(host.querySelector('pre b')).toBeNull()
  })

  // The description is written by other people. This is the one place where their text
  // becomes HTML, so these assertions are the security boundary, not a nicety.
  it('does not let raw HTML through', () => {
    const host = render('<script>window.pwned = 1</script>\n\n<img src=x onerror="window.pwned = 1">')

    expect(host.querySelector('script')).toBeNull()
    expect(host.querySelector('img')).toBeNull()
    // The markup survives as visible text, never as an element or an event attribute.
    const attributes = [...host.querySelectorAll('*')].flatMap(element => [...element.attributes])
    expect(attributes.filter(attribute => attribute.name.startsWith('on'))).toHaveLength(0)
    expect(host.innerHTML).toContain('&lt;img')
    expect((window as unknown as { pwned?: number }).pwned).toBeUndefined()
  })

  it('refuses a javascript: link', () => {
    const host = render('[klik](javascript:window.pwned = 1)')

    expect(host.querySelector('a')?.getAttribute('href') ?? '').not.toContain('javascript:')
  })

  it('opens every link in a new tab without handing over the opener', () => {
    const link = render('[a](https://example.com)').querySelector('a')!

    expect(link.getAttribute('target')).toBe('_blank')
    expect(link.getAttribute('rel')).toBe('noopener noreferrer')
  })

  it('replaces an attachment image with a link, because it would render broken', () => {
    const host = render('![diagram](https://dev.azure.com/org/_apis/wit/attachments/abc)')

    expect(host.querySelector('img')).toBeNull()
    const link = host.querySelector('a.description-attachment')!
    expect(link.getAttribute('href')).toBe('https://dev.azure.com/org/_apis/wit/attachments/abc')
    expect(link.textContent).toContain('diagram')
  })

  it('links a work item mention only when the pull request really has it', () => {
    const host = render('Naprawia #1234 oraz #9999, ale nie `#1234` w kodzie.',
      [{ id: '1234', url: 'https://dev.azure.com/org/_workitems/edit/1234' }])

    const links = [...host.querySelectorAll('a')]
    expect(links).toHaveLength(1)
    expect(links[0]!.textContent).toBe('#1234')
    expect(links[0]!.getAttribute('href')).toBe('https://dev.azure.com/org/_workitems/edit/1234')
    expect(host.querySelector('code')?.textContent).toBe('#1234')
    expect(host.textContent).toContain('#9999')
  })
})
