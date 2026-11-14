import type { ReactNode } from 'react'
import { cn, titleCase, toneForStatus, type Tone } from '@/lib/utils'

const toneClasses: Record<Tone, string> = {
  neutral: 'border-line bg-surface-2 text-ink-2',
  brand: 'border-transparent bg-brand-soft text-brand-text',
  success: 'border-transparent bg-success-soft text-success',
  warning: 'border-transparent bg-warning-soft text-warning',
  danger: 'border-transparent bg-danger-soft text-danger',
  info: 'border-transparent bg-info-soft text-info',
}

const dotClasses: Record<Tone, string> = {
  neutral: 'bg-muted',
  brand: 'bg-brand',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-danger',
  info: 'bg-info',
}

export function Badge({
  children,
  tone = 'neutral',
  className,
  dot = false,
}: {
  children: ReactNode
  tone?: Tone
  className?: string
  dot?: boolean
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-md border px-2 py-0.5 font-mono text-[10.5px] font-medium uppercase tracking-[0.09em]',
        toneClasses[tone],
        className,
      )}
    >
      {dot ? <span className={cn('size-1.5 rounded-full', dotClasses[tone])} aria-hidden /> : null}
      {children}
    </span>
  )
}

export function StatusPill({
  status,
  className,
  pulse = false,
}: {
  status: string | null | undefined
  className?: string
  pulse?: boolean
}) {
  const tone = toneForStatus(status)
  const label = status ? titleCase(status) : 'Unknown'
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-md border px-2 py-0.5 text-[11.5px] font-medium',
        toneClasses[tone],
        className,
      )}
    >
      <span
        className={cn('size-1.5 shrink-0 rounded-full', dotClasses[tone], pulse && 'animate-pulse-dot')}
        aria-hidden
      />
      {label}
    </span>
  )
}

export function HealthDot({ status, className }: { status: string; className?: string }) {
  const tone = toneForStatus(status)
  return <span className={cn('size-2 rounded-full', dotClasses[tone], className)} aria-label={status} />
}
