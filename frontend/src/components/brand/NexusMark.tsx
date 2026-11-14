import { cn } from '@/lib/utils'

export function NexusMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" className={cn('size-8 shrink-0', className)} role="img" aria-label="NexusBank">
      <rect width="32" height="32" rx="8.5" fill="#15171C" />
      <rect x="0.6" y="0.6" width="30.8" height="30.8" rx="8" fill="none" stroke="rgba(245,196,0,0.28)" strokeWidth="0.8" />
      <path d="M9 23V9h3.2l6.6 9.1V9H22v14h-3.2l-6.6-9.1V23z" fill="#F5C400" />
      <circle cx="25.2" cy="22.6" r="1.9" fill="#F5C400" />
    </svg>
  )
}

export function NexusWordmark({ className, showTag = true }: { className?: string; showTag?: boolean }) {
  return (
    <div className={cn('flex items-center gap-2.5', className)}>
      <NexusMark />
      <div className="leading-none">
        <div className="font-display text-[15px] font-semibold tracking-[0.08em] text-ink">
          NEXUS<span className="font-light text-ink-2">BANK</span>
        </div>
        {showTag ? <div className="mt-1 font-mono text-[8.5px] uppercase tracking-[0.28em] text-muted">Integration Platform</div> : null}
      </div>
    </div>
  )
}
