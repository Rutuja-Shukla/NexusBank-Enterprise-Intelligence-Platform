import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Braces, Clock3, Copy, CornerDownLeft, History, Play, Search, Terminal, Trash2, Zap } from 'lucide-react'
import { PageHeader } from '@/components/data/PageHeader'
import { EmptyState, ErrorState, LoadingPanel } from '@/components/data/States'
import { JsonView } from '@/components/data/JsonView'
import { SearchInput } from '@/components/data/Toolbar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel, PanelHeader } from '@/components/ui/card'
import { Field, Input, Label } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { api, describeError } from '@/lib/api'
import { cn, formatBytes, formatMs, formatRelative, formatTime } from '@/lib/utils'
import type { EndpointDescriptor, ExecuteResponse } from '@/types'

interface HistoryEntry {
  id: string
  method: string
  path: string
  statusCode: number
  durationMs: number
  at: string
}

const HISTORY_KEY = 'nexusbank.explorer.history'
const methods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] as const

const methodTone: Record<string, string> = {
  GET: 'text-info',
  POST: 'text-success',
  PUT: 'text-warning',
  PATCH: 'text-warning',
  DELETE: 'text-danger',
}

function loadHistory(): HistoryEntry[] {
  try {
    const raw = localStorage.getItem(HISTORY_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as HistoryEntry[]
    return Array.isArray(parsed) ? parsed.slice(0, 25) : []
  } catch {
    return []
  }
}

export function ApiExplorerPage() {
  const [method, setMethod] = useState('GET')
  const [path, setPath] = useState('/api/v1/customers?page=1&pageSize=10')
  const [body, setBody] = useState('')
  const [idempotencyKey, setIdempotencyKey] = useState('')
  const [response, setResponse] = useState<ExecuteResponse | null>(null)
  const [running, setRunning] = useState(false)
  const [filter, setFilter] = useState('')
  const [selected, setSelected] = useState<EndpointDescriptor | null>(null)
  const [history, setHistory] = useState<HistoryEntry[]>(loadHistory)
  const [responseTab, setResponseTab] = useState<'body' | 'headers'>('body')

  const catalogueQuery = useQuery({
    queryKey: ['explorer', 'catalogue'],
    queryFn: () => api.get<EndpointDescriptor[]>('/api/v1/explorer/catalogue'),
    staleTime: 10 * 60_000,
  })

  const grouped = useMemo(() => {
    const needle = filter.trim().toLowerCase()
    const groups = new Map<string, EndpointDescriptor[]>()
    for (const endpoint of catalogueQuery.data ?? []) {
      if (needle && !`${endpoint.method} ${endpoint.path} ${endpoint.summary}`.toLowerCase().includes(needle)) continue
      const bucket = groups.get(endpoint.category) ?? []
      bucket.push(endpoint)
      groups.set(endpoint.category, bucket)
    }
    return [...groups.entries()]
  }, [catalogueQuery.data, filter])

  function selectEndpoint(endpoint: EndpointDescriptor) {
    setSelected(endpoint)
    setMethod(endpoint.method)
    setPath(endpoint.path.replace(/\{(\w+)\}/g, '00000000-0000-0000-0000-000000000000'))
    setBody(endpoint.sampleBody ?? '')
    setResponseTab('body')
  }

  function recordHistory(entry: HistoryEntry) {
    setHistory((current) => {
      const next = [entry, ...current].slice(0, 25)
      try {
        localStorage.setItem(HISTORY_KEY, JSON.stringify(next))
      } catch {
        /* storage may be unavailable in private browsing */
      }
      return next
    })
  }

  async function run() {
    if (!path.trim()) {
      toast.error('Enter a request path')
      return
    }
    setRunning(true)
    setResponseTab('body')
    try {
      const result = await api.post<ExecuteResponse>('/api/v1/explorer/execute', {
        method,
        path: path.trim(),
        body: method === 'GET' || method === 'DELETE' ? null : body || null,
        idempotencyKey: idempotencyKey.trim() || null,
      })
      setResponse(result)
      recordHistory({
        id: `${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
        method,
        path: path.trim(),
        statusCode: result.statusCode,
        durationMs: result.durationMs,
        at: result.executedAt,
      })
      if (result.error) toast.warning(`Completed with ${result.statusCode}`, { description: result.error })
    } catch (error) {
      toast.error('Request could not be executed', { description: describeError(error) })
    } finally {
      setRunning(false)
    }
  }

  async function copyCurl() {
    const payload = method === 'GET' || method === 'DELETE' ? '' : ` \\\n  -H 'Content-Type: application/json' \\\n  -d '${body.replace(/'/g, "'\\''")}'`
    const command = `curl -X ${method} '${window.location.origin}${path}' \\\n  -H 'Authorization: Bearer $TOKEN'${payload}`
    try {
      await navigator.clipboard.writeText(command)
      toast.success('cURL copied', { description: 'The bearer token placeholder must be replaced with a real access token.' })
    } catch {
      toast.error('Clipboard unavailable in this browser context')
    }
  }

  const statusTone = response
    ? response.statusCode >= 500
      ? 'text-danger'
      : response.statusCode >= 400
        ? 'text-warning'
        : 'text-success'
    : 'text-muted'

  return (
    <div className="space-y-4 sm:space-y-5">
      <PageHeader
        eyebrow="API gateway"
        title="API explorer"
        description="Execute signed-in requests against this platform's own API, with real status codes, latency and payloads."
        actions={
          <Badge tone="neutral">
            <Terminal className="mr-1 size-3" /> /api/v1
          </Badge>
        }
      />

      <div className="grid gap-4 xl:grid-cols-[minmax(0,300px)_minmax(0,1fr)]">
        <div className="space-y-4">
          <Panel className="overflow-hidden">
            <PanelHeader title="Catalogue" description="Endpoints grouped by domain" icon={<Braces />} actions={<Badge tone="neutral">{catalogueQuery.data?.length ?? 0}</Badge>} />
            <div className="border-b border-line px-3 py-2">
              <SearchInput value={filter} onChange={setFilter} placeholder="Filter endpoints" />
            </div>
            {catalogueQuery.isError ? (
              <ErrorState error={catalogueQuery.error} onRetry={() => void catalogueQuery.refetch()} />
            ) : catalogueQuery.isLoading ? (
              <LoadingPanel rows={8} />
            ) : grouped.length === 0 ? (
              <EmptyState title="No endpoints match" description="Clear the filter to see the full catalogue." icon={<Search />} />
            ) : (
              <div className="max-h-[560px] overflow-y-auto">
                {grouped.map(([category, endpoints]) => (
                  <div key={category}>
                    <p className="sticky top-0 border-b border-line bg-surface-2/95 px-3 py-1.5 font-mono text-[8.5px] uppercase tracking-[0.14em] text-muted backdrop-blur">
                      {category}
                    </p>
                    <ul>
                      {endpoints.map((endpoint) => {
                        const active = selected?.method === endpoint.method && selected?.path === endpoint.path
                        return (
                          <li key={`${endpoint.method}-${endpoint.path}`}>
                            <button
                              type="button"
                              onClick={() => selectEndpoint(endpoint)}
                              className={cn(
                                'flex w-full items-start gap-2 border-l-2 px-3 py-2 text-left transition-colors',
                                active ? 'border-brand bg-brand-soft/60' : 'border-transparent hover:bg-surface-2/60',
                              )}
                            >
                              <span className={cn('mt-[1px] w-9 shrink-0 font-mono text-[8.5px] font-semibold uppercase tracking-[0.04em]', methodTone[endpoint.method] ?? 'text-muted')}>
                                {endpoint.method}
                              </span>
                              <span className="min-w-0">
                                <span className="block truncate font-mono text-[10.5px] text-ink">{endpoint.path}</span>
                                <span className="mt-0.5 block truncate text-[10px] text-muted">{endpoint.summary}</span>
                              </span>
                            </button>
                          </li>
                        )
                      })}
                    </ul>
                  </div>
                ))}
              </div>
            )}
          </Panel>

          <Panel className="overflow-hidden">
            <PanelHeader
              title="Recent requests"
              description="Stored locally in this browser"
              icon={<History />}
              actions={
                history.length > 0 ? (
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label="Clear request history"
                    onClick={() => {
                      setHistory([])
                      try { localStorage.removeItem(HISTORY_KEY) } catch { /* ignore */ }
                    }}
                  >
                    <Trash2 />
                  </Button>
                ) : null
              }
            />
            {history.length === 0 ? (
              <p className="px-4 py-6 text-center text-[11.5px] text-muted">Executed requests will be listed here.</p>
            ) : (
              <ul className="max-h-[240px] divide-y divide-line overflow-y-auto">
                {history.map((entry) => (
                  <li key={entry.id}>
                    <button
                      type="button"
                      onClick={() => {
                        setMethod(entry.method)
                        setPath(entry.path)
                        setSelected(null)
                      }}
                      className="flex w-full items-center gap-2 px-3 py-2 text-left hover:bg-surface-2/60"
                    >
                      <span className={cn('w-9 shrink-0 font-mono text-[8.5px] font-semibold uppercase', methodTone[entry.method] ?? 'text-muted')}>{entry.method}</span>
                      <span className="min-w-0 flex-1 truncate font-mono text-[10px] text-ink-2">{entry.path}</span>
                      <span className={cn('shrink-0 font-mono text-[9.5px]', entry.statusCode >= 400 ? 'text-warning' : 'text-success')}>{entry.statusCode}</span>
                      <span className="shrink-0 font-mono text-[9px] text-muted">{formatMs(entry.durationMs)}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>

        <div className="space-y-4">
          <Panel>
            <PanelHeader
              title="Request builder"
              description="Requests run under your own identity — permissions are enforced exactly as they are for any client."
              icon={<CornerDownLeft />}
              actions={
                <Button variant="ghost" size="sm" onClick={() => void copyCurl()}>
                  <Copy /> cURL
                </Button>
              }
            />

            <div className="space-y-3 p-4">
              <div className="flex flex-col gap-2 sm:flex-row">
                <Select value={method} onValueChange={setMethod}>
                  <SelectTrigger className="h-10 w-full sm:w-[120px]"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    {methods.map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}
                  </SelectContent>
                </Select>
                <Input
                  value={path}
                  onChange={(event) => setPath(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') void run()
                  }}
                  spellCheck={false}
                  className="h-10 flex-1 font-mono text-[12px]"
                  aria-label="Request path"
                />
                <Button className="h-10 sm:w-[130px]" onClick={() => void run()} disabled={running}>
                  {running ? <Zap className="animate-pulse" /> : <Play />} {running ? 'Sending…' : 'Send request'}
                </Button>
              </div>

              {selected ? (
                <div className="flex flex-wrap items-center gap-2 rounded-lg border border-line bg-surface-2/60 px-2.5 py-2">
                  <span className="text-[11px] text-ink-2">{selected.summary}</span>
                  <span className="ml-auto flex items-center gap-1.5">
                    {selected.requiresAuth ? <Badge tone="neutral">bearer auth</Badge> : <Badge tone="warning">anonymous</Badge>}
                    <span className="rounded border border-line bg-surface px-1.5 py-0.5 font-mono text-[8.5px] text-muted">{selected.permission}</span>
                  </span>
                  {selected.pathParameters ? (
                    <p className="w-full font-mono text-[9.5px] text-muted">path parameters: {selected.pathParameters}</p>
                  ) : null}
                </div>
              ) : null}

              {method === 'POST' || method === 'PUT' || method === 'PATCH' ? (
                <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_220px]">
                  <div>
                    <Label htmlFor="request-body">JSON body</Label>
                    <textarea
                      id="request-body"
                      rows={9}
                      value={body}
                      onChange={(event) => setBody(event.target.value)}
                      spellCheck={false}
                      placeholder={'{\n  "key": "value"\n}'}
                      className="mt-1.5 w-full rounded-lg border border-line bg-surface px-3 py-2 font-mono text-[11.5px] leading-relaxed text-ink focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25"
                    />
                  </div>
                  <div className="space-y-3">
                    <Field label="Idempotency-Key" hint="Required for payments; replays return the original result.">
                      <Input value={idempotencyKey} onChange={(event) => setIdempotencyKey(event.target.value)} placeholder="optional UUID" className="font-mono text-[11px]" />
                    </Field>
                    <div className="rounded-lg border border-line bg-surface-2/60 p-2.5">
                      <p className="flex items-center gap-1.5 font-mono text-[8.5px] uppercase tracking-[0.12em] text-muted">
                        <Clock3 className="size-3" /> execution notes
                      </p>
                      <ul className="mt-1.5 space-y-1 text-[10px] leading-relaxed text-muted">
                        <li>Rate limited to protect the platform.</li>
                        <li>Only catalogue paths are executable.</li>
                        <li>Your token, not a service token, is used.</li>
                      </ul>
                    </div>
                  </div>
                </div>
              ) : (
                <p className="font-mono text-[10px] text-muted">No request body for {method} requests. Query parameters belong in the path.</p>
              )}
            </div>
          </Panel>

          <Panel className="overflow-hidden">
            <PanelHeader
              title="Response"
              description={response ? `Executed ${formatTime(response.executedAt)}` : 'Awaiting a request'}
              icon={<Terminal />}
              actions={response ? <Badge tone={response.statusCode >= 500 ? 'danger' : response.statusCode >= 400 ? 'warning' : 'success'}>{response.statusCode} {response.reasonPhrase}</Badge> : null}
            />
            {!response ? (
              <EmptyState
                title="No response yet"
                description="Choose an endpoint or type a path, then send the request. The response is the live API payload."
                icon={<Play />}
              />
            ) : (
              <>
                <div className="flex flex-wrap items-center gap-4 border-b border-line px-4 py-2">
                  <Stat label="Status" value={String(response.statusCode)} className={statusTone} />
                  <Stat label="Latency" value={formatMs(response.durationMs)} />
                  <Stat label="Size" value={formatBytes(response.sizeBytes)} />
                  <Stat label="Method" value={method} />
                  <div className="ml-auto flex items-center gap-1">
                    <button type="button" onClick={() => setResponseTab('body')} className={cn('rounded px-2 py-1 font-mono text-[9.5px] uppercase tracking-[0.1em]', responseTab === 'body' ? 'bg-surface-2 text-ink' : 'text-muted hover:text-ink')}>
                      Body
                    </button>
                    <button type="button" onClick={() => setResponseTab('headers')} className={cn('rounded px-2 py-1 font-mono text-[9.5px] uppercase tracking-[0.1em]', responseTab === 'headers' ? 'bg-surface-2 text-ink' : 'text-muted hover:text-ink')}>
                      Headers
                    </button>
                  </div>
                </div>

                <div className="p-4">
                  {responseTab === 'body' ? (
                    response.bodyIsJson ? (
                      <JsonView value={response.body} maxHeight="26rem" />
                    ) : (
                      <pre className="max-h-[26rem] overflow-auto rounded-lg border border-line bg-surface-2 p-3 font-mono text-[11px] leading-relaxed text-ink-2">{response.body || '(empty body)'}</pre>
                    )
                  ) : (
                    <div className="overflow-hidden rounded-lg border border-line">
                      <table className="w-full text-[10.5px]">
                        <tbody>
                          {Object.entries(response.headers).map(([key, value]) => (
                            <tr key={key} className="border-b border-line/60 last:border-0">
                              <td className="w-[42%] px-3 py-1.5 align-top font-mono text-muted">{key}</td>
                              <td className="break-all px-3 py-1.5 font-mono text-ink-2">{value}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                  {response.error ? (
                    <p className="mt-3 rounded-md border border-warning/25 bg-warning-soft px-2.5 py-2 text-[11px] leading-relaxed text-warning">{response.error}</p>
                  ) : null}
                  <p className="mt-2 font-mono text-[9px] text-muted">
                    {method} {path} · executed {formatRelative(response.executedAt)}
                  </p>
                </div>
              </>
            )}
          </Panel>
        </div>
      </div>
    </div>
  )
}

function Stat({ label, value, className }: { label: string; value: string; className?: string }) {
  return (
    <span className="flex items-baseline gap-1.5">
      <span className="font-mono text-[8.5px] uppercase tracking-[0.12em] text-muted">{label}</span>
      <span className={cn('font-mono text-[11.5px] font-medium text-ink', className)}>{value}</span>
    </span>
  )
}
