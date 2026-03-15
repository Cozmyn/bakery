import React, { useState } from 'react'
import { api } from '../components/api'

type Props = { onLoggedIn: (token: string, role: string) => void }

export default function LoginPage({ onLoggedIn }: Props){
  const [email, setEmail] = useState('admin@local')
  const [password, setPassword] = useState('Admin123!')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(){
    setBusy(true)
    setError(null)
    try{
      const r = await api<{token:string, role:string, email:string}>(`/auth/login`, null, {
        method: 'POST',
        body: JSON.stringify({ email, password })
      })
      onLoggedIn(r.token, r.role)
    }catch(e:any){
      setError(e?.message ?? 'Login failed')
    }finally{
      setBusy(false)
    }
  }

  return (
    <div className="container" style={{ maxWidth: 520 }}>
      <div className="topbar" style={{ marginTop: 40 }}>
        <div className="brand">
          <h1>Bakery Line</h1>
          <span>Login</span>
        </div>
      </div>

      <div className="card" style={{ marginTop: 14 }}>
        <div className="row" style={{ justifyContent: 'space-between' }}>
          <div>
            <div style={{ fontSize: 12, color: 'var(--muted)' }}>Email</div>
            <input value={email} onChange={e => setEmail(e.target.value)} style={{ width: '100%' }} />
          </div>
          <div>
            <div style={{ fontSize: 12, color: 'var(--muted)' }}>Password</div>
            <input type="password" value={password} onChange={e => setPassword(e.target.value)} style={{ width: '100%' }} />
          </div>
        </div>
        <div className="row" style={{ marginTop: 12, justifyContent: 'space-between' }}>
          <div className="badge">Dev seed users: admin@local / operator@local</div>
          <button className="primary" disabled={busy} onClick={submit}>{busy ? '...' : 'Login'}</button>
        </div>
        {error && <div style={{ marginTop: 10, color: '#b91c1c', fontSize: 12 }}>{error}</div>}
      </div>
    </div>
  )
}
