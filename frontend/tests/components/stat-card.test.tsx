import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StatCard } from '@/components/data/StatCard'

describe('StatCard', () => {
  it('renders the label, value and supporting hint', () => {
    render(<StatCard label="API requests" value="42,318" hint="last 24 hours" />)

    expect(screen.getByText('API requests')).toBeInTheDocument()
    expect(screen.getByText('42,318')).toBeInTheDocument()
    expect(screen.getByText('last 24 hours')).toBeInTheDocument()
  })

  it('only shows a delta when a finite value is supplied', () => {
    const { rerender } = render(<StatCard label="Latency" value="240 ms" />)
    expect(screen.queryByText(/%$/)).not.toBeInTheDocument()

    rerender(<StatCard label="Latency" value="240 ms" deltaPct={-12.4} invertDelta />)
    expect(screen.getByText('12.4%')).toBeInTheDocument()
  })

  it('describes a flat trend instead of pretending it improved', () => {
    render(<StatCard label="Integrations" value="8/8" deltaPct={0.01} />)
    expect(screen.getByText('0.0%')).toBeInTheDocument()
  })
})
