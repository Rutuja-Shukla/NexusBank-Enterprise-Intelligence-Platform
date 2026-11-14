import type { ReactNode } from 'react'
import { ArrowDownRight, ArrowUpRight, Minus } from 'lucide-react'
import { cn } from '@/lib/utils'

interface StatCardProps {
  label: string
  value: ReactNode
  hint?: ReactNode
  deltaPct?: number
  invertDelta?: boolean
  icon?: ReactNode
  featured?: boolean
  tone?: 'neutral' | 'brand' | 'danger' | 'warning' | 'success'
  className?: string
  children?: ReactNode
}

const toneBorder: Record<string, string> = {
  brand: 'border-brand/30',
  danger: 'border-danger/30',
  warning: 'border-warning/30',
  success: 'border-success/30',
  neutral: 'border-line',
}

export function StatCard({ label, value, hint, deltaPct, invertDelta = false, icon, featured, tone = 'neutral', className, children }: StatCardProps) {
  const showDelta = typeof deltaPct === 'number' && Number.isFinite(deltaPct)
  const improving = showDelta ? (invertDelta ? deltaPct! < 0 : deltaPct! > 0) : false
  const flat = showDelta && Math.abs(deltaPct!) < 0.05

  return (
    <div className={cn('panel relative overflow-hidden p-4 transition-colors hover:border-line-strong', toneBorder[tone], className)}>
      {featured ? <span className="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-brand to-transparent" /> : null}
      <div className="flex items-start justify-between gap-2">
        <p className="mono-label">{label}</p>
        {icon ? <span className="text-muted [&_svg]:size-4">{icon}</span> : null}
      </div>
      <div className="mt-2 flex items-baseline gap-1.5">
        <span className="num text-[26px] font-semibold leading-none text-ink">{value}</span>
      </div>
      <div className="mt-2 flex min-h-4 flex-wrap items-center gap-2 text-[11.5px]">
        {showDelta ? (
          <span
            className={cn(
              'inline-flex items-center gap-0.5 rounded px-1 py-0.5 font-mono text-[10.5px]',
              flat ? 'bg-surface-2 text-muted' : improving ? 'bg-success-soft text-success' : 'bg-danger-soft text-danger',
            )}
          >
            {flat ? <Minus className="size-3" /> : deltaPct! > 0 ? <ArrowUpRight className="size-3" /> : <ArrowDownRight className="size-3" />}
            {Math.abs(deltaPct!).toFixed(1)}%
          </span>
        ) : null}
        {hint ? <span className="text-muted">{hint}</span> : null}
      </div>
      {children}
    </div>
  )
}
