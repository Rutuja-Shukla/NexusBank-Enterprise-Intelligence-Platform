const ACCESS_KEY = 'nexusbank.accessToken'
const REFRESH_KEY = 'nexusbank.refreshToken'
const PROFILE_KEY = 'nexusbank.profile'

export interface StoredProfile {
  id: string
  email: string
  displayName: string
  role: string
  roleSlug: string
  permissions: string[]
  lastLoginAt: string | null
}

export class ApiError extends Error {
  status: number
  code?: string
  errors?: Record<string, string[]>
  correlationId?: string

  constructor(status: number, message: string, options?: { code?: string; errors?: Record<string, string[]>; correlationId?: string }) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = options?.code
    this.errors = options?.errors
    this.correlationId = options?.correlationId
  }

  /** First field message, useful for inline form errors. */
  get firstFieldError(): string | undefined {
    if (!this.errors) return undefined
    const values = Object.values(this.errors).flat()
    return values[0]
  }
}

/** Human-readable message for anything thrown by the API client. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) return error.firstFieldError ?? error.message
  if (error instanceof Error) return error.message
  return 'Unexpected error'
}

export const tokenStore = {
  get access() {
    return localStorage.getItem(ACCESS_KEY)
  },
  get refresh() {
    return localStorage.getItem(REFRESH_KEY)
  },
  get profile(): StoredProfile | null {
    const raw = localStorage.getItem(PROFILE_KEY)
    if (!raw) return null
    try {
      return JSON.parse(raw) as StoredProfile
    } catch {
      return null
    }
  },
  save(access: string, refresh: string, profile?: StoredProfile) {
    localStorage.setItem(ACCESS_KEY, access)
    localStorage.setItem(REFRESH_KEY, refresh)
    if (profile) localStorage.setItem(PROFILE_KEY, JSON.stringify(profile))
  },
  saveProfile(profile: StoredProfile) {
    localStorage.setItem(PROFILE_KEY, JSON.stringify(profile))
  },
  clear() {
    localStorage.removeItem(ACCESS_KEY)
    localStorage.removeItem(REFRESH_KEY)
    localStorage.removeItem(PROFILE_KEY)
  },
}

let refreshInFlight: Promise<boolean> | null = null

async function refreshSession(): Promise<boolean> {
  const refreshToken = tokenStore.refresh
  if (!refreshToken) return false

  refreshInFlight ??= (async () => {
    try {
      const response = await fetch('/api/v1/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      })
      if (!response.ok) return false
      const data = (await response.json()) as {
        accessToken: string
        refreshToken: string
        user: StoredProfile
      }
      tokenStore.save(data.accessToken, data.refreshToken, data.user)
      window.dispatchEvent(new CustomEvent('nexusbank:session-refreshed'))
      return true
    } catch {
      return false
    } finally {
      setTimeout(() => {
        refreshInFlight = null
      }, 0)
    }
  })()

  return refreshInFlight
}

async function parseError(response: Response): Promise<ApiError> {
  let payload: Record<string, unknown> = {}
  try {
    payload = (await response.json()) as Record<string, unknown>
  } catch {
    /* non-JSON error body */
  }

  const detail = typeof payload.detail === 'string' ? payload.detail : undefined
  const title = typeof payload.title === 'string' ? payload.title : undefined

  return new ApiError(response.status, detail ?? title ?? `Request failed with status ${response.status}`, {
    code: typeof payload.code === 'string' ? payload.code : undefined,
    errors: payload.errors as Record<string, string[]> | undefined,
    correlationId: typeof payload.correlationId === 'string' ? payload.correlationId : undefined,
  })
}

async function request<T>(path: string, init: RequestInit = {}, allowRefresh = true): Promise<T> {
  const headers = new Headers(init.headers)
  if (!headers.has('Content-Type') && init.body) headers.set('Content-Type', 'application/json')

  const accessToken = tokenStore.access
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)

  const response = await fetch(path, { ...init, headers })

  if (response.status === 401 && allowRefresh && tokenStore.refresh) {
    const refreshed = await refreshSession()
    if (refreshed) return request<T>(path, init, false)
    tokenStore.clear()
    window.dispatchEvent(new CustomEvent('nexusbank:signed-out'))
  }

  if (!response.ok) throw await parseError(response)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const api = {
  get: <T>(path: string, params?: Record<string, unknown>) => request<T>(`${path}${queryString(params)}`),
  post: <T>(path: string, body?: unknown, headers?: Record<string, string>) =>
    request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body), headers }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body) }),
  patch: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PATCH', body: body === undefined ? undefined : JSON.stringify(body) }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
}

export function queryString(params?: Record<string, unknown>): string {
  if (!params) return ''
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    search.set(key, String(value))
  }
  const result = search.toString()
  return result ? `?${result}` : ''
}

export async function login(email: string, password: string): Promise<{ accessToken: string; refreshToken: string; user: StoredProfile }> {
  const data = await request<{ accessToken: string; refreshToken: string; user: StoredProfile }>(
    '/api/v1/auth/login',
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    },
    false,
  )
  tokenStore.save(data.accessToken, data.refreshToken, data.user)
  return data
}

export async function logout(): Promise<void> {
  const refreshToken = tokenStore.refresh
  try {
    await fetch('/api/v1/auth/logout', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${tokenStore.access ?? ''}` },
      body: JSON.stringify({ refreshToken }),
    })
  } catch {
    /* local sign-out still proceeds */
  } finally {
    tokenStore.clear()
  }
}
