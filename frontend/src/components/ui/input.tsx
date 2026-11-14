import { forwardRef, type InputHTMLAttributes, type LabelHTMLAttributes, type ReactNode, type TextareaHTMLAttributes } from 'react'
import * as LabelPrimitive from '@radix-ui/react-label'
import { cn } from '@/lib/utils'

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(({ className, ...props }, ref) => (
  <input
    ref={ref}
    className={cn(
      'h-9 w-full rounded-lg border border-line bg-surface px-3 text-[13.5px] text-ink shadow-inner shadow-transparent transition-colors placeholder:text-muted/70 hover:border-line-strong focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25 disabled:cursor-not-allowed disabled:opacity-60',
      'aria-[invalid=true]:border-danger aria-[invalid=true]:focus:ring-danger/20',
      className,
    )}
    {...props}
  />
))
Input.displayName = 'Input'

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(({ className, ...props }, ref) => (
  <textarea
    ref={ref}
    className={cn(
      'w-full rounded-lg border border-line bg-surface px-3 py-2 font-mono text-[12.5px] leading-relaxed text-ink transition-colors placeholder:text-muted/70 hover:border-line-strong focus:border-brand/70 focus:outline-none focus:ring-2 focus:ring-brand/25',
      className,
    )}
    {...props}
  />
))
Textarea.displayName = 'Textarea'

export function Label({ className, ...props }: LabelHTMLAttributes<HTMLLabelElement>) {
  return <LabelPrimitive.Root className={cn('text-[12px] font-medium text-ink-2', className)} {...props} />
}

export function Field({
  label,
  htmlFor,
  error,
  hint,
  children,
  className,
  action,
}: {
  label: ReactNode
  htmlFor?: string
  error?: string
  hint?: ReactNode
  children: ReactNode
  className?: string
  action?: ReactNode
}) {
  return (
    <div className={cn('space-y-1.5', className)}>
      <div className="flex items-center justify-between gap-2">
        <Label htmlFor={htmlFor}>{label}</Label>
        {action}
      </div>
      {children}
      {error ? (
        <p className="text-[11.5px] font-medium text-danger" role="alert">
          {error}
        </p>
      ) : hint ? (
        <p className="text-[11.5px] text-muted">{hint}</p>
      ) : null}
    </div>
  )
}
