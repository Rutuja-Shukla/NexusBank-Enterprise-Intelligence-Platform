import { useMemo, useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn, safeJson } from '@/lib/utils'

const TOKEN = /("(?:\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(?:true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+-]?\d+)?)/g

function tokenClass(token: string) {
  if (token.startsWith('"')) return token.trimEnd().endsWith(':') ? 'text-info' : 'text-success'
  if (token === 'true' || token === 'false') return 'text-warning'
  if (token === 'null') return 'text-muted'
  return 'text-brand-text'
}

export function JsonView({ value, className, maxHeight = '22rem', allowCopy = true }: { value: string; className?: string; maxHeight?: string; allowCopy?: boolean }) {
  const [copied, setCopied] = useState(false)
  const pretty = useMemo(() => safeJson(value), [value])

  const parts = useMemo(() => {
    const nodes: Array<{ text: string; className?: string }> = []
    let lastIndex = 0
    for (const match of pretty.matchAll(TOKEN)) {
      const index = match.index ?? 0
      if (index > lastIndex) nodes.push({ text: pretty.slice(lastIndex, index) })
      nodes.push({ text: match[0], className: tokenClass(match[0]) })
      lastIndex = index + match[0].length
    }
    if (lastIndex < pretty.length) nodes.push({ text: pretty.slice(lastIndex) })
    return nodes
  }, [pretty])

  async function copy() {
    try {
      await navigator.clipboard.writeText(pretty)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1600)
    } catch {
      setCopied(false)
    }
  }

  return (
    <div className={cn('relative', className)}>
      {allowCopy ? (
        <div className="absolute right-2 top-2 z-10">
          <Button variant="ghost" size="icon-sm" onClick={copy} aria-label="Copy JSON">
            {copied ? <Check className="text-success" /> : <Copy />}
          </Button>
        </div>
      ) : null}
      <pre
        className="code-surface overflow-auto p-3 pr-10 text-ink-2"
        style={{ maxHeight }}
        tabIndex={0}
      >
        <code>
          {parts.map((part, index) => (
            <span key={index} className={part.className}>
              {part.text}
            </span>
          ))}
        </code>
      </pre>
    </div>
  )
}
