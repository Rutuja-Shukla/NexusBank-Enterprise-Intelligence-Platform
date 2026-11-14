import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { LatencyDistribution } from '@/types'
import { ChartTooltip, axisProps, chartColors } from './ChartKit'

const shades = ['#F5C400', '#E9BB0B', '#D9A400', '#C08F16', '#E59B18', '#F0736F']

export function LatencyChart({ data, height = 200 }: { data: LatencyDistribution[]; height?: number }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={data} layout="vertical" margin={{ top: 4, right: 16, bottom: 0, left: 4 }}>
        <XAxis type="number" {...axisProps} hide />
        <YAxis type="category" dataKey="bucket" {...axisProps} width={84} />
        <Tooltip content={<ChartTooltip />} cursor={{ fill: 'var(--surface-2)' }} />
        <Bar dataKey="count" name="Requests" radius={[0, 3, 3, 0]} isAnimationActive={false} maxBarSize={16}>
          {data.map((entry, index) => (
            <Cell key={entry.bucket} fill={shades[Math.min(index, shades.length - 1)]} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

export function Sparkline({ data, color = chartColors.brand }: { data: number[]; color?: string }) {
  const points = data.length > 1 ? data : [0, 0]
  const max = Math.max(...points, 1)
  const min = Math.min(...points, 0)
  const range = max - min || 1
  const path = points
    .map((value, index) => {
      const x = (index / (points.length - 1)) * 100
      const y = 26 - ((value - min) / range) * 24
      return `${index === 0 ? 'M' : 'L'}${x.toFixed(2)},${y.toFixed(2)}`
    })
    .join(' ')

  return (
    <svg viewBox="0 0 100 28" preserveAspectRatio="none" className="h-7 w-full" aria-hidden>
      <path d={`${path} L100,28 L0,28 Z`} fill={color} opacity={0.12} />
      <path d={path} fill="none" stroke={color} strokeWidth={1.4} vectorEffect="non-scaling-stroke" strokeLinecap="round" />
    </svg>
  )
}
