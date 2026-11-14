import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ChevronLeft } from 'lucide-react'
import { cn } from '@/lib/utils'

export function PageHeader({
  title,
  description,
  eyebrow,
  actions,
  backTo,
  backLabel,
  className,
  children,
}: {
  title: ReactNode
  description?: ReactNode
  eyebrow?: string
  actions?: ReactNode
  backTo?: string
  backLabel?: string
  className?: string
  children?: ReactNode
}) {
  return (
    <div className={cn('space-y-3', className)}>
      {backTo ? (
        <Link
          to={backTo}
          className="inline-flex items-center gap-1 font-mono text-[10.5px] uppercase tracking-[0.14em] text-muted transition-colors hover:text-ink"
        >
          <ChevronLeft className="size-3" />
          {backLabel ?? 'Back'}
        </Link>
      ) : null}
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          {eyebrow ? <p className="mono-label mb-1.5">{eyebrow}</p> : null}
          <h1 className="text-[22px] font-semibold leading-tight tracking-tight text-ink sm:text-[26px]">{title}</h1>
          {description ? <p className="mt-1 max-w-2xl text-[13px] leading-relaxed text-muted">{description}</p> : null}
        </div>
        {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
      </div>
      {children}
    </div>
  )
}
