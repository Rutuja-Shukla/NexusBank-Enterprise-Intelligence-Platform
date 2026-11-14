import { z } from 'zod'

export const loginSchema = z.object({
  email: z.string().min(1, 'Email is required').email('Enter a valid email address'),
  password: z.string().min(8, 'Password must be at least 8 characters'),
})
export type LoginValues = z.infer<typeof loginSchema>

export const paymentSchema = z.object({
  debtorAccountId: z.string().min(1, 'Select the account to debit'),
  creditorName: z.string().min(2, 'Beneficiary name is required').max(140),
  creditorAccountNumber: z.string().min(8, 'Enter a beneficiary account number').max(34),
  creditorBic: z
    .string()
    .regex(/^[A-Za-z0-9]{8}([A-Za-z0-9]{3})?$/, 'BIC must be 8 or 11 characters')
    .or(z.literal(''))
    .optional(),
  amount: z.coerce.number().positive('Amount must be greater than zero').max(1_000_000, 'Amount exceeds the sandbox limit'),
  currency: z.enum(['NZD', 'AUD', 'USD', 'GBP']),
  type: z.enum(['Transfer', 'CardPayment', 'DirectDebit', 'Refund']),
  description: z.string().max(280, 'Keep the description under 280 characters').optional(),
  simulateFailure: z.boolean().optional(),
})
export type PaymentValues = z.infer<typeof paymentSchema>

export const integrationSchema = z.object({
  name: z.string().min(1, 'Name is required').max(120),
  key: z
    .string()
    .regex(/^[a-z0-9][a-z0-9-]{2,63}$/, 'Use 3-64 lowercase letters, numbers or hyphens'),
  description: z.string().max(500).optional(),
  category: z.enum(['Core', 'Payments', 'Cards', 'Risk', 'Data', 'External']),
  baseUrl: z.string().min(1, 'Endpoint is required'),
  authType: z.enum(['ApiKey', 'OAuth2ClientCredentials', 'MutualTls', 'JwtBearer', 'None']),
  ownerTeam: z.string().min(1, 'Owner team is required').max(80),
  version: z.string().min(1, 'Version is required').max(20),
  timeoutSeconds: z.coerce.number().min(1).max(120),
})
export type IntegrationValues = z.infer<typeof integrationSchema>

export const incidentSchema = z.object({
  title: z.string().min(8, 'Give the incident a descriptive title').max(160),
  description: z.string().min(20, 'Describe the impact and observed behaviour').max(2000),
  severity: z.enum(['Sev1', 'Sev2', 'Sev3', 'Sev4']),
  category: z.enum(['Integration', 'Payment', 'Security', 'Performance', 'DataQuality', 'Infrastructure']),
  team: z.string().min(1, 'Owning team is required').max(80),
  affectedService: z.string().max(120).optional(),
  impactSummary: z.string().max(500).optional(),
})
export type IncidentValues = z.infer<typeof incidentSchema>

export const createUserSchema = z.object({
  email: z.string().email('Enter a valid email address'),
  displayName: z.string().min(2, 'Display name is required').max(120),
  roleSlug: z.string().min(1, 'Select a role'),
  password: z
    .string()
    .min(12, 'Use at least 12 characters')
    .regex(/[A-Z]/, 'Include an uppercase letter')
    .regex(/[a-z]/, 'Include a lowercase letter')
    .regex(/[0-9]/, 'Include a digit'),
})
export type CreateUserValues = z.infer<typeof createUserSchema>

export const noteSchema = z.object({
  detail: z.string().min(3, 'Write at least a few words').max(1000),
})
export type NoteValues = z.infer<typeof noteSchema>

export const eventSchema = z.object({
  topic: z.string().min(1, 'Select a topic'),
  eventType: z.string().min(3, 'Event type is required').max(200),
  payload: z
    .string()
    .min(2, 'Provide a JSON payload')
    .refine((value) => {
      try {
        JSON.parse(value)
        return true
      } catch {
        return false
      }
    }, 'Payload must be valid JSON'),
})
export type EventValues = z.infer<typeof eventSchema>
