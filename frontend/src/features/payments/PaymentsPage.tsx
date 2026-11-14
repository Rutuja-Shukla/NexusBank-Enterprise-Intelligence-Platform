import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'sonner'
import { ArrowRight, BadgeCheck, CreditCard, Landmark, Plus, ShieldAlert, Timer, Wallet } from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { DataTable, TableSkeleton, type Column } from '@/components/data/DataTable'
import { EmptyState, ErrorState, StatGridSkeleton } from '@/components/data/States'
import { Pagination } from '@/components/data/Pagination'
import { SearchInput, Toolbar } from '@/components/data/Toolbar'
import { StatCard } from '@/components/data/StatCard'
import { StatusPill } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel } from '@/components/ui/card'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Field, Input, Label } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/misc'
import { useAuth } from '@/hooks/useAuth'
import { api, describeError } from '@/lib/api'
import { paymentSchema, type PaymentValues } from '@/lib/schemas'
import { cn, formatCurrency, formatMs, formatNumber, formatRelative, toneForStatus } from '@/lib/utils'
import type { AccountOption, PagedResult, PaymentCreated, PaymentListItem, PaymentStats } from '@/types'

const statusOptions = ['Initiated', 'Validating', 'Processing', 'Pending', 'Completed', 'Failed', 'Reversed'] as const
const typeOptions = ['Transfer', 'CardPayment', 'DirectDebit', 'Refund'] as const

export function PaymentsPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('all')
  const [type, setType] = useState('all')
  const [sort, setSort] = useState('-created')
  const [createOpen, setCreateOpen] = useState(searchParams.get('new') === '1')

  const listQuery = useQuery({
    queryKey: ['payments', { page, search, status, type, sort }],
    queryFn: () =>
      api.get<PagedResult<PaymentListItem>>('/api/v1/payments', {
        page,
        pageSize: 15,
        search,
        status: status === 'all' ? undefined : status,
        type: type === 'all' ? undefined : type,
        sort,
      }),
    placeholderData: (previous) => previous,
  })

  const statsQuery = useQuery({
    queryKey: ['payments', 'stats'],
    queryFn: () => api.get<PaymentStats>('/api/v1/payments/stats'),
  })

  const accountsQuery = useQuery({
    queryKey: ['payment-accounts'],
    queryFn: () => api.get<AccountOption[]>('/api/v1/payments/accounts'),
    enabled: createOpen,
  })

  const form = useForm<PaymentValues>({
    resolver: zodResolver(paymentSchema),
    defaultValues: {
      debtorAccountId: '',
      creditorName: '',
      creditorAccountNumber: '',
      creditorBic: '',
      amount: 250,
      currency: 'NZD',
      type: 'Transfer',
      description: '',
      simulateFailure: false,
    },
  })

  const createMutation = useMutation({
    mutationFn: (values: PaymentValues) => {
      const idempotencyKey = crypto.randomUUID()
      return api.post<PaymentCreated>(
        '/api/v1/payments',
        {
          debtorAccountId: values.debtorAccountId,
          creditorName: values.creditorName,
          creditorAccountNumber: values.creditorAccountNumber,
          creditorBic: values.creditorBic || undefined,
          amount: values.amount,
          currency: values.currency,
          type: values.type,
          description: values.description || undefined,
          simulateFailure: values.simulateFailure ?? false,
        },
        { 'Idempotency-Key': idempotencyKey },
      )
    },
    onSuccess: (payment) => {
      toast.success(payment.replayed ? 'Idempotent replay' : `${payment.reference} ${payment.status.toLowerCase()}`, {
        description: payment.replayed
          ? 'An existing payment was returned for this idempotency key.'
          : `${formatCurrency(payment.amount, payment.currency)} routed via the payment processor.`,
      })
      setCreateOpen(false)
      closeParam()
      form.reset({ ...form.getValues(), creditorName: '', creditorAccountNumber: '', creditorBic: '', description: '' })
      void queryClient.invalidateQueries({ queryKey: ['payments'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      navigate(`/payments/${payment.id}`)
    },
    onError: (error) => toast.error('Payment rejected', { description: describeError(error) }),
  })

  useEffect(() => {
    if (searchParams.get('new') === '1') setCreateOpen(true)
  }, [searchParams])

  function closeParam() {
    if (!searchParams.has('new')) return
    const next = new URLSearchParams(searchParams)
    next.delete('new')
    setSearchParams(next, { replace: true })
  }

  const columns = useMemo<Column<PaymentListItem>[]>(
    () => [
      {
        key: 'reference',
        header: 'Reference',
        cell: (row) => (
          <div>
            <p className="font-mono text-[11.5px] font-medium text-ink">{row.reference}</p>
            <p className="mt-0.5 font-mono text-[9px] uppercase tracking-[0.1em] text-muted">{row.type} · {row.channel}</p>
          </div>
        ),
      },
      {
        key: 'creditor',
        header: 'Beneficiary',
        cell: (row) => (
          <div className="max-w-[190px]">
            <p className="truncate text-[12px] text-ink-2">{row.creditorName}</p>
            <p className="mt-0.5 font-mono text-[9.5px] text-muted">{row.debtorAccountNumber}</p>
          </div>
        ),
      },
      { key: 'status', header: 'Status', cell: (row) => <StatusPill status={row.status} /> },
      {
        key: 'amount',
        header: 'Amount',
        align: 'right',
        cell: (row) => <span className="num text-[12.5px] font-medium text-ink">{formatCurrency(row.amount, row.currency)}</span>,
      },
      {
        key: 'risk',
        header: 'Risk',
        hideBelow: 'lg',
        cell: (row) => (
          <span className={cn('num text-[11px]', row.riskScore >= 65 ? 'text-danger' : row.riskScore >= 35 ? 'text-warning' : 'text-muted')}>
            {row.riskScore}
            <span className="ml-1 font-mono text-[8px] uppercase text-muted">/100</span>
          </span>
        ),
      },
      {
        key: 'duration',
        header: 'Processing',
        align: 'right',
        hideBelow: 'md',
        cell: (row) => <span className="font-mono text-[10.5px] text-muted">{formatMs(row.processingDurationMs)}</span>,
      },
      {
        key: 'created',
        header: 'Created',
        align: 'right',
        hideBelow: 'sm',
        cell: (row) => <span className="font-mono text-[10px] text-muted">{formatRelative(row.createdAt)}</span>,
      },
    ],
    [],
  )

  const stats = statsQuery.data

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        eyebrow="Payment operations"
        title="Transaction control"
        description="Every instruction, its lifecycle journey, ledger postings and investigation trail."
        actions={
          hasPermission('payments:write') ? (
            <Button size="sm" onClick={() => setCreateOpen(true)}>
              <Plus /> New payment
            </Button>
          ) : null
        }
      />

      {statsQuery.isLoading || !stats ? (
        <StatGridSkeleton count={4} />
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Payments (30d)" value={formatNumber(stats.totalCount)} hint={`${formatCurrency(stats.totalVolume, 'NZD', { compact: true })} settled value`} icon={<CreditCard />} featured />
          <StatCard label="Success rate" value={`${stats.successRatePct.toFixed(2)}%`} hint={`${formatNumber(stats.failedCount)} failed instructions`} icon={<BadgeCheck />} tone={stats.successRatePct >= 95 ? 'success' : 'warning'} />
          <StatCard label="Average processing" value={formatMs(stats.averageProcessingMs)} hint="end-to-end journey time" icon={<Timer />} />
          <StatCard label="In flight" value={formatNumber(stats.pendingCount)} hint="pending or processing" icon={<Wallet />} />
        </div>
      )}

      <Panel className="overflow-hidden">
        <div className="border-b border-line px-4 py-3">
          <Toolbar>
            <SearchInput value={search} onChange={(value) => { setSearch(value); setPage(1) }} placeholder="Search reference, beneficiary, correlation" className="w-full sm:w-72" />
            <Select value={status} onValueChange={(value) => { setStatus(value); setPage(1) }}>
              <SelectTrigger className="w-[150px]"><SelectValue placeholder="Status" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All statuses</SelectItem>
                {statusOptions.map((option) => <SelectItem key={option} value={option}>{option}</SelectItem>)}
              </SelectContent>
            </Select>
            <Select value={type} onValueChange={(value) => { setType(value); setPage(1) }}>
              <SelectTrigger className="w-[140px]"><SelectValue placeholder="Type" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All types</SelectItem>
                {typeOptions.map((option) => <SelectItem key={option} value={option}>{option}</SelectItem>)}
              </SelectContent>
            </Select>
            <Select value={sort} onValueChange={setSort}>
              <SelectTrigger className="w-[160px]"><SelectValue placeholder="Sort" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="-created">Newest first</SelectItem>
                <SelectItem value="created">Oldest first</SelectItem>
                <SelectItem value="-amount">Largest amount</SelectItem>
                <SelectItem value="reference">Reference</SelectItem>
                <SelectItem value="status">Status</SelectItem>
              </SelectContent>
            </Select>
            <span className="ml-auto font-mono text-[10px] uppercase tracking-[0.1em] text-muted">
              {listQuery.data ? `${formatNumber(listQuery.data.totalCount)} instructions` : '—'}
            </span>
          </Toolbar>
        </div>

        {listQuery.isError ? (
          <ErrorState error={listQuery.error} onRetry={() => void listQuery.refetch()} />
        ) : listQuery.isLoading ? (
          <TableSkeleton rows={8} columns={6} />
        ) : (
          <DataTable
            rows={listQuery.data?.items ?? []}
            columns={columns}
            getRowId={(row) => row.id}
            onRowClick={(row) => navigate(`/payments/${row.id}`)}
            caption="Payments"
            empty={
              <EmptyState
                title="No payments match the current filters"
                description="Adjust the search or status filter, or create a synthetic instruction."
                icon={<CreditCard />}
              />
            }
          />
        )}

        {listQuery.data && listQuery.data.totalCount > 0 ? (
          <Pagination page={listQuery.data.page} totalPages={listQuery.data.totalPages} totalCount={listQuery.data.totalCount} onPageChange={setPage} label="payments" />
        ) : null}
      </Panel>

      <div className="grid gap-4 md:grid-cols-2">
        <Panel className="p-4">
          <p className="mono-label">Status distribution</p>
          <div className="mt-2.5 space-y-1.5">
            {Object.entries(stats?.byStatus ?? {}).sort((a, b) => b[1] - a[1]).map(([key, value]) => {
              const total = stats?.totalCount || 1
              return (
                <div key={key} className="flex items-center gap-2">
                  <span className="w-24 shrink-0 text-[11.5px] text-ink-2">{key}</span>
                  <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-surface-2">
                    <div
                      className={cn('h-full rounded-full', toneForStatus(key) === 'success' ? 'bg-success' : toneForStatus(key) === 'danger' ? 'bg-danger' : toneForStatus(key) === 'warning' ? 'bg-warning' : 'bg-brand')}
                      style={{ width: `${Math.max(2, (value / total) * 100)}%` }}
                    />
                  </div>
                  <span className="w-9 shrink-0 text-right font-mono text-[10px] text-muted">{value}</span>
                </div>
              )
            })}
          </div>
        </Panel>
        <Panel className="p-4">
          <p className="mono-label">Instruction mix</p>
          <div className="mt-2.5 grid grid-cols-2 gap-2">
            {Object.entries(stats?.byType ?? {}).map(([key, value]) => (
              <div key={key} className="rounded-md border border-line bg-surface-2/50 px-2.5 py-2">
                <p className="font-mono text-[9px] uppercase tracking-[0.1em] text-muted">{key}</p>
                <p className="num mt-0.5 text-[15px] font-semibold text-ink">{formatNumber(value)}</p>
              </div>
            ))}
          </div>
        </Panel>
      </div>

      <Dialog
        open={createOpen}
        onOpenChange={(open) => {
          setCreateOpen(open)
          if (!open) closeParam()
        }}
      >
        <DialogContent
          title="Create payment instruction"
          description="Synthetic instruction with a single-use Idempotency-Key, double-entry postings and a full journey record."
          size="lg"
          footer={
            <>
              <Button variant="ghost" onClick={() => { setCreateOpen(false); closeParam() }}>Cancel</Button>
              <Button onClick={form.handleSubmit((values) => createMutation.mutate(values))} disabled={createMutation.isPending}>
                {createMutation.isPending ? 'Submitting…' : 'Submit payment'}
                <ArrowRight />
              </Button>
            </>
          }
        >
          <form className="grid gap-4 sm:grid-cols-2" onSubmit={form.handleSubmit((values) => createMutation.mutate(values))}>
            <Field label="Debit account" error={form.formState.errors.debtorAccountId?.message} className="sm:col-span-2">
              <Select value={form.watch('debtorAccountId')} onValueChange={(value) => form.setValue('debtorAccountId', value, { shouldValidate: true })}>
                <SelectTrigger>
                  <SelectValue placeholder={accountsQuery.isLoading ? 'Loading accounts…' : 'Select a synthetic account'} />
                </SelectTrigger>
                <SelectContent>
                  {(accountsQuery.data ?? []).map((account) => (
                    <SelectItem key={account.id} value={account.id}>
                      {account.customerName} · {account.maskedNumber} · {formatCurrency(account.availableBalance, account.currency, { compact: true })}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>

            <Field label="Beneficiary name" error={form.formState.errors.creditorName?.message}>
              <Input placeholder="Harbour Consulting Ltd" {...form.register('creditorName')} />
            </Field>
            <Field label="Beneficiary account" error={form.formState.errors.creditorAccountNumber?.message}>
              <Input placeholder="12-3141-0098765-00" {...form.register('creditorAccountNumber')} />
            </Field>
            <Field label="Amount" error={form.formState.errors.amount?.message}>
              <Input type="number" step="0.01" min="0.01" {...form.register('amount')} />
            </Field>
            <Field label="Currency">
              <Select value={form.watch('currency')} onValueChange={(value) => form.setValue('currency', value as PaymentValues['currency'])}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{['NZD', 'AUD', 'USD', 'GBP'].map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Field label="Instruction type">
              <Select value={form.watch('type')} onValueChange={(value) => form.setValue('type', value as PaymentValues['type'])}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{typeOptions.map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Field label="Beneficiary BIC" hint="Set for an external rail; leave blank for an internal transfer." error={form.formState.errors.creditorBic?.message}>
              <Input placeholder="ASBBNZ2A" {...form.register('creditorBic')} />
            </Field>
            <div className="sm:col-span-2">
              <Label htmlFor="payment-description">Description</Label>
              <Input id="payment-description" placeholder="Invoice INV-2026-0912" className="mt-1.5" {...form.register('description')} />
            </div>

            <div className="flex items-center justify-between gap-3 rounded-lg border border-line bg-surface-2/50 px-3 py-2.5 sm:col-span-2">
              <div className="flex items-start gap-2">
                <ShieldAlert className="mt-0.5 size-4 shrink-0 text-warning" />
                <div>
                  <p className="text-[12px] font-medium text-ink">Simulate a downstream failure</p>
                  <p className="text-[11px] text-muted">Fails the instruction at the processor or gateway so the investigation flow can be demonstrated.</p>
                </div>
              </div>
              <Switch checked={form.watch('simulateFailure') ?? false} onCheckedChange={(value) => form.setValue('simulateFailure', value)} />
            </div>

            <p className="flex items-center gap-1.5 text-[10.5px] text-muted sm:col-span-2">
              <Landmark className="size-3" /> Funds are synthetic. Ledger postings are balanced and never leave this sandbox.
            </p>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  )
}
