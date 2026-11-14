import { expect, test } from '@playwright/test'

/**
 * Requires a running API (:8080) and console (:5173) with the seeded database.
 * Not executed as part of `npm test`; run with `npx playwright test`.
 */
test.describe('operator journey', () => {
  test('signs in, inspects the estate and opens a payment journey', async ({ page }) => {
    await page.goto('/login')

    await page.getByLabel(/email/i).fill('admin@nexusbank.dev')
    await page.getByLabel(/password/i).fill('NexusBank!2026')
    await page.getByRole('button', { name: /sign in/i }).click()

    await expect(page.getByRole('heading', { name: /command centre/i })).toBeVisible()
    await expect(page.getByText(/integration network/i)).toBeVisible()

    await page.getByRole('link', { name: /payments/i }).first().click()
    await expect(page.getByRole('heading', { name: /payment operations/i })).toBeVisible()

    await page.getByRole('row').nth(1).click()
    await expect(page.getByText(/transaction journey/i)).toBeVisible()
    await expect(page.getByText(/ledger/i).first()).toBeVisible()
  })

  test('auditor cannot perform write actions', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel(/email/i).fill('auditor@nexusbank.dev')
    await page.getByLabel(/password/i).fill('NexusBank!2026')
    await page.getByRole('button', { name: /sign in/i }).click()

    await expect(page.getByRole('heading', { name: /command centre/i })).toBeVisible()
    await page.goto('/payments')
    await expect(page.getByRole('button', { name: /new payment/i })).toHaveCount(0)
  })
})
