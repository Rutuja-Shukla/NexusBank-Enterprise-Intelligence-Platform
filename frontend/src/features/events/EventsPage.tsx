import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'sonner'
import { AlertTriangle, ArrowRight, Database, Layers, Radio, RefreshCw, RotateCcw, Send, ShieldAlert, Timer } from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { DataTable, TableSkeleton, type Column } from '@/components/data/DataTable'
import { EmptyState, ErrorState, LoadingPanel, StatGridSkeleton } from '@/components/data/States'
import { Pagination } from '@/components/data/Pagination'
import { SearchInput, Toolbar } from '@/components/data/Toolbar'
import { JsonView } from '@/components/data/JsonView'
import { StatCard } from '@/components/data/StatCard'
import { Badge, StatusPill } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel, PanelHeader } from '@/components/ui/card'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Field, Input, Label } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAuth } from '@/hooks/useAuth'
import { api, describeError } from '@/lib/api'
import { eventSchema, type EventValues } from '@/lib/schemas'
import { cn, formatDateTime, formatNumber, formatRelative, formatTime } from '@/lib/utils'
import type { ConsumerGroup, KafkaEvent, PagedResult, StreamStatus, Topic } from '@/types'

const eventStatuses = ['Produced', 'Consumed', 'Failed', 'DeadLettered', 'Retried'] as const

export function EventsPage() {
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const tab = searchParams.get('tab') ?? 'stream'
  const [publishOpen, setPublishOpen] = useState(searchParams.get('publish') === '1')
  const [selectedEvent, setSelectedEvent] = useState<KafkaEvent | null>(null)
  const [topicId, setTopicId] = useState('all')
  const [status, setStatus] = useState('all')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)

  const statusQuery = useQuery({
    queryKey: ['stream-status'],
    queryFn: () => api.get<StreamStatus>('/api/v1/events/status'),
    refetchInterval: 30_000,
  })

  const topicsQuery = useQuery({
    queryKey: ['topics'],
    queryFn: () => api.get<Topic[]>('/api/v1/events/topics'),
  })

  const groupsQuery = useQuery({
    queryKey: ['consumer-groups'],
    queryFn: () => api.get<ConsumerGroup[]>('/api/v1/events/consumer-groups'),
  })

  const deadLettersOnly = tab === 'dead-letters'

  const eventsQuery = useQuery({
    queryKey: ['events', { tab, topicId, status, search, page }],
    queryFn: () =>
      api.get<PagedResult<KafkaEvent>>('/api/v1/events', {
        page,
        pageSize: 15,
        topicId: topicId === 'all' ? undefined : topicId,
        status: status === 'all' ? undefined : status,
        search,
        deadLettersOnly,
      }),
    placeholderData: (previous) => previous,
  })

  const form = useForm<EventValues>({
    resolver: zodResolver(eventSchema),
    defaultValues: {
      topic: 'payments.events',
      eventType: 'payment.reconciliation.requested',
      payload: '{\n  "window": "T+1",\n  "requestedBy": "operations"\n}',
    },
  })

  const publishMutation = useMutation({
    mutationFn: (values: EventValues) => api.post<KafkaEvent>('/api/v1/events/publish', values),
    onSuccess: (event) => {
      toast.success(`Event produced · ${event.eventType}`, {
        description: event.isSimulated
          ? 'Delivered through the labelled in-process fallback because no Kafka broker is reachable.'
          : `Acknowledged by Kafka on partition ${event.partition}, offset ${event.offset}.`,
      })
      setPublishOpen(false)
      closeParam()
      void queryClient.invalidateQueries({ queryKey: ['events'] })
      void queryClient.invalidateQueries({ queryKey: ['topics'] })
      void queryClient.invalidateQueries({ queryKey: ['stream-status'] })
    },
    onError: (error) => toast.error('Publish failed', { description: describeError(error) }),
  })

  const retryMutation = useMutation({
    mutationFn: (eventId: string) => api.post<KafkaEvent>(`/api/v1/events/${eventId}/retry`),
    onSuccess: (event) => {
      setSelectedEvent(event)
      toast.success('Replay attempted', {
        description: event.status === 'DeadLettered'
          ? event.errorMessage ?? 'The broker did not acknowledge the message.'
          : `Event re-delivered${event.isSimulated ? ' via the in-process fallback' : ' to Kafka'}.`,
      })
      void queryClient.invalidateQueries({ queryKey: ['events'] })
      void queryClient.invalidateQueries({ queryKey: ['topics'] })
    },
    onError: (error) => toast.error('Replay rejected', { description: error.message }),
  })

  useEffect(() => {
    if (searchParams.get('publish') === '1') setPublishOpen(true)
  }, [searchParams])

  function setTab(next: string) {
    const params = new URLSearchParams(searchParams)
    if (next === 'stream') params.delete('tab')
    else params.set('tab', next)
    setSearchParams(params, { replace: true })
    setPage(1)
    setTopicId('all')
    setStatus('all')
  }

  function closeParam() {
    const params = new URLSearchParams(searchParams)
    params.delete('publish')
    setSearchParams(params, { replace: true })
  }

  const stream = statusQuery.data

  const columns: Column<KafkaEvent>[] = [
    {
      key: 'type',
      header: 'Event',
      cell: (row) => (
        <div>
          <p className="font-mono text-[11.5px] text-ink">{row.eventType}</p>
          <p className="mt-0.5 font-mono text-[9px] uppercase tracking-[0.08em] text-muted">{row.topic} · p{row.partition}@{row.offset}</p>
        </div>
      ),
    },
    {
      key: 'key',
      header: 'Key',
      hideBelow: 'lg',
      cell: (row) => <span className="font-mono text-[10px] text-muted">{row.eventKey}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      cell: (row) => (
        <div className="flex flex-wrap items-center gap-1">
          <StatusPill status={row.status} />
          {row.isSimulated ? <Badge tone="warning">fallback</Badge> : null}
          {row.retryCount > 0 ? <Badge tone="neutral">retry {row.retryCount}</Badge> : null}
        </div>
      ),
    },
    { key: 'produced', header: 'Produced', align: 'right', cell: (row) => <span className="font-mono text-[10px] text-muted">{formatRelative(row.producedAt)}</span> },
    {
      key: 'actions',
      header: '',
      align: 'right',
      cell: (row) => (
        <span className="flex justify-end gap-1">
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Inspect ${row.eventType}`}
            onClick={(event) => {
              event.stopPropagation()
              setSelectedEvent(row)
            }}
          >
            <ArrowRight />
          </Button>
          {(row.status === 'DeadLettered' || row.status === 'Failed') && hasPermission('events:write') ? (
            <Button
              variant="soft"
              size="icon-sm"
              aria-label="Replay event"
              disabled={retryMutation.isPending}
              onClick={(event) => {
                event.stopPropagation()
                retryMutation.mutate(row.id)
              }}
            >
              <RotateCcw />
            </Button>
          ) : null}
        </span>
      ),
    },
  ]

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        eyebrow="Event streaming"
        title="Kafka operations"
        description="Topic registry, consumer lag, poison messages and replay — with broker state reported honestly."
        actions={
          <>
            <Button variant="secondary" size="sm" onClick={() => { void statusQuery.refetch(); void eventsQuery.refetch() }}>
              <RefreshCw className={cn(eventsQuery.isFetching && 'animate-spin')} /> Refresh
            </Button>
            {hasPermission('events:write') ? (
              <Button size="sm" onClick={() => setPublishOpen(true)}>
                <Send /> Publish event
              </Button>
            ) : null}
          </>
        }
      />

      <Panel className={cn('border', stream?.brokerConnected ? 'border-line' : 'border-warning/30')}>
        <div className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-start gap-2.5">
            <span className={cn('mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg border', stream?.brokerConnected ? 'border-success/30 bg-success-soft text-success' : 'border-warning/30 bg-warning-soft text-warning')}>
              {stream?.brokerConnected ? <Radio className="size-4" /> : <ShieldAlert className="size-4" />}
            </span>
            <div>
              <p className="flex flex-wrap items-center gap-2 text-[12.5px] font-medium text-ink">
                {stream?.mode ?? 'Checking broker…'}
                <Badge tone={stream?.brokerConnected ? 'success' : 'warning'}>{stream?.brokerConnected ? 'broker connected' : 'fallback active'}</Badge>
              </p>
              <p className="mt-0.5 max-w-2xl text-[11.5px] leading-relaxed text-muted">{stream?.detail ?? 'Reading broker state from the platform.'}</p>
            </div>
          </div>
          <div className="grid shrink-0 grid-cols-3 gap-3">
            <MiniMetric label="Topics" value={String(stream?.topicCount ?? 0)} />
            <MiniMetric label="Events / h" value={formatNumber(stream?.eventsLastHour ?? 0)} />
            <MiniMetric label="Lag" value={formatNumber(stream?.totalLag ?? 0)} />
          </div>
        </div>
      </Panel>

      {statusQuery.isLoading ? (
        <StatGridSkeleton count={4} />
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Registered topics" value={String(stream?.topicCount ?? 0)} hint="across four business domains" icon={<Layers />} featured />
          <StatCard label="Events last hour" value={formatNumber(stream?.eventsLastHour ?? 0)} hint="produced across all topics" icon={<Radio />} />
          <StatCard label="Dead-letter depth" value={String(stream?.deadLetterCount ?? 0)} hint="requires triage and replay" icon={<AlertTriangle />} tone={stream && stream.deadLetterCount > 0 ? 'warning' : 'neutral'} />
          <StatCard label="Consumer lag" value={formatNumber(stream?.totalLag ?? 0)} hint="messages behind across groups" icon={<Timer />} />
        </div>
      )}

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="stream">Event stream</TabsTrigger>
          <TabsTrigger value="topics">Topics</TabsTrigger>
          <TabsTrigger value="groups">Consumer groups</TabsTrigger>
          <TabsTrigger value="dead-letters" count={stream?.deadLetterCount}>Dead letters</TabsTrigger>
        </TabsList>

        <TabsContent value="stream" className="mt-4">
          <Panel className="overflow-hidden">
            <div className="border-b border-line px-4 py-3">
              <Toolbar>
                <SearchInput value={search} onChange={(value) => { setSearch(value); setPage(1) }} placeholder="Search event type or key" className="w-full sm:w-64" />
                <Select value={topicId} onValueChange={(value) => { setTopicId(value); setPage(1) }}>
                  <SelectTrigger className="w-[190px]"><SelectValue placeholder="Topic" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All topics</SelectItem>
                    {(topicsQuery.data ?? []).map((topic) => <SelectItem key={topic.id} value={topic.id}>{topic.name}</SelectItem>)}
                  </SelectContent>
                </Select>
                <Select value={status} onValueChange={(value) => { setStatus(value); setPage(1) }}>
                  <SelectTrigger className="w-[150px]"><SelectValue placeholder="Status" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All statuses</SelectItem>
                    {eventStatuses.map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}
                  </SelectContent>
                </Select>
              </Toolbar>
            </div>
            {eventsQuery.isError ? (
              <ErrorState error={eventsQuery.error} onRetry={() => void eventsQuery.refetch()} />
            ) : eventsQuery.isLoading ? (
              <TableSkeleton rows={8} columns={5} />
            ) : (
              <DataTable
                rows={eventsQuery.data?.items ?? []}
                columns={columns}
                getRowId={(row) => row.id}
                onRowClick={(row) => setSelectedEvent(row)}
                caption="Event stream"
                empty={<EmptyState title="No events match these filters" description="Try another topic or publish a synthetic event." icon={<Radio />} />}
              />
            )}
            {eventsQuery.data && eventsQuery.data.totalCount > 0 ? (
              <Pagination page={eventsQuery.data.page} totalPages={eventsQuery.data.totalPages} totalCount={eventsQuery.data.totalCount} onPageChange={setPage} label="events" />
            ) : null}
          </Panel>
        </TabsContent>

        <TabsContent value="topics" className="mt-4">
          {topicsQuery.isError ? (
            <Panel><ErrorState error={topicsQuery.error} onRetry={() => void topicsQuery.refetch()} /></Panel>
          ) : topicsQuery.isLoading ? (
            <StatGridSkeleton count={6} />
          ) : (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {(topicsQuery.data ?? []).map((topic) => (
                <Panel key={topic.id} className="p-4">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="truncate font-mono text-[12px] font-medium text-ink">{topic.name}</p>
                      <p className="mt-0.5 font-mono text-[9px] uppercase tracking-[0.1em] text-muted">{topic.ownerTeam}</p>
                    </div>
                    <Badge tone={topic.failedCount24h > 0 ? 'warning' : 'neutral'}>{topic.failedCount24h} failed</Badge>
                  </div>
                  <p className="mt-2 line-clamp-2 min-h-[2.2rem] text-[11.5px] leading-relaxed text-muted">{topic.description}</p>

                  <div className="mt-2.5 flex items-end gap-1" aria-hidden>
                    {Array.from({ length: topic.partitions }).map((_, index) => (
                      <span
                        key={index}
                        className="w-full rounded-sm bg-brand/70"
                        style={{ height: `${10 + ((index * 7963) % 15)}px` }}
                      />
                    ))}
                  </div>

                  <dl className="mt-3 grid grid-cols-2 gap-2 border-t border-line pt-2.5">
                    <TopicMetric label="24h events" value={formatNumber(topic.eventCount24h)} />
                    <TopicMetric label="Total lag" value={formatNumber(topic.totalLag)} />
                    <TopicMetric label="Partitions" value={String(topic.partitions)} />
                    <TopicMetric label="Retention" value={`${topic.retentionHours}h`} />
                  </dl>
                  <p className="mt-2 font-mono text-[9px] text-muted">last event {formatRelative(topic.lastEventAt)}</p>
                </Panel>
              ))}
            </div>
          )}
        </TabsContent>

        <TabsContent value="groups" className="mt-4">
          <Panel className="overflow-hidden">
            <PanelHeader title="Consumer groups" description="Lag is reported by each group; heartbeat staleness indicates a stalled consumer." icon={<Database />} />
            {groupsQuery.isError ? (
              <ErrorState error={groupsQuery.error} onRetry={() => void groupsQuery.refetch()} />
            ) : groupsQuery.isLoading ? (
              <LoadingPanel rows={6} />
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-[12px]">
                  <thead>
                    <tr className="border-b border-line">
                      {['Consumer group', 'Topic', 'State', 'Members', 'Lag', 'Committed offset', 'Heartbeat'].map((header) => (
                        <th key={header} className="px-4 py-2 text-left font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">{header}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {(groupsQuery.data ?? []).map((group) => (
                      <tr key={group.id} className="border-b border-line/60 last:border-0">
                        <td className="px-4 py-2.5 font-mono text-[11px] text-ink">{group.groupId}</td>
                        <td className="px-4 py-2.5 text-ink-2">{group.topic}</td>
                        <td className="px-4 py-2.5"><StatusPill status={group.state} /></td>
                        <td className="px-4 py-2.5 font-mono text-[10.5px] text-muted">{group.members}</td>
                        <td className="px-4 py-2.5">
                          <div className="flex items-center gap-2">
                            <div className="h-1.5 w-20 overflow-hidden rounded-full bg-surface-2">
                              <div className={cn('h-full rounded-full', group.lag > 100 ? 'bg-danger' : group.lag > 20 ? 'bg-warning' : 'bg-success')} style={{ width: `${Math.min(100, Math.max(3, group.lag / 2))}%` }} />
                            </div>
                            <span className="num text-[11px] text-ink-2">{formatNumber(group.lag)}</span>
                          </div>
                        </td>
                        <td className="px-4 py-2.5 font-mono text-[10.5px] text-muted">{formatNumber(group.committedOffset)}</td>
                        <td className="px-4 py-2.5 font-mono text-[10px] text-muted">{formatRelative(group.lastHeartbeatAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Panel>
        </TabsContent>

        <TabsContent value="dead-letters" className="mt-4">
          <Panel className="overflow-hidden">
            <PanelHeader
              title="Dead-letter queue"
              description="Poison messages with their last failure. Replay once the consumer defect is fixed."
              icon={<AlertTriangle />}
              actions={<Badge tone={stream && stream.deadLetterCount > 0 ? 'danger' : 'success'}>{stream?.deadLetterCount ?? 0} parked</Badge>}
            />
            {eventsQuery.isLoading ? (
              <TableSkeleton rows={5} columns={4} />
            ) : (
              <DataTable
                rows={eventsQuery.data?.items ?? []}
                columns={columns}
                getRowId={(row) => row.id}
                onRowClick={(row) => setSelectedEvent(row)}
                caption="Dead-lettered events"
                empty={<EmptyState title="Dead-letter queue is empty" description="No poison messages are awaiting triage." icon={<AlertTriangle />} />}
              />
            )}
            {eventsQuery.data && eventsQuery.data.totalCount > 0 ? (
              <Pagination page={eventsQuery.data.page} totalPages={eventsQuery.data.totalPages} totalCount={eventsQuery.data.totalCount} onPageChange={setPage} label="dead letters" />
            ) : null}
          </Panel>
        </TabsContent>
      </Tabs>

      <Dialog open={Boolean(selectedEvent)} onOpenChange={(open) => !open && setSelectedEvent(null)}>
        {selectedEvent ? (
          <DialogContent
            title={selectedEvent.eventType}
            description={`${selectedEvent.topic} · partition ${selectedEvent.partition} · offset ${selectedEvent.offset}`}
            size="lg"
            footer={
              <>
                <Button variant="ghost" onClick={() => setSelectedEvent(null)}>Close</Button>
                {hasPermission('events:write') && (selectedEvent.status === 'DeadLettered' || selectedEvent.status === 'Failed') ? (
                  <Button variant="soft" onClick={() => retryMutation.mutate(selectedEvent.id)} disabled={retryMutation.isPending}>
                    <RotateCcw /> {retryMutation.isPending ? 'Replaying…' : 'Replay message'}
                  </Button>
                ) : null}
              </>
            }
          >
            <div className="space-y-3">
              <div className="flex flex-wrap items-center gap-2">
                <StatusPill status={selectedEvent.status} />
                {selectedEvent.isSimulated ? <Badge tone="warning">in-process fallback</Badge> : <Badge tone="success">kafka</Badge>}
                <Badge tone="neutral">retry {selectedEvent.retryCount}</Badge>
                <span className="font-mono text-[9.5px] text-muted">key {selectedEvent.eventKey}</span>
              </div>

              <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                <TopicMetric label="Produced" value={formatTime(selectedEvent.producedAt)} />
                <TopicMetric label="Consumed" value={selectedEvent.consumedAt ? formatTime(selectedEvent.consumedAt) : '—'} />
                <TopicMetric label="Consumer group" value={selectedEvent.consumerGroup ?? '—'} />
                <TopicMetric label="Correlation" value={selectedEvent.correlationId ?? '—'} />
              </dl>

              {selectedEvent.errorMessage ? (
                <p className="rounded-md border border-danger/25 bg-danger-soft px-2.5 py-2 text-[11.5px] leading-relaxed text-danger">{selectedEvent.errorMessage}</p>
              ) : null}

              <div>
                <p className="mono-label mb-1.5">Payload</p>
                <JsonView value={selectedEvent.payload} maxHeight="16rem" />
              </div>
              {selectedEvent.headers ? (
                <div>
                  <p className="mono-label mb-1.5">Headers</p>
                  <JsonView value={selectedEvent.headers} maxHeight="8rem" />
                </div>
              ) : null}
              <p className="font-mono text-[9.5px] text-muted">Produced {formatDateTime(selectedEvent.producedAt)}</p>
            </div>
          </DialogContent>
        ) : null}
      </Dialog>

      <Dialog
        open={publishOpen}
        onOpenChange={(open) => {
          setPublishOpen(open)
          if (!open) closeParam()
        }}
      >
        <DialogContent
          title="Publish event"
          description="Produces to the configured broker, or to the labelled in-process fallback when Kafka is unreachable."
          size="md"
          footer={
            <>
              <Button variant="ghost" onClick={() => { setPublishOpen(false); closeParam() }}>Cancel</Button>
              <Button onClick={form.handleSubmit((values) => publishMutation.mutate(values))} disabled={publishMutation.isPending}>
                <Send /> {publishMutation.isPending ? 'Publishing…' : 'Publish'}
              </Button>
            </>
          }
        >
          <form className="space-y-3" onSubmit={form.handleSubmit((values) => publishMutation.mutate(values))}>
            <Field label="Topic" error={form.formState.errors.topic?.message}>
              <Select value={form.watch('topic')} onValueChange={(value) => form.setValue('topic', value, { shouldValidate: true })}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(topicsQuery.data ?? []).map((topic) => <SelectItem key={topic.id} value={topic.name}>{topic.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Event type" error={form.formState.errors.eventType?.message}>
              <Input placeholder="payment.reconciliation.requested" {...form.register('eventType')} />
            </Field>
            <div>
              <Label htmlFor="payload">JSON payload</Label>
              <textarea
                id="payload"
                rows={8}
                className="mt-1.5 w-full rounded-lg border border-line bg-surface px-3 py-2 font-mono text-[12px] leading-relaxed text-ink focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25"
                {...form.register('payload')}
              />
              {form.formState.errors.payload ? <p className="mt-1 text-[11.5px] text-danger">{form.formState.errors.payload.message}</p> : null}
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  )
}

function MiniMetric({ label, value }: { label: string; value: string }) {
  return (
    <div className="text-right">
      <p className="font-mono text-[8.5px] uppercase tracking-[0.12em] text-muted">{label}</p>
      <p className="num mt-0.5 text-[15px] font-semibold text-ink">{value}</p>
    </div>
  )
}

function TopicMetric({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <dt className="font-mono text-[8.5px] uppercase tracking-[0.1em] text-muted">{label}</dt>
      <dd className="mt-0.5 truncate font-mono text-[10.5px] text-ink-2">{value}</dd>
    </div>
  )
}
