import type { ReactNode } from 'react'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { NexusMark } from '@/components/brand/NexusMark'
import { AppShell } from '@/components/layout/AppShell'
import { useAuth } from '@/hooks/useAuth'
import { LoginPage } from '@/features/auth/LoginPage'
import { DashboardPage } from '@/features/dashboard/DashboardPage'
import { IntegrationsPage } from '@/features/integrations/IntegrationsPage'
import { IntegrationDetailPage } from '@/features/integrations/IntegrationDetailPage'
import { PaymentsPage } from '@/features/payments/PaymentsPage'
import { PaymentDetailPage } from '@/features/payments/PaymentDetailPage'
import { EventsPage } from '@/features/events/EventsPage'
import { ObservabilityPage } from '@/features/observability/ObservabilityPage'
import { IncidentsPage } from '@/features/incidents/IncidentsPage'
import { IncidentDetailPage } from '@/features/incidents/IncidentDetailPage'
import { SecurityPage } from '@/features/security/SecurityPage'
import { SystemHealthPage } from '@/features/health/SystemHealthPage'
import { ApiExplorerPage } from '@/features/explorer/ApiExplorerPage'
import { NotFoundPage } from '@/features/not-found/NotFoundPage'

function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isBootstrapping } = useAuth()
  const location = useLocation()

  if (isBootstrapping) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg">
        <NexusMark className="size-10 animate-pulse" />
        <p className="font-mono text-[10px] uppercase tracking-[0.18em] text-muted">Restoring session…</p>
      </div>
    )
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }

  return <>{children}</>
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        element={
          <ProtectedRoute>
            <AppShell />
          </ProtectedRoute>
        }
      >
        <Route index element={<DashboardPage />} />
        <Route path="integrations" element={<IntegrationsPage />} />
        <Route path="integrations/:id" element={<IntegrationDetailPage />} />
        <Route path="payments" element={<PaymentsPage />} />
        <Route path="payments/:id" element={<PaymentDetailPage />} />
        <Route path="events" element={<EventsPage />} />
        <Route path="observability" element={<ObservabilityPage />} />
        <Route path="incidents" element={<IncidentsPage />} />
        <Route path="incidents/:id" element={<IncidentDetailPage />} />
        <Route path="security" element={<SecurityPage />} />
        <Route path="health" element={<SystemHealthPage />} />
        <Route path="explorer" element={<ApiExplorerPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}
