import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'sonner'
import { Boxes, ExternalLink, Landmark, Network, Plus, RefreshCw, ShieldCheck, Workflow } from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { EmptyState, ErrorState, StatGridSkeleton, LoadingPanel } from '@/components/data/States'
import { Pagination } from '@/components/data/Pagination'
import { SearchInput, Toolbar } from '@/components/data/Toolbar'
import { IntegrationTopology, TopologyNodeDetail } from '@/components/topology/IntegrationTopology'
import { Badge, StatusPill } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel, PanelHeader } from '@/components/ui/card'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Field, Input, Label } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/misc'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAuth } from '@/hooks/useAuth'
import { api } from '@/lib/api'
import { integrationSchema, type IntegrationValues } from '@/lib/schemas'
import { categoryLabels, cn, formatMs, formatNumber, formatRelative, titleCase } from '@/lib/utils'
import type { IntegrationListItem, PagedResult, Topology, TopologyNode } from '@/types'

const categories = ['Core', 'Payments', 'Cards', 'Risk', 'Data', 'External'] as const
const statuses = ['Healthy', 'Degraded', 'Down', 'Maintenance', 'Unknown'] as const

export function IntegrationsPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState<string>('all')
  const [status, setStatus] = useState<string>('all')
  const [enabled, setEnabled] = useState<string>('all')
  const [registerOpen, setRegisterOpen] = useState(false)
  const [selectedNode, setSelectedNode] = useState<TopologyNode | null>(null)
  const tab = searchParams.get('view') === 'topology' ? 'topology' : 'directory'

  const listQuery = useQuery({
    queryKey: ['integrations', { page, search, category, status, enabled }],
    queryFn: () =>
      api.get<PagedResult<IntegrationListItem>>('/api/v1/integrations', {
        page,
        pageSize: 12,
        search,
        category: category === 'all' ? undefined : category,
        status: status === 'all' ? undefined : status,
        enabled: enabled === 'all' ? undefined : enabled === 'enabled',
        sort: 'name',
      }),
    placeholderData: (previous) => previous,
  })

  const topologyQuery = useQuery({
    queryKey: ['topology'],
    queryFn: () => api.get<Topology>('/api/v1/integrations/topology'),
    enabled: tab === 'topology' || registerOpen,
  })

  const createMutation = useMutation({
    mutationFn: (values: IntegrationValues) =>
      api.post('/api/v1/integrations', {
        ...values,
        healthEndpointPath: '/health',
        description: values.description ?? '',
        retryPolicy: { maxAttempts: 3, baseDelayMs: 250, maxDelayMs: 5000, circuitBreakerFailureThreshold: 5, circuitBreakerBreakSeconds: 30 },
      }),
    onSuccess: () => {
      toast.success('Integration registered', { description: 'The health monitor will probe it on the next cycle.' })
      setRegisterOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['integrations'] })
      void queryClient.invalidateQueries({ queryKey: ['topology'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (error) => toast.error('Registration failed', { description: error.message }),
  })

  const form = useForm<IntegrationValues>({
    resolver: zodResolver(integrationSchema),
    defaultValues: {
      name: '',
      key: '',
      description: '',
      category: 'External',
      baseUrl: 'https://',
      authType: 'OAuth2ClientCredentials',
      ownerTeam: 'Platform Engineering',
      version: '1.0.0',
      timeoutSeconds: 10,
    },
  })

  const nodes = topologyQuery.data?.nodes ?? []
  const edges = topologyQuery.data?.edges ?? []
  const activeNode = selectedNode ?? nodes[0] ?? null
  const items = listQuery.data?.items ?? []

  const counts = useMemo(() => {
    const list = nodes.length > 0 ? nodes : items
    return {
      total: list.length,
      healthy: list.filter((item) => item.status === 'Healthy').length,
      degraded: list.filter((item) => item.status === 'Degraded' || item.status === 'Down').length,
    }
  }, [nodes, items])

  function setTab(next: string) {
    const params = new URLSearchParams(searchParams)
    if (next === 'topology') params.set('view', 'topology')
    else params.delete('view')
    setSearchParams(params, { replace: true })
  }

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        eyebrow="Integration hub"
        title="Banking service estate"
        description="Registered internal and external systems, their connectivity policies and live dependency graph."
        actions={
          <>
            <Button variant="secondary" size="sm" onClick={() => { void listQuery.refetch(); void topologyQuery.refetch() }}>
              <RefreshCw className={cn(listQuery.isFetching && 'animate-spin')} /> Refresh
            </Button>
            {hasPermission('integrations:write') ? (
              <Button size="sm" onClick={() => setRegisterOpen(true)}>
                <Plus /> Register integration
              </Button>
            ) : null}
          </>
        }
      />

      <div className="grid gap-3 sm:grid-cols-3">
        <MiniStat label="Registered services" value={formatNumber(counts.total)} icon={<Boxes />} />
        <MiniStat label="Healthy" value={String(counts.healthy)} icon={<ShieldCheck />} tone="success" />
        <MiniStat label="Needs attention" value={String(counts.degraded)} icon={<Workflow />} tone={counts.degraded > 0 ? 'warning' : undefined} />
      </div>

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="directory">Directory</TabsTrigger>
          <TabsTrigger value="topology">Topology</TabsTrigger>
        </TabsList>

        <TabsContent value="directory" className="mt-4 space-y-4">
          <Toolbar>
            <SearchInput value={search} onChange={(value) => { setSearch(value); setPage(1) }} placeholder="Search name, key or team" className="w-full sm:w-64" />
            <Select value={category} onValueChange={(value) => { setCategory(value); setPage(1) }}>
              <SelectTrigger className="w-[150px]"><SelectValue placeholder="Category" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All categories</SelectItem>
                {categories.map((item) => <SelectItem key={item} value={item}>{categoryLabels[item]}</SelectItem>)}
              </SelectContent>
            </Select>
            <Select value={status} onValueChange={(value) => { setStatus(value); setPage(1) }}>
              <SelectTrigger className="w-[140px]"><SelectValue placeholder="Status" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All statuses</SelectItem>
                {statuses.map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}
              </SelectContent>
            </Select>
            <Select value={enabled} onValueChange={(value) => { setEnabled(value); setPage(1) }}>
              <SelectTrigger className="w-[130px]"><SelectValue placeholder="Enabled" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Any state</SelectItem>
                <SelectItem value="enabled">Enabled</SelectItem>
                <SelectItem value="disabled">Disabled</SelectItem>
              </SelectContent>
            </Select>
            <span className="ml-auto font-mono text-[10px] uppercase tracking-[0.1em] text-muted">
              {listQuery.data ? `${formatNumber(listQuery.data.totalCount)} services` : '—'}
            </span>
          </Toolbar>

          {listQuery.isError ? (
            <Panel><ErrorState error={listQuery.error} onRetry={() => void listQuery.refetch()} /></Panel>
          ) : listQuery.isLoading ? (
            <StatGridSkeleton count={6} />
          ) : items.length === 0 ? (
            <Panel>
              <EmptyState title="No integrations match those filters" description="Try clearing the search or status filters." icon={<Network />} />
            </Panel>
          ) : (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {items.map((integration) => (
                <article
                  key={integration.id}
                  className="panel group relative overflow-hidden p-0 transition-colors hover:border-line-strong"
                >
                  <button type="button" onClick={() => navigate(`/integrations/${integration.id}`)} className="w-full text-left">
                    <span
                      className={cn(
                        'absolute inset-y-0 left-0 w-[2px]',
                        integration.status === 'Healthy' ? 'bg-success/60' : integration.status === 'Degraded' ? 'bg-warning' : integration.status === 'Down' ? 'bg-danger' : 'bg-line-strong',
                      )}
                    />
                    <div className="p-4 pl-4.5">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex min-w-0 items-start gap-2.5">
                          <span className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg border border-line bg-surface-2 text-brand-text">
                            <Landmark className="size-4" />
                          </span>
                          <div className="min-w-0">
                            <h3 className="truncate text-[13.5px] font-semibold text-ink">{integration.name}</h3>
                            <p className="truncate font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">{integration.key}</p>
                          </div>
                        </div>
                        <StatusPill status={integration.status} pulse={integration.status === 'Down'} />
                      </div>

                      <p className="mt-2.5 line-clamp-2 min-h-[2.4rem] text-[11.5px] leading-relaxed text-muted">{integration.description}</p>

                      <div className="mt-3 grid grid-cols-3 gap-2 border-t border-line pt-3">
                        <Metric label="Latency" value={formatMs(integration.avgLatencyMs)} />
                        <Metric label="Failures" value={`${integration.failureRatePct.toFixed(1)}%`} />
                        <Metric label="24h calls" value={formatNumber(integration.requestCount24h, { compact: true })} />
                      </div>

                      <div className="mt-3 flex items-center justify-between gap-2">
                        <span className="truncate font-mono text-[9.5px] text-muted">{integration.ownerTeam}</span>
                        <span className="flex items-center gap-1.5">
                          {integration.isSimulated ? <Badge tone="warning">fixture</Badge> : null}
                          {!integration.enabled ? <Badge tone="neutral">disabled</Badge> : null}
                          <span className="font-mono text-[9px] uppercase tracking-[0.08em] text-muted">{formatRelative(integration.lastSuccessAt)}</span>
                        </span>
                      </div>
                    </div>
                  </button>
                </article>
              ))}
            </div>
          )}

          {listQuery.data && listQuery.data.totalCount > 0 ? (
            <Panel className="overflow-hidden">
              <Pagination page={listQuery.data.page} totalPages={listQuery.data.totalPages} totalCount={listQuery.data.totalCount} onPageChange={setPage} label="integrations" />
            </Panel>
          ) : null}
        </TabsContent>

        <TabsContent value="topology" className="mt-4">
          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_320px]">
            <Panel className="overflow-hidden">
              <PanelHeader
                eyebrow="Signature view"
                title="Dependency topology"
                description="Animated links show live traffic direction. Dashed red links indicate a degraded dependency."
                icon={<Network />}
                actions={topologyQuery.isLoading ? <Skeleton className="h-6 w-24" /> : <Badge tone="brand">{edges.length} edges</Badge>}
              />
              <div className="p-3">
                {topologyQuery.isError ? (
                  <ErrorState error={topologyQuery.error} onRetry={() => void topologyQuery.refetch()} />
                ) : topologyQuery.isLoading ? (
                  <LoadingPanel rows={8} className="min-h-[440px]" />
                ) : (
                  <IntegrationTopology nodes={nodes} edges={edges} selectedId={activeNode?.id ?? null} onSelect={setSelectedNode} height={520} />
                )}
              </div>
            </Panel>

            <div className="space-y-4">
              <Panel className="p-4">
                {activeNode ? (
                  <TopologyNodeDetail node={activeNode} edges={edges} nodes={nodes} onOpen={() => navigate(`/integrations/${activeNode.id}`)} />
                ) : (
                  <p className="text-[12px] text-muted">Select a service to inspect it.</p>
                )}
              </Panel>
              <Panel className="overflow-hidden">
                <PanelHeader title="Critical paths" description="Dependencies marked business critical" />
                <ul className="divide-y divide-line">
                  {edges.filter((edge) => edge.isCritical).slice(0, 8).map((edge) => {
                    const source = nodes.find((node) => node.id === edge.source)
                    const target = nodes.find((node) => node.id === edge.target)
                    return (
                      <li key={edge.id} className="flex items-center gap-2 px-4 py-2">
                        <span className="truncate text-[11.5px] text-ink-2">{source?.name}</span>
                        <ExternalLink className="size-3 shrink-0 text-muted" />
                        <span className="truncate text-[11.5px] text-ink-2">{target?.name}</span>
                        <Badge tone={edge.degraded ? 'danger' : 'neutral'} className="ml-auto shrink-0">{edge.protocol}</Badge>
                      </li>
                    )
                  })}
                </ul>
              </Panel>
            </div>
          </div>
        </TabsContent>
      </Tabs>

      <Dialog open={registerOpen} onOpenChange={setRegisterOpen}>
        <DialogContent
          title="Register integration"
          description="Add a banking system to the estate. Health probes run on a one-minute cycle."
          size="lg"
          footer={
            <>
              <Button variant="ghost" onClick={() => setRegisterOpen(false)}>Cancel</Button>
              <Button onClick={form.handleSubmit((values) => createMutation.mutate(values))} disabled={createMutation.isPending}>
                {createMutation.isPending ? 'Registering…' : 'Register integration'}
              </Button>
            </>
          }
        >
          <form className="grid gap-4 sm:grid-cols-2" onSubmit={form.handleSubmit((values) => createMutation.mutate(values))}>
            <Field label="Display name" error={form.formState.errors.name?.message} className="sm:col-span-2">
              <Input placeholder="Open Banking Consent Service" {...form.register('name')} />
            </Field>
            <Field label="Key" error={form.formState.errors.key?.message} hint="Stable identifier used in routing and events.">
              <Input placeholder="open-banking-consent" {...form.register('key')} />
            </Field>
            <Field label="Category">
              <Select value={form.watch('category')} onValueChange={(value) => form.setValue('category', value as IntegrationValues['category'])}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{categories.map((item) => <SelectItem key={item} value={item}>{categoryLabels[item]}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Field label="Base URL" error={form.formState.errors.baseUrl?.message} className="sm:col-span-2" hint="Use mock:// to register a labelled synthetic fixture.">
              <Input placeholder="https://consent.example.test" {...form.register('baseUrl')} />
            </Field>
            <Field label="Authentication">
              <Select value={form.watch('authType')} onValueChange={(value) => form.setValue('authType', value as IntegrationValues['authType'])}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {['OAuth2ClientCredentials', 'ApiKey', 'JwtBearer', 'MutualTls', 'None'].map((item) => (
                    <SelectItem key={item} value={item}>{titleCase(item)}</SelectItem>
                  ))}
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
              <Input type="number" min={1} max={120} {...form.register('timeoutSeconds')} />
            </Field>
            <div className="sm:col-span-2">
              <Label htmlFor="description">Description</Label>
              <textarea
                id="description"
                rows={3}
                className="mt-1.5 w-full rounded-lg border border-line bg-surface px-3 py-2 text-[12.5px] text-ink focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25"
                placeholder="What this system owns and when it is called."
                {...form.register('description')}
              />
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  )
}

function MiniStat({ label, value, icon, tone }: { label: string; value: string; icon: React.ReactNode; tone?: 'success' | 'warning' }) {
  return (
    <Panel className="flex items-center gap-3 px-4 py-3">
      <span className={cn('flex size-8 items-center justify-center rounded-lg border border-line bg-surface-2 [&_svg]:size-4', tone === 'success' ? 'text-success' : tone === 'warning' ? 'text-warning' : 'text-brand-text')}>
        {icon}
      </span>
      <div>
        <p className="mono-label">{label}</p>
        <p className="num mt-0.5 text-[17px] font-semibold text-ink">{value}</p>
      </div>
    </Panel>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="font-mono text-[8.5px] uppercase tracking-[0.1em] text-muted">{label}</p>
      <p className="num mt-0.5 text-[12px] font-medium text-ink">{value}</p>
    </div>
  )
}
