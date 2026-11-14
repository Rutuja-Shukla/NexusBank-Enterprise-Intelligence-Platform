import type { ReactNode } from 'react'
import * as SwitchPrimitive from '@radix-ui/react-switch'
import * as TooltipPrimitive from '@radix-ui/react-tooltip'
import * as DropdownPrimitive from '@radix-ui/react-dropdown-menu'
import { cn } from '@/lib/utils'

export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('skeleton rounded-md', className)} aria-hidden />
}

export function Separator({ className, vertical = false }: { className?: string; vertical?: boolean }) {
  return <div role="separator" className={cn(vertical ? 'h-full w-px' : 'h-px w-full', 'bg-line', className)} />
}

export function Switch({ checked, onCheckedChange, disabled, id }: { checked: boolean; onCheckedChange: (value: boolean) => void; disabled?: boolean; id?: string }) {
  return (
    <SwitchPrimitive.Root
      id={id}
      checked={checked}
      onCheckedChange={onCheckedChange}
      disabled={disabled}
      className={cn(
        'relative inline-flex h-5 w-9 shrink-0 items-center rounded-full border border-line transition-colors',
        'data-[state=checked]:border-brand data-[state=checked]:bg-brand data-[state=unchecked]:bg-surface-3',
        'disabled:cursor-not-allowed disabled:opacity-50',
      )}
    >
      <SwitchPrimitive.Thumb className="block size-3.5 translate-x-0.5 rounded-full bg-white shadow transition-transform data-[state=checked]:translate-x-[18px] data-[state=checked]:bg-on-brand" />
    </SwitchPrimitive.Root>
  )
}

export function Tooltip({ children, label, side = 'top' }: { children: ReactNode; label: ReactNode; side?: 'top' | 'bottom' | 'left' | 'right' }) {
  return (
    <TooltipPrimitive.Provider delayDuration={200}>
      <TooltipPrimitive.Root>
        <TooltipPrimitive.Trigger asChild>{children}</TooltipPrimitive.Trigger>
        <TooltipPrimitive.Portal>
          <TooltipPrimitive.Content
            side={side}
            sideOffset={6}
            className="z-50 max-w-xs rounded-md border border-line bg-surface-2 px-2 py-1 text-[11.5px] text-ink shadow-lifted animate-fade-in"
          >
            {label}
            <TooltipPrimitive.Arrow className="fill-[color:var(--surface-2)]" />
          </TooltipPrimitive.Content>
        </TooltipPrimitive.Portal>
      </TooltipPrimitive.Root>
    </TooltipPrimitive.Provider>
  )
}

export const DropdownMenu = DropdownPrimitive.Root
export const DropdownMenuTrigger = DropdownPrimitive.Trigger

export function DropdownMenuContent({ children, align = 'end', className }: { children: ReactNode; align?: 'start' | 'center' | 'end'; className?: string }) {
  return (
    <DropdownPrimitive.Portal>
      <DropdownPrimitive.Content
        align={align}
        sideOffset={6}
        className={cn('z-50 min-w-48 overflow-hidden rounded-lg border border-line bg-surface p-1 shadow-lifted animate-scale-in', className)}
      >
        {children}
      </DropdownPrimitive.Content>
    </DropdownPrimitive.Portal>
  )
}

export function DropdownMenuItem({ children, onSelect, destructive = false, disabled }: { children: ReactNode; onSelect?: () => void; destructive?: boolean; disabled?: boolean }) {
  return (
    <DropdownPrimitive.Item
      disabled={disabled}
      onSelect={onSelect}
      className={cn(
        'flex cursor-default select-none items-center gap-2 rounded-md px-2.5 py-1.5 text-[13px] outline-none data-[highlighted]:bg-surface-2',
        destructive ? 'text-danger' : 'text-ink-2',
        'data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
      )}
    >
      {children}
    </DropdownPrimitive.Item>
  )
}

export function DropdownMenuLabel({ children }: { children: ReactNode }) {
  return <div className="px-2.5 py-1.5 font-mono text-[10px] uppercase tracking-[0.14em] text-muted">{children}</div>
}

export function DropdownMenuSeparator() {
  return <div className="my-1 h-px bg-line" />
}
