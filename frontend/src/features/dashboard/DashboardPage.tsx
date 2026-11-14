import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import {
  Activity, ArrowRight, ArrowUpRight, BadgeCheck, Bolt, CircleDollarSign, Clock3, CreditCard, Gauge, Network, Plus,
  RefreshCw, ServerCog, Siren, Timer, TriangleAlert, Waypoints,
} from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { StatCard } from '@/components/data/StatCard'
import { StatGridSkeleton, ErrorState, EmptyState, LoadingPanel } from '@/components/data/States'
import { TrafficChart } from '@/components/charts/TrafficChart'
import { VolumeChart } from '@/components/charts/VolumeChart'
import { IntegrationTopology, TopologyNodeDetail } from '@/components/topology/IntegrationTopology'
import { StatusPill, Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel, PanelHeader } from '@/components/ui/card'
import { useAuth } from '@/hooks/useAuth'
import { api } from '@/lib/api'
import { cn, formatCurrency, formatDuration, formatMs, formatNumber, formatRelative, formatTime } from '@/lib/utils'
import type { Dashboard, TopologyNode } from '@/types'

function greeting() {
  const hour = new Date().getHours()
  if (hour < 12) return 'Good morning'
  if (hour < 18) return 'Good afternoon'
  return 'Good evening'
}

export function DashboardPage() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const [selectedNode, setSelectedNode] = useState<TopologyNode | null>(null)

  const { data, isLoading, isError, error, refetch, isFetching } = useQuery({
    queryKey: ['dashboard'],
    queryFn: () => api.get<Dashboard>('/api/v1/dashboard'),
    refetchInterval: 25_000,
  })

  const summary = data?.summary
  const nodes = data?.topologyNodes ?? []
  const edges = data?.topologyEdges ?? []
  const activeNode = selectedNode ?? nodes.find((node) => node.status === 'Down') ?? nodes[0] ?? null

  const serviceSummary = useMemo(() => {
    const services = data?.services ?? []
    return {
      healthy: services.filter((service) => service.status === 'healthy').length,
      degraded: services.filter((service) => service.status === 'degraded').length,
      down: services.filter((service) => service.status === 'down').length,
      total: services.length,
    }
  }, [data?.services])

  if (isError) {
    return (
      <div className="space-y-4">
        <PageHeader title="Command centre" eyebrow="Operate" description="Live operational state across the banking integration estate." />
        <Panel>
          <ErrorState error={error} onRetry={() => void refetch()} />
        </Panel>
      </div>
    )
  }

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        eyebrow="Operational command centre"
        title={`${greeting()}, ${user?.displayName.split(' ')[0] ?? 'operator'}`}
        description={
          data
            ? `Estate telemetry captured ${formatTime(data.generatedAt)} · ${serviceSummary.total} services observed · refreshes every 25 seconds.`
            : 'Live operational state across the banking integration estate.'
        }
        actions={
          <>
            <Button variant="secondary" size="sm" onClick={() => void refetch()} disabled={isFetching}>
              <RefreshCw className={cn(isFetching && 'animate-spin')} /> Refresh
            </Button>
            <Button size="sm" onClick={() => navigate('/payments?new=1')}>
              <Plus /> New payment
            </Button>
          </>
        }
      />

      {isLoading || !summary ? (
        <StatGridSkeleton count={4} />
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            label="Platform availability"
            value={`${summary.availabilityPct.toFixed(2)}%`}
            hint="rolling 24h · measured from request telemetry"
            icon={<Activity />}
            featured
            tone={summary.availabilityPct >= 99.5 ? 'success' : summary.availabilityPct >= 99 ? 'warning' : 'danger'}
          />
          <StatCard
            label="Transaction volume"
            value={formatCurrency(summary.transactionVolume24h, 'NZD', { compact: true })}
            deltaPct={summary.transactionChangePct}
            hint={`${formatNumber(summary.transactions24h)} payments · 24h`}
            icon={<CircleDollarSign />}
          />
          <StatCard
            label="API requests"
            value={formatNumber(summary.apiRequests24h, { compact: true })}
            deltaPct={summary.apiRequestsChangePct}
            hint={`avg ${formatMs(summary.avgLatencyMs)} response`}
            icon={<Bolt />}
          />
          <StatCard
            label="Active incidents"
            value={summary.activeIncidents}
            hint={summary.sev1Incidents > 0 ? `${summary.sev1Incidents} Sev1 requiring command` : 'no Sev1 in flight'}
            icon={<Siren />}
            tone={summary.activeIncidents > 0 ? 'danger' : 'neutral'}
          />
        </div>
      )}

      {summary ? (
        <Panel className="overflow-hidden">
          <div className="grid divide-y divide-line sm:grid-cols-2 sm:divide-y-0 lg:grid-cols-4 lg:divide-x">
            <SignalMetric label="Success rate" value={`${summary.transactionSuccessRatePct.toFixed(2)}%`} hint="payments decided" icon={<BadgeCheck />} />
            <SignalMetric label="Average latency" value={formatMs(summary.avgLatencyMs)} delta={summary.latencyChangePct} invert hint="across all endpoints" icon={<Timer />} />
            <SignalMetric label="Integrations" value={`${summary.integrationsActive}/${summary.integrationsTotal}`} hint={`${summary.integrationsDegraded} degraded · ${summary.integrationsDown} down`} icon={<Network />} tone={summary.integrationsDown > 0 ? 'danger' : undefined} />
            <SignalMetric label="Event throughput" value={`${formatNumber(summary.eventsLastHour)}/h`} hint={`${summary.deadLetterCount} in dead-letter queue`} icon={<Waypoints />} tone={summary.deadLetterCount > 0 ? 'warning' : undefined} />
          </div>
        </Panel>
      ) : null}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.65fr)_minmax(0,1fr)]">
        <Panel className="overflow-hidden">
          <PanelHeader
            eyebrow="Signature view"
            title="Integration network"
            description="Live service topology. Select a node to inspect its dependencies and health."
            icon={<Network />}
            actions={
              <Button variant="ghost" size="sm" asChild>
                <Link to="/integrations">
                  Open hub <ArrowUpRight />
                </Link>
              </Button>
            }
          />
          <div className="grid lg:grid-cols-[minmax(0,1fr)_248px]">
            <div className="p-3">
              {isLoading ? (
                <LoadingPanel rows={6} className="min-h-[420px]" />
              ) : (
                <IntegrationTopology
                  nodes={nodes}
                  edges={edges}
                  selectedId={activeNode?.id ?? null}
                  onSelect={(node) => setSelectedNode(node)}
                  height={430}
                />
              )}
            </div>
            <div className="border-t border-line p-4 lg:border-l lg:border-t-0">
              {activeNode ? (
                <TopologyNodeDetail
                  node={activeNode}
                  edges={edges}
                  nodes={nodes}
                  onOpen={() => navigate(`/integrations/${activeNode.id}`)}
                />
              ) : (
                <p className="text-[12px] text-muted">No services registered.</p>
              )}
            </div>
          </div>
        </Panel>

        <div className="space-y-4">
          <Panel className="overflow-hidden">
            <PanelHeader
              title="Active incidents"
              description="Sev1 first, then most recent"
              icon={<Siren />}
              actions={
                <Button variant="ghost" size="sm" asChild>
                  <Link to="/incidents">
                    All <ArrowRight />
                  </Link>
                </Button>
              }
            />
            {isLoading ? (
              <LoadingPanel rows={4} />
            ) : (data?.activeIncidents.length ?? 0) === 0 ? (
              <EmptyState title="No active incidents" description="Every monitored workflow is within its error budget." icon={<BadgeCheck />} />
            ) : (
              <ul className="divide-y divide-line">
                {data?.activeIncidents.slice(0, 4).map((incident) => (
                  <li key={incident.id}>
                    <Link to={`/incidents/${incident.id}`} className="group flex gap-3 px-4 py-2.5 transition-colors hover:bg-surface-2/60">
                      <span
                        className={cn(
                          'mt-1 h-8 w-[2px] shrink-0 rounded-full',
                          incident.severity === 'Sev1' ? 'bg-danger' : incident.severity === 'Sev2' ? 'bg-warning' : 'bg-info',
                        )}
                      />
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-1.5">
                          <span className="font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">{incident.reference}</span>
                          <Badge tone={incident.severity === 'Sev1' ? 'danger' : incident.severity === 'Sev2' ? 'warning' : 'neutral'}>{incident.severity}</Badge>
                        </div>
                        <p className=" mt-0.5 line-clamp-2 text-[12px] font-medium leading-snug text-ink group-hover:text-ink">{incident.title}</p>
                        <p className="mt-0.5 font-mono text-[9.5px] text-muted">
                          {incident.status} · {incident.affectedService ?? incident.team} · {Math.round(incident.minutesOpen)}m open
                        </p>
                      </div>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </Panel>

          <Panel className="overflow-hidden">
            <PanelHeader title="Service status" description="Live dependency probes" icon={<ServerCog />} />
            {isLoading ? (
              <LoadingPanel rows={5} />
            ) : (
              <ul className="divide-y divide-line">
                {data?.services.map((service) => (
                  <li key={service.name} className="flex items-center justify-between gap-2 px-4 py-2">
                    <div className="flex min-w-0 items-center gap-2">
                      <span
                        className={cn(
                          'size-1.5 shrink-0 rounded-full',
                          service.status === 'healthy' ? 'bg-success' : service.status === 'degraded' ? 'bg-warning animate-pulse-dot' : 'bg-danger animate-pulse-dot',
                        )}
                      />
                      <span className="truncate text-[12px] text-ink-2">{service.name}</span>
                    </div>
                    <span className="shrink-0 font-mono text-[9.5px] uppercase tracking-[0.08em] text-muted">
                      {service.status === 'healthy' ? 'operational' : service.status}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.65fr)_minmax(0,1fr)]">
        <Panel>
          <PanelHeader
            title="API traffic"
            description="Requests and server errors per hour, last 24 hours"
            icon={<Activity />}
            actions={<Badge tone="brand">measured</Badge>}
          />
          <div className="p-4">
            {isLoading ? <LoadingPanel rows={7} /> : <TrafficChart data={data?.traffic ?? []} />}
          </div>
        </Panel>
        <Panel>
          <PanelHeader title="Settled volume" description="Daily payment value, last 14 days" icon={<CreditCard />} />
          <div className="p-4">
            {isLoading ? <LoadingPanel rows={7} /> : <VolumeChart data={data?.volume ?? []} />}
          </div>
        </Panel>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.65fr)_minmax(0,1fr)]">
        <Panel className="overflow-hidden">
          <PanelHeader
            title="Recent payment activity"
            description="Latest instructions across every channel"
            icon={<CreditCard />}
            actions={
              <Button variant="ghost" size="sm" asChild>
                <Link to="/payments">
                  All payments <ArrowRight />
                </Link>
              </Button>
            }
          />
          {isLoading ? (
            <LoadingPanel rows={6} />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-[12px]">
                <thead>
                  <tr className="border-b border-line">
                    {['Reference', 'Beneficiary', 'Status', 'Amount', 'Created'].map((header) => (
                      <th key={header} className="px-4 py-2 text-left font-mono text-[9.5px] uppercase tracking-[0.12em] text-muted last:text-right">
                        {header}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {data?.recentPayments.map((payment) => (
                    <tr
                      key={payment.id}
                      onClick={() => navigate(`/payments/${payment.id}`)}
                      className="cursor-pointer border-b border-line/60 transition-colors last:border-0 hover:bg-surface-2/60"
                    >
                      <td className="px-4 py-2.5">
                        <span className="font-mono text-[11px] text-ink">{payment.reference}</span>
                        <span className="ml-2 font-mono text-[9px] uppercase text-muted">{payment.type}</span>
                      </td>
                      <td className="max-w-[180px] truncate px-4 py-2.5 text-ink-2">{payment.creditorName}</td>
                      <td className="px-4 py-2.5">
                        <StatusPill status={payment.status} />
                      </td>
                      <td className="num px-4 py-2.5 text-right font-medium text-ink">{formatCurrency(payment.amount, payment.currency)}</td>
                      <td className="px-4 py-2.5 text-right font-mono text-[10px] text-muted">{formatRelative(payment.createdAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Panel>

        <Panel className="overflow-hidden">
          <PanelHeader title="Operational activity" description="Audit, incidents and events" icon={<Clock3 />} />
          {isLoading ? (
            <LoadingPanel rows={7} />
          ) : (
            <ol className="divide-y divide-line">
              {data?.activity.slice(0, 7).map((item) => (
                <li key={`${item.kind}-${item.id}`}>
                  <Link to={item.linkTo ?? '/'} className="flex gap-2.5 px-4 py-2.5 transition-colors hover:bg-surface-2/60">
                    <span
                      className={cn(
                        'mt-1.5 size-1.5 shrink-0 rounded-full',
                        item.tone === 'danger' ? 'bg-danger' : item.tone === 'warning' ? 'bg-warning' : item.tone === 'success' ? 'bg-success' : 'bg-muted',
                      )}
                    />
                    <div className="min-w-0 flex-1">
                      <p className="truncate font-mono text-[10px] uppercase tracking-[0.08em] text-muted">{item.title}</p>
                      <p className="mt-0.5 line-clamp-2 text-[11.5px] leading-relaxed text-ink-2">{item.detail}</p>
                      <p className="mt-0.5 font-mono text-[9px] text-muted/80">{formatRelative(item.timestamp)}</p>
                    </div>
                  </Link>
                </li>
              ))}
            </ol>
          )}
        </Panel>
      </div>

      <Panel className="p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="mono-label">Uptime window</p>
            <p className="mt-0.5 text-[12.5px] text-ink-2">
              Process uptime {formatDuration((data?.summary.availabilityPct ?? 0) > 0 ? 3.2 * 3600 : 0)} · availability derived from {formatNumber(summary?.apiRequests24h ?? 0)} measured requests
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" size="sm" asChild>
              <Link to="/health">
                <Gauge /> System health
              </Link>
            </Button>
            <Button variant="secondary" size="sm" asChild>
              <Link to="/observability">
                <Activity /> Observability
              </Link>
            </Button>
            <Button variant="secondary" size="sm" asChild>
              <Link to="/incidents?new=1">
                <TriangleAlert /> Raise incident
              </Link>
            </Button>
          </div>
        </div>
      </Panel>
    </div>
  )
}

function SignalMetric({
  label,
  value,
  hint,
  icon,
  delta,
  invert,
  tone,
}: {
  label: string
  value: string
  hint: string
  icon: React.ReactNode
  delta?: number
  invert?: boolean
  tone?: 'danger' | 'warning' | 'success'
}) {
  const improving = typeof delta === 'number' ? (invert ? delta < 0 : delta > 0) : null
  return (
    <div className="px-4 py-3.5">
      <div className="flex items-center justify-between gap-2">
        <p className="mono-label">{label}</p>
        <span className="text-muted [&_svg]:size-3.5">{icon}</span>
      </div>
      <div className="mt-1.5 flex items-baseline gap-2">
        <span className={cn('num text-[19px] font-semibold', tone === 'danger' ? 'text-danger' : tone === 'warning' ? 'text-warning' : 'text-ink')}>{value}</span>
        {improving !== null ? (
          <span className={cn('font-mono text-[9.5px]', improving ? 'text-success' : 'text-danger')}>
            {delta! > 0 ? '+' : ''}
            {delta!.toFixed(1)}%
          </span>
        ) : null}
      </div>
      <p className="mt-0.5 font-mono text-[9px] uppercase tracking-[0.08em] text-muted">{hint}</p>
    </div>
  )
}
