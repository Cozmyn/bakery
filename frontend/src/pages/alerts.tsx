import React, { useEffect, useMemo, useState } from 'react'
import { api } from '../components/api'
import { UiChip, Pill } from '../components/ui'

type AlertItem = {
  id: string
  runId?: string | null
  type: string
  title: string
  message: string
  severity: 'Info'|'Warning'|'Critical'
  status: 'Active'|'Snoozed'|'Acknowledged'|'Closed'
  triggeredAtUtc: string
  snoozedUntilUtc?: string | null
  acknowledgedByEmail?: string | null
  acknowledgedAtUtc?: string | null
}

function fmtLocal(utc?: string | null){
  if(!utc) return '—'
  return new Date(utc).toLocaleString()
}

export default function AlertsPage({ token, role }:{ token: string, role: string }){
  const [items, setItems] = useState<AlertItem[]>([])
  const [err, setErr] = useState<string | null>(null)
  const [filter, setFilter] = useState<'all'|'critical'|'warning'|'info'>('all')

  async function refresh(){
    try{
      setItems(await api<AlertItem[]>(`/alerts/active`, token))
      setErr(null)
    }catch(e:any){ setErr(e?.message || 'Failed to load alerts') }
  }

  useEffect(() => {
    refresh()
    const t = setInterval(refresh, 4000)
    return () => clearInterval(t)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token])

  const filtered = useMemo(() => {
    if(filter==='all') return items
    if(filter==='critical') return items.filter(x => x.severity==='Critical')
    if(filter==='warning') return items.filter(x => x.severity==='Warning')
    return items.filter(x => x.severity==='Info')
  }, [items, filter])

  const ack = async (id: string) => {
    await api(`/alerts/${id}/ack`, token, { method:'POST', body: JSON.stringify({ note: null }) })
    await refresh()
  }

  const snooze = async (id: string, minutes: number) => {
    await api(`/alerts/${id}/snooze`, token, { method:'POST', body: JSON.stringify({ minutes }) })
    await refresh()
  }

  const close = async (id: string) => {
    await api(`/alerts/${id}/close`, token, { method:'POST' })
    await refresh()
  }

  return (
    <div className="grid2">
      <div className="card">
        <div className="cardHeader">
          <h2>Alert Center</h2>
          <span className="badge">Persisted • ACK / Snooze</span>
        </div>

        <div className="row" style={{ justifyContent:'space-between', marginTop: 10 }}>
          <div className="row">
            <Pill tone="info">Filter</Pill>
            <UiChip selected={filter==='all'} onClick={()=>setFilter('all')}>All</UiChip>
            <UiChip selected={filter==='critical'} onClick={()=>setFilter('critical')}>Critical</UiChip>
            <UiChip selected={filter==='warning'} onClick={()=>setFilter('warning')}>Warning</UiChip>
            <UiChip selected={filter==='info'} onClick={()=>setFilter('info')}>Info</UiChip>
          </div>
          <button className="btn" onClick={refresh}>Refresh</button>
        </div>

        {err && <div className="error" style={{ marginTop: 10 }}>{err}</div>}

        {filtered.length === 0 ? (
          <div className="hint" style={{ marginTop: 12 }}>No active alerts.</div>
        ) : (
          <div style={{ display:'flex', flexDirection:'column', gap: 10, marginTop: 12 }}>
            {filtered.map(a => (
              <div key={a.id} className="alertRow">
                <div style={{ minWidth: 0 }}>
                  <div className="row" style={{ gap: 8, alignItems:'center', flexWrap:'wrap' }}>
                    <Pill tone={a.severity==='Critical' ? 'bad' : (a.severity==='Warning' ? 'warn' : 'info')}>{a.severity}</Pill>
                    <b style={{ fontSize: 14 }}>{a.title}</b>
                    <span className="muted" style={{ fontSize: 12 }}>{a.type}</span>
                    {a.runId && <span className="muted" style={{ fontSize: 12 }}>Run {a.runId.slice(0,8)}…</span>}
                  </div>
                  <div className="hint" style={{ marginTop: 6 }}>{a.message}</div>
                  <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
                    Triggered: {fmtLocal(a.triggeredAtUtc)}
                    {a.snoozedUntilUtc ? <span> • Snoozed until {fmtLocal(a.snoozedUntilUtc)}</span> : null}
                    {a.acknowledgedAtUtc ? <span> • ACK {fmtLocal(a.acknowledgedAtUtc)}</span> : null}
                  </div>
                </div>

                <div className="row" style={{ gap: 6, alignItems:'center', flexWrap:'wrap' }}>
                  <button className="btn" onClick={() => ack(a.id)}>ACK</button>
                  <button className="btn" onClick={() => snooze(a.id, 2)}>Snooze 2m</button>
                  <button className="btn" onClick={() => snooze(a.id, 10)}>10m</button>
                  <button className="btn" onClick={() => snooze(a.id, 30)}>30m</button>
                  <button className="btn danger" onClick={() => close(a.id)}>Close</button>
                </div>
              </div>
            ))}
          </div>
        )}

        <div className="hint" style={{ marginTop: 10 }}>
          Note: VIS defect rate is <b>measured</b> when VIS emits OK/total events (camera counts all pieces). If only defect events exist, the system falls back to an <b>estimated</b> rate based on P2 expected.
        </div>
      </div>

      <div className="card">
        <div className="cardHeader">
          <h2>How alerts are generated</h2>
          <span className="badge">Data-limited, audit-friendly</span>
        </div>
        <ul style={{ marginTop: 10, fontSize: 13, color:'var(--text)', lineHeight: 1.5 }}>
          <li><b>Downtime</b>: open mandatory downtime prompt → critical alert.</li>
          <li><b>Defect spike</b>: last full 10-min bucket defectRate ≥ max(3%, mean+2σ) using VIS measured total (preferred) or fallback P2 expected; aligned by travel-time P2→VIS.</li>
          <li><b>P2 growth drift</b>: height delta P2−P1 deviates from baseline (mean±2σ).</li>
          <li><b>Mix scrap</b>: MIX_SCRAP waste event creates a warning alert.</li>
        </ul>
        {role==='Admin' && (
          <div className="hint" style={{ marginTop: 12 }}>
            Admin can tune thresholds later (ETAPA 6+). For now they are safe defaults.
          </div>
        )}
      </div>
    </div>
  )
}