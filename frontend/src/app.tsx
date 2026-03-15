import React, { useEffect, useMemo, useState } from 'react'
import LoginPage from './pages/login'
import LivePage from './pages/live'
import ProductsPage from './pages/products'
import AnalyticsPage from './pages/analytics'
import OeePage from './pages/oee'
import ReportsPage from './pages/reports'
import AlertsPage from './pages/alerts'
import UsersPage from './pages/users'
import AuditPage from './pages/audit'
import { UiChip, Icon, Modal, Pill } from './components/ui'

type Route = 'live' | 'alerts' | 'analytics' | 'oee' | 'reports' | 'products' | 'users' | 'audit'

export default function App(){
  const [token, setToken] = useState<string | null>(localStorage.getItem('token'))
  const [role, setRole] = useState<string | null>(localStorage.getItem('role'))
  const [route, setRoute] = useState<Route>('live')
  const [kiosk, setKiosk] = useState(false)
  const [theme, setTheme] = useState<'light'|'dark'>(() => (localStorage.getItem('theme') as any) || 'light')
  const [cmd, setCmd] = useState(false)

  useEffect(() => {
    if(token) localStorage.setItem('token', token)
    else localStorage.removeItem('token')
    if(role) localStorage.setItem('role', role)
    else localStorage.removeItem('role')
  }, [token, role])

  // Theme (premium: light default, dark optional)
  useEffect(() => {
    localStorage.setItem('theme', theme)
    document.documentElement.setAttribute('data-theme', theme)
  }, [theme])

  const routes = useMemo(() => {
    const base: { key: Route, label: string, icon: any, adminOnly?: boolean }[] = [
      { key:'live', label:'Live Cockpit', icon:'live' },
      { key:'alerts', label:'Alerts', icon:'alert' },
      { key:'analytics', label:'Analytics', icon:'chart' },
      { key:'oee', label:'OEE', icon:'bolt' },
      { key:'reports', label:'Reports', icon:'factory' },
      { key:'products', label:'Master Data', icon:'admin', adminOnly:true },
      { key:'users', label:'Users', icon:'users', adminOnly:true },
      { key:'audit', label:'Audit', icon:'audit', adminOnly:true }
    ]
    return base.filter(x => !x.adminOnly || role === 'Admin')
  }, [role])

  // Global shortcuts (enabled only when logged in)
  useEffect(() => {
    if(!token) return
    const onKey = (e: KeyboardEvent) => {
      if((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k'){
        e.preventDefault(); setCmd(v => !v)
      }
      if(e.key === 'F1'){
        e.preventDefault(); window.dispatchEvent(new CustomEvent('app-action', { detail: { type: 'downtime' } }))
      }
      if(e.key === 'F2'){
        e.preventDefault(); window.dispatchEvent(new CustomEvent('app-action', { detail: { type: 'batch' } }))
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [token])

  if(!token){
    return <LoginPage onLoggedIn={(t,r) => { setToken(t); setRole(r); }} />
  }

  const roleLabel = role === 'Admin' ? 'Admin' : 'Operator'

  return (
    <div className={`shell ${kiosk ? 'kiosk' : ''}`}>
      <aside className="sidebar">
        <div className="sideBrand">
          <div className="t">Bakery Ops</div>
          <div className="s">{roleLabel} • UTC DB → Local UI</div>
        </div>

        <nav className="nav">
          {routes.map(r => (
            <button key={r.key} className={`navItem ${route===r.key ? 'navItemActive' : ''}`} onClick={() => setRoute(r.key)}>
              <Icon name={r.icon} />
              <span className="label">{r.label}</span>
            </button>
          ))}
          <div style={{ marginTop: 8 }} />
          <button className="navItem" onClick={() => { setToken(null); setRole(null); }}>
            <Icon name="logout" />
            <span className="label">Logout</span>
          </button>
        </nav>
      </aside>

      <main className="main">
        <div className="topbar2">
          <div>
            <div className="title">{
              route === 'live' ? 'Live Line Overview' :
              route === 'alerts' ? 'Alerts & Recommendations' :
              route === 'analytics' ? 'Analytics & Heatmaps' :
              route === 'oee' ? 'OEE (per segment)' :
              route === 'reports' ? 'Reports & Waste Center' :
              route === 'users' ? 'Users (Admin)' :
              route === 'audit' ? 'Audit & Compliance' :
              'Admin & Master Data'
            }</div>
            <div className="subtitle">Fast actions, minimal typing, audit-ready</div>
          </div>
          <div className="row">
            <Pill tone="info"><Icon name="factory" /> {roleLabel}</Pill>
            <button className="btn" onClick={() => setTheme(t => t === 'light' ? 'dark' : 'light')} style={{ padding: '8px 10px' }}>
              {theme === 'light' ? 'Dark' : 'Light'}
            </button>
            <button className="btn" onClick={() => setKiosk(v => !v)} style={{ padding: '8px 10px' }}>{kiosk ? 'Exit kiosk' : 'Kiosk'}</button>
            <button className="btn" onClick={() => setCmd(true)} style={{ padding: '8px 10px' }}>Ctrl+K</button>
          </div>
        </div>

        <div className="container" style={{ marginTop: 14 }}>
          {route === 'live' && <LivePage token={token} role={role ?? 'Operator'} />}
          {route === 'alerts' && <AlertsPage token={token} role={role ?? 'Operator'} />}
          {route === 'analytics' && <AnalyticsPage token={token} role={role ?? 'Operator'} />}
          {route === 'oee' && <OeePage token={token} />}
          {route === 'reports' && <ReportsPage token={token} role={role ?? 'Operator'} />}
          {route === 'products' && role === 'Admin' && <ProductsPage token={token} />}
          {route === 'users' && role === 'Admin' && <UsersPage token={token} role={role ?? 'Admin'} />}
          {route === 'audit' && role === 'Admin' && <AuditPage token={token} role={role ?? 'Admin'} />}
        </div>

        <Modal
          open={cmd}
          title="Quick actions"
          subtitle="Ctrl+K • F1 Downtime • F2 Batch"
          actions={<button className="btn" onClick={() => setCmd(false)}>Close</button>}
        >
          <div className="row" style={{ flexWrap:'wrap', gap: 8 }}>
            {routes.map(r => (
              <UiChip key={r.key} selected={route===r.key} onClick={() => { setRoute(r.key); setCmd(false) }}>{r.label}</UiChip>
            ))}
            <UiChip onClick={() => { window.dispatchEvent(new CustomEvent('app-action', { detail: { type:'downtime' } })); setCmd(false) }}>Downtime (F1)</UiChip>
            <UiChip onClick={() => { window.dispatchEvent(new CustomEvent('app-action', { detail: { type:'batch' } })); setCmd(false) }}>Batch (F2)</UiChip>
            <UiChip onClick={() => { setTheme(t => t === 'light' ? 'dark' : 'light'); setCmd(false) }}>Toggle theme</UiChip>
          </div>
          <div className="hint" style={{ marginTop: 10 }}>Kiosk mode hides menus for large screens.</div>
        </Modal>
      </main>
    </div>
  )
}