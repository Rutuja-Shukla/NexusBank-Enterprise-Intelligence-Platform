import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

const currencyFormatters = new Map<string, Intl.NumberFormat>()

export function formatCurrency(value: number, currency = 'NZD', options?: { compact?: boolean; decimals?: number }) {
  const compact = options?.compact ?? false
  const key = `${currency}-${compact}-${options?.decimals ?? 'auto'}`
  if (!currencyFormatters.has(key)) {
    currencyFormatters.set(
      key,
      new Intl.NumberFormat('en-NZ', {
        style: 'currency',
        currency,
        notation: compact ? 'compact' : 'standard',
        maximumFractionDigits: options?.decimals ?? (compact ? 1 : 2),
        minimumFractionDigits: compact ? 0 : (options?.decimals ?? 2),
      }),
    )
  }
  return currencyFormatters.get(key)!.format(value)
}

export function formatNumber(value: number, options?: { compact?: boolean; decimals?: number }) {
  return new Intl.NumberFormat('en-NZ', {
    notation: options?.compact ? 'compact' : 'standard',
    maximumFractionDigits: options?.decimals ?? (options?.compact ? 1 : 0),
  }).format(value)
}

export function formatPercent(value: number, decimals = 1) {
  return `${value.toFixed(decimals)}%`
}

export function formatMs(value: number | null | undefined) {
  if (value === null || value === undefined) return '—'
  if (value >= 1000) return `${(value / 1000).toFixed(2)} s`
  return `${Math.round(value)} ms`
}

export function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function formatDateTime(value: string | number | null | undefined) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('en-NZ', {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

export function formatDate(value: string | null | undefined) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('en-NZ', { day: '2-digit', month: 'short', year: 'numeric' }).format(date)
}

export function formatTime(value: string | null | undefined) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('en-NZ', { hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(date)
}

export function formatRelative(value: string | null | undefined) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  const seconds = Math.round((Date.now() - date.getTime()) / 1000)
  if (seconds < 0) return 'just now'
  if (seconds < 60) return `${seconds}s ago`
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.round(hours / 24)
  if (days < 7) return `${days}d ago`
  return formatDate(value)
}

export function formatDuration(seconds: number) {
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${minutes}m`
  return `${minutes}m`
}

export type Tone = 'neutral' | 'brand' | 'success' | 'warning' | 'danger' | 'info'

const statusTones: Record<string, Tone> = {
  Healthy: 'success',
  healthy: 'success',
  Completed: 'success',
  Consumed: 'success',
  Stable: 'success',
  Resolved: 'neutral',
  Succeeded: 'success',
  Success: 'success',
  Degraded: 'warning',
  degraded: 'warning',
  Processing: 'info',
  Retried: 'info',
  Rebalancing: 'warning',
  Investigating: 'warning',
  Mitigated: 'info',
  Pending: 'warning',
  Open: 'warning',
  Down: 'danger',
  down: 'danger',
  Failed: 'danger',
  Failure: 'danger',
  Denied: 'danger',
  DeadLettered: 'danger',
  Dead: 'danger',
  Sev1: 'danger',
  Sev2: 'warning',
  UnderMaintenance: 'neutral',
  Maintenance: 'neutral',
  Preventive: 'neutral',
  UnderReview: 'info',
  Unknown: 'neutral',
  unknown: 'neutral',
  Produced: 'neutral',
  Acknowledged: 'info',
  Initiated: 'neutral',
  Validating: 'info',
  Running: 'info',
  Reversed: 'warning',
  Skipped: 'neutral',
  Active: 'success',
  inactive: 'neutral',
}

export function toneForStatus(status: string | null | undefined): Tone {
  if (!status) return 'neutral'
  return statusTones[status] ?? 'neutral'
}

export function titleCase(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[-_]/g, ' ')
    .replace(/\b\w/g, (c) => c.toUpperCase())
}

export function initials(name: string) {
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}

export function safeJson(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

export function riskTone(score: number): Tone {
  if (score >= 65) return 'danger'
  if (score >= 35) return 'warning'
  return 'success'
}

export const categoryLabels: Record<string, string> = {
  Core: 'Core banking',
  Payments: 'Payments',
  Cards: 'Cards',
  Risk: 'Risk & fraud',
  Data: 'Data',
  External: 'External',
}
