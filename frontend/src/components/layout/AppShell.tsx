import { useEffect, useMemo, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import {
  Activity, Bell, ChevronRight, CreditCard, Gauge, LogOut, Menu, Moon, Network, Radio, Search, ServerCog, ShieldCheck,
  Siren, Sun, UserRound, Waypoints, X,
} from 'lucide-react'
import { NexusWordmark } from '@/components/brand/NexusMark'
import { CommandPalette } from '@/components/layout/CommandPalette'
import { StatusPill } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/misc'
import { useAuth } from '@/hooks/useAuth'
import { useTheme } from '@/hooks/useTheme'
import { api } from '@/lib/api'
import { cn, formatRelative, initials } from '@/lib/utils'
import type { AlertResponse, SystemHealth } from '@/types'

interface NavItem {
  to: string
  label: string
  icon: typeof Gauge
  permission?: string
  badgeKey?: 'incidents' | 'dlq'
}

const navGroups: Array<{ label: string; items: NavItem[] }> = [
  {
    label: 'Operate',
    items: [
      { to: '/', label: 'Command centre', icon: Gauge },
      { to: '/integrations', label: 'Integration hub', icon: Network, permission: 'integrations:read' },
      { to: '/payments', label: 'Payments', icon: CreditCard, permission: 'payments:read' },
      { to: '/events', label: 'Event stream', icon: Radio, permission: 'events:read', badgeKey: 'dlq' },
      { to: '/incidents', label: 'Incidents', icon: Siren, permission: 'incidents:read', badgeKey: 'incidents' },
      { to: '/health', label: 'System health', icon: ServerCog, permission: 'health:read' },
    ],
  },
  {
    label: 'Analyse',
    items: [{ to: '/observability', label: 'Observability', icon: Activity, permission: 'observability:read' }],
  },
  {
    label: 'Develop',
    items: [{ to: '/explorer', label: 'API explorer', icon: Waypoints, permission: 'explorer:execute' }],
  },
  {
    label: 'Admin',
    items: [{ to: '/security', label: 'Security centre', icon: ShieldCheck, permission: 'security:read' }],
  },
]

export function AppShell() {
  const { user, signOut, hasPermission } = useAuth()
  const { resolved, setTheme } = useTheme()
  const location = useLocation()
  const navigate = useNavigate()
  const [paletteOpen, setPaletteOpen] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)

  const healthQuery = useQuery({
    queryKey: ['system-health'],
    queryFn: () => api.get<SystemHealth>('/api/v1/system/health'),
    refetchInterval: 30_000,
  })

  const alertsQuery = useQuery({
    queryKey: ['alerts'],
    queryFn: () => api.get<AlertResponse>('/api/v1/observability/alerts'),
    refetchInterval: 45_000,
    enabled: hasPermission('observability:read'),
  })

  const incidentsQuery = useQuery({
    queryKey: ['incidents', 'open-count'],
    queryFn: () => api.get<{ items: unknown[] }>('/api/v1/incidents?openOnly=true&pageSize=50'),
    refetchInterval: 60_000,
    enabled: hasPermission('incidents:read'),
  })

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setPaletteOpen((open) => !open)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])

  useEffect(() => {
    setMobileOpen(false)
  }, [location.pathname])

  const counts = useMemo(
    () => ({
      incidents: incidentsQuery.data?.items.length ?? 0,
      dlq: healthQuery.data?.deadLetterCount ?? 0,
    }),
    [incidentsQuery.data, healthQuery.data],
  )

  const alerts = alertsQuery.data?.alerts ?? []
  const criticalAlerts = alerts.filter((alert) => alert.severity === 'critical').length
  const overall = healthQuery.data?.overallStatus ?? 'unknown'

  const pageTitle = useMemo(() => {
    const path = location.pathname
    if (path === '/') return 'Command centre'
    if (path.startsWith('/integrations')) return path === '/integrations' ? 'Integration hub' : 'Integration record'
    if (path.startsWith('/payments')) return path === '/payments' ? 'Payment operations' : 'Payment detail'
    if (path.startsWith('/events')) return 'Event streaming'
    if (path.startsWith('/incidents')) return path === '/incidents' ? 'Incident management' : 'Incident detail'
    if (path.startsWith('/observability')) return 'Observability'
    if (path.startsWith('/security')) return 'Security centre'
    if (path.startsWith('/health')) return 'System health'
    if (path.startsWith('/explorer')) return 'API explorer'
    return 'NexusBank'
  }, [location.pathname])

  const visibleGroups = navGroups
    .map((group) => ({ ...group, items: group.items.filter((item) => !item.permission || hasPermission(item.permission)) }))
    .filter((group) => group.items.length > 0)

  const sidebar = (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between px-4 pb-4 pt-5">
        <NavLink to="/" className="rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand/70">
          <NexusWordmark />
        </NavLink>
        <button type="button" onClick={() => setMobileOpen(false)} className="rounded-md p-1 text-muted hover:text-ink lg:hidden" aria-label="Close navigation">
          <X className="size-4" />
        </button>
      </div>

      <div className="mx-3 mb-4 flex items-center justify-between rounded-lg border border-line bg-surface-2/60 px-2.5 py-1.5">
        <span className="font-mono text-[9px] uppercase tracking-[0.16em] text-muted">Environment</span>
        <span className="rounded bg-brand-soft px-1.5 py-0.5 font-mono text-[9px] uppercase tracking-[0.12em] text-brand-text">Sandbox</span>
      </div>

      <nav className="flex-1 space-y-5 overflow-y-auto px-3 pb-4" aria-label="Primary">
        {visibleGroups.map((group) => (
          <div key={group.label}>
            <p className="px-2 pb-1.5 font-mono text-[9px] uppercase tracking-[0.18em] text-muted/80">{group.label}</p>
            <ul className="space-y-0.5">
              {group.items.map((item) => {
                const Icon = item.icon
                const badge = item.badgeKey ? counts[item.badgeKey] : 0
                return (
                  <li key={item.to}>
                    <NavLink
                      to={item.to}
                      end={item.to === '/'}
                      className={({ isActive }) =>
                        cn(
                          'group relative flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-[12.5px] font-medium transition-colors',
                          isActive ? 'nav-active text-ink' : 'text-ink-2 hover:bg-surface-2 hover:text-ink',
                        )
                      }
                    >
                      {({ isActive }) => (
                        <>
                          <span
                            className={cn(
                              'absolute left-0 top-1/2 h-4 w-[2px] -translate-y-1/2 rounded-full transition-opacity',
                              isActive ? 'bg-brand opacity-100' : 'opacity-0',
                            )}
                            aria-hidden
                          />
                          <Icon className={cn('size-4 shrink-0', isActive ? 'text-brand-text' : 'text-muted group-hover:text-ink-2')} />
                          <span className="flex-1 truncate">{item.label}</span>
                          {badge > 0 ? (
                            <span
                              className={cn(
                                'rounded px-1 py-0.5 font-mono text-[9px] tabular-nums',
                                item.badgeKey === 'incidents' ? 'bg-danger-soft text-danger' : 'bg-warning-soft text-warning',
                              )}
                            >
                              {badge}
                            </span>
                          ) : null}
                        </>
                      )}
                    </NavLink>
                  </li>
                )
              })}
            </ul>
          </div>
        ))}
      </nav>

      <div className="border-t border-line px-3 py-3">
        <button
          type="button"
          onClick={() => setPaletteOpen(true)}
          className="mb-2 flex w-full items-center gap-2 rounded-lg border border-line bg-surface-2/50 px-2.5 py-1.5 text-left transition-colors hover:border-line-strong"
        >
          <Search className="size-3.5 text-muted" />
          <span className="flex-1 text-[11.5px] text-muted">Search</span>
          <kbd className="rounded border border-line bg-surface px-1 py-0.5 font-mono text-[9px] text-muted">⌘K</kbd>
        </button>

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button type="button" className="flex w-full items-center gap-2.5 rounded-lg px-1 py-1.5 text-left transition-colors hover:bg-surface-2">
              <span className="flex size-8 shrink-0 items-center justify-center rounded-lg border border-line bg-surface-2 font-display text-[11px] font-semibold text-brand-text">
                {initials(user?.displayName ?? 'NA')}
              </span>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-[12px] font-medium text-ink">{user?.displayName}</span>
                <span className="block truncate font-mono text-[9px] uppercase tracking-[0.1em] text-muted">{user?.role}</span>
              </span>
              <ChevronRight className="size-3.5 shrink-0 text-muted" />
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            <DropdownMenuLabel>Signed in as</DropdownMenuLabel>
            <div className="px-2.5 pb-1.5 text-[11.5px] text-muted">{user?.email}</div>
            <DropdownMenuSeparator />
            <DropdownMenuItem onSelect={() => navigate('/security')}>
              <UserRound className="size-3.5" /> Access &amp; sessions
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={() => setTheme(resolved === 'dark' ? 'light' : 'dark')}>
              {resolved === 'dark' ? <Sun className="size-3.5" /> : <Moon className="size-3.5" />}
              Switch to {resolved === 'dark' ? 'light' : 'dark'} theme
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem destructive onSelect={() => void signOut()}>
              <LogOut className="size-3.5" /> Sign out
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </div>
  )

  return (
    <div className="flex min-h-screen bg-bg">
      <aside className="sticky top-0 hidden h-screen w-[248px] shrink-0 border-r border-line bg-surface lg:block">{sidebar}</aside>

      {mobileOpen ? (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-black/60" onClick={() => setMobileOpen(false)} aria-hidden />
          <aside className="absolute inset-y-0 left-0 w-[270px] border-r border-line bg-surface shadow-lifted">{sidebar}</aside>
        </div>
      ) : null}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-line bg-bg/85 px-4 backdrop-blur-md sm:px-6">
          <Button variant="ghost" size="icon" className="lg:hidden" onClick={() => setMobileOpen(true)} aria-label="Open navigation">
            <Menu />
          </Button>

          <div className="min-w-0 flex-1">
            <p className="font-mono text-[9px] uppercase tracking-[0.18em] text-muted">NexusBank · Sandbox</p>
            <h1 className="truncate text-[13.5px] font-semibold text-ink">{pageTitle}</h1>
          </div>

          <div className="hidden items-center gap-2 md:flex">
            <button
              type="button"
              onClick={() => setPaletteOpen(true)}
              className="flex h-8 items-center gap-2 rounded-lg border border-line bg-surface px-2.5 text-[11.5px] text-muted transition-colors hover:border-line-strong hover:text-ink"
            >
              <Search className="size-3.5" />
              <span>Search or jump to…</span>
              <kbd className="rounded border border-line bg-surface-2 px-1 py-0.5 font-mono text-[9px]">⌘K</kbd>
            </button>
          </div>

          <button
            type="button"
            onClick={() => navigate('/health')}
            className="hidden items-center gap-2 rounded-lg border border-line bg-surface px-2.5 py-1.5 transition-colors hover:border-line-strong sm:flex"
            aria-label={`Platform status: ${overall}`}
          >
            <StatusPill status={overall} pulse={overall !== 'healthy'} className="border-0 bg-transparent px-0" />
            <span className="h-3 w-px bg-line" aria-hidden />
            <span className="font-mono text-[10px] tabular-nums text-muted">{healthQuery.data ? `${healthQuery.data.availabilityPct.toFixed(2)}%` : '—'}</span>
          </button>

          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                type="button"
                className="relative flex size-8 items-center justify-center rounded-lg border border-line bg-surface text-muted transition-colors hover:text-ink"
                aria-label={`Alerts (${alerts.length})`}
              >
                <Bell className="size-4" />
                {alerts.length > 0 ? (
                  <span
                    className={cn(
                      'absolute -right-1 -top-1 flex size-3.5 items-center justify-center rounded-full font-mono text-[8px] font-semibold text-white',
                      criticalAlerts > 0 ? 'bg-danger' : 'bg-warning',
                    )}
                  >
                    {alerts.length > 9 ? '9+' : alerts.length}
                  </span>
                ) : null}
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent className="w-[330px] p-0">
              <div className="flex items-center justify-between border-b border-line px-3 py-2">
                <span className="font-mono text-[9.5px] uppercase tracking-[0.14em] text-muted">Operational alerts</span>
                <span className="font-mono text-[9.5px] text-muted">{alerts.length}</span>
              </div>
              {alerts.length === 0 ? (
                <p className="px-3 py-5 text-center text-[11.5px] text-muted">Nothing requires attention right now.</p>
              ) : (
                <ul className="max-h-80 overflow-y-auto">
                  {alerts.slice(0, 6).map((alert) => (
                    <li key={alert.id}>
                      <button
                        type="button"
                        onClick={() => alert.linkTo && navigate(alert.linkTo)}
                        className="w-full border-b border-line/60 px-3 py-2 text-left transition-colors last:border-0 hover:bg-surface-2"
                      >
                        <div className="flex items-center gap-1.5">
                          <span className={cn('size-1.5 rounded-full', alert.severity === 'critical' ? 'bg-danger' : alert.severity === 'warning' ? 'bg-warning' : 'bg-info')} />
                          <span className="truncate text-[11.5px] font-medium text-ink">{alert.title}</span>
                        </div>
                        <p className="mt-0.5 line-clamp-2 pl-3 text-[10.5px] leading-relaxed text-muted">{alert.detail}</p>
                        <p className="mt-0.5 pl-3 font-mono text-[9px] uppercase tracking-[0.08em] text-muted/80">{formatRelative(alert.detectedAt)}</p>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </DropdownMenuContent>
          </DropdownMenu>

          <button
            type="button"
            onClick={() => setTheme(resolved === 'dark' ? 'light' : 'dark')}
            className="flex size-8 items-center justify-center rounded-lg border border-line bg-surface text-muted transition-colors hover:text-ink"
            aria-label={`Switch to ${resolved === 'dark' ? 'light' : 'dark'} theme`}
          >
            {resolved === 'dark' ? <Sun className="size-4" /> : <Moon className="size-4" />}
          </button>
        </header>

        <main className="mx-auto w-full max-w-[1500px] flex-1 px-4 py-5 sm:px-6 sm:py-6">
          <Outlet />
        </main>

        <footer className="mt-auto border-t border-line px-4 py-3 sm:px-6">
          <div className="mx-auto flex max-w-[1500px] flex-wrap items-center justify-between gap-2 font-mono text-[9.5px] uppercase tracking-[0.1em] text-muted/80">
            <span>NexusBank · synthetic data only · not affiliated with any bank</span>
            <span>{healthQuery.data ? `v${healthQuery.data.version} · ${healthQuery.data.environment}` : 'connecting…'}</span>
          </div>
        </footer>
      </div>

      <CommandPalette open={paletteOpen} onOpenChange={setPaletteOpen} />
    </div>
  )
}
