const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export interface Currency {
  id: string
  code: string
  nameAr: string
  nameEn: string
}

export async function fetchCurrencies(): Promise<Currency[]> {
  const res = await fetch(`${API_BASE_URL}/api/v1/reference/currencies`)
  if (!res.ok) throw new Error(`Failed to fetch currencies: ${res.status}`)
  return res.json()
}

export async function fetchHealth(): Promise<string> {
  const res = await fetch(`${API_BASE_URL}/health`)
  if (!res.ok) throw new Error(`Health check failed: ${res.status}`)
  return res.text()
}
