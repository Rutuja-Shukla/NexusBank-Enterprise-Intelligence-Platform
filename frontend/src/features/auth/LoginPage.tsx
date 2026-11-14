import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowRight, Eye, EyeOff, Loader2, ShieldCheck, Terminal } from 'lucide-react'
import { NexusMark, NexusWordmark } from '@/components/brand/NexusMark'
import { Button } from '@/components/ui/button'
import { Field, Input } from '@/components/ui/input'
import { useAuth } from '@/hooks/useAuth'
import { ApiError } from '@/lib/api'
import { loginSchema, type LoginValues } from '@/lib/schemas'
import { cn } from '@/lib/utils'

const demoAccounts = [
  { email: 'admin@nexusbank.dev', role: 'Administrator' },
  { email: 'ops@nexusbank.dev', role: 'Operations' },
  { email: 'dev@nexusbank.dev', role: 'Developer' },
  { email: 'auditor@nexusbank.dev', role: 'Auditor' },
]

export function LoginPage() {
  const { signIn, isAuthenticated, isBootstrapping } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [showPassword, setShowPassword] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const form = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: 'ops@nexusbank.dev', password: 'NexusBank!2026' },
  })

  if (!isBootstrapping && isAuthenticated) return <Navigate to="/" replace />

  const redirectTo = (location.state as { from?: string } | null)?.from ?? '/'

  async function onSubmit(values: LoginValues) {
    setFormError(null)
    try {
      await signIn(values.email, values.password)
      navigate(redirectTo, { replace: true })
    } catch (error) {
      setFormError(error instanceof ApiError ? error.message : 'Unable to reach the platform. Please try again.')
    }
  }

  return (
    <div className="grid min-h-screen lg:grid-cols-[1.05fr_1fr]">
      <section className="relative hidden overflow-hidden bg-[#15171C] px-12 py-10 lg:flex lg:flex-col">
        <div className="pointer-events-none absolute inset-0 grid-canvas opacity-[0.55]" aria-hidden />
        <div
          className="pointer-events-none absolute -left-24 top-1/3 size-[520px] rounded-full border border-[#F5C400]/10"
          aria-hidden
        />
        <div className="pointer-events-none absolute -left-10 top-1/3 size-[360px] rounded-full border border-[#F5C400]/15" aria-hidden />
        <div
          className="pointer-events-none absolute right-[-140px] top-[12%] size-[420px] rounded-full opacity-40 blur-3xl"
          style={{ background: 'radial-gradient(circle, rgba(245,196,0,0.22), transparent 65%)' }}
          aria-hidden
        />

        <div className="relative z-10 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <NexusMark className="size-9" />
            <div>
              <div className="font-display text-[15px] font-semibold tracking-[0.1em] text-white">
                NEXUS<span className="font-light text-white/60">BANK</span>
              </div>
              <div className="mt-0.5 font-mono text-[8.5px] uppercase tracking-[0.26em] text-white/40">Integration Platform</div>
            </div>
          </div>
          <span className="rounded border border-white/10 px-2 py-1 font-mono text-[9px] uppercase tracking-[0.16em] text-white/50">Sandbox</span>
        </div>

        <div className="relative z-10 mt-auto max-w-lg">
          <p className="mono-label !text-[#F5C400]/80">Operational control plane</p>
          <h1 className="mt-3 font-display text-[40px] font-semibold leading-[1.08] tracking-[-0.02em] text-white">
            Run the bank&apos;s
            <br />
            integration nerve centre.
          </h1>
          <p className="mt-4 max-w-md text-[13.5px] leading-relaxed text-white/55">
            One workspace for integration topology, payment lifecycles, event streams and operational escalations — with
            telemetry that is measured, never mocked.
          </p>

          <div className="mt-8 grid max-w-md grid-cols-3 gap-3">
            {[
              { value: '8', label: 'Banking services' },
              { value: '6', label: 'Event topics' },
              { value: 'ISO 20022', label: 'Aligned flows' },
            ].map((item) => (
              <div key={item.label} className="rounded-lg border border-white/10 bg-white/[0.03] px-3 py-2.5">
                <p className="num text-[19px] font-semibold text-[#F5C400]">{item.value}</p>
                <p className="mt-0.5 font-mono text-[8.5px] uppercase leading-tight tracking-[0.12em] text-white/40">{item.label}</p>
              </div>
            ))}
          </div>
        </div>

        <div className="relative z-10 mt-10 flex items-center gap-2 font-mono text-[9.5px] uppercase tracking-[0.14em] text-white/30">
          <ShieldCheck className="size-3.5" />
          Synthetic data · no customer information · portfolio build
        </div>
      </section>

      <section className="flex items-center justify-center px-5 py-10 sm:px-10">
        <div className="w-full max-w-[400px] animate-fade-up">
          <div className="mb-8 lg:hidden">
            <NexusWordmark />
          </div>

          <p className="mono-label">Operator access</p>
          <h2 className="mt-2 text-[24px] font-semibold tracking-tight text-ink">Sign in to the console</h2>
          <p className="mt-1 text-[13px] text-muted">Authenticate with your platform credentials to continue.</p>

          <form className="mt-7 space-y-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
            <Field label="Work email" htmlFor="email" error={form.formState.errors.email?.message}>
              <Input
                id="email"
                type="email"
                autoComplete="username"
                placeholder="you@nexusbank.dev"
                aria-invalid={Boolean(form.formState.errors.email)}
                {...form.register('email')}
              />
            </Field>

            <Field label="Password" htmlFor="password" error={form.formState.errors.password?.message}>
              <div className="relative">
                <Input
                  id="password"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  placeholder="••••••••••••"
                  aria-invalid={Boolean(form.formState.errors.password)}
                  className="pr-10"
                  {...form.register('password')}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((visible) => !visible)}
                  className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-muted hover:text-ink"
                  aria-label={showPassword ? 'Hide password' : 'Show password'}
                >
                  {showPassword ? <EyeOff className="size-3.5" /> : <Eye className="size-3.5" />}
                </button>
              </div>
            </Field>

            {formError ? (
              <div className="rounded-lg border border-danger/30 bg-danger-soft px-3 py-2 text-[12px] leading-relaxed text-danger" role="alert">
                {formError}
              </div>
            ) : null}

            <Button type="submit" className="w-full" size="lg" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting ? <Loader2 className="animate-spin" /> : <Terminal />}
              {form.formState.isSubmitting ? 'Authenticating…' : 'Sign in'}
              {!form.formState.isSubmitting ? <ArrowRight /> : null}
            </Button>
          </form>

          <div className="mt-7">
            <div className="flex items-center gap-2">
              <span className="hairline flex-1" />
              <span className="font-mono text-[9px] uppercase tracking-[0.16em] text-muted">Demo identities</span>
              <span className="hairline flex-1" />
            </div>
            <p className="mt-2.5 text-center text-[11px] text-muted">
              All accounts use <code className="rounded bg-surface-2 px-1 py-0.5 font-mono text-[10.5px] text-ink-2">NexusBank!2026</code>
            </p>
            <div className="mt-2.5 grid grid-cols-2 gap-1.5">
              {demoAccounts.map((account) => {
                const selected = form.watch('email') === account.email
                return (
                  <button
                    key={account.email}
                    type="button"
                    onClick={() => {
                      form.setValue('email', account.email)
                      form.setValue('password', 'NexusBank!2026')
                      setFormError(null)
                    }}
                    className={cn(
                      'rounded-lg border px-2.5 py-1.5 text-left transition-colors',
                      selected ? 'border-brand/60 bg-brand-soft' : 'border-line bg-surface hover:border-line-strong',
                    )}
                  >
                    <span className="block text-[11.5px] font-medium text-ink">{account.role}</span>
                    <span className="block truncate font-mono text-[9px] text-muted">{account.email}</span>
                  </button>
                )
              })}
            </div>
          </div>
        </div>
      </section>
    </div>
  )
}
