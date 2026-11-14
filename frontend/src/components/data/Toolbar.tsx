import { useEffect, useState, type ReactNode } from 'react'
import { Search, X } from 'lucide-react'
import { cn } from '@/lib/utils'

export function SearchInput({
  value,
  onChange,
  placeholder = 'Search',
  className,
  ariaLabel,
}: {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  className?: string
  ariaLabel?: string
}) {
  const [local, setLocal] = useState(value)

  useEffect(() => setLocal(value), [value])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (local !== value) onChange(local)
    }, 280)
    return () => window.clearTimeout(timer)
  }, [local, onChange, value])

  return (
    <div className={cn('relative', className)}>
      <Search className="pointer-events-none absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted" />
      <input
        type="search"
        value={local}
        aria-label={ariaLabel ?? placeholder}
        onChange={(event) => setLocal(event.target.value)}
        placeholder={placeholder}
        className="h-9 w-full rounded-lg border border-line bg-surface pl-8 pr-8 text-[12.5px] text-ink placeholder:text-muted/70 transition-colors hover:border-line-strong focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25"
      />
      {local ? (
        <button
          type="button"
          aria-label="Clear search"
          onClick={() => {
            setLocal('')
            onChange('')
          }}
          className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-0.5 text-muted hover:text-ink"
        >
          <X className="size-3.5" />
        </button>
      ) : null}
    </div>
  )
}

export function Toolbar({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('flex flex-wrap items-center gap-2', className)}>{children}</div>
}

export function FilterLabel({ children }: { children: ReactNode }) {
  return <span className="font-mono text-[10px] uppercase tracking-[0.12em] text-muted">{children}</span>
}
