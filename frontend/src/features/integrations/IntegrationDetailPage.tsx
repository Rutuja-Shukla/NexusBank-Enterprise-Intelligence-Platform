import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'sonner'
import {
  Activity, Cable, ExternalLink, FileClock, Gauge, ShieldCheck, Trash2, TriangleAlert, Zap,
} from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { ErrorState, LoadingPanel } from '@/components/data/States'
import { Badge, StatusPill } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel, PanelHeader } from '@/components/ui/card'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Field, Input, Label } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/misc'
import { useAuth } from '@/hooks/useAuth'
import { api } from '@/lib/api'
import { integrationSchema, type IntegrationValues } from '@/lib/schemas'
import { categoryLabels, cn, formatDateTime, formatMs, formatNumber, formatRelative, titleCase } from '@/lib/utils'
import type { HealthProbe, IntegrationDetail } from '@/types'
import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip } from 'recharts'

export function IntegrationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [editOpen, setEditOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)

  const detailQuery = useQuery({
    queryKey: ['integration', id],
    queryFn: () => api.get<IntegrationDetail>(`/api/v1/integrations/${id}`),
    enabled: Boolean(id),
  })

  const integration = detailQuery.data

  const probeMutation = useMutation({
    mutationFn: () => api.post<HealthProbe>(`/api/v1/integrations/${id}/probe`),
    onSuccess: (probe) => {
      toast.success(`${probe.name}: ${probe.status}`, { description: `${probe.detail} (${probe.telemetrySource}, ${formatMs(probe.latencyMs)})` })
      void queryClient.invalidateQueries({ queryKey: ['integration', id] })
      void queryClient.invalidateQueries({ queryKey: ['integrations'] })
      void queryClient.invalidateQueries({ queryKey: ['topology'] })
    },
    onError: (error) => toast.error('Probe failed', { description: error.message }),
  })

  const toggleMutation = useMutation({
    mutationFn: (enabled: boolean) => api.patch(`/api/v1/integrations/${id}/enabled`, { enabled, reason: 'Toggled from the integration record' }),
    onSuccess: (_data, enabled) => {
      toast.success(enabled ? 'Integration enabled' : 'Integration disabled')
      void queryClient.invalidateQueries({ queryKey: ['integration', id] })
      void queryClient.invalidateQueries({ queryKey: ['integrations'] })
      void queryClient.invalidateQueries({ queryKey: ['topology'] })
    },
    onError: (error) => toast.error('Could not change state', { description: error.message }),
  })

  const deleteMutation = useMutation({
    mutationFn: () => api.delete(`/api/v1/integrations/${id}`),
    onSuccess: () => {
      toast.success('Integration removed')
      void queryClient.invalidateQueries({ queryKey: ['integrations'] })
      void queryClient.invalidateQueries({ queryKey: ['topology'] })
      navigate('/integrations')
    },
    onError: (error) => toast.error('Delete blocked', { description: error.message }),
  })

  const form = useForm<IntegrationValues>({
    resolver: zodResolver(integrationSchema),
    defaultValues: { name: '', key: '', description: '', category: 'Core', baseUrl: '', authType: 'ApiKey', ownerTeam: '', version: '1.0.0', timeoutSeconds: 10 },
  })

  useEffect(() => {
    if (!integration) return
    form.reset({
      name: integration.name,
      key: integration.key,
      description: integration.description,
      category: integration.category,
      baseUrl: integration.baseUrl,
      authType: integration.authType,
      ownerTeam: integration.ownerTeam,
      version: integration.version,
      timeoutSeconds: integration.timeoutSeconds,
    })
  }, [integration, form])

  const updateMutation = useMutation({
    mutationFn: (values: IntegrationValues) =>
      api.put(`/api/v1/integrations/${id}`, {
        ...values,
        description: values.description ?? '',
        healthEndpointPath: integration?.healthEndpointPath ?? '/health',
        enabled: integration?.enabled ?? true,
        isInternal: integration?.isInternal ?? false,
        secretReference: integration?.secretReference ?? null,
        retryPolicy: integration?.retryPolicy ?? { maxAttempts: 3, baseDelayMs: 250, maxDelayMs: 5000, circuitBreakerFailureThreshold: 5, circuitBreakerBreakSeconds: 30 },
      }),
    onSuccess: () => {
      toast.success('Integration updated')
      setEditOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['integration', id] })
      void queryClient.invalidateQueries({ queryKey: ['integrations'] })
    },
    onError: (error) => toast.error('Update failed', { description: error.message }),
  })

  if (detailQuery.isError) {
    return (
      <Panel className="mt-4">
        <ErrorState error={detailQuery.error} onRetry={() => void detailQuery.refetch()} />
      </Panel>
    )
  }

  if (detailQuery.isLoading || !integration) {
    return (
      <div className="space-y-4">
        <PageHeader backTo="/integrations" backLabel="Integration hub" title="Loading integration…" eyebrow="Integration record" />
        <Panel><LoadingPanel rows={8} /></Panel>
      </div>
    )
  }

  const history = integration.healthHistory.map((sample) => ({
    label: new Date(sample.timestamp).toLocaleTimeString('en-NZ', { hour: '2-digit', minute: '2-digit' }),
    latency: Math.round(sample.latencyMs),
    status: sample.status,
    success: sample.successRatePct,
  }))

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        backTo="/integrations"
        backLabel="Integration hub"
        eyebrow={`${categoryLabels[integration.category]} · ${integration.key}`}
        title={
          <span className="flex flex-wrap items-center gap-2.5">
            {integration.name}
            <StatusPill status={integration.status} pulse={integration.status === 'Down'} />
            {integration.isSimulated ? <Badge tone="warning">fixture</Badge> : <Badge tone="neutral">live probe</Badge>}
            {!integration.enabled ? <Badge tone="neutral">disabled</Badge> : null}
          </span>
        }
        description={integration.description}
        actions={
          <>
            <Button variant="secondary" size="sm" onClick={() => probeMutation.mutate()} disabled={probeMutation.isPending || !hasPermission('integrations:write')}>
              <Zap className={cn(probeMutation.isPending && 'animate-pulse')} /> Run probe
            </Button>
            {hasPermission('integrations:write') ? (
              <>
                <Button variant="secondary" size="sm" onClick={() => setEditOpen(true)}>
                  <Cable /> Edit configuration
                </Button>
                <Button variant="ghost" size="icon" aria-label="Delete integration" onClick={() => setDeleteOpen(true)}>
                  <Trash2 className="text-danger" />
                </Button>
              </>
            ) : null}
          </>
        }
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Kpi label="Average latency" value={formatMs(integration.avgLatencyMs)} icon={<Gauge />} />
        <Kpi label="Failure rate" value={`${integration.failureRatePct.toFixed(2)}%`} icon={<TriangleAlert />} tone={integration.failureRatePct > 2 ? 'warning' : undefined} />
        <Kpi label="Requests (24h)" value={formatNumber(integration.requestCount24h)} icon={<Activity />} />
        <Kpi label="Last success" value={formatRelative(integration.lastSuccessAt)} icon={<ShieldCheck />} />
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
        <div className="space-y-4">
          <Panel>
            <PanelHeader
              title="Connectivity"
              description="Endpoint, authentication and resilience policy applied to every call."
              icon={<Cable />}
              actions={
                <div className="flex items-center gap-2">
                  <span className="font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">Enabled</span>
                  <Switch checked={integration.enabled} onCheckedChange={(value) => toggleMutation.mutate(value)} disabled={!hasPermission('integrations:write') || toggleMutation.isPending} />
                </div>
              }
            />
            <div className="grid gap-x-4 gap-y-3 p-4 sm:grid-cols-2">
              <Detail label="Base URL" value={integration.baseUrl} mono />
              <Detail label="Health path" value={integration.healthEndpointPath} mono />
              <Detail label="Authentication" value={titleCase(integration.authType)} />
              <Detail label="Auth header" value={integration.authHeaderName ?? '—'} mono />
              <Detail label="Secret reference" value={integration.secretReference ?? '—'} mono />
              <Detail label="Timeout" value={`${integration.timeoutSeconds}s`} />
              <Detail label="Owner team" value={integration.ownerTeam} />
              <Detail label="Version" value={integration.version} />
              <Detail label="Internal service" value={integration.isInternal ? 'Yes' : 'No'} />
              <Detail label="Registered" value={formatDateTime(integration.createdAt)} />
            </div>
            <div className="border-t border-line px-4 py-3">
              <p className="mono-label mb-2">Resilience policy</p>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
                <PolicyTile label="Retries" value={String(integration.retryPolicy.maxAttempts)} />
                <PolicyTile label="Base delay" value={`${integration.retryPolicy.baseDelayMs}ms`} />
                <PolicyTile label="Max delay" value={`${integration.retryPolicy.maxDelayMs}ms`} />
                <PolicyTile label="Breaker threshold" value={String(integration.retryPolicy.circuitBreakerFailureThreshold)} />
                <PolicyTile label="Break duration" value={`${integration.retryPolicy.circuitBreakerBreakSeconds}s`} />
              </div>
            </div>
          </Panel>

          <Panel>
            <PanelHeader title="Health history" description="Measured latency per probe over the last 24 hours" icon={<Activity />} />
            <div className="p-4">
              <ResponsiveContainer width="100%" height={150}>
                <BarChart data={history}>
                  <Tooltip
                    cursor={{ fill: 'var(--surface-2)' }}
                    content={({ active, payload }) => {
                      if (!active || !payload?.length) return null
                      const point = payload[0].payload as { label: string; latency: number; status: string; success: number }
                      return (
                        <div className="rounded-lg border border-line bg-surface px-2.5 py-2 shadow-lifted">
                          <p className="font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">{point.label}</p>
                          <p className="num mt-0.5 text-[12px] text-ink">{point.latency} ms · {point.status}</p>
                          <p className="font-mono text-[9.5px] text-muted">success {point.success.toFixed(1)}%</p>
                        </div>
                      )
                    }}
                  />
                  <Bar dataKey="latency" radius={[2, 2, 0, 0]}>
                    {history.map((point, index) => (
                      <Cell
                        key={`${point.label}-${index}`}
                        fill={point.status === 'Healthy' ? 'var(--success)' : point.status === 'Degraded' ? 'var(--warning)' : 'var(--danger)'}
                        opacity={0.85}
                      />
                    ))}
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
              <div className="mt-2 flex flex-wrap gap-3 font-mono text-[9px] uppercase tracking-[0.1em] text-muted">
                <span className="flex items-center gap-1.5"><span className="size-1.5 rounded-full bg-success" /> Healthy</span>
                <span className="flex items-center gap-1.5"><span className="size-1.5 rounded-full bg-warning" /> Degraded</span>
                <span className="flex items-center gap-1.5"><span className="size-1.5 rounded-full bg-danger" /> Down</span>
              </div>
              {integration.lastFailureReason ? (
                <p className="mt-3 rounded-md border border-warning/25 bg-warning-soft px-2.5 py-2 text-[11.5px] leading-relaxed text-warning">
                  Last failure {formatRelative(integration.lastFailureAt)} · {integration.lastFailureReason}
                </p>
              ) : null}
            </div>
          </Panel>

          <div className="grid gap-4 md:grid-cols-2">
            <Panel className="overflow-hidden">
              <PanelHeader title="Depends on" description={`${integration.dependsOn.length} upstream services`} />
              <DependencyList items={integration.dependsOn} empty="No upstream dependencies recorded." />
            </Panel>
            <Panel className="overflow-hidden">
              <PanelHeader title="Consumed by" description={`${integration.consumedBy.length} downstream services`} />
              <DependencyList items={integration.consumedBy} empty="No downstream consumers recorded." />
            </Panel>
          </div>
        </div>

        <div className="space-y-4">
          <Panel className="overflow-hidden">
            <PanelHeader title="Audit trail" description="Configuration and health changes" icon={<FileClock />} />
            <ol className="divide-y divide-line">
              {integration.auditTrail.map((entry) => (
                <li key={entry.id} className="px-4 py-2.5">
                  <div className="flex items-center justify-between gap-2">
                    <Badge tone={entry.action === 'created' ? 'brand' : entry.action === 'health' ? 'warning' : 'neutral'}>{entry.action}</Badge>
                    <span className="font-mono text-[9px] text-muted">{formatDateTime(entry.timestamp)}</span>
                  </div>
                  <p className="mt-1 text-[11.5px] leading-relaxed text-ink-2">{entry.details}</p>
                  <p className="mt-0.5 font-mono text-[9px] text-muted">{entry.actor}</p>
                </li>
              ))}
              {integration.auditTrail.length === 0 ? <li className="px-4 py-6 text-center text-[11.5px] text-muted">No audit entries yet.</li> : null}
            </ol>
          </Panel>

          <Panel className="p-4">
            <p className="mono-label">Health semantics</p>
            <p className="mt-1.5 text-[11.5px] leading-relaxed text-muted">
              {integration.isSimulated
                ? 'This record uses a mock:// fixture endpoint. Its health is produced by the deterministic simulator and is labelled as simulated telemetry throughout the platform.'
                : 'This endpoint is probed over HTTP with the configured timeout, retry policy and circuit breaker. Status reflects the last real probe result.'}
            </p>
            <Button variant="secondary" size="sm" className="mt-3 w-full" asChild>
              <Link to="/health">
                <Gauge /> View system health
              </Link>
            </Button>
          </Panel>
        </div>
      </div>

      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent
          title="Edit integration"
          description="Changes are recorded in the integration audit trail and the security audit log."
          size="lg"
          footer={
            <>
              <Button variant="ghost" onClick={() => setEditOpen(false)}>Cancel</Button>
              <Button onClick={form.handleSubmit((values) => updateMutation.mutate(values))} disabled={updateMutation.isPending}>
                {updateMutation.isPending ? 'Saving…' : 'Save changes'}
              </Button>
            </>
          }
        >
          <form className="grid gap-4 sm:grid-cols-2" onSubmit={form.handleSubmit((values) => updateMutation.mutate(values))}>
            <Field label="Display name" error={form.formState.errors.name?.message} className="sm:col-span-2">
              <Input {...form.register('name')} />
            </Field>
            <Field label="Key" error={form.formState.errors.key?.message}>
              <Input {...form.register('key')} />
            </Field>
            <Field label="Category">
              <Select value={form.watch('category')} onValueChange={(value) => form.setValue('category', value as IntegrationValues['category'])}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{Object.keys(categoryLabels).map((item) => <SelectItem key={item} value={item}>{categoryLabels[item]}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Field label="Base URL" error={form.formState.errors.baseUrl?.message} className="sm:col-span-2">
              <Input {...form.register('baseUrl')} />
            </Field>
            <Field label="Authentication">
              <Select value={form.watch('authType')} onValueChange={(value) => form.setValue('authType', value as IntegrationValues['authType'])}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {['OAuth2ClientCredentials', 'ApiKey', 'JwtBearer', 'MutualTls', 'None'].map((item) => <SelectItem key={item} value={item}>{titleCase(item)}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Owner team" error={form.formState.errors.ownerTeam?.message}>
              <Input {...form.register('ownerTeam')} />
            </Field>
            <Field label="Version" error={form.formState.errors.version?.message}>
              <Input {...form.register('version')} />
            </Field>
            <Field label="Timeout (seconds)" error={form.formState.errors.timeoutSeconds?.message}>
              <Input type="number" {...form.register('timeoutSeconds')} />
            </Field>
            <div className="sm:col-span-2">
              <Label htmlFor="edit-description">Description</Label>
              <textarea id="edit-description" rows={3} className="mt-1.5 w-full rounded-lg border border-line bg-surface px-3 py-2 text-[12.5px] text-ink focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25" {...form.register('description')} />
            </div>
          </form>
        </DialogContent>
      </Dialog>

      <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <DialogContent
          title="Remove integration"
          description="Deletion is only permitted when there is no payment history and the service is not a protected core system."
          size="sm"
          footer={
            <>
              <Button variant="ghost" onClick={() => setDeleteOpen(false)}>Cancel</Button>
              <Button variant="danger" onClick={() => deleteMutation.mutate()} disabled={deleteMutation.isPending}>
                {deleteMutation.isPending ? 'Removing…' : 'Remove integration'}
              </Button>
            </>
          }
        >
          <p className="text-[12.5px] leading-relaxed text-ink-2">
            Removing <strong className="text-ink">{integration.name}</strong> deletes its dependency links, health samples and audit entries. Consider disabling it instead.
          </p>
        </DialogContent>
      </Dialog>
    </div>
  )
}

function Kpi({ label, value, icon, tone }: { label: string; value: string; icon: React.ReactNode; tone?: 'warning' }) {
  return (
    <Panel className="flex items-start justify-between gap-2 p-4">
      <div>
        <p className="mono-label">{label}</p>
        <p className={cn('num mt-1 text-[20px] font-semibold', tone === 'warning' ? 'text-warning' : 'text-ink')}>{value}</p>
      </div>
      <span className="text-muted [&_svg]:size-4">{icon}</span>
    </Panel>
  )
}

function Detail({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="min-w-0">
      <p className="mono-label">{label}</p>
      <p className={cn('mt-0.5 truncate text-[12.5px] text-ink-2', mono && 'font-mono text-[11.5px]')}>{value}</p>
    </div>
  )
}

function PolicyTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-line bg-surface-2/50 px-2 py-1.5">
      <p className="font-mono text-[8.5px] uppercase tracking-[0.1em] text-muted">{label}</p>
      <p className="num mt-0.5 text-[12px] font-medium text-ink">{value}</p>
    </div>
  )
}

function DependencyList({ items, empty }: { items: IntegrationDetail['dependsOn']; empty: string }) {
  if (items.length === 0) return <p className="px-4 py-5 text-center text-[11.5px] text-muted">{empty}</p>
  return (
    <ul className="divide-y divide-line">
      {items.map((item) => (
        <li key={item.id} className="flex items-center justify-between gap-2 px-4 py-2.5">
          <Link to={`/integrations/${item.id}`} className="group flex min-w-0 items-center gap-2">
            <span className={cn('size-1.5 shrink-0 rounded-full', item.status === 'Healthy' ? 'bg-success' : item.status === 'Degraded' ? 'bg-warning' : item.status === 'Down' ? 'bg-danger' : 'bg-muted')} />
            <span className="truncate text-[12px] text-ink-2 group-hover:text-ink">{item.name}</span>
            <ExternalLink className="size-3 shrink-0 text-muted opacity-0 group-hover:opacity-100" />
          </Link>
          <div className="flex shrink-0 items-center gap-1.5">
            {item.isCritical ? <Badge tone="brand">critical</Badge> : null}
            <Badge tone="neutral">{item.protocol}</Badge>
          </div>
        </li>
      ))}
    </ul>
  )
}
