import { Area, CartesianGrid, ComposedChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { TrafficPoint } from '@/types'
import { ChartLegend, ChartTooltip, axisProps, chartColors, gridProps } from './ChartKit'

export function TrafficChart({ data, height = 260, showErrors = true }: { data: TrafficPoint[]; height?: number; showErrors?: boolean }) {
  return (
    <div className="space-y-2">
      <ResponsiveContainer width="100%" height={height}>
        <ComposedChart data={data} margin={{ top: 8, right: 4, bottom: 0, left: -18 }}>
          <defs>
            <linearGradient id="trafficFill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={chartColors.brand} stopOpacity={0.28} />
              <stop offset="100%" stopColor={chartColors.brand} stopOpacity={0.01} />
            </linearGradient>
          </defs>
          <CartesianGrid {...gridProps} />
          <XAxis dataKey="bucket" {...axisProps} interval="preserveStartEnd" minTickGap={28} />
          <YAxis {...axisProps} width={44} tickFormatter={(value: number) => (value >= 1000 ? `${Math.round(value / 1000)}k` : String(value))} />
          <Tooltip content={<ChartTooltip />} cursor={{ stroke: 'var(--line-strong)', strokeWidth: 1 }} />
          <Area
            type="monotone"
            dataKey="requests"
            name="Requests"
            stroke={chartColors.brand}
            strokeWidth={1.6}
            fill="url(#trafficFill)"
            activeDot={{ r: 3, strokeWidth: 0, fill: chartColors.brand }}
            isAnimationActive={false}
          />
          {showErrors ? (
            <Line type="monotone" dataKey="errors" name="5xx errors" stroke={chartColors.danger} strokeWidth={1.2} dot={false} isAnimationActive={false} />
          ) : null}
        </ComposedChart>
      </ResponsiveContainer>
      <ChartLegend
        items={[{ label: 'Requests', color: chartColors.brand }, ...(showErrors ? [{ label: 'Server errors', color: chartColors.danger }] : [])]}
      />
    </div>
  )
}
