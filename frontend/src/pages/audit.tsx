import React, { useEffect, useMemo, useState } from 'react'
import { api } from '../components/api'
import { UiChip, Modal, Pill, SkeletonCard } from '../components/ui'

type Props = { token: string, role: string }

type AuditRow = {
  id: string
  tsUtc: string
  method: string
  path: string
  action: string
  userEmail?: string|null
  userRole?: string|null
  entityType?: string|null
  entityId?: string|null
  ipAddress?: string|null
  statusCode: number
  detailJson?: string|null
}

export default function AuditPage({ token, role }: Props){
  const [days, setDays] = useState(7)
  const [qUser, setQUser] = useState('')
  const [qEntity, setQEntity] = useState('')
  const [data, setData] = useState<{ total: number, items: AuditRow[] }|null>(null)
  const [err, setErr] = useState<string|null>(null)
  const [openId, setOpenId] = useState<string|null>(null)

  const selected = useMemo(() => data?.items.find(x => x.id === openId) ?? null, [data, openId])

  useEffect(() => {
    if(role !== 'Admin') return
    let alive = true
    ;(async () => {
      try{
        setErr(null)
        const qs = new URLSearchParams()
        qs.set('days', String(days))
        qs.set('limit', '250')
        if(qUser.trim()) qs.set('userEmail', qUser.trim())
        if(qEntity.trim()) qs.set('entityType', qEntity.trim())
        const res = await api<any>(`/audit?${qs.toString()}`, token)
        if(alive) setData(res)
      }catch(e:any){
        if(alive) setErr(e?.message ?? 'Failed to load audit log')
      }
    })()
    return () => { alive = false }
  }, [token, role, days, qUser, qEntity])

  if(role !== 'Admin'){
    return (
      <div className="page">
        <div className="pageTitle">Audit Log</div>
        <div className="card" style={{ padding: 18 }}>
          <div style={{ color:'var(--muted)' }}>Admin access required.</div>
        </div>
      </div>
    )
  }

  function toneForMethod(m:string){
    if(m === 'POST') return 'info'
    if(m === 'PUT' || m === 'PATCH') return 'warn'
    if(m === 'DELETE') return 'bad'
    return 'neutral'
  }

  function toneForStatus(code:number){
    if(code >= 500) return 'bad'
    if(code >= 400) return 'warn'
    return 'good'
  }

  return (
    <div className="page">
      <div className="pageHeader">
        <div>
          <div className="pageTitle">Audit Log</div>
          <div className="pageSubtitle">Every mutating action is recorded with UTC timestamp, user, route, status, entity hint, and IP.</div>
        </div>
        <div style={{ display:'flex', gap: 10, alignItems:'center', flexWrap:'wrap', justifyContent:'flex-end' }}>
          <UiChip selected={days===7} onClick={() => setDays(7)}>7d</UiChip>
          <UiChip selected={days===30} onClick={() => setDays(30)}>30d</UiChip>
          <UiChip selected={days===90} onClick={() => setDays(90)}>90d</UiChip>
        </div>
      </div>

      <div className="card" style={{ marginBottom: 12 }}>
        <div className="filtersRow">
          <label className="field" style={{ minWidth: 220 }}>
            <div className="label">User filter</div>
            <input value={qUser} onChange={e => setQUser(e.target.value)} placeholder="e.g. admin@local" />
          </label>
          <label className="field" style={{ minWidth: 220 }}>
            <div className="label">Entity filter</div>
            <input value={qEntity} onChange={e => setQEntity(e.target.value)} placeholder="e.g. products, prompts, batches" />
          </label>
          <div style={{ color:'var(--muted)', fontSize: 12, marginLeft:'auto' }}>
            {data ? `${data.total} records (showing ${data.items.length})` : '—'}
          </div>
        </div>
      </div>

      {err && <div className="warnBox">{err}</div>}

      <div className="card">
        {!data ? (
          <SkeletonCard lines={8} />
        ) : (
          <div className="table">
            <div className="tr th">
              <div>Time</div>
              <div>User</div>
              <div>Action</div>
              <div>Entity</div>
              <div>IP</div>
              <div>Status</div>
              <div style={{ textAlign:'right' }}>Detail</div>
            </div>

            {data.items.map(a => (
              <div className="tr" key={a.id}>
                <div style={{ fontSize: 12, color:'var(--muted)' }}>{new Date(a.tsUtc).toLocaleString()}</div>
                <div style={{ display:'flex', gap: 8, alignItems:'center', flexWrap:'wrap' }}>
                  <span style={{ fontWeight: 700 }}>{a.userEmail ?? '—'}</span>
                  {a.userRole && <Pill tone="neutral">{a.userRole}</Pill>}
                </div>
                <div style={{ display:'flex', gap: 8, alignItems:'center', flexWrap:'wrap' }}>
                  <Pill tone={toneForMethod(a.method)}>{a.method}</Pill>
                  <span className="mono" style={{ fontSize: 12 }}>{a.path}</span>
                </div>
                <div style={{ fontSize: 12 }}>
                  <span style={{ fontWeight: 700 }}>{a.entityType ?? '—'}</span>
                  {a.entityId && <span style={{ color:'var(--muted)' }}> · {a.entityId}</span>}
                </div>
                <div className="mono" style={{ fontSize: 12, color:'var(--muted)' }}>{a.ipAddress ?? '—'}</div>
                <div><Pill tone={toneForStatus(a.statusCode)}>{a.statusCode}</Pill></div>
                <div style={{ textAlign:'right' }}>
                  <button className="btn btn-ghost" onClick={() => setOpenId(a.id)}>Open</button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      <Modal
        open={!!openId}
        title="Audit Detail"
        subtitle={selected ? `${selected.method} ${selected.path}` : ''}
        actions={
          <div style={{ display:'flex', justifyContent:'flex-end' }}>
            <button className="btn btn-ghost" onClick={() => setOpenId(null)}>Close</button>
          </div>
        }
      >
        {selected ? (
          <div style={{ display:'grid', gap: 10 }}>
            <div className="kvGrid">
              <div className="kv">
                <div className="k">Timestamp (UTC)</div>
                <div className="v mono">{selected.tsUtc}</div>
              </div>
              <div className="kv">
                <div className="k">User</div>
                <div className="v">{selected.userEmail ?? '—'}</div>
              </div>
              <div className="kv">
                <div className="k">Role</div>
                <div className="v">{selected.userRole ?? '—'}</div>
              </div>
              <div className="kv">
                <div className="k">Entity</div>
                <div className="v">{selected.entityType ?? '—'}{selected.entityId ? ` / ${selected.entityId}` : ''}</div>
              </div>
              <div className="kv">
                <div className="k">IP</div>
                <div className="v mono">{selected.ipAddress ?? '—'}</div>
              </div>
              <div className="kv">
                <div className="k">Status</div>
                <div className="v">{selected.statusCode}</div>
              </div>
            </div>

            <div className="jsonBox">
              <div style={{ fontWeight: 800, marginBottom: 8 }}>Detail JSON</div>
              <pre style={{ margin: 0, whiteSpace:'pre-wrap', fontSize: 12, lineHeight: 1.35 }}>
{selected.detailJson ?? '—'}
              </pre>
            </div>
          </div>
        ) : (
          <SkeletonCard lines={6} />
        )}
      </Modal>
    </div>
  )
}