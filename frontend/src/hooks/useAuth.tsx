import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, login as loginRequest, logout as logoutRequest, tokenStore, type StoredProfile } from '@/lib/api'

interface AuthContextValue {
  user: StoredProfile | null
  isAuthenticated: boolean
  isBootstrapping: boolean
  signIn: (email: string, password: string) => Promise<StoredProfile>
  signOut: () => Promise<void>
  hasPermission: (permission: string) => boolean
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<StoredProfile | null>(() => tokenStore.profile)
  const [isBootstrapping, setIsBootstrapping] = useState(Boolean(tokenStore.access))
  const queryClient = useQueryClient()

  useEffect(() => {
    let cancelled = false

    async function bootstrap() {
      if (!tokenStore.access) {
        setIsBootstrapping(false)
        return
      }
      try {
        const profile = await api.get<StoredProfile>('/api/v1/auth/me')
        if (cancelled) return
        tokenStore.saveProfile(profile)
        setUser(profile)
      } catch {
        if (cancelled) return
        tokenStore.clear()
        setUser(null)
      } finally {
        if (!cancelled) setIsBootstrapping(false)
      }
    }

    void bootstrap()
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    const onSignedOut = () => {
      tokenStore.clear()
      setUser(null)
      queryClient.clear()
    }
    const onRefreshed = () => {
      const profile = tokenStore.profile
      if (profile) setUser(profile)
    }

    window.addEventListener('nexusbank:signed-out', onSignedOut)
    window.addEventListener('nexusbank:session-refreshed', onRefreshed)
    return () => {
      window.removeEventListener('nexusbank:signed-out', onSignedOut)
      window.removeEventListener('nexusbank:session-refreshed', onRefreshed)
    }
  }, [queryClient])

  const signIn = useCallback(async (email: string, password: string) => {
    const response = await loginRequest(email, password)
    setUser(response.user)
    return response.user
  }, [])

  const signOut = useCallback(async () => {
    await logoutRequest()
    queryClient.clear()
    setUser(null)
  }, [queryClient])

  const hasPermission = useCallback(
    (permission: string) => Boolean(user?.permissions.includes(permission)) || user?.roleSlug === 'administrator',
    [user],
  )

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAuthenticated: Boolean(user),
      isBootstrapping,
      signIn,
      signOut,
      hasPermission,
    }),
    [user, isBootstrapping, signIn, signOut, hasPermission],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
