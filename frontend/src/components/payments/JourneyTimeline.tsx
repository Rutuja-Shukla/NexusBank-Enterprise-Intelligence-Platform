import { AlertOctagon, ArrowRight, Check, Clock, Loader2, MinusCircle, X } from 'lucide-react'
import type { PaymentStep } from '@/types'
import { cn, formatMs, formatTime, titleCase, toneForStatus } from '@/lib/utils'

const stepLabels: Record<string, string> = {
  Initiated: 'Initiated',
  Validation: 'Validation',
  AccountLookup: 'Account lookup',
  PaymentProcessing: 'Payment processing',
  ExternalGateway: 'External gateway',
  Confirmation: 'Confirmation',
  Reversal: 'Reversal',
}

function StepIcon({ status }: { status: PaymentStep['status'] }) {
  if (status === 'Succeeded') return <Check className="size-3" />
  if (status === 'Failed') return <X className="size-3" />
  if (status === 'Running') return <Loader2 className="size-3 animate-spin" />
  if (status === 'Skipped') return <MinusCircle className="size-3" />
  return <Clock className="size-3" />
}

export function JourneyTimeline({ steps }: { steps: PaymentStep[] }) {
  if (steps.length === 0) {
    return <p className="px-4 py-6 text-center text-[12.5px] text-muted">No journey steps have been recorded for this payment.</p>
  }

  const failed = steps.find((step) => step.status === 'Failed')

  return (
    <div className="space-y-4">
      <div className="flex gap-2 overflow-x-auto pb-2" role="list" aria-label="Payment journey">
        {steps.map((step, index) => {
          const tone = toneForStatus(step.status)
          const isLast = index === steps.length - 1
          return (
            <div key={`${step.sequence}-${step.step}`} className="flex min-w-[152px] flex-1 items-start gap-1.5" role="listitem">
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-1.5">
                  <span
                    className={cn(
                      'flex size-5 shrink-0 items-center justify-center rounded-full border text-[10px] font-semibold',
                      tone === 'success' && 'border-success/40 bg-success-soft text-success',
                      tone === 'danger' && 'border-danger/40 bg-danger-soft text-danger',
                      tone === 'info' && 'border-info/40 bg-info-soft text-info',
                      tone === 'warning' && 'border-warning/40 bg-warning-soft text-warning',
                      tone === 'neutral' && 'border-line bg-surface-2 text-muted',
                    )}
                    title={step.status}
                  >
                    <StepIcon status={step.status} />
                  </span>
                  <span className="h-px flex-1 bg-line" aria-hidden />
                  {!isLast ? <ArrowRight className="size-3 shrink-0 text-muted/60" aria-hidden /> : null}
                </div>
                <div
                  className={cn(
                    'mt-2 rounded-lg border px-2.5 py-2 transition-colors',
                    step.status === 'Failed' ? 'border-danger/35 bg-danger-soft/40' : 'border-line bg-surface-2/50',
                  )}
                >
                  <p className="truncate text-[12px] font-medium text-ink">{stepLabels[step.step] ?? titleCase(step.step)}</p>
                  <p className="mt-0.5 truncate font-mono text-[9.5px] uppercase tracking-[0.08em] text-muted">{step.serviceName}</p>
                  <p className="num mt-1.5 text-[11px] text-ink-2">
                    {formatMs(step.durationMs)} <span className="text-muted">· +{formatMs(step.offsetMs)}</span>
                  </p>
                  <p className="mt-0.5 font-mono text-[9px] text-muted">{formatTime(step.timestamp)}</p>
                </div>
              </div>
            </div>
          )
        })}
      </div>

      {failed ? (
        <div className="flex gap-2.5 rounded-lg border border-danger/30 bg-danger-soft/50 px-3 py-2.5">
          <AlertOctagon className="mt-0.5 size-4 shrink-0 text-danger" />
          <div className="min-w-0 space-y-0.5">
            <p className="text-[12px] font-medium text-danger">
              Failed at {stepLabels[failed.step] ?? failed.step} · {failed.serviceName}
            </p>
            <p className="text-[11.5px] leading-relaxed text-ink-2">{failed.errorDetail ?? failed.detail ?? 'The step reported a failure without a detail message.'}</p>
          </div>
        </div>
      ) : null}

      <div className="grid gap-2 sm:grid-cols-2">
        {steps.map((step) => (
          <div key={`detail-${step.sequence}`} className="rounded-md border border-line bg-surface px-3 py-2">
            <div className="flex items-center justify-between gap-2">
              <p className="mono-label">
                {step.sequence}. {stepLabels[step.step] ?? step.step}
              </p>
              <span className="font-mono text-[9.5px] text-muted">{formatMs(step.durationMs)}</span>
            </div>
            <p className="mt-1 text-[11.5px] leading-relaxed text-ink-2">{step.detail ?? '—'}</p>
          </div>
        ))}
      </div>
    </div>
  )
}
