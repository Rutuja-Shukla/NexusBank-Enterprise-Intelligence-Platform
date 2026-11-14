import { useMemo, useState } from 'react'
import { Landmark, CreditCard, ShieldCheck, Database, Globe2, Wallet, Network } from 'lucide-react'
import { StatusPill } from '@/components/ui/badge'
import { useReducedMotion } from '@/hooks/useReducedMotion'
import { categoryLabels, cn, formatMs, formatNumber, formatRelative, toneForStatus } from '@/lib/utils'
import type { IntegrationCategory, TopologyEdge, TopologyNode } from '@/types'

const categoryIcons: Record<IntegrationCategory, typeof Landmark> = {
  Core: Landmark,
  Payments: Wallet,
  Cards: CreditCard,
  Risk: ShieldCheck,
  Data: Database,
  External: Globe2,
}

interface TopologyProps {
  nodes: TopologyNode[]
  edges: TopologyEdge[]
  selectedId?: string | null
  onSelect?: (node: TopologyNode) => void
  height?: number
  compact?: boolean
}

export function IntegrationTopology({ nodes, edges, selectedId, onSelect, height = 460, compact = false }: TopologyProps) {
  const reducedMotion = useReducedMotion()
  const [hoveredId, setHoveredId] = useState<string | null>(null)
  const byId = useMemo(() => new Map(nodes.map((node) => [node.id, node])), [nodes])
  const activeId = hoveredId ?? selectedId ?? null

  const relatedIds = useMemo(() => {
    if (!activeId) return new Set<string>()
    const set = new Set<string>([activeId])
    for (const edge of edges) {
      if (edge.source === activeId) set.add(edge.target)
      if (edge.target === activeId) set.add(edge.source)
    }
    return set
  }, [activeId, edges])

  return (
    <div className="relative overflow-hidden rounded-xl border border-line bg-surface" style={{ minHeight: height }}>
      <div className="pointer-events-none absolute inset-0 grid-canvas opacity-70" aria-hidden />
      <div className="pointer-events-none absolute inset-x-0 top-0 h-24 bg-gradient-to-b from-surface to-transparent" aria-hidden />

      <svg className="absolute inset-0 h-full w-full" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden>
        {edges.map((edge) => {
          const source = byId.get(edge.source)
          const target = byId.get(edge.target)
          if (!source || !target) return null

          const isActive = activeId ? edge.source === activeId || edge.target === activeId : false
          const dimmed = activeId ? !isActive : false

          // A gentle curve keeps reciprocal edges visually distinct.
          const midX = (source.x + target.x) / 2
          const midY = (source.y + target.y) / 2 - 6
          const path = `M ${source.x} ${source.y} Q ${midX} ${midY} ${target.x} ${target.y}`

          return (
            <g key={edge.id} className="transition-opacity" opacity={dimmed ? 0.16 : isActive ? 1 : 0.55}>
              <path
                d={path}
                fill="none"
                stroke={edge.degraded ? 'var(--danger)' : isActive ? 'var(--brand)' : 'var(--line-strong)'}
                strokeWidth={edge.isCritical ? 1.1 : 0.7}
                vectorEffect="non-scaling-stroke"
                strokeDasharray={edge.degraded ? '4 3' : undefined}
              />
              {!reducedMotion ? (
                <circle r={edge.isCritical ? 0.62 : 0.46} fill={edge.degraded ? 'var(--danger)' : 'var(--brand)'}>
                  <animateMotion dur={`${edge.isCritical ? 5 : 7}s`} repeatCount="indefinite" path={path} begin={`${Math.random() * 3}s`} />
                </circle>
              ) : null}
            </g>
          )
        })}
      </svg>

      {nodes.map((node) => {
        const Icon = categoryIcons[node.category] ?? Network
        const isSelected = selectedId === node.id
        const isRelated = relatedIds.has(node.id)
        const dimmed = activeId ? !isRelated : false

        return (
          <button
            key={node.id}
            type="button"
            onClick={() => onSelect?.(node)}
            onMouseEnter={() => setHoveredId(node.id)}
            onMouseLeave={() => setHoveredId(null)}
            onFocus={() => setHoveredId(node.id)}
            onBlur={() => setHoveredId(null)}
            aria-pressed={isSelected}
            aria-label={`${node.name} — ${node.status}`}
            className={cn(
              'group absolute z-10 -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-surface/95 px-2.5 py-2 text-left shadow-panel backdrop-blur transition-all duration-200',
              'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand/70',
              isSelected ? 'border-brand ring-1 ring-brand/35 shadow-brand-glow' : 'border-line hover:border-line-strong',
              dimmed && 'opacity-45',
              node.status === 'Down' && 'border-danger/40',
              compact ? 'w-[124px]' : 'w-[148px]',
            )}
            style={{ left: `${node.x}%`, top: `${node.y}%` }}
          >
            <span className="flex items-center gap-1.5">
              <Icon className={cn('size-3.5', toneForStatus(node.status) === 'danger' ? 'text-danger' : 'text-brand-text')} />
              <span className="truncate font-mono text-[9px] uppercase tracking-[0.12em] text-muted">{categoryLabels[node.category] ?? node.category}</span>
              <span
                className={cn(
                  'ml-auto size-1.5 shrink-0 rounded-full',
                  node.status === 'Healthy' ? 'bg-success' : node.status === 'Degraded' ? 'bg-warning' : node.status === 'Down' ? 'bg-danger' : 'bg-muted',
                  node.status !== 'Healthy' && !reducedMotion && 'animate-pulse-dot',
                )}
              />
            </span>
            <span className="mt-1 block truncate text-[12px] font-medium text-ink">{node.name}</span>
            {!compact ? (
              <span className="mt-1 flex items-center justify-between font-mono text-[9.5px] text-muted">
                <span>{formatMs(node.avgLatencyMs)}</span>
                <span>{formatNumber(node.requestCount24h, { compact: true })} req</span>
              </span>
            ) : null}
            {node.isSimulated ? (
              <span className="mt-1 block font-mono text-[8.5px] uppercase tracking-[0.1em] text-muted/80">fixture</span>
            ) : null}
          </button>
        )
      })}

      <div className="pointer-events-none absolute bottom-3 left-3 flex flex-wrap items-center gap-2">
        <span className="rounded border border-line bg-surface/90 px-1.5 py-0.5 font-mono text-[9px] uppercase tracking-[0.12em] text-muted">
          {nodes.length} services · {edges.length} connections
        </span>
      </div>
      <div className="pointer-events-none absolute bottom-3 right-3 hidden items-center gap-3 sm:flex">
        <LegendDot color="bg-success" label="Healthy" />
        <LegendDot color="bg-warning" label="Degraded" />
        <LegendDot color="bg-danger" label="Down" />
      </div>
    </div>
  )
}

function LegendDot({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5 font-mono text-[9px] uppercase tracking-[0.1em] text-muted">
      <span className={cn('size-1.5 rounded-full', color)} />
      {label}
    </span>
  )
}

/** Detail rail shown beside the map when a node is selected. */
export function TopologyNodeDetail({ node, edges, nodes, onOpen }: { node: TopologyNode; edges: TopologyEdge[]; nodes: TopologyNode[]; onOpen?: () => void }) {
  const connected = edges
    .filter((edge) => edge.source === node.id || edge.target === node.id)
    .map((edge) => nodes.find((candidate) => candidate.id === (edge.source === node.id ? edge.target : edge.source)))
    .filter((candidate): candidate is TopologyNode => Boolean(candidate))

  return (
    <div className="space-y-4 animate-fade-up">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="mono-label">{categoryLabels[node.category] ?? node.category}</p>
          <h3 className="mt-1 text-[15px] font-semibold leading-snug text-ink">{node.name}</h3>
          <p className="mt-0.5 font-mono text-[10.5px] text-muted">{node.baseUrl}</p>
        </div>
        <StatusPill status={node.status} pulse={node.status === 'Down'} />
      </div>

      <dl className="grid grid-cols-2 gap-2">
        <Metric label="Avg latency" value={formatMs(node.avgLatencyMs)} />
        <Metric label="Failure rate" value={`${node.failureRatePct.toFixed(2)}%`} />
        <Metric label="Requests (24h)" value={formatNumber(node.requestCount24h, { compact: true })} />
        <Metric label="Version" value={node.version} />
      </dl>

      <div>
        <p className="mono-label mb-1.5">Owner</p>
        <p className="text-[12.5px] text-ink-2">{node.ownerTeam}</p>
      </div>

      <div>
        <p className="mono-label mb-1.5">Last contact</p>
        <p className="text-[12.5px] text-ink-2">
          Success {formatRelative(node.lastSuccessAt)} · Failure {formatRelative(node.lastFailureAt)}
        </p>
      </div>

      <div>
        <p className="mono-label mb-1.5">Connected services ({connected.length})</p>
        <ul className="space-y-1">
          {connected.map((candidate) => (
            <li key={candidate.id} className="flex items-center justify-between gap-2 rounded-md border border-line bg-surface-2/60 px-2 py-1.5">
              <span className="truncate text-[12px] text-ink-2">{candidate.name}</span>
              <span
                className={cn(
                  'size-1.5 shrink-0 rounded-full',
                  candidate.status === 'Healthy' ? 'bg-success' : candidate.status === 'Degraded' ? 'bg-warning' : candidate.status === 'Down' ? 'bg-danger' : 'bg-muted',
                )}
              />
            </li>
          ))}
        </ul>
      </div>

      {node.isSimulated ? (
        <p className="rounded-md border border-warning/25 bg-warning-soft px-2.5 py-2 text-[11px] leading-relaxed text-warning">
          This service is a synthetic fixture. Health telemetry is produced by the deterministic simulator, not a live upstream.
        </p>
      ) : null}

      {onOpen ? (
        <button
          type="button"
          onClick={onOpen}
          className="w-full rounded-lg border border-line bg-surface-2 px-3 py-2 text-[12.5px] font-medium text-ink transition-colors hover:border-line-strong"
        >
          Open integration record
        </button>
      ) : null}
    </div>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-line bg-surface-2/50 px-2.5 py-2">
      <dt className="font-mono text-[9px] uppercase tracking-[0.12em] text-muted">{label}</dt>
      <dd className="num mt-0.5 text-[13.5px] font-medium text-ink">{value}</dd>
    </div>
  )
}
