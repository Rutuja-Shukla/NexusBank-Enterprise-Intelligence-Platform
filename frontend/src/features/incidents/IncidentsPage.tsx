import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'sonner'
import { ArrowUpRight, Clock3, Plus, Siren, Timer, TriangleAlert, Users } from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { EmptyState, ErrorState, StatGridSkeleton } from '@/components/data/States'
import { Pagination } from '@/components/data/Pagination'
import { SearchInput, Toolbar } from '@/components/data/Toolbar'
import { StatCard } from '@/components/data/StatCard'
import { Badge, StatusPill } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel } from '@/components/ui/card'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Field, Input, Label } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useAuth } from '@/hooks/useAuth'
import { api, describeError } from '@/lib/api'
import { incidentSchema, type IncidentValues } from '@/lib/schemas'
import { cn, formatNumber, formatRelative } from '@/lib/utils'
import type { IncidentDetail, IncidentListItem, IncidentStats, PagedResult } from '@/types'

const severities = ['Sev1', 'Sev2', 'Sev3', 'Sev4'] as const
const categories = ['Integration', 'Payment', 'Security', 'Performance', 'DataQuality', 'Infrastructure'] as const
const statuses = ['Open', 'Acknowledged', 'Investigating', 'Mitigated', 'Resolved'] as const

const severityRail: Record<string, string> = {
  Sev1: 'bg-danger',
  Sev2: 'bg-warning',
  Sev3: 'bg-info',
  Sev4: 'bg-line-strong',
}

export function IncidentsPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const [createOpen, setCreateOpen] = useState(searchParams.get('new') === '1')
  const [search, setSearch] = useState('')
  const [severity, setSeverity] = useState('all')
  const [status, setStatus] = useState('all')
  const [openOnly, setOpenOnly] = useState(true)
  const [page, setPage] = useState(1)

  const listQuery = useQuery({
    queryKey: ['incidents', { search, severity, status, openOnly, page }],
    queryFn: () =>
      api.get<PagedResult<IncidentListItem>>('/api/v1/incidents', {
        page,
        pageSize: 12,
        search,
        severity: severity === 'all' ? undefined : severity,
        status: status === 'all' ? undefined : status,
        openOnly: status === 'all' ? openOnly : undefined,
      }),
    placeholderData: (previous) => previous,
  })

  const statsQuery = useQuery({
    queryKey: ['incidents', 'stats'],
    queryFn: () => api.get<IncidentStats>('/api/v1/incidents/stats'),
  })

  const form = useForm<IncidentValues>({
    resolver: zodResolver(incidentSchema),
    defaultValues: {
      title: '',
      description: '',
      severity: 'Sev3',
      category: 'Integration',
      team: 'Platform Engineering',
      affectedService: '',
      impactSummary: '',
    },
  })

  const createMutation = useMutation({
    mutationFn: (values: IncidentValues) => api.post<IncidentDetail>('/api/v1/incidents', values),
    onSuccess: (incident) => {
      toast.success(`${incident.reference} raised`, { description: `${incident.severity} · ${incident.title}` })
      setCreateOpen(false)
      closeParam()
      form.reset()
      void queryClient.invalidateQueries({ queryKey: ['incidents'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      navigate(`/incidents/${incident.id}`)
    },
    onError: (error) => toast.error('Could not raise incident', { description: describeError(error) }),
  })

  useEffect(() => {
    if (searchParams.get('new') === '1') setCreateOpen(true)
  }, [searchParams])

  function closeParam() {
    const params = new URLSearchParams(searchParams)
    params.delete('new')
    setSearchParams(params, { replace: true })
  }

  const stats = statsQuery.data
  const incidents = listQuery.data?.items ?? []

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        eyebrow="Incident management"
        title="Operational escalations"
        description="Every incident with its severity, owning team, timeline and remediation record."
        actions={
          hasPermission('incidents:write') ? (
            <Button size="sm" onClick={() => setCreateOpen(true)}>
              <Plus /> Raise incident
            </Button>
          ) : null
        }
      />

      {statsQuery.isLoading || !stats ? (
        <StatGridSkeleton count={4} />
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Open workload" value={String(stats.open + stats.acknowledged + stats.investigating + stats.mitigated)} hint={`${stats.open} unacknowledged`} icon={<Siren />} tone={stats.sev1 > 0 ? 'danger' : 'neutral'} featured />
          <StatCard label="Severity 1 & 2" value={String(stats.sev1 + stats.sev2)} hint={`${stats.sev1} Sev1 · ${stats.sev2} Sev2`} icon={<TriangleAlert />} tone={stats.sev1 + stats.sev2 > 0 ? 'warning' : 'success'} />
          <StatCard label="Investigating" value={String(stats.investigating)} hint={`${stats.acknowledged} acknowledged`} icon={<Users />} />
          <StatCard label="Mean time to resolve" value={`${formatNumber(stats.meanTimeToResolveMinutes)} min`} hint={`${stats.resolved} resolved incidents`} icon={<Timer />} />
        </div>
      )}

      <Panel className="overflow-hidden">
        <div className="border-b border-line px-4 py-3">
          <Toolbar>
            <SearchInput value={search} onChange={(value) => { setSearch(value); setPage(1) }} placeholder="Search reference, title or team" className="w-full sm:w-72" />
            <Select value={severity} onValueChange={(value) => { setSeverity(value); setPage(1) }}>
              <SelectTrigger className="w-[130px]"><SelectValue placeholder="Severity" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All severities</SelectItem>
                {severities.map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}
              </SelectContent>
            </Select>
            <Select value={status} onValueChange={(value) => { setStatus(value); setPage(1) }}>
              <SelectTrigger className="w-[150px]"><SelectValue placeholder="Status" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">{openOnly ? 'Open (all states)' : 'All statuses'}</SelectItem>
                {statuses.map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}
              </SelectContent>
            </Select>
            <button
              type="button"
              onClick={() => { setOpenOnly((value) => !value); setStatus('all'); setPage(1) }}
              className={cn(
                'h-9 rounded-lg border px-3 text-[12px] font-medium transition-colors',
                openOnly ? 'border-brand/40 bg-brand-soft text-brand-text' : 'border-line bg-surface text-muted hover:text-ink',
              )}
            >
              {openOnly ? 'Showing open only' : 'Showing all'}
            </button>
            <span className="ml-auto font-mono text-[10px] uppercase tracking-[0.1em] text-muted">
              {listQuery.data ? `${listQuery.data.totalCount} incidents` : '—'}
            </span>
          </Toolbar>
        </div>

        {listQuery.isError ? (
          <ErrorState error={listQuery.error} onRetry={() => void listQuery.refetch()} />
        ) : listQuery.isLoading ? (
          <div className="divide-y divide-line">
            {Array.from({ length: 5 }).map((_, index) => (
              <div key={index} className="flex gap-3 px-4 py-3.5">
                <div className="skeleton h-10 w-[2px]" />
                <div className="flex-1 space-y-2">
                  <div className="skeleton h-2.5 w-24" />
                  <div className="skeleton h-3 w-2/3" />
                  <div className="skeleton h-2 w-40" />
                </div>
              </div>
            ))}
          </div>
        ) : incidents.length === 0 ? (
          <EmptyState title="No incidents match this view" description="Change the filters, or raise a new incident to begin an investigation." icon={<Siren />} />
        ) : (
          <ul className="divide-y divide-line">
            {incidents.map((incident) => (
              <li key={incident.id}>
                <button
                  type="button"
                  onClick={() => navigate(`/incidents/${incident.id}`)}
                  className="group flex w-full items-stretch gap-3 px-4 py-3 text-left transition-colors hover:bg-surface-2/50"
                >
                  <span className={cn('w-[2px] shrink-0 rounded-full', severityRail[incident.severity])} aria-hidden />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-[10px] uppercase tracking-[0.12em] text-muted">{incident.reference}</span>
                      <Badge tone={incident.severity === 'Sev1' ? 'danger' : incident.severity === 'Sev2' ? 'warning' : incident.severity === 'Sev3' ? 'info' : 'neutral'}>{incident.severity}</Badge>
                      <StatusPill status={incident.status} />
                      <span className="font-mono text-[9px] uppercase tracking-[0.08em] text-muted">{incident.category}</span>
                    </div>
                    <p className="mt-1 line-clamp-1 text-[13px] font-medium text-ink">{incident.title}</p>
                    <p className="mt-0.5 flex flex-wrap items-center gap-x-3 gap-y-0.5 font-mono text-[9.5px] text-muted">
                      <span className="flex items-center gap-1"><Users className="size-3" />{incident.team}</span>
                      {incident.affectedService ? <span>{incident.affectedService}</span> : null}
                      <span className="flex items-center gap-1"><Clock3 className="size-3" />{Math.round(incident.minutesOpen)}m open</span>
                      <span>{incident.activityCount} timeline entries</span>
                      <span>detected {formatRelative(incident.detectedAt)}</span>
                    </p>
                  </div>
                  <div className="flex shrink-0 flex-col items-end justify-between gap-2">
                    <span className="font-mono text-[9px] uppercase tracking-[0.08em] text-muted">{incident.assignee ?? 'unassigned'}</span>
                    <ArrowUpRight className="size-3.5 text-muted transition-colors group-hover:text-brand-text" />
                  </div>
                </button>
              </li>
            ))}
          </ul>
        )}

        {listQuery.data && listQuery.data.totalCount > 0 ? (
          <Pagination page={listQuery.data.page} totalPages={listQuery.data.totalPages} totalCount={listQuery.data.totalCount} onPageChange={setPage} label="incidents" />
        ) : null}
      </Panel>

      <Dialog
        open={createOpen}
        onOpenChange={(open) => {
          setCreateOpen(open)
          if (!open) closeParam()
        }}
      >
        <DialogContent
          title="Raise incident"
          description="Creates an incident with an append-only timeline. Severity drives the command centre alerting."
          size="lg"
          footer={
            <>
              <Button variant="ghost" onClick={() => { setCreateOpen(false); closeParam() }}>Cancel</Button>
              <Button onClick={form.handleSubmit((values) => createMutation.mutate(values))} disabled={createMutation.isPending}>
                {createMutation.isPending ? 'Raising…' : 'Raise incident'}
              </Button>
            </>
          }
        >
          <form className="grid gap-4 sm:grid-cols-2" onSubmit={form.handleSubmit((values) => createMutation.mutate(values))}>
            <Field label="Title" error={form.formState.errors.title?.message} className="sm:col-span-2">
              <Input placeholder="Elevated 5xx responses on account lookup" {...form.register('title')} />
            </Field>
            <Field label="Severity">
              <Select value={form.watch('severity')} onValueChange={(value) => form.setValue('severity', value as IncidentValues['severity'])}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{severities.map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Field label="Category">
              <Select value={form.watch('category')} onValueChange={(value) => form.setValue('category', value as IncidentValues['category'])}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{categories.map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Field label="Owning team" error={form.formState.errors.team?.message}>
              <Input {...form.register('team')} />
            </Field>
            <Field label="Affected service">
              <Input placeholder="Account Management Service" {...form.register('affectedService')} />
            </Field>
            <div className="sm:col-span-2">
              <Label htmlFor="description">Description</Label>
              <textarea
                id="description"
                rows={4}
                className="mt-1.5 w-full rounded-lg border border-line bg-surface px-3 py-2 text-[12.5px] text-ink focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25"
                placeholder="Observed behaviour, scope, customer or processing impact."
                {...form.register('description')}
              />
              {form.formState.errors.description ? <p className="mt-1 text-[11.5px] text-danger">{form.formState.errors.description.message}</p> : null}
            </div>
            <Field label="Impact summary" className="sm:col-span-2" hint="Shown on the command centre and alert feed.">
              <Input placeholder="Approximately 480 outbound payments affected" {...form.register('impactSummary')} />
            </Field>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  )
}
