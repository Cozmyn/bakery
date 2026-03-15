const API_BASE = import.meta.env.VITE_API_BASE_URL || 'http://localhost:8080'

function hasJsonContentType(res: Response){
  const ct = res.headers.get('content-type') || ''
  return ct.includes('application/json') || ct.includes('+json')
}

export async function api<T>(path: string, token: string | null, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
      ...(init?.headers || {})
    }
  })

  if(!res.ok){
    const txt = await res.text()
    throw new Error(txt || `HTTP ${res.status}`)
  }

  // Many endpoints return 200/204 with an empty body (e.g., /runs/{id}/stop). Avoid JSON parse errors.
  if(res.status === 204) return undefined as unknown as T

  const txt = await res.text()
  if(!txt) return undefined as unknown as T

  if(hasJsonContentType(res)){
    return JSON.parse(txt) as T
  }
  return txt as unknown as T
}

export function apiBase(){
  return API_BASE
}
