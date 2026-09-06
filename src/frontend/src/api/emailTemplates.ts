import { apiFetch } from './auth'

/** T-076. */
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
  /** Must appear in both bodies. The server refuses a save that drops one — an invitation without its
   *  link is an invitation nobody can accept. */
  requiredTokens: string[]
  optionalTokens: string[]
  override: EmailTemplateCopy | null
  /** The shipped wording with its tokens still in it, so the screen can show what an override replaces. */
  shipped: EmailTemplateCopy
}

/** Parameter properties are not available under `erasableSyntaxOnly`, so the fields are declared. */
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
    // The token refusals arrive as §7 problem documents with the offending tokens on an extension member,
    // and the screen shows them: "a token is missing" is not something an administrator can act on. Read
    // `code`, not `error` - the middleware reshapes every non-2xx into problem+json.
    const body = text ? (JSON.parse(text) as { code?: string; tokens?: string[] }) : {}
    throw new EmailTemplateError(body.code ?? 'SAVE_FAILED', body.tokens ?? [])
  }
  return JSON.parse(text) as EmailTemplateCopy
}

export async function deleteEmailTemplate(key: string): Promise<void> {
  const response = await apiFetch(`/api/v1/admin/email-templates/${encodeURIComponent(key)}`, { method: 'DELETE' })
  if (!response.ok) throw new Error('email_template_delete_failed')
}
