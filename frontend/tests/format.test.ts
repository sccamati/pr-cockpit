import { describe, expect, it } from 'vitest'
import { usageLabel } from '../src/format'

const at = (...paths: string[]) => paths.map(path => ({ path }))

describe('usageLabel', () => {
  it('uses the Polish plural forms', () => {
    expect(usageLabel(at(), 'semantic')).toBe('0 użyć')
    expect(usageLabel(at('/a.cs'), 'semantic')).toBe('1 użycie')
    expect(usageLabel(at('/a', '/b', '/c'), 'semantic')).toBe('3 użycia')
    expect(usageLabel(at(...Array<string>(5).fill('/a')), 'semantic')).toBe('5 użyć')
    expect(usageLabel(at(...Array<string>(12).fill('/a')), 'semantic')).toBe('12 użyć')
    expect(usageLabel(at(...Array<string>(22).fill('/a')), 'semantic')).toBe('22 użycia')
  })

  it('marks a count matched by name', () => {
    expect(usageLabel(at('/src/App.vue'), 'name')).toBe('~1 użycie')
  })

  it('says so when only tests use it', () => {
    expect(usageLabel(at('/tests/PRCockpit.Api.Tests/InvoiceTests.cs', '/frontend/tests/App.test.ts'), 'semantic'))
      .toBe('2 użycia · tylko testy')
    expect(usageLabel(at('/src/Billing.Tests/Send.cs', '/src/__tests__/x.js', '/src/UnitTests/A.cs'), 'semantic'))
      .toBe('3 użycia · tylko testy')
    expect(usageLabel(at('/tests/A.cs', '/src/B.cs'), 'semantic')).toBe('2 użycia')
    expect(usageLabel(at('/src/Contest.cs', '/src/latest/a.ts'), 'semantic')).toBe('2 użycia')
  })
})
