import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Activity, AlertTriangle, ArrowRight, Gauge, Layers, RefreshCw, ScrollText, ServerCog, Timer } from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { ErrorState, LoadingPanel, StatGridSkeleton } from '@/components/data/States'
import { StatCard } from '@/components/data/StatCard'
import { TrafficChart } from '@/components/charts/TrafficChart'
import { LatencyChart } from '@/components/charts/LatencyChart'
import { Badge, StatusPill } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel, PanelHeader } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { api } from '@/lib/api'
import { cn, formatDuration, formatMs, formatNumber, formatRelative, formatTime } from '@/lib/utils'
import type { AlertResponse, ErrorGroup, LatencyDistribution, LogEntry, ObservabilityOverview, Trace, TrafficPoint } from '@/types'

export function ObservabilityPage() {
  const [hours, setHours] = useState('24')
  const [logLevel, setLogLevel] = useState('all')

  const overviewQuery = useQuery({
    queryKey: ['observability', 'overview', hours],
    queryFn: () => api.get<ObservabilityOverview>('/api/v1/observability/overview', { hours }),
    refetchInterval: 30_000,
  })

  const trafficQuery = useQuery({
    queryKey: ['observability', 'traffic', hours],
    queryFn: () => api.get<TrafficPoint[]>('/api/v1/observability/traffic', { hours }),
    refetchInterval: 30_000,
  })

  const latencyQuery = useQuery({
    queryKey: ['observability', 'latency', hours],
    queryFn: () => api.get<LatencyDistribution[]>('/api/v1/observability/latency', { hours }),
  })

  const errorsQuery = useQuery({
    queryKey: ['observability', 'errors', hours],
    queryFn: () => api.get<ErrorGroup[]>('/api/v1/observability/errors', { hours, limit: 15 }),
  })

  const tracesQuery = useQuery({
    queryKey: ['observability', 'traces'],
    queryFn: () => api.get<Trace[]>('/api/v1/observability/traces', { limit: 25 }),
    refetchInterval: 30_000,
  })

  const logsQuery = useQuery({
    queryKey: ['observability', 'logs', logLevel],
    queryFn: () => api.get<LogEntry[]>('/api/v1/observability/logs', { limit: 60, level: logLevel }),
    refetchInterval: 30_000,
  })

  const alertsQuery = useQuery({
    queryKey: ['observability', 'alerts'],
    queryFn: () => api.get<AlertResponse>('/api/v1/observability/alerts'),
    refetchInterval: 30_000,
  })

  const overview = overviewQuery.data

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        eyebrow="Observability"
        title="Service telemetry"
        description="Every metric is aggregated from persisted request records — no decorative chart data."
        actions={
          <>
            <Select value={hours} onValueChange={setHours}>
              <SelectTrigger className="w-[140px]"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="6">Last 6 hours</SelectItem>
                <SelectItem value="24">Last 24 hours</SelectItem>
                <SelectItem value="72">Last 3 days</SelectItem>
                <SelectItem value="168">Last 7 days</SelectItem>
              </SelectContent>
            </Select>
            <Button variant="secondary" size="sm" onClick={() => { void overviewQuery.refetch(); void trafficQuery.refetch(); void errorsQuery.refetch() }}>
              <RefreshCw className={cn(overviewQuery.isFetching && 'animate-spin')} /> Refresh
            </Button>
          </>
        }
      />

      {overviewQuery.isError ? (
        <Panel><ErrorState error={overviewQuery.error} onRetry={() => void overviewQuery.refetch()} /></Panel>
      ) : overviewQuery.isLoading || !overview ? (
        <StatGridSkeleton count={4} />
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Request volume" value={formatNumber(overview.requestVolume24h)} hint={`${formatDuration(overview.uptimeSeconds)} process uptime`} icon={<Activity />} featured />
            <StatCard label="Latency p95" value={formatMs(overview.p95LatencyMs)} hint={`mean ${formatMs(overview.avgLatencyMs)}`} icon={<Timer />} />
            <StatCard label="Server error rate" value={`${overview.errorRatePct.toFixed(2)}%`} hint="5xx responses / total" icon={<AlertTriangle />} tone={overview.errorRatePct > 2 ? 'danger' : 'success'} />
            <StatCard label="Availability" value={`${overview.availabilityPct.toFixed(3)}%`} hint={`${overview.servicesUp} up · ${overview.servicesDown} down`} icon={<Gauge />} tone={overview.availabilityPct >= 99.5 ? 'success' : 'warning'} />
          </div>

          <div className="grid gap-4 xl:grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)]">
            <Panel>
              <PanelHeader title="Request traffic" description="Requests and 5xx errors per hour" icon={<Activity />} actions={<Badge tone="brand">live</Badge>} />
              <div className="p-4">
                {trafficQuery.isError ? <ErrorState error={trafficQuery.error} onRetry={() => void trafficQuery.refetch()} /> : trafficQuery.isLoading ? <LoadingPanel rows={7} /> : <TrafficChart data={trafficQuery.data ?? []} height={250} />}
              </div>
            </Panel>
            <Panel>
              <PanelHeader title="Latency distribution" description="Response time bands" icon={<Timer />} />
              <div className="p-4">
                {latencyQuery.isLoading ? <LoadingPanel rows={5} /> : <LatencyChart data={latencyQuery.data ?? []} height={220} />}
              </div>
            </Panel>
          </div>

          <div className="grid gap-4 xl:grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)]">
            <Panel className="overflow-hidden">
              <PanelHeader title="Request traces" description="Correlated requests with measured durations" icon={<Layers />} />
              <Tabs defaultValue="traces">
                <div className="px-4 pt-3">
                  <TabsList>
                    <TabsTrigger value="traces">Traces</TabsTrigger>
                    <TabsTrigger value="errors" count={errorsQuery.data?.length}>Error groups</TabsTrigger>
                    <TabsTrigger value="logs">Logs</TabsTrigger>
                  </TabsList>
                </div>

                <TabsContent value="traces">
                  {tracesQuery.isLoading ? (
                    <LoadingPanel rows={8} />
                  ) : (
                    <div className="max-h-[420px] overflow-y-auto">
                      <table className="w-full text-[11.5px]">
                        <thead className="sticky top-0 bg-surface">
                          <tr className="border-b border-line">
                            {['Request', 'Status', 'Duration', 'Service', 'Correlation', 'When'].map((header) => (
                              <th key={header} className="px-4 py-2 text-left font-mono text-[9px] uppercase tracking-[0.1em] text-muted">{header}</th>
                            ))}
                          </tr>
                        </thead>
                        <tbody>
                          {(tracesQuery.data ?? []).map((trace, index) => (
                            <tr key={`${trace.correlationId}-${index}`} className="border-b border-line/60 last:border-0">
                              <td className="px-4 py-2 font-mono text-[10.5px] text-ink">
                                <span className="mr-1.5 rounded bg-surface-2 px-1 py-0.5 text-[9px] text-muted">{trace.method}</span>
                                {trace.path}
                              </td>
                              <td className="px-4 py-2">
                                <span className={cn('font-mono text-[10.5px]', trace.statusCode >= 500 ? 'text-danger' : trace.statusCode >= 400 ? 'text-warning' : 'text-success')}>
                                  {trace.statusCode}
                                </span>
                              </td>
                              <td className="px-4 py-2 font-mono text-[10.5px] text-ink-2">{formatMs(trace.durationMs)}</td>
                              <td className="px-4 py-2 text-[10.5px] text-muted">{trace.serviceName ?? '—'}</td>
                              <td className="px-4 py-2 font-mono text-[9.5px] text-muted">{trace.correlationId}</td>
                              <td className="px-4 py-2 font-mono text-[9.5px] text-muted">{formatRelative(String(trace.timestamp))}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </TabsContent>

                <TabsContent value="errors">
                  {errorsQuery.isLoading ? (
                    <LoadingPanel rows={6} />
                  ) : (errorsQuery.data ?? []).length === 0 ? (
                    <p className="px-4 py-10 text-center text-[12px] text-muted">No 4xx or 5xx responses in this window.</p>
                  ) : (
                    <div className="max-h-[420px] overflow-y-auto">
                      <table className="w-full text-[11.5px]">
                        <thead className="sticky top-0 bg-surface">
                          <tr className="border-b border-line">
                            {['Endpoint', 'Status', 'Count', 'Avg latency', 'Last seen'].map((header) => (
                              <th key={header} className="px-4 py-2 text-left font-mono text-[9px] uppercase tracking-[0.1em] text-muted">{header}</th>
                            ))}
                          </tr>
                        </thead>
                        <tbody>
                          {errorsQuery.data?.map((group) => (
                            <tr key={`${group.path}-${group.statusCode}`} className="border-b border-line/60 last:border-0">
                              <td className="px-4 py-2 font-mono text-[10.5px] text-ink">{group.method} {group.path}</td>
                              <td className="px-4 py-2"><Badge tone={group.statusCode >= 500 ? 'danger' : 'warning'}>{group.statusCode}</Badge></td>
                              <td className="px-4 py-2 font-mono text-[10.5px] text-ink-2">{group.count}</td>
                              <td className="px-4 py-2 font-mono text-[10.5px] text-muted">{formatMs(group.avgLatencyMs)}</td>
                              <td className="px-4 py-2 font-mono text-[9.5px] text-muted">{formatRelative(group.lastSeenAt)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </TabsContent>

                <TabsContent value="logs">
                  <div className="flex items-center gap-2 px-4 py-2.5">
                    <ScrollText className="size-3.5 text-muted" />
                    <span className="font-mono text-[9px] uppercase tracking-[0.12em] text-muted">Structured request &amp; audit log</span>
                    <Select value={logLevel} onValueChange={setLogLevel}>
                      <SelectTrigger className="ml-auto h-7 w-[110px] text-[11px]"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem value="all">All levels</SelectItem>
                        <SelectItem value="error">Error</SelectItem>
                        <SelectItem value="warn">Warn</SelectItem>
                        <SelectItem value="info">Info</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                  <div className="max-h-[360px] divide-y divide-line overflow-y-auto font-mono text-[10.5px]">
                    {logsQuery.isLoading ? <LoadingPanel rows={8} /> : logsQuery.data?.map((entry) => (
                      <div key={`${entry.source}-${entry.id}-${entry.timestamp}`} className="flex items-start gap-2 px-4 py-1.5">
                        <span className="w-14 shrink-0 text-muted/80">{formatTime(entry.timestamp)}</span>
                        <span className={cn('w-10 shrink-0 uppercase', entry.level === 'error' ? 'text-danger' : entry.level === 'warn' ? 'text-warning' : 'text-success')}>{entry.level}</span>
                        <span className="w-10 shrink-0 text-muted">{entry.source}</span>
                        <span className="min-w-0 flex-1 truncate text-ink-2">{entry.message}</span>
                      </div>
                    ))}
                  </div>
                </TabsContent>
              </Tabs>
            </Panel>

            <div className="space-y-4">
              <Panel className="overflow-hidden">
                <PanelHeader title="Operational alerts" description="Derived from live thresholds" icon={<AlertTriangle />} actions={<Badge tone={(alertsQuery.data?.alerts.length ?? 0) > 0 ? 'warning' : 'success'}>{alertsQuery.data?.alerts.length ?? 0}</Badge>} />
                <ul className="max-h-[300px] divide-y divide-line overflow-y-auto">
                  {alertsQuery.isLoading ? <li><LoadingPanel rows={4} /></li> : (alertsQuery.data?.alerts ?? []).map((alert) => (
                    <li key={alert.id} className="px-4 py-2.5">
                      <div className="flex items-start gap-2">
                        <span className={cn('mt-1.5 size-1.5 shrink-0 rounded-full', alert.severity === 'critical' ? 'bg-danger' : alert.severity === 'warning' ? 'bg-warning' : 'bg-info')} />
                        <div className="min-w-0">
                          <p className="text-[11.5px] font-medium text-ink">{alert.title}</p>
                          <p className="mt-0.5 line-clamp-2 text-[10.5px] leading-relaxed text-muted">{alert.detail}</p>
                          <p className="mt-0.5 font-mono text-[9px] uppercase tracking-[0.08em] text-muted/80">{alert.source} · {formatRelative(alert.detectedAt)}</p>
                        </div>
                      </div>
                    </li>
                  ))}
                  {(alertsQuery.data?.alerts.length ?? 0) === 0 && !alertsQuery.isLoading ? (
                    <li className="px-4 py-6 text-center text-[11.5px] text-muted">No alerts. All thresholds are within budget.</li>
                  ) : null}
                </ul>
              </Panel>

              <Panel className="overflow-hidden">
                <PanelHeader title="Dependencies" description="Live probes, not cached status" icon={<ServerCog />} />
                <ul className="divide-y divide-line">
                  {overview.dependencies.map((dependency) => (
                    <li key={dependency.name} className="px-4 py-2.5">
                      <div className="flex items-center justify-between gap-2">
                        <span className="text-[12px] text-ink-2">{dependency.name}</span>
                        <StatusPill status={dependency.status} />
                      </div>
                      <p className="mt-0.5 line-clamp-2 text-[10.5px] leading-relaxed text-muted">{dependency.detail}</p>
                      {dependency.mode ? <p className="mt-0.5 truncate font-mono text-[9px] text-muted/80">{dependency.mode}</p> : null}
                    </li>
                  ))}
                </ul>
                <div className="border-t border-line px-4 py-2.5">
                  <Button variant="ghost" size="sm" asChild className="w-full justify-between">
                    <a href="/metrics" target="_blank" rel="noreferrer">
                      Prometheus metrics <ArrowRight />
                    </a>
                  </Button>
                </div>
              </Panel>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
