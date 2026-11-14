import { useQuery } from '@tanstack/react-query'
import { Activity, AlertTriangle, ArrowUpRight, Database, Gauge, HeartPulse, RefreshCw, ServerCog, Timer, Users } from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { ErrorState, LoadingPanel } from '@/components/data/States'
import { StatusPill } from '@/components/ui/badge'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel, PanelHeader } from '@/components/ui/card'
import { api } from '@/lib/api'
import { cn, formatDuration, formatMs, formatRelative, formatTime } from '@/lib/utils'
import type { Dependency, SystemHealth } from '@/types'

export function SystemHealthPage() {
  const { data, isLoading, isError, error, refetch, isFetching } = useQuery({
    queryKey: ['system-health-page'],
    queryFn: () => api.get<SystemHealth>('/api/v1/system/health'),
    refetchInterval: 20_000,
  })

  const overallTone = data?.overallStatus === 'healthy' ? 'text-success' : data?.overallStatus === 'degraded' ? 'text-warning' : data?.overallStatus === 'unhealthy' ? 'text-danger' : 'text-muted'

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        eyebrow="System health centre"
        title="Component truth"
        description="Probe results for every dependency and worker. Nothing is reported healthy unless it answered."
        actions={
          <Button variant="secondary" size="sm" onClick={() => void refetch()} disabled={isFetching}>
            <RefreshCw className={cn(isFetching && 'animate-spin')} /> Re-probe
          </Button>
        }
      />

      {isError ? (
        <Panel><ErrorState error={error} onRetry={() => void refetch()} /></Panel>
      ) : isLoading || !data ? (
        <Panel><LoadingPanel rows={8} /></Panel>
      ) : (
        <>
          <Panel className="relative overflow-hidden">
            <div className="pointer-events-none absolute inset-0 grid-canvas opacity-40" aria-hidden />
            <div className="relative grid gap-5 p-5 lg:grid-cols-[1.2fr_1fr]">
              <div>
                <div className="flex items-center gap-2">
                  <HeartPulse className={cn('size-4', overallTone)} />
                  <span className="mono-label">Overall status</span>
                </div>
                <p className={cn('mt-2 font-display text-[34px] font-semibold capitalize leading-none tracking-tight', overallTone)}>
                  {data.overallStatus}
                </p>
                <p className="mt-2 max-w-lg text-[12.5px] leading-relaxed text-muted">
                  {data.overallStatus === 'healthy'
                    ? 'Every critical dependency answered its probe and all workers reported a recent heartbeat.'
                    : data.overallStatus === 'degraded'
                      ? 'The platform is serving traffic, but at least one dependency or worker is degraded. Fallbacks are labelled in each module.'
                      : 'A critical dependency is unreachable. The API cannot guarantee correct responses until the database recovers.'}
                </p>
                <div className="mt-4 flex flex-wrap gap-2">
                  <Badge tone={data.databaseConnected ? 'success' : 'danger'}>database {data.databaseConnected ? 'connected' : 'unavailable'}</Badge>
                  <Badge tone={data.cacheMode.startsWith('Redis') ? 'success' : 'warning'}>{data.cacheMode.startsWith('Redis') ? 'redis' : 'memory cache fallback'}</Badge>
                  <Badge tone={data.eventBusMode.includes('Kafka (') ? 'success' : 'warning'}>{data.eventBusMode.includes('Kafka (') ? 'kafka' : 'event bus fallback'}</Badge>
                </div>
              </div>

              <dl className="grid grid-cols-2 gap-3 self-center">
                <HeroMetric label="Availability" value={`${data.availabilityPct.toFixed(3)}%`} hint="rolling 24h" icon={<Gauge />} />
                <HeroMetric label="Process uptime" value={formatDuration(data.uptimeSeconds)} hint={`checked ${formatTime(data.checkedAt)}`} icon={<Timer />} />
                <HeroMetric label="Version" value={data.version} hint={data.environment} icon={<ServerCog />} />
                <HeroMetric label="Dead letters" value={String(data.deadLetterCount)} hint="parked messages" icon={<AlertTriangle />} />
              </dl>
            </div>
          </Panel>

          <div className="grid gap-4 xl:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
            <Panel className="overflow-hidden">
              <PanelHeader title="Dependencies" description="Each probe runs on demand with a measured latency" icon={<Database />} />
              <div className="divide-y divide-line">
                {data.components.map((component) => (
                  <ComponentRow key={component.name} component={component} />
                ))}
              </div>
            </Panel>

            <Panel className="overflow-hidden">
              <PanelHeader title="Background workers" description="Heartbeat age and failure streak" icon={<Activity />} />
              <div className="divide-y divide-line">
                {data.workers.map((worker) => (
                  <ComponentRow key={worker.name} component={worker} compact />
                ))}
              </div>
              <div className="border-t border-line px-4 py-2.5">
                <p className="text-[10.5px] leading-relaxed text-muted">
                  Workers record a heartbeat after every cycle. A worker with no heartbeat is reported as degraded rather than assumed running.
                </p>
              </div>
            </Panel>
          </div>

          <div className="grid gap-4 xl:grid-cols-2">
            <Panel className="overflow-hidden">
              <PanelHeader title="Recent failures" description="Server errors and denied actions, last 24 hours" icon={<AlertTriangle />} />
              <ul className="divide-y divide-line">
                {data.recentFailures.length === 0 ? (
                  <li className="px-4 py-6 text-center text-[11.5px] text-muted">No failures recorded in the last 24 hours.</li>
                ) : (
                  data.recentFailures.map((failure) => (
                    <li key={`${failure.id}-${failure.timestamp}`} className="flex items-start gap-2.5 px-4 py-2.5">
                      <span className={cn('mt-1.5 size-1.5 shrink-0 rounded-full', failure.tone === 'warning' ? 'bg-warning' : 'bg-danger')} />
                      <div className="min-w-0">
                        <p className="truncate font-mono text-[10.5px] text-ink">{failure.title}</p>
                        <p className="truncate text-[10.5px] text-muted">{failure.detail}</p>
                        <p className="font-mono text-[9px] text-muted/80">{formatRelative(failure.timestamp)}</p>
                      </div>
                    </li>
                  ))
                )}
              </ul>
            </Panel>

            <Panel className="p-4">
              <p className="mono-label">How to read this page</p>
              <ul className="mt-2 space-y-2 text-[11.5px] leading-relaxed text-muted">
                <li className="flex gap-2"><span className="text-brand-text">01</span> SQL Server is the system of record. Local development and tests run on SQLite with the same EF model.</li>
                <li className="flex gap-2"><span className="text-brand-text">02</span> Redis is optional: when it is unreachable the cache reports a memory fallback rather than pretending to be distributed.</li>
                <li className="flex gap-2"><span className="text-brand-text">03</span> Kafka is optional: without a broker, published events are flagged as in-process fallback in the event stream.</li>
                <li className="flex gap-2"><span className="text-brand-text">04</span> Integration fixtures (<code className="font-mono">mock://</code>) report simulated telemetry, labelled on every screen they appear.</li>
              </ul>
              <div className="mt-3 flex flex-wrap gap-2">
                <Button variant="secondary" size="sm" asChild>
                  <a href="/metrics" target="_blank" rel="noreferrer">Prometheus metrics <ArrowUpRight /></a>
                </Button>
                <Button variant="ghost" size="sm" asChild>
                  <a href="/swagger" target="_blank" rel="noreferrer">OpenAPI document <ArrowUpRight /></a>
                </Button>
              </div>
              <div className="mt-4 flex items-center gap-2 border-t border-line pt-3">
                <Users className="size-3.5 text-muted" />
                <p className="text-[10.5px] text-muted">Probes executed {formatTime(data.checkedAt)} · next sweep in 20 seconds.</p>
              </div>
            </Panel>
          </div>
        </>
      )}
    </div>
  )
}

function HeroMetric({ label, value, hint, icon }: { label: string; value: string; hint: string; icon: React.ReactNode }) {
  return (
    <div className="rounded-lg border border-line bg-surface/80 px-3 py-2.5">
      <div className="flex items-center justify-between gap-2">
        <p className="font-mono text-[8.5px] uppercase tracking-[0.12em] text-muted">{label}</p>
        <span className="text-muted [&_svg]:size-3.5">{icon}</span>
      </div>
      <p className="num mt-1 truncate text-[16px] font-semibold text-ink">{value}</p>
      <p className="mt-0.5 truncate font-mono text-[8.5px] uppercase tracking-[0.08em] text-muted">{hint}</p>
    </div>
  )
}

function ComponentRow({ component, compact = false }: { component: Dependency; compact?: boolean }) {
  return (
    <div className="px-4 py-3">
      <div className="flex items-center justify-between gap-2">
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate text-[12.5px] font-medium text-ink">{component.name}</span>
          <span className="shrink-0 font-mono text-[8.5px] uppercase tracking-[0.1em] text-muted">{component.type}</span>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {component.latencyMs > 0 && !compact ? <span className="font-mono text-[9.5px] text-muted">{formatMs(component.latencyMs)}</span> : null}
          <StatusPill status={component.status} pulse={component.status === 'down'} />
        </div>
      </div>
      <p className="mt-1 text-[11px] leading-relaxed text-muted">{component.detail}</p>
      {component.mode ? <p className="mt-0.5 truncate font-mono text-[9px] text-muted/70">{component.mode}</p> : null}
    </div>
  )
}
