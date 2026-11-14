import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { ArrowRight, CheckCircle2, ClipboardList, Link2, MessageSquarePlus, ShieldCheck, Siren, UserRound } from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { ErrorState, LoadingPanel } from '@/components/data/States'
import { Badge, StatusPill } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel, PanelHeader } from '@/components/ui/card'
import { Input, Label } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useAuth } from '@/hooks/useAuth'
import { api } from '@/lib/api'
import { cn, formatDateTime, formatRelative, titleCase } from '@/lib/utils'
import type { IncidentDetail, IncidentSeverity, IncidentStatus } from '@/types'

const nextStatus: Partial<Record<IncidentStatus, { value: IncidentStatus; label: string }>> = {
  Open: { value: 'Acknowledged', label: 'Acknowledge' },
  Acknowledged: { value: 'Investigating', label: 'Start investigating' },
  Investigating: { value: 'Mitigated', label: 'Mark mitigated' },
  Mitigated: { value: 'Resolved', label: 'Resolve incident' },
}

const activityIcon: Record<string, typeof Siren> = {
  Created: Siren,
  StatusChanged: ArrowRight,
  Note: MessageSquarePlus,
  Assignment: UserRound,
  Escalation: Siren,
  Mitigation: ShieldCheck,
  Resolution: CheckCircle2,
}

export function IncidentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [note, setNote] = useState('')
  const [rootCause, setRootCause] = useState('')
  const [correctiveActions, setCorrectiveActions] = useState('')

  const incidentQuery = useQuery({
    queryKey: ['incident', id],
    queryFn: () => api.get<IncidentDetail>(`/api/v1/incidents/${id}`),
    enabled: Boolean(id),
  })

  const incident = incidentQuery.data

  useEffect(() => {
    if (!incident) return
    setRootCause(incident.rootCause ?? '')
    setCorrectiveActions(incident.correctiveActions ?? '')
  }, [incident])

  const updateMutation = useMutation({
    mutationFn: (payload: Record<string, unknown>) => api.patch<IncidentDetail>(`/api/v1/incidents/${id}`, payload),
    onSuccess: () => {
      toast.success('Incident updated')
      void queryClient.invalidateQueries({ queryKey: ['incident', id] })
      void queryClient.invalidateQueries({ queryKey: ['incidents'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (error) => toast.error('Update rejected', { description: error.message }),
  })

  const noteMutation = useMutation({
    mutationFn: (detail: string) => api.post<IncidentDetail>(`/api/v1/incidents/${id}/notes`, { detail }),
    onSuccess: () => {
      setNote('')
      toast.success('Note added to the timeline')
      void queryClient.invalidateQueries({ queryKey: ['incident', id] })
    },
    onError: (error) => toast.error('Could not add note', { description: error.message }),
  })

  if (incidentQuery.isError) {
    return <Panel className="mt-4"><ErrorState error={incidentQuery.error} onRetry={() => void incidentQuery.refetch()} /></Panel>
  }

  if (incidentQuery.isLoading || !incident) {
    return (
      <div className="space-y-4">
        <PageHeader backTo="/incidents" backLabel="Incidents" eyebrow="Incident detail" title="Loading incident…" />
        <Panel><LoadingPanel rows={8} /></Panel>
      </div>
    )
  }

  const canWrite = hasPermission('incidents:write')
  const next = nextStatus[incident.status]
  const isResolved = incident.status === 'Resolved'

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        backTo="/incidents"
        backLabel="Incidents"
        eyebrow={`${incident.reference} · detected ${formatRelative(incident.detectedAt)}`}
        title={
          <span className="flex flex-wrap items-center gap-2.5">
            {incident.title}
            <Badge tone={incident.severity === 'Sev1' ? 'danger' : incident.severity === 'Sev2' ? 'warning' : incident.severity === 'Sev3' ? 'info' : 'neutral'}>{incident.severity}</Badge>
            <StatusPill status={incident.status} pulse={!isResolved} />
          </span>
        }
        description={incident.impactSummary ?? incident.description}
        actions={
          canWrite && !isResolved ? (
            <>
              {next ? (
                <Button size="sm" onClick={() => updateMutation.mutate({ status: next.value })} disabled={updateMutation.isPending}>
                  <CheckCircle2 /> {next.label}
                </Button>
              ) : null}
              <Select value={incident.severity} onValueChange={(value) => updateMutation.mutate({ severity: value as IncidentSeverity })}>
                <SelectTrigger className="w-[110px]"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(['Sev1', 'Sev2', 'Sev3', 'Sev4'] as IncidentSeverity[]).map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}
                </SelectContent>
              </Select>
            </>
          ) : null
        }
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Meta label="Severity" value={incident.severity} />
        <Meta label="Owning team" value={incident.team} />
        <Meta label="Assignee" value={incident.assignee ?? 'Unassigned'} />
        <Meta label="Status" value={incident.status} />
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
        <div className="space-y-4">
          <Panel className="overflow-hidden">
            <PanelHeader title="Incident timeline" description="Append-only record of everything that happened" icon={<ClipboardList />} actions={<Badge tone="neutral">{incident.activities.length} entries</Badge>} />
            <ol className="relative space-y-0 px-4 py-3">
              {incident.activities.map((activity, index) => {
                const Icon = activityIcon[activity.type] ?? MessageSquarePlus
                const isLast = index === incident.activities.length - 1
                return (
                  <li key={activity.id} className="relative flex gap-3 pb-4 last:pb-0">
                    {!isLast ? <span className="absolute left-[13px] top-7 h-[calc(100%-1.75rem)] w-px bg-line" aria-hidden /> : null}
                    <span className={cn(
                      'relative z-10 mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full border',
                      activity.type === 'Resolution' ? 'border-success/40 bg-success-soft text-success' : activity.type === 'Created' ? 'border-brand/40 bg-brand-soft text-brand-text' : 'border-line bg-surface-2 text-muted',
                    )}>
                      <Icon className="size-3.5" />
                    </span>
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">{titleCase(activity.type)}</span>
                        <span className="font-mono text-[9px] text-muted/80">{formatDateTime(activity.timestamp)}</span>
                      </div>
                      <p className="mt-0.5 text-[12px] leading-relaxed text-ink-2">{activity.detail}</p>
                      <p className="mt-0.5 font-mono text-[9px] text-muted">{activity.author}</p>
                    </div>
                  </li>
                )
              })}
            </ol>
            {canWrite ? (
              <div className="border-t border-line p-3">
                <Label htmlFor="note">Add investigation note</Label>
                <div className="mt-1.5 flex gap-2">
                  <Input id="note" placeholder="Correlated with the payment routing deployment at 14:20." value={note} onChange={(event) => setNote(event.target.value)} />
                  <Button onClick={() => noteMutation.mutate(note)} disabled={note.trim().length < 3 || noteMutation.isPending}>
                    <MessageSquarePlus /> Add
                  </Button>
                </div>
              </div>
            ) : null}
          </Panel>

          <Panel>
            <PanelHeader title="Description" description="Raised by the on-call engineer" />
            <p className="whitespace-pre-line px-4 py-3 text-[12.5px] leading-relaxed text-ink-2">{incident.description}</p>
          </Panel>
        </div>

        <div className="space-y-4">
          <Panel>
            <PanelHeader title="Remediation record" description="Root cause and corrective actions" icon={<ShieldCheck />} />
            <div className="space-y-3 p-4">
              <div>
                <Label htmlFor="root-cause">Root cause</Label>
                <textarea
                  id="root-cause"
                  rows={3}
                  className="mt-1.5 w-full rounded-lg border border-line bg-surface px-3 py-2 text-[12px] leading-relaxed text-ink focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25"
                  placeholder="What actually caused the failure?"
                  value={rootCause}
                  onChange={(event) => setRootCause(event.target.value)}
                  disabled={!canWrite}
                />
              </div>
              <div>
                <Label htmlFor="corrective">Corrective actions</Label>
                <textarea
                  id="corrective"
                  rows={3}
                  className="mt-1.5 w-full rounded-lg border border-line bg-surface px-3 py-2 text-[12px] leading-relaxed text-ink focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25"
                  placeholder="What changed to prevent recurrence?"
                  value={correctiveActions}
                  onChange={(event) => setCorrectiveActions(event.target.value)}
                  disabled={!canWrite}
                />
              </div>
              {canWrite ? (
                <Button
                  variant="secondary"
                  className="w-full"
                  onClick={() => updateMutation.mutate({ rootCause, correctiveActions })}
                  disabled={updateMutation.isPending || (!rootCause && !correctiveActions)}
                >
                  Save remediation record
                </Button>
              ) : null}
            </div>
          </Panel>

          <Panel>
            <PanelHeader title="Ownership" description="Team and assignment" icon={<UserRound />} />
            <div className="space-y-3 p-4">
              <div>
                <Label htmlFor="assignee">Assignee</Label>
                <Input
                  id="assignee"
                  className="mt-1.5"
                  defaultValue={incident.assignee ?? ''}
                  placeholder="name@nexusbank.dev"
                  disabled={!canWrite}
                  onBlur={(event) => {
                    if (event.target.value !== (incident.assignee ?? '')) updateMutation.mutate({ assignee: event.target.value })
                  }}
                />
              </div>
              <div>
                <Label htmlFor="team">Owning team</Label>
                <Input
                  id="team"
                  className="mt-1.5"
                  defaultValue={incident.team}
                  disabled={!canWrite}
                  onBlur={(event) => {
                    if (event.target.value !== incident.team) updateMutation.mutate({ team: event.target.value })
                  }}
                />
              </div>
            </div>
          </Panel>

          <Panel>
            <PanelHeader title="Linked context" description="Affected service and origin" />
            <dl className="space-y-2.5 p-4">
              <Row label="Affected service" value={incident.affectedService ?? '—'} />
              <Row label="Integration" value={
                incident.integrationName && incident.integrationId ? (
                  <Link to={`/integrations/${incident.integrationId}`} className="inline-flex items-center gap-1 text-brand-text hover:underline">
                    {incident.integrationName} <Link2 className="size-3" />
                  </Link>
                ) : '—'
              } />
              <Row label="Created by" value={incident.createdBy} />
              <Row label="Detected" value={formatDateTime(incident.detectedAt)} />
              <Row label="Acknowledged" value={formatDateTime(incident.acknowledgedAt)} />
              <Row label="Resolved" value={formatDateTime(incident.resolvedAt)} />
            </dl>
          </Panel>
        </div>
      </div>
    </div>
  )
}

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <Panel className="px-4 py-3">
      <p className="mono-label">{label}</p>
      <p className="mt-0.5 truncate text-[13px] font-medium text-ink">{value}</p>
    </Panel>
  )
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-3">
      <dt className="shrink-0 font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">{label}</dt>
      <dd className="min-w-0 text-right text-[11.5px] text-ink-2">{value}</dd>
    </div>
  )
}
