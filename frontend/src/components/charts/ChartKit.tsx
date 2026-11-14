import type { TooltipProps } from 'recharts'
import { cn, formatNumber } from '@/lib/utils'

export const chartColors = {
  brand: '#F5C400',
  brandSoft: 'rgba(245, 196, 0, 0.18)',
  danger: '#F0736F',
  info: '#63A6E8',
  success: '#34C98A',
  grid: 'var(--line)',
  axis: 'var(--muted)',
}

export const axisProps = {
  tick: { fill: 'var(--muted)', fontSize: 10.5, fontFamily: 'JetBrains Mono Variable, monospace' },
  axisLine: false as const,
  tickLine: false as const,
}

export const gridProps = {
  stroke: 'var(--line)',
  strokeDasharray: '3 3',
  vertical: false as const,
}

export function ChartTooltip({ active, payload, label, suffix }: TooltipProps<number, string> & { suffix?: string }) {
  if (!active || !payload || payload.length === 0) return null

  return (
    <div className="rounded-lg border border-line bg-surface px-2.5 py-2 shadow-lifted">
      <p className="font-mono text-[10px] uppercase tracking-[0.12em] text-muted">{label}</p>
      <div className="mt-1 space-y-0.5">
        {payload.map((entry) => (
          <div key={`${entry.dataKey}`} className="flex items-center justify-between gap-4 text-[11.5px]">
            <span className="flex items-center gap-1.5 text-ink-2">
              <span className="size-1.5 rounded-full" style={{ background: entry.color }} />
              {entry.name}
            </span>
            <span className="num font-medium text-ink">
              {typeof entry.value === 'number' ? formatNumber(entry.value, { compact: entry.value > 999 }) : String(entry.value)}
              {suffix ?? ''}
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

export function ChartLegend({ items, className }: { items: Array<{ label: string; color: string }>; className?: string }) {
  return (
    <div className={cn('flex flex-wrap items-center gap-3', className)}>
      {items.map((item) => (
        <span key={item.label} className="flex items-center gap-1.5 font-mono text-[10px] uppercase tracking-[0.1em] text-muted">
          <span className="size-1.5 rounded-full" style={{ background: item.color }} />
          {item.label}
        </span>
      ))}
    </div>
  )
}
