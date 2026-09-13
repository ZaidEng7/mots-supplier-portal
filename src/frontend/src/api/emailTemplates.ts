// T-076: the admin screen over the 23 transactional emails.
//
// Required tokens must appear in both bodies, and the server refuses a save that drops one - an invitation
// without its link is an invitation nobody can accept. The shipped wording travels with its tokens still in it,
// so the screen can show what an override replaces.
//
// EmailTemplateError declares its fields rather than using parameter properties, which are not available under
// erasableSyntaxOnly.
//
// The token refusals arrive as §7 problem documents with the offending tokens on an extension member, and the
// screen shows them, because "a token is missing" on its own is not something an administrator can act on. It
// reads `code` rather than `error`: the middleware reshapes every non-2xx into problem+json.

import { apiFetch } from './auth'

export interface EmailTemplateCopy {
  key: string
  subjectAr: string
  subjectEn: string
  bodyAr: string
  bodyEn: string
  updatedAt: string
}

export interface EmailTemplateRow {
  key: string
  requiredTokens: string[]
  optionalTokens: string[]
  override: EmailTemplateCopy | null
  shipped: EmailTemplateCopy
}

export class EmailTemplateError extends Error {
  readonly reason: string
  readonly tokens: string[]

  constructor(reason: string, tokens: string[]) {
    super(reason)
    this.reason = reason
    this.tokens = tokens
  }
}

export async function listEmailTemplates(): Promise<EmailTemplateRow[]> {
  const response = await apiFetch('/api/v1/admin/email-templates/')
  if (!response.ok) throw new Error('email_templates_unavailable')
  return (await response.json()) as EmailTemplateRow[]
}

export async function upsertEmailTemplate(
  key: string,
  copy: { subjectAr: string; subjectEn: string; bodyAr: string; bodyEn: string },
): Promise<EmailTemplateCopy> {
  const response = await apiFetch(`/api/v1/admin/email-templates/${encodeURIComponent(key)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(copy),
  })
  const text = await response.text()
  if (!response.ok) {
    const body = text ? (JSON.parse(text) as { code?: string; tokens?: string[] }) : {}
    throw new EmailTemplateError(body.code ?? 'SAVE_FAILED', body.tokens ?? [])
  }
  return JSON.parse(text) as EmailTemplateCopy
}

export async function deleteEmailTemplate(key: string): Promise<void> {
  const response = await apiFetch(`/api/v1/admin/email-templates/${encodeURIComponent(key)}`, { method: 'DELETE' })
  if (!response.ok) throw new Error('email_template_delete_failed')
}
