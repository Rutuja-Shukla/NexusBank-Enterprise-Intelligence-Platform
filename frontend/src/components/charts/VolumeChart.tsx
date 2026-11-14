import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { VolumePoint } from '@/types'
import { ChartTooltip, axisProps, chartColors, gridProps } from './ChartKit'
import { formatCurrency } from '@/lib/utils'

export function VolumeChart({ data, height = 220 }: { data: VolumePoint[]; height?: number }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={data} margin={{ top: 6, right: 4, bottom: 0, left: -18 }} barCategoryGap="28%">
        <CartesianGrid {...gridProps} />
        <XAxis dataKey="bucket" {...axisProps} interval="preserveStartEnd" minTickGap={20} />
        <YAxis
          {...axisProps}
          width={48}
          tickFormatter={(value: number) => (value >= 1000 ? `$${Math.round(value / 1000)}k` : `$${value}`)}
        />
        <Tooltip
          content={<ChartTooltip />}
          cursor={{ fill: 'var(--surface-2)' }}
          formatter={(value: number) => formatCurrency(value, 'NZD', { compact: true })}
        />
        <Bar dataKey="volume" name="Volume" fill={chartColors.brand} radius={[3, 3, 0, 0]} isAnimationActive={false} maxBarSize={26} />
      </BarChart>
    </ResponsiveContainer>
  )
}
