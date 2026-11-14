import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { AlertOctagon, ArrowRight, Banknote, CircleCheck, CornerUpLeft, FileText, Landmark, Scale, ShieldAlert, Timer } from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { ErrorState, LoadingPanel } from '@/components/data/States'
import { JourneyTimeline } from '@/components/payments/JourneyTimeline'
import { Badge, StatusPill } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel, PanelHeader } from '@/components/ui/card'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { Field, Input, Label } from '@/components/ui/input'
import { useAuth } from '@/hooks/useAuth'
import { api } from '@/lib/api'
import { cn, formatCurrency, formatDateTime, formatMs, formatRelative, riskTone } from '@/lib/utils'
import type { PaymentCreated, PaymentDetail } from '@/types'

export function PaymentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const [refundOpen, setRefundOpen] = useState(false)
  const [reverseOpen, setReverseOpen] = useState(false)
  const [refundAmount, setRefundAmount] = useState('')
  const [refundReason, setRefundReason] = useState('')
  const [reverseReason, setReverseReason] = useState('')

  const paymentQuery = useQuery({
    queryKey: ['payment', id],
    queryFn: () => api.get<PaymentDetail>(`/api/v1/payments/${id}`),
    enabled: Boolean(id),
  })

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['payment', id] })
    void queryClient.invalidateQueries({ queryKey: ['payments'] })
    void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }

  const refundMutation = useMutation({
    mutationFn: () =>
      api.post<PaymentCreated>(`/api/v1/payments/${id}/refund`, {
        amount: refundAmount ? Number(refundAmount) : undefined,
        reason: refundReason,
      }),
    onSuccess: (created) => {
      toast.success('Refund settled', { description: `${created.reference} · ${formatCurrency(created.amount, created.currency)}` })
      setRefundOpen(false)
      setRefundAmount('')
      setRefundReason('')
      refresh()
    },
    onError: (error) => toast.error('Refund rejected', { description: error.message }),
  })

  const reverseMutation = useMutation({
    mutationFn: () => api.post<PaymentDetail>(`/api/v1/payments/${id}/reverse`, { reason: reverseReason }),
    onSuccess: () => {
      toast.success('Payment reversed', { description: 'Compensating ledger entries have been posted.' })
      setReverseOpen(false)
      setReverseReason('')
      refresh()
    },
    onError: (error) => toast.error('Reversal rejected', { description: error.message }),
  })

  if (paymentQuery.isError) {
    return <Panel className="mt-4"><ErrorState error={paymentQuery.error} onRetry={() => void paymentQuery.refetch()} /></Panel>
  }

  if (paymentQuery.isLoading || !paymentQuery.data) {
    return (
      <div className="space-y-4">
        <PageHeader backTo="/payments" backLabel="Payments" eyebrow="Payment detail" title="Loading instruction…" />
        <Panel><LoadingPanel rows={8} /></Panel>
      </div>
    )
  }

  const payment = paymentQuery.data
  const canWrite = hasPermission('payments:write')
  const canRefund = canWrite && payment.status === 'Completed'
  const canReverse = canWrite && payment.status === 'Completed'

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        backTo="/payments"
        backLabel="Payments"
        eyebrow={`${payment.type} · ${payment.channel} · ${formatRelative(payment.createdAt)}`}
        title={
          <span className="flex flex-wrap items-center gap-2.5">
            <span className="font-mono">{payment.reference}</span>
            <StatusPill status={payment.status} pulse={payment.status === 'Processing'} />
            {payment.ledgerBalanced ? <Badge tone="success">ledger balanced</Badge> : <Badge tone="danger">ledger imbalance</Badge>}
          </span>
        }
        description={payment.description ?? `${payment.creditorName} · ${payment.integrationName ?? 'routed internally'}`}
        actions={
          <>
            {canReverse ? (
              <Button variant="secondary" size="sm" onClick={() => setReverseOpen(true)}>
                <CornerUpLeft /> Reverse
              </Button>
            ) : null}
            {canRefund ? (
              <Button size="sm" onClick={() => { setRefundAmount(payment.amount.toFixed(2)); setRefundOpen(true) }}>
                <Banknote /> Refund
              </Button>
            ) : null}
          </>
        }
      />

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <Panel>
          <PanelHeader
            eyebrow="Signature view"
            title="Transaction journey"
            description="Each hop through the banking estate with measured processing time."
            icon={<ArrowRight />}
            actions={<Badge tone="brand">{payment.journey.length} steps</Badge>}
          />
          <div className="p-4">
            <div className="mb-4 flex flex-wrap items-end justify-between gap-3 rounded-lg border border-line bg-surface-2/40 px-3 py-2.5">
              <div>
                <p className="mono-label">Amount</p>
                <p className="num mt-0.5 text-[24px] font-semibold leading-none text-ink">{formatCurrency(payment.amount, payment.currency)}</p>
              </div>
              <div className="text-right">
                <p className="mono-label">End-to-end</p>
                <p className="num mt-0.5 text-[15px] font-medium text-ink">{formatMs(payment.processingDurationMs)}</p>
              </div>
              <div className="text-right">
                <p className="mono-label">Completed</p>
                <p className="mt-0.5 text-[11.5px] text-ink-2">{formatDateTime(payment.completedAt)}</p>
              </div>
            </div>
            <JourneyTimeline steps={payment.journey} />
          </div>
        </Panel>

        <div className="space-y-4">
          <Panel>
            <PanelHeader title="Instruction" description="Masked identifiers only" icon={<FileText />} />
            <dl className="grid gap-x-4 gap-y-3 p-4 sm:grid-cols-2 xl:grid-cols-1">
              <Detail label="Debtor account" value={payment.debtorAccountNumber} mono />
              <Detail label="Beneficiary" value={payment.creditorName} />
              <Detail label="Beneficiary account" value={payment.creditorAccountNumber} mono />
              <Detail label="Beneficiary BIC" value={payment.creditorBic ?? 'Internal transfer'} mono />
              <Detail label="Routed via" value={payment.integrationName ?? '—'} />
              <Detail label="Risk score" value={`${payment.riskScore}/100`} tone={riskTone(payment.riskScore)} />
              <Detail label="Correlation" value={payment.correlationId} mono />
              <Detail label="Idempotency key" value={payment.idempotencyKey} mono />
              <Detail label="Created" value={formatDateTime(payment.createdAt)} />
              <Detail label="Last update" value={formatDateTime(payment.updatedAt)} />
            </dl>
          </Panel>

          {payment.status === 'Failed' ? (
            <Panel className="border-danger/30">
              <PanelHeader title="Failure investigation" description="Downstream rejection detail" icon={<AlertOctagon />} />
              <div className="space-y-2 p-4">
                <p className="font-mono text-[10px] uppercase tracking-[0.12em] text-danger">{payment.failureCode}</p>
                <p className="text-[12.5px] leading-relaxed text-ink-2">{payment.failureReason}</p>
                <p className="rounded-md border border-line bg-surface-2 px-2.5 py-2 text-[11px] leading-relaxed text-muted">
                  Retries exhausted under the configured resilience policy. Investigate the integration health history and replay the
                  instruction once the upstream recovers.
                </p>
              </div>
            </Panel>
          ) : null}

          <Panel>
            <PanelHeader
              title="Ledger postings"
              description="Simplified double-entry"
              icon={<Scale />}
              actions={payment.ledgerBalanced ? <Badge tone="success">debits = credits</Badge> : <Badge tone="danger">imbalance</Badge>}
            />
            <div className="divide-y divide-line">
              {payment.ledgerEntries.length === 0 ? (
                <p className="px-4 py-5 text-center text-[11.5px] text-muted">No postings — the instruction did not reach settlement.</p>
              ) : (
                payment.ledgerEntries.map((entry, index) => (
                  <div key={`${entry.entryType}-${index}`} className="flex items-start justify-between gap-3 px-4 py-2.5">
                    <div className="min-w-0">
                      <div className="flex items-center gap-1.5">
                        <Badge tone={entry.entryType === 'Debit' ? 'danger' : 'success'}>{entry.entryType}</Badge>
                        <span className="font-mono text-[11px] text-ink-2">{entry.accountNumber}</span>
                      </div>
                      <p className="mt-0.5 truncate text-[10.5px] text-muted">{entry.description}</p>
                    </div>
                    <span className="num shrink-0 text-[12px] font-medium text-ink">{formatCurrency(entry.amount, entry.currency)}</span>
                  </div>
                ))
              )}
            </div>
            {payment.ledgerEntries.length > 0 ? (
              <div className="flex items-center justify-between border-t border-line px-4 py-2.5 font-mono text-[10px] uppercase tracking-[0.1em] text-muted">
                <span>Dr {formatCurrency(payment.ledgerDebitTotal, payment.currency)} · Cr {formatCurrency(payment.ledgerCreditTotal, payment.currency)}</span>
                <CircleCheck className={cn('size-3.5', payment.ledgerBalanced ? 'text-success' : 'text-danger')} />
              </div>
            ) : null}
          </Panel>
        </div>
      </div>

      <Dialog open={refundOpen} onOpenChange={setRefundOpen}>
        <DialogContent
          title="Refund payment"
          description="Creates a new refund instruction and credits the original debtor account."
          size="sm"
          footer={
            <>
              <Button variant="ghost" onClick={() => setRefundOpen(false)}>Cancel</Button>
              <Button onClick={() => refundMutation.mutate()} disabled={refundMutation.isPending}>
                {refundMutation.isPending ? 'Processing…' : 'Settle refund'}
              </Button>
            </>
          }
        >
          <div className="space-y-3">
            <Field label="Refund amount" hint={`Original amount ${formatCurrency(payment.amount, payment.currency)}`}>
              <Input type="number" step="0.01" value={refundAmount} onChange={(event) => setRefundAmount(event.target.value)} />
            </Field>
            <div>
              <Label htmlFor="refund-reason">Reason</Label>
              <Input id="refund-reason" className="mt-1.5" placeholder="Beneficiary dispute resolved" value={refundReason} onChange={(event) => setRefundReason(event.target.value)} />
            </div>
            <p className="flex items-start gap-1.5 rounded-md border border-line bg-surface-2/60 px-2.5 py-2 text-[11px] leading-relaxed text-muted">
              <ShieldAlert className="mt-0.5 size-3.5 shrink-0" />
              Refunds require a completed payment and are recorded in the audit trail with your identity.
            </p>
          </div>
        </DialogContent>
      </Dialog>

      <Dialog open={reverseOpen} onOpenChange={setReverseOpen}>
        <DialogContent
          title="Reverse payment"
          description="Reversal posts compensating ledger entries and marks the instruction as Reversed."
          size="sm"
          footer={
            <>
              <Button variant="ghost" onClick={() => setReverseOpen(false)}>Cancel</Button>
              <Button variant="danger" onClick={() => reverseMutation.mutate()} disabled={reverseMutation.isPending}>
                {reverseMutation.isPending ? 'Reversing…' : 'Confirm reversal'}
              </Button>
            </>
          }
        >
          <div className="space-y-3">
            <div className="flex items-center gap-2 rounded-md border border-line bg-surface-2/60 px-2.5 py-2">
              <Timer className="size-3.5 text-muted" />
              <span className="text-[11.5px] text-ink-2">Only completed payments can be reversed.</span>
            </div>
            <div>
              <Label htmlFor="reverse-reason">Investigation reason</Label>
              <Input id="reverse-reason" className="mt-1.5" placeholder="Beneficiary account closed" value={reverseReason} onChange={(event) => setReverseReason(event.target.value)} />
            </div>
            <p className="flex items-start gap-1.5 text-[11px] leading-relaxed text-muted">
              <Landmark className="mt-0.5 size-3.5 shrink-0" />
              Reversals are compensating entries — the original postings are never edited or deleted.
            </p>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  )
}

function Detail({ label, value, mono, tone }: { label: string; value: string; mono?: boolean; tone?: 'success' | 'warning' | 'danger' | 'brand' | 'neutral' | 'info' }) {
  return (
    <div className="min-w-0">
      <dt className="mono-label">{label}</dt>
      <dd className={cn('mt-0.5 truncate text-[12.5px]', mono ? 'font-mono text-[11.5px]' : '', tone === 'danger' ? 'text-danger' : tone === 'warning' ? 'text-warning' : tone === 'success' ? 'text-success' : 'text-ink-2')}>
        {value}
      </dd>
    </div>
  )
}
