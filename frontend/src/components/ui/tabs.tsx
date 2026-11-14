import type { ReactNode } from 'react'
import * as TabsPrimitive from '@radix-ui/react-tabs'
import { cn } from '@/lib/utils'

export const Tabs = TabsPrimitive.Root

export function TabsList({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <TabsPrimitive.List className={cn('flex items-center gap-1 overflow-x-auto border-b border-line', className)}>
      {children}
    </TabsPrimitive.List>
  )
}

export function TabsTrigger({ value, children, count }: { value: string; children: ReactNode; count?: number }) {
  return (
    <TabsPrimitive.Trigger
      value={value}
      className={cn(
        'relative -mb-px flex shrink-0 items-center gap-2 border-b-2 border-transparent px-3 py-2.5 text-[13px] font-medium text-muted transition-colors hover:text-ink',
        'data-[state=active]:border-brand data-[state=active]:text-ink',
      )}
    >
      {children}
      {count !== undefined ? (
        <span className="rounded bg-surface-2 px-1.5 py-0.5 font-mono text-[10px] tabular-nums text-muted">{count}</span>
      ) : null}
    </TabsPrimitive.Trigger>
  )
}

export function TabsContent({ value, children, className }: { value: string; children: ReactNode; className?: string }) {
  return (
    <TabsPrimitive.Content value={value} className={cn('focus-visible:outline-none', className)}>
      {children}
    </TabsPrimitive.Content>
  )
}
