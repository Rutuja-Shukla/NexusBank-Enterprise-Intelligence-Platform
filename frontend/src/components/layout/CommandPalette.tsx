import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Activity, ArrowRight, CreditCard, Gauge, Network, Search, ServerCog, ShieldCheck, Siren, Waypoints, Plus, Radio,
} from 'lucide-react'
import { Dialog, DialogContent } from '@/components/ui/dialog'
import { cn } from '@/lib/utils'

interface Command {
  id: string
  label: string
  hint: string
  to: string
  icon: typeof Search
  keywords?: string
}

const commands: Command[] = [
  { id: 'dashboard', label: 'Command centre', hint: 'Operate', to: '/', icon: Gauge, keywords: 'home overview dashboard' },
  { id: 'integrations', label: 'Integration hub', hint: 'Operate', to: '/integrations', icon: Network, keywords: 'topology services' },
  { id: 'payments', label: 'Payment operations', hint: 'Operate', to: '/payments', icon: CreditCard, keywords: 'transactions refund' },
  { id: 'events', label: 'Event streaming', hint: 'Operate', to: '/events', icon: Radio, keywords: 'kafka topics dlq' },
  { id: 'incidents', label: 'Incidents', hint: 'Operate', to: '/incidents', icon: Siren, keywords: 'sev timeline' },
  { id: 'health', label: 'System health', hint: 'Operate', to: '/health', icon: ServerCog, keywords: 'dependencies redis kafka' },
  { id: 'observability', label: 'Observability', hint: 'Analyse', to: '/observability', icon: Activity, keywords: 'traces logs latency errors' },
  { id: 'explorer', label: 'API explorer', hint: 'Develop', to: '/explorer', icon: Waypoints, keywords: 'swagger rest request' },
  { id: 'security', label: 'Security centre', hint: 'Admin', to: '/security', icon: ShieldCheck, keywords: 'users roles audit' },
  { id: 'new-payment', label: 'Create a payment', hint: 'Action', to: '/payments?new=1', icon: Plus, keywords: 'send transfer' },
  { id: 'publish-event', label: 'Publish an event', hint: 'Action', to: '/events?publish=1', icon: Radio, keywords: 'kafka produce' },
  { id: 'raise-incident', label: 'Raise an incident', hint: 'Action', to: '/incidents?new=1', icon: Siren, keywords: 'create sev' },
]

export function CommandPalette({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [activeIndex, setActiveIndex] = useState(0)

  const results = useMemo(() => {
    const term = query.trim().toLowerCase()
    if (!term) return commands
    return commands.filter((command) =>
      `${command.label} ${command.hint} ${command.keywords ?? ''}`.toLowerCase().includes(term),
    )
  }, [query])

  useEffect(() => {
    if (!open) {
      setQuery('')
      setActiveIndex(0)
    }
  }, [open])

  useEffect(() => {
    setActiveIndex(0)
  }, [query])

  function run(command: Command | undefined) {
    if (!command) return
    navigate(command.to)
    onOpenChange(false)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title="Command palette" description="Jump to a workspace or start an action." size="md" className="p-0">
        <div className="-mx-5 -my-4">
          <div className="flex items-center gap-2 border-b border-line px-4 py-3">
            <Search className="size-4 shrink-0 text-muted" />
            <input
              autoFocus
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'ArrowDown') {
                  event.preventDefault()
                  setActiveIndex((index) => Math.min(index + 1, results.length - 1))
                }
                if (event.key === 'ArrowUp') {
                  event.preventDefault()
                  setActiveIndex((index) => Math.max(index - 1, 0))
                }
                if (event.key === 'Enter') {
                  event.preventDefault()
                  run(results[activeIndex])
                }
              }}
              placeholder="Search workspaces, actions…"
              aria-label="Search commands"
              className="h-7 w-full bg-transparent text-[14px] text-ink placeholder:text-muted focus:outline-none"
            />
            <kbd className="hidden rounded border border-line bg-surface-2 px-1.5 py-0.5 font-mono text-[9.5px] text-muted sm:block">ESC</kbd>
          </div>

          <ul className="max-h-80 overflow-y-auto p-2" role="listbox" aria-label="Commands">
            {results.length === 0 ? (
              <li className="px-3 py-6 text-center text-[12.5px] text-muted">No matching commands.</li>
            ) : (
              results.map((command, index) => {
                const Icon = command.icon
                return (
                  <li key={command.id}>
                    <button
                      type="button"
                      role="option"
                      aria-selected={index === activeIndex}
                      onMouseEnter={() => setActiveIndex(index)}
                      onClick={() => run(command)}
                      className={cn(
                        'flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left transition-colors',
                        index === activeIndex ? 'bg-brand-soft text-ink' : 'text-ink-2 hover:bg-surface-2',
                      )}
                    >
                      <Icon className={cn('size-4 shrink-0', index === activeIndex ? 'text-brand-text' : 'text-muted')} />
                      <span className="flex-1 truncate text-[13px] font-medium">{command.label}</span>
                      <span className="font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">{command.hint}</span>
                      <ArrowRight className="size-3.5 shrink-0 text-muted" />
                    </button>
                  </li>
                )
              })
            )}
          </ul>
          <div className="flex items-center justify-between border-t border-line px-4 py-2">
            <span className="font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">↑↓ navigate · ↵ open</span>
            <span className="font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted">NexusBank console</span>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
