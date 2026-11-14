import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'sonner'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { AlertTriangle, Fingerprint, KeyRound, Lock, LogOut, MonitorSmartphone, Plus, ScrollText, ShieldCheck, UserCog, Users } from 'lucide-react'
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
import { Field, Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAuth } from '@/hooks/useAuth'
import { api, describeError } from '@/lib/api'
import { createUserSchema, type CreateUserValues } from '@/lib/schemas'
import { cn, formatDateTime, formatRelative, initials, titleCase } from '@/lib/utils'
import type { AccessHistoryPoint, AuditLog, AuditOutcome, PagedResult, PermissionDto, Role, SessionDto, UserListItem } from '@/types'

const outcomes: AuditOutcome[] = ['Success', 'Failure', 'Denied']

export function SecurityPage() {
  const { user, hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [tab, setTab] = useState('directory')
  const [search, setSearch] = useState('')
  const [roleFilter, setRoleFilter] = useState('all')
  const [page, setPage] = useState(1)
  const [createOpen, setCreateOpen] = useState(false)
  const [editing, setEditing] = useState<UserListItem | null>(null)
  const [auditSearch, setAuditSearch] = useState('')
  const [auditOutcome, setAuditOutcome] = useState('all')
  const [auditPage, setAuditPage] = useState(1)
  const [selectedAudit, setSelectedAudit] = useState<AuditLog | null>(null)

  const canWrite = hasPermission('security:write')

  const usersQuery = useQuery({
    queryKey: ['security', 'users', { search, roleFilter, page }],
    queryFn: () =>
      api.get<PagedResult<UserListItem>>('/api/v1/security/users', {
        page,
        pageSize: 12,
        search,
        role: roleFilter === 'all' ? undefined : roleFilter,
      }),
    placeholderData: (previous) => previous,
  })

  const rolesQuery = useQuery({
    queryKey: ['security', 'roles'],
    queryFn: () => api.get<Role[]>('/api/v1/security/roles'),
  })

  const permissionsQuery = useQuery({
    queryKey: ['security', 'permissions'],
    queryFn: () => api.get<PermissionDto[]>('/api/v1/security/permissions'),
  })

  const sessionsQuery = useQuery({
    queryKey: ['security', 'sessions'],
    queryFn: () => api.get<SessionDto[]>('/api/v1/auth/sessions'),
  })

  const accessHistoryQuery = useQuery({
    queryKey: ['security', 'access-history'],
    queryFn: () => api.get<AccessHistoryPoint[]>('/api/v1/security/access-history', { days: 14 }),
  })

  const eventsQuery = useQuery({
    queryKey: ['security', 'events'],
    queryFn: () => api.get<AuditLog[]>('/api/v1/security/events', { limit: 8 }),
    refetchInterval: 60_000,
  })

  const auditQuery = useQuery({
    queryKey: ['security', 'audit', { auditSearch, auditOutcome, auditPage }],
    queryFn: () =>
      api.get<PagedResult<AuditLog>>('/api/v1/security/audit-logs', {
        page: auditPage,
        pageSize: 15,
        search: auditSearch,
        outcome: auditOutcome === 'all' ? undefined : auditOutcome,
      }),
    placeholderData: (previous) => previous,
    enabled: tab === 'audit',
  })

  const form = useForm<CreateUserValues>({
    resolver: zodResolver(createUserSchema),
    defaultValues: { email: '', displayName: '', roleSlug: 'operations', password: '' },
  })

  const createMutation = useMutation({
    mutationFn: (values: CreateUserValues) => api.post<UserListItem>('/api/v1/security/users', values),
    onSuccess: (created) => {
      toast.success('User provisioned', { description: `${created.email} · ${created.role}` })
      setCreateOpen(false)
      form.reset()
      void queryClient.invalidateQueries({ queryKey: ['security'] })
    },
    onError: (error) => toast.error('Could not create user', { description: describeError(error) }),
  })

  const updateMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: Record<string, unknown> }) =>
      api.put<UserListItem>(`/api/v1/security/users/${id}`, payload),
    onSuccess: (updated) => {
      toast.success('User updated', { description: `${updated.email} · ${updated.isActive ? 'active' : 'disabled'}` })
      setEditing(null)
      void queryClient.invalidateQueries({ queryKey: ['security'] })
    },
    onError: (error) => toast.error('Update rejected', { description: describeError(error) }),
  })

  const revokeMutation = useMutation({
    mutationFn: (sessionId: string) => api.delete<{ revoked: boolean }>(`/api/v1/auth/sessions/${sessionId}`),
    onSuccess: () => {
      toast.success('Session revoked')
      void queryClient.invalidateQueries({ queryKey: ['security', 'sessions'] })
    },
    onError: (error) => toast.error('Could not revoke session', { description: error.message }),
  })

  const users = usersQuery.data?.items ?? []
  const activeUsers = users.filter((item) => item.isActive).length
  const totalUsers = usersQuery.data?.totalCount ?? 0
  const lockedUsers = users.filter((item) => item.isLockedOut).length
  const sessions = sessionsQuery.data ?? []

  const userColumns: Column<UserListItem>[] = [
    {
      key: 'user',
      header: 'User',
      cell: (row) => (
        <div className="flex items-center gap-2.5">
          <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-brand-soft font-mono text-[10px] font-semibold text-brand-text">
            {initials(row.displayName)}
          </span>
          <div className="min-w-0">
            <p className="truncate text-[12.5px] font-medium text-ink">{row.displayName}</p>
            <p className="truncate font-mono text-[10px] text-muted">{row.email}</p>
          </div>
        </div>
      ),
    },
    { key: 'role', header: 'Role', cell: (row) => <Badge tone={row.roleSlug === 'administrator' ? 'brand' : 'neutral'}>{row.role}</Badge> },
    {
      key: 'status',
      header: 'Status',
      cell: (row) => (
        <div className="flex flex-wrap gap-1">
          <Badge tone={row.isActive ? 'success' : 'neutral'}>{row.isActive ? 'active' : 'disabled'}</Badge>
          {row.isLockedOut ? <Badge tone="danger">locked</Badge> : null}
          {row.id === user?.id ? <Badge tone="brand">you</Badge> : null}
        </div>
      ),
    },
    {
      key: 'sessions',
      header: 'Sessions',
      align: 'right',
      hideBelow: 'lg',
      cell: (row) => <span className="num text-[11.5px] text-ink-2">{row.activeSessions}</span>,
    },
    {
      key: 'lastLogin',
      header: 'Last sign-in',
      align: 'right',
      hideBelow: 'md',
      cell: (row) => <span className="font-mono text-[10px] text-muted">{row.lastLoginAt ? formatRelative(row.lastLoginAt) : 'never'}</span>,
    },
    {
      key: 'actions',
      header: '',
      align: 'right',
      cell: (row) => (
        <Button variant="ghost" size="sm" disabled={!canWrite} onClick={() => setEditing(row)}>
          <UserCog /> Manage
        </Button>
      ),
    },
  ]

  const auditColumns: Column<AuditLog>[] = [
    {
      key: 'action',
      header: 'Action',
      cell: (row) => (
        <div>
          <p className="font-mono text-[11px] text-ink">{row.action}</p>
          <p className="mt-0.5 font-mono text-[9.5px] text-muted">{row.entityType}{row.entityId ? ` · ${row.entityId.slice(0, 8)}` : ''}</p>
        </div>
      ),
    },
    { key: 'actor', header: 'Actor', cell: (row) => <span className="text-[11.5px] text-ink-2">{row.actor}</span> },
    { key: 'outcome', header: 'Outcome', cell: (row) => <Badge tone={row.outcome === 'Success' ? 'success' : row.outcome === 'Denied' ? 'danger' : 'warning'}>{row.outcome}</Badge> },
    { key: 'severity', header: 'Severity', hideBelow: 'lg', cell: (row) => <span className="font-mono text-[9.5px] uppercase tracking-[0.08em] text-muted">{row.severity}</span> },
    { key: 'ip', header: 'Source', align: 'right', hideBelow: 'md', cell: (row) => <span className="font-mono text-[10px] text-muted">{row.ipAddress ?? '—'}</span> },
    { key: 'at', header: 'When', align: 'right', cell: (row) => <span className="font-mono text-[10px] text-muted">{formatDateTime(row.timestamp)}</span> },
  ]

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        eyebrow="Security centre"
        title="Access governance"
        description="Identity, role boundaries, session control and an immutable audit trail of every privileged action."
        actions={
          canWrite ? (
            <Button size="sm" onClick={() => setCreateOpen(true)}>
              <Plus /> Provision user
            </Button>
          ) : null
        }
      />

      {usersQuery.isLoading ? (
        <StatGridSkeleton count={4} />
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Directory size" value={String(totalUsers)} hint={`${activeUsers} active on this page`} icon={<Users />} featured />
          <StatCard label="Roles" value={String(rolesQuery.data?.length ?? 0)} hint={`${permissionsQuery.data?.length ?? 0} granular permissions`} icon={<KeyRound />} />
          <StatCard label="Live sessions" value={String(sessions.length)} hint="revocable per device" icon={<MonitorSmartphone />} />
          <StatCard label="Locked accounts" value={String(lockedUsers)} hint="after five failed attempts" icon={<Lock />} tone={lockedUsers > 0 ? 'warning' : 'success'} />
        </div>
      )}

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="directory">Directory</TabsTrigger>
          <TabsTrigger value="roles">Roles &amp; permissions</TabsTrigger>
          <TabsTrigger value="audit">Audit trail</TabsTrigger>
          <TabsTrigger value="access">Access &amp; sessions</TabsTrigger>
        </TabsList>

        <TabsContent value="directory" className="mt-4">
          <Panel className="overflow-hidden">
            <div className="border-b border-line px-4 py-3">
              <Toolbar>
                <SearchInput value={search} onChange={(value) => { setSearch(value); setPage(1) }} placeholder="Search name or email" className="w-full sm:w-64" />
                <Select value={roleFilter} onValueChange={(value) => { setRoleFilter(value); setPage(1) }}>
                  <SelectTrigger className="w-[190px]"><SelectValue placeholder="Role" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All roles</SelectItem>
                    {(rolesQuery.data ?? []).map((role) => <SelectItem key={role.id} value={role.slug}>{role.name}</SelectItem>)}
                  </SelectContent>
                </Select>
                <span className="ml-auto font-mono text-[10px] uppercase tracking-[0.1em] text-muted">
                  {usersQuery.data ? `${usersQuery.data.totalCount} identities` : '—'}
                </span>
              </Toolbar>
            </div>
            {usersQuery.isError ? (
              <ErrorState error={usersQuery.error} onRetry={() => void usersQuery.refetch()} />
            ) : usersQuery.isLoading ? (
              <TableSkeleton rows={6} columns={5} />
            ) : (
              <DataTable
                rows={users}
                columns={userColumns}
                getRowId={(row) => row.id}
                caption="User directory"
                empty={<EmptyState title="No identities match" description="Adjust the search or role filter." icon={<Users />} />}
              />
            )}
            {usersQuery.data && usersQuery.data.totalCount > 0 ? (
              <Pagination page={usersQuery.data.page} totalPages={usersQuery.data.totalPages} totalCount={usersQuery.data.totalCount} onPageChange={setPage} label="users" />
            ) : null}
          </Panel>
        </TabsContent>

        <TabsContent value="roles" className="mt-4">
          {rolesQuery.isLoading ? (
            <StatGridSkeleton count={4} />
          ) : (
            <div className="grid gap-3 md:grid-cols-2">
              {(rolesQuery.data ?? []).map((role) => (
                <Panel key={role.id} className="p-4">
                  <div className="flex items-start justify-between gap-2">
                    <div className="flex items-center gap-2">
                      <span className="flex size-8 items-center justify-center rounded-lg border border-brand/25 bg-brand-soft text-brand-text">
                        <ShieldCheck className="size-4" />
                      </span>
                      <div>
                        <p className="text-[13px] font-semibold text-ink">{role.name}</p>
                        <p className="font-mono text-[9px] uppercase tracking-[0.1em] text-muted">{role.slug}</p>
                      </div>
                    </div>
                    <div className="flex items-center gap-1.5">
                      {role.isSystem ? <Badge tone="neutral">system</Badge> : null}
                      <Badge tone="brand">{role.userCount} users</Badge>
                    </div>
                  </div>
                  <p className="mt-2 text-[11.5px] leading-relaxed text-muted">{role.description}</p>
                  <div className="mt-3 flex flex-wrap gap-1 border-t border-line pt-2.5">
                    {role.slug === 'administrator' ? (
                      <Badge tone="brand">all permissions (policy bypass)</Badge>
                    ) : (
                      role.permissions.map((permission) => (
                        <span key={permission} className="rounded border border-line bg-surface-2 px-1.5 py-0.5 font-mono text-[9px] text-ink-2">{permission}</span>
                      ))
                    )}
                  </div>
                </Panel>
              ))}
            </div>
          )}

          <Panel className="mt-4 overflow-hidden">
            <PanelHeader title="Permission catalogue" description="Every policy code enforced by the API" icon={<KeyRound />} />
            <div className="grid gap-x-6 gap-y-1.5 p-4 sm:grid-cols-2 xl:grid-cols-3">
              {(permissionsQuery.data ?? []).map((permission) => (
                <div key={permission.code} className="flex items-start justify-between gap-2 border-b border-line/60 py-1.5">
                  <div className="min-w-0">
                    <p className="font-mono text-[10.5px] text-ink">{permission.code}</p>
                    <p className="truncate text-[10.5px] text-muted">{permission.description}</p>
                  </div>
                  <span className="shrink-0 font-mono text-[8.5px] uppercase tracking-[0.08em] text-muted">{permission.category}</span>
                </div>
              ))}
            </div>
          </Panel>
        </TabsContent>

        <TabsContent value="audit" className="mt-4">
          <Panel className="overflow-hidden">
            <div className="border-b border-line px-4 py-3">
              <Toolbar>
                <SearchInput value={auditSearch} onChange={(value) => { setAuditSearch(value); setAuditPage(1) }} placeholder="Search actor, action or entity" className="w-full sm:w-72" />
                <Select value={auditOutcome} onValueChange={(value) => { setAuditOutcome(value); setAuditPage(1) }}>
                  <SelectTrigger className="w-[150px]"><SelectValue placeholder="Outcome" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All outcomes</SelectItem>
                    {outcomes.map((outcome) => <SelectItem key={outcome} value={outcome}>{outcome}</SelectItem>)}
                  </SelectContent>
                </Select>
                <span className="ml-auto flex items-center gap-1.5 font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">
                  <Fingerprint className="size-3" /> append-only
                </span>
              </Toolbar>
            </div>
            {auditQuery.isError ? (
              <ErrorState error={auditQuery.error} onRetry={() => void auditQuery.refetch()} />
            ) : auditQuery.isLoading ? (
              <TableSkeleton rows={8} columns={6} />
            ) : (
              <DataTable
                rows={auditQuery.data?.items ?? []}
                columns={auditColumns}
                getRowId={(row) => row.id}
                onRowClick={(row) => setSelectedAudit(row)}
                caption="Audit trail"
                empty={<EmptyState title="No audit records" description="Privileged actions will appear here as they occur." icon={<ScrollText />} />}
              />
            )}
            {auditQuery.data && auditQuery.data.totalCount > 0 ? (
              <Pagination page={auditQuery.data.page} totalPages={auditQuery.data.totalPages} totalCount={auditQuery.data.totalCount} onPageChange={setAuditPage} label="records" />
            ) : null}
          </Panel>
        </TabsContent>

        <TabsContent value="access" className="mt-4">
          <div className="grid gap-4 xl:grid-cols-[minmax(0,1.3fr)_minmax(0,1fr)]">
            <Panel>
              <PanelHeader title="Sign-in activity" description="Successful and denied authentications, last 14 days" icon={<Fingerprint />} />
              <div className="px-2 py-4">
                {accessHistoryQuery.isLoading ? (
                  <LoadingPanel rows={5} />
                ) : (
                  <ResponsiveContainer width="100%" height={230}>
                    <BarChart data={accessHistoryQuery.data ?? []} barGap={2} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
                      <CartesianGrid stroke="var(--line)" strokeDasharray="3 4" vertical={false} />
                      <XAxis dataKey="label" tick={{ fontSize: 9, fontFamily: 'var(--font-mono)', fill: 'var(--muted)' }} tickLine={false} axisLine={false} interval="preserveStartEnd" />
                      <YAxis tick={{ fontSize: 9, fontFamily: 'var(--font-mono)', fill: 'var(--muted)' }} tickLine={false} axisLine={false} allowDecimals={false} />
                      <Tooltip
                        cursor={{ fill: 'var(--surface-2)', opacity: 0.5 }}
                        contentStyle={{ background: 'var(--surface)', border: '1px solid var(--line)', borderRadius: 8, fontSize: 11, color: 'var(--ink)' }}
                      />
                      <Bar dataKey="signIns" name="Sign-ins" fill="var(--info)" radius={[2, 2, 0, 0]} maxBarSize={16} />
                      <Bar dataKey="failures" name="Failures" fill="var(--danger)" radius={[2, 2, 0, 0]} maxBarSize={16} />
                    </BarChart>
                  </ResponsiveContainer>
                )}
              </div>
              <div className="flex items-center gap-4 border-t border-line px-4 py-2">
                <LegendSwatch color="bg-info" label="Successful sign-ins" />
                <LegendSwatch color="bg-danger" label="Failed attempts" />
              </div>
            </Panel>

            <Panel className="overflow-hidden">
              <PanelHeader title="Security events" description="Authentication and authorisation outcomes" icon={<AlertTriangle />} />
              <ul className="max-h-[320px] divide-y divide-line overflow-y-auto">
                {eventsQuery.isLoading ? (
                  <li><LoadingPanel rows={5} /></li>
                ) : (
                  eventsQuery.data?.map((event) => (
                    <li key={event.id} className="flex items-start gap-2.5 px-4 py-2.5">
                      <span className={cn('mt-1.5 size-1.5 shrink-0 rounded-full', event.outcome === 'Success' ? 'bg-success' : event.outcome === 'Denied' ? 'bg-danger' : 'bg-warning')} />
                      <div className="min-w-0 flex-1">
                        <p className="truncate font-mono text-[10.5px] text-ink">{event.action}</p>
                        <p className="truncate text-[10.5px] text-muted">{event.actor}{event.ipAddress ? ` · ${event.ipAddress}` : ''}</p>
                      </div>
                      <span className="shrink-0 font-mono text-[9px] text-muted">{formatRelative(event.timestamp)}</span>
                    </li>
                  ))
                )}
              </ul>
            </Panel>
          </div>

          <Panel className="mt-4 overflow-hidden">
            <PanelHeader
              title="Active sessions"
              description="Rotating refresh tokens; revoking a session invalidates it immediately"
              icon={<MonitorSmartphone />}
              actions={<Badge tone="neutral">{sessions.length}</Badge>}
            />
            {sessionsQuery.isError ? (
              <ErrorState error={sessionsQuery.error} onRetry={() => void sessionsQuery.refetch()} />
            ) : sessionsQuery.isLoading ? (
              <LoadingPanel rows={4} />
            ) : (
              <ul className="divide-y divide-line">
                {sessions.map((session) => (
                  <li key={session.id} className="flex flex-wrap items-center gap-3 px-4 py-3">
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-mono text-[10.5px] text-ink">{session.ipAddress ?? 'unknown source'}</span>
                        {session.isCurrent ? <Badge tone="brand">this device</Badge> : null}
                      </div>
                      <p className="mt-0.5 truncate font-mono text-[9.5px] text-muted">{session.userAgent ?? 'unknown client'}</p>
                      <p className="mt-0.5 font-mono text-[9px] text-muted/80">
                        started {formatDateTime(session.createdAt)} · expires {formatDateTime(session.expiresAt)}
                      </p>
                    </div>
                    <Button
                      variant="soft"
                      size="sm"
                      disabled={revokeMutation.isPending}
                      onClick={() => revokeMutation.mutate(session.id)}
                    >
                      <LogOut /> Revoke
                    </Button>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </TabsContent>
      </Tabs>

      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent
          title="Provision user"
          description="Passwords are hashed with BCrypt before persistence; they are never logged or returned."
          footer={
            <>
              <Button variant="ghost" onClick={() => setCreateOpen(false)}>Cancel</Button>
              <Button onClick={form.handleSubmit((values) => createMutation.mutate(values))} disabled={createMutation.isPending}>
                {createMutation.isPending ? 'Creating…' : 'Create user'}
              </Button>
            </>
          }
        >
          <form className="grid gap-3 sm:grid-cols-2" onSubmit={form.handleSubmit((values) => createMutation.mutate(values))}>
            <Field label="Display name" error={form.formState.errors.displayName?.message}>
              <Input placeholder="Aroha Ngata" {...form.register('displayName')} />
            </Field>
            <Field label="Email" error={form.formState.errors.email?.message}>
              <Input type="email" placeholder="aro-ha@nexusbank.dev" {...form.register('email')} />
            </Field>
            <Field label="Role">
              <Select value={form.watch('roleSlug')} onValueChange={(value) => form.setValue('roleSlug', value, { shouldValidate: true })}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(rolesQuery.data ?? []).map((role) => <SelectItem key={role.slug} value={role.slug}>{role.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Temporary password" error={form.formState.errors.password?.message} hint="12+ chars, upper, lower, digit">
              <Input type="password" autoComplete="new-password" {...form.register('password')} />
            </Field>
          </form>
        </DialogContent>
      </Dialog>

      <Dialog open={Boolean(editing)} onOpenChange={(open) => !open && setEditing(null)}>
        {editing ? <EditUserDialog user={editing} saving={updateMutation.isPending} onSave={(payload) => updateMutation.mutate({ id: editing.id, payload })} onClose={() => setEditing(null)} /> : null}
      </Dialog>

      <Dialog open={Boolean(selectedAudit)} onOpenChange={(open) => !open && setSelectedAudit(null)}>
        {selectedAudit ? (
          <DialogContent
            title={selectedAudit.action}
            description={`${selectedAudit.actor} · ${formatDateTime(selectedAudit.timestamp)}`}
            size="md"
            footer={<Button variant="ghost" onClick={() => setSelectedAudit(null)}>Close</Button>}
          >
            <dl className="grid grid-cols-2 gap-3">
              <Detail label="Outcome" value={<StatusPill status={selectedAudit.outcome} />} />
              <Detail label="Severity" value={titleCase(selectedAudit.severity)} />
              <Detail label="Entity" value={`${selectedAudit.entityType}${selectedAudit.entityId ? ` · ${selectedAudit.entityId}` : ''}`} />
              <Detail label="Source IP" value={selectedAudit.ipAddress ?? '—'} />
              <Detail label="Correlation" value={selectedAudit.correlationId ?? '—'} />
            </dl>
            <div className="mt-3">
              <p className="mono-label mb-1.5">Metadata</p>
              <JsonView value={selectedAudit.metadata ?? '{}'} maxHeight="14rem" />
            </div>
          </DialogContent>
        ) : null}
      </Dialog>
    </div>
  )
}

function EditUserDialog({
  user,
  saving,
  onSave,
  onClose,
}: {
  user: UserListItem
  saving: boolean
  onSave: (payload: Record<string, unknown>) => void
  onClose: () => void
}) {
  const [displayName, setDisplayName] = useState(user.displayName)
  const [roleSlug, setRoleSlug] = useState(user.roleSlug)
  const [isActive, setIsActive] = useState(user.isActive)
  const [newPassword, setNewPassword] = useState('')

  return (
    <DialogContent
      title="Manage user"
      description={user.email}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button
            disabled={saving}
            onClick={() => onSave({ displayName, roleSlug, isActive, newPassword: newPassword || null })}
          >
            {saving ? 'Saving…' : 'Save changes'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Display name">
          <Input value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
        </Field>
        <Field label="Role">
          <Select value={roleSlug} onValueChange={setRoleSlug}>
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {['administrator', 'operations', 'developer', 'auditor'].map((slug) => (
                <SelectItem key={slug} value={slug}>{titleCase(slug)}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
        <Field label="Reset password" hint="Leave blank to keep the current credential.">
          <Input type="password" autoComplete="new-password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} placeholder="••••••••••••" />
        </Field>
        <label className="flex items-center gap-2 text-[12px] text-ink-2">
          <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} className="size-3.5 accent-[var(--brand)]" />
          Account enabled
        </label>
        <p className="rounded-md border border-line bg-surface-2 px-2.5 py-2 text-[10.5px] leading-relaxed text-muted">
          Disabling an account does not delete transaction history. Role changes take effect on the next token refresh.
        </p>
      </div>
    </DialogContent>
  )
}

function Detail({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <dt className="mono-label">{label}</dt>
      <dd className="mt-0.5 break-words text-[11.5px] text-ink-2">{value}</dd>
    </div>
  )
}

function LegendSwatch({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5 text-[10px] text-muted">
      <span className={cn('size-2 rounded-sm', color)} />
      {label}
    </span>
  )
}
