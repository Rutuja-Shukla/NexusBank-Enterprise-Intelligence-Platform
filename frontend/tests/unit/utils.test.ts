import { describe, expect, it } from 'vitest'
import {
  cn,
  formatBytes,
  formatCurrency,
  formatDuration,
  formatMs,
  formatNumber,
  formatPercent,
  formatRelative,
  initials,
  safeJson,
  titleCase,
  toneForStatus,
} from '@/lib/utils'

describe('formatting helpers', () => {
  it('formats milliseconds with a second-scale fallback', () => {
    expect(formatMs(42.4)).toBe('42 ms')
    expect(formatMs(1500)).toBe('1.50 s')
    expect(formatMs(undefined)).toBe('—')
  })

  it('formats bytes into readable units', () => {
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(2048)).toBe('2.0 KB')
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB')
  })

  it('formats numbers and percentages for the console locale', () => {
    expect(formatNumber(12345)).toBe('12,345')
    expect(formatPercent(98.456, 2)).toBe('98.46%')
  })

  it('formats NZD currency with two decimals', () => {
    expect(formatCurrency(1234.5)).toContain('1,234.50')
  })

  it('formats uptime durations', () => {
    expect(formatDuration(90)).toBe('1m')
    expect(formatDuration(3700)).toBe('1h 1m')
    expect(formatDuration(90_000)).toBe('1d 1h')
  })

  it('describes recent timestamps relative to now', () => {
    const twoMinutesAgo = new Date(Date.now() - 120_000).toISOString()
    expect(formatRelative(twoMinutesAgo)).toBe('2m ago')
    expect(formatRelative(null)).toBe('—')
  })
})

describe('presentation helpers', () => {
  it('maps statuses to semantic tones', () => {
    expect(toneForStatus('Completed')).toBe('success')
    expect(toneForStatus('Failed')).toBe('danger')
    expect(toneForStatus('Degraded')).toBe('warning')
  })

  it('pretty prints JSON and preserves invalid input', () => {
    expect(safeJson('{"a":1}')).toBe('{\n  "a": 1\n}')
    expect(safeJson('not-json')).toBe('not-json')
  })

  it('builds initials and title case labels', () => {
    expect(initials('Daniel Kaur')).toBe('DK')
    expect(titleCase('operations engineer')).toBe('Operations Engineer')
  })

  it('merges conflicting tailwind classes deterministically', () => {
    expect(cn('px-2 py-1', 'px-4')).toBe('py-1 px-4')
  })
})
