import { describe, expect, it } from 'vitest'
import { ApiError, describeError, queryString, tokenStore } from '@/lib/api'

describe('queryString', () => {
  it('keeps populated values and drops empty ones', () => {
    expect(queryString({ page: 1, search: 'harbour', status: undefined, role: null, cursor: '' })).toBe('?page=1&search=harbour')
  })

  it('returns an empty string when there is nothing to send', () => {
    expect(queryString()).toBe('')
    expect(queryString({ search: '' })).toBe('')
  })
})

describe('ApiError', () => {
  it('exposes the first validation message for inline form display', () => {
    const error = new ApiError(400, 'One or more validation errors occurred.', {
      errors: { CreditorName: ['Beneficiary name is required.'], Amount: ['Amount must be positive.'] },
    })

    expect(error.firstFieldError).toBe('Beneficiary name is required.')
    expect(describeError(error)).toBe('Beneficiary name is required.')
  })

  it('falls back to the problem detail when no field errors exist', () => {
    const error = new ApiError(403, 'Your role does not grant the permission required for this operation.')

    expect(describeError(error)).toContain('does not grant')
  })

  it('never leaks an object representation for unknown failures', () => {
    expect(describeError('boom')).toBe('Unexpected error')
  })
})

describe('tokenStore', () => {
  it('persists and clears the session material', () => {
    tokenStore.save('access', 'refresh', {
      id: 'u-1',
      email: 'ops@nexusbank.dev',
      displayName: 'Operations',
      role: 'Operations Engineer',
      roleSlug: 'operations',
      permissions: ['payments:read'],
      lastLoginAt: null,
    })

    expect(tokenStore.access).toBe('access')
    expect(tokenStore.profile?.roleSlug).toBe('operations')

    tokenStore.clear()

    expect(tokenStore.access).toBeNull()
    expect(tokenStore.profile).toBeNull()
  })
})
