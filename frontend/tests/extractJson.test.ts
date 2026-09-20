// The one piece of logic in the AI adapter. "Return only JSON" is a request to a language
// model, and the CLI can print a notice of its own on the same stream — this is what stands
// between that and a 502 the user cannot act on.
// @ts-expect-error — a plain .mjs script outside the frontend, deliberately untyped.
import { extractJson } from '../../scripts/extract-json.mjs'
import { describe, expect, it } from 'vitest'

const payload = '{"schemaVersion":2,"sentences":["Zmieniono przepływ faktur."]}'

describe('extractJson', () => {
  it.each([
    ['bare JSON', payload],
    ['a ```json fence', '```json\n' + payload + '\n```'],
    ['a fence with no language', '```\n' + payload + '\n```'],
    ['a sentence in front', 'Oto podsumowanie:\n' + payload],
    ['a sentence after', payload + '\n\nDaj znać, jeśli mam coś rozwinąć.'],
    ['a CLI notice in front', 'Update available: 1.2.3 -> 1.3.0\n' + payload],
  ])('takes the object out of %s', (_name, input) => {
    expect(JSON.parse(extractJson(input))).toEqual(JSON.parse(payload))
  })

  it('counts braces only outside string literals', () => {
    const tricky = '{"sentences":["kod ma { klamrę i \\" cudzysłów }"],"schemaVersion":2}'
    expect(extractJson('banner\n' + tricky + '\ntail')).toBe(tricky)
  })

  it('keeps nested objects whole', () => {
    expect(extractJson('x {"a":{"b":{"c":1}},"d":2} y')).toBe('{"a":{"b":{"c":1}},"d":2}')
  })

  // Anything it cannot make sense of goes on unchanged, so the backend logs what really
  // arrived instead of a truncated guess.
  it.each([
    ['no object at all', 'Nie mogę tego zrobić.'],
    ['an unbalanced object', '{"schemaVersion":2,"sentences":['],
  ])('hands back %s untouched', (_name, input) => {
    expect(extractJson(input)).toBe(input)
  })
})
