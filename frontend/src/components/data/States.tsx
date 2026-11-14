import type { ReactNode } from 'react'
import { AlertTriangle, Inbox, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/misc'
import { ApiError } from '@/lib/api'
import { cn } from '@/lib/utils'

export function EmptyState({
  title,
  description,
  icon,
  action,
  className,
}: {
  title: string
  description?: ReactNode
  icon?: ReactNode
  action?: ReactNode
  className?: string
}) {
  return (
    <div className={cn('flex flex-col items-center justify-center gap-3 px-6 py-12 text-center', className)}>
      <span className="flex size-11 items-center justify-center rounded-xl border border-line bg-surface-2 text-muted [&_svg]:size-5">
        {icon ?? <Inbox />}
      </span>
      <div className="space-y-1">
        <p className="text-[13.5px] font-medium text-ink">{title}</p>
        {description ? <p className="mx-auto max-w-sm text-[12px] leading-relaxed text-muted">{description}</p> : null}
      </div>
      {action}
    </div>
  )
}

export function ErrorState({ error, onRetry, className }: { error: unknown; onRetry?: () => void; className?: string }) {
  const apiError = error instanceof ApiError ? error : undefined
  const message = apiError?.message ?? (error instanceof Error ? error.message : 'The request could not be completed.')

  return (
    <div className={cn('flex flex-col items-center justify-center gap-3 px-6 py-12 text-center', className)} role="alert">
      <span className="flex size-11 items-center justify-center rounded-xl border border-danger/30 bg-danger-soft text-danger [&_svg]:size-5">
        <AlertTriangle />
      </span>
      <div className="space-y-1">
        <p className="text-[13.5px] font-medium text-ink">Something went wrong</p>
        <p className="mx-auto max-w-md text-[12px] leading-relaxed text-muted">{message}</p>
        {apiError?.correlationId ? (
          <p className="font-mono text-[10.5px] text-muted">correlation {apiError.correlationId}</p>
        ) : null}
      </div>
      {onRetry ? (
        <Button variant="secondary" size="sm" onClick={onRetry}>
          <RefreshCw /> Retry
        </Button>
      ) : null}
    </div>
  )
}

export function LoadingPanel({ rows = 4, className }: { rows?: number; className?: string }) {
  return (
    <div className={cn('space-y-2.5 p-4', className)} aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }).map((_, index) => (
        <Skeleton key={index} className={cn('h-3.5', index === 0 ? 'w-1/3' : index % 2 === 0 ? 'w-11/12' : 'w-3/4')} />
      ))}
    </div>
  )
}

export function StatGridSkeleton({ count = 4 }: { count?: number }) {
  return (
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      {Array.from({ length: count }).map((_, index) => (
        <div key={index} className="panel space-y-3 p-4">
          <Skeleton className="h-2.5 w-24" />
          <Skeleton className="h-7 w-32" />
          <Skeleton className="h-2.5 w-20" />
        </div>
      ))}
    </div>
  )
}
