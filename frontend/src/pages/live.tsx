import React, { useEffect, useMemo, useRef, useState } from 'react'
import { api, apiBase } from '../components/api'
import { startStream } from '../components/stream'
import { UiChip, Icon, Modal, Pill } from '../components/ui'
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from 'recharts'
import { PremiumTooltip, ChartColors } from '../components/charts'

type Props = { token: string, role: string }

type Product = { id: string, code: string, name: string, publishedAtUtc?: string | null }

type CurrentRunResp =
  | { running: false }
  | { running: true, run: { runId: string, startUtc: string, status: string, productionEndUtc?: string|null, greyZoneStartUtc?: string|null, greyZoneEndUtc?: string|null, product: { productId: string, code: string, name: string }, batches: any[] } }

type PointResp = { running: boolean, runId?: string, point: string, pieces: any[] }

type StreamSnapshot = { running: boolean, runId?: string, status?: string, productionEndUtc?: string|null, vis?: { total: number, defects: number, good: number, defectRate: number }, defects: any[], prompts: any[], alerts?: any[] }

type Reason = { code: string, label: string, category: string, isOneTap: boolean }

function fmtLocal(utc?: string | null){
  if(!utc) return '—'
  return new Date(utc).toLocaleString()
}

function toUtcISOString(d: Date){
  return d.toISOString()
}

function clamp(n:number, a:number, b:number){ return Math.max(a, Math.min(b, n)) }

function elapsedSince(utc?: string | null){
  if(!utc) return '—'
  const ms = Math.max(0, Date.now() - new Date(utc).getTime())
  const h = Math.floor(ms / 3_600_000)
  const m = Math.floor((ms % 3_600_000) / 60_000)
  return h > 0 ? `${h}h ${m}m` : `${m}m`
}

function promptLabel(type?: string){
  if(type === 'DOWNTIME_REQUIRED') return 'Downtime reason required'
  if(type === 'BELT_EMPTY_REQUIRED') return 'Belt empty reason required'
  if(type === 'CHANGEOVER_QUESTION') return 'Changeover confirmation required'
  return 'Operator action required'
}

function alertTone(severity?: string): 'bad'|'warn'|'info' {
  if(severity === 'Critical') return 'bad'
  if(severity === 'Warning') return 'warn'
  return 'info'
}

function proofTone(minutes?: number | null): 'warn'|'good'|'neutral' {
  if(minutes == null) return 'neutral'
  if(minutes < 40 || minutes > 70) return 'warn'
  return 'good'
}

export default function LivePage({ token, role }: Props){
  const [current, setCurrent] = useState<CurrentRunResp>({ running: false })
  const [p1, setP1] = useState<PointResp | null>(null)
  const [p2, setP2] = useState<PointResp | null>(null)
  const [p3, setP3] = useState<PointResp | null>(null)
  const [stream, setStreamState] = useState<StreamSnapshot>({ running:false, defects:[], prompts:[], alerts:[] })
  const [products, setProducts] = useState<Product[]>([])
  const [selectedProduct, setSelectedProduct] = useState<string>('')
  const [reasons, setReasons] = useState<Reason[]>([])
  const [defectTypes, setDefectTypes] = useState<Record<string, { label: string, category: string, severity: number }>>({})

  const [busy, setBusy] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  // Modals
  const [discardModal, setDiscardModal] = useState<{ open:boolean, batchId?:string }>({ open:false })
  const [discardReason, setDiscardReason] = useState<string>('NO_DOUGH')

  const [batchModal, setBatchModal] = useState<{ open:boolean, mode:'create'|'edit', batch?:any }>({ open:false, mode:'create' })
  const [mixedAt, setMixedAt] = useState<Date | null>(null)
  const [addedAt, setAddedAt] = useState<Date | null>(null)
  const [batchReason, setBatchReason] = useState<string>('OTHER')

  const [promptModal, setPromptModal] = useState<{ open:boolean, prompt?:any }>({ open:false })
  const [promptReason, setPromptReason] = useState<string>('OTHER')
  const [promptChoice, setPromptChoice] = useState<'YES'|'NO'|'END'>('NO')
  const [promptNewProduct, setPromptNewProduct] = useState<string>('')

  // Mix sheet modal
  const [mixModal, setMixModal] = useState<{ open:boolean, batch?:any, data?:any }>({ open:false })
  const [mixErr, setMixErr] = useState<string | null>(null)
  const [allIngredients, setAllIngredients] = useState<any[]>([])

  // Prevent re-initializing prompt state on every stream tick.
  const activePromptIdRef = useRef<string | null>(null)

  const abortRef = useRef<AbortController | null>(null)

  const pendingStreamRef = useRef<StreamSnapshot | null>(null)
  const streamTimerRef = useRef<number | null>(null)

  // Images disabled (stability): do not fetch or render live defect images.
  function AuthImage(_props: { imageTokenId?: string | null }){
    return null
  }

  const running = current.running
  const runId = current.running ? current.run.runId : null

  async function refreshCurrent(){
    try{ setCurrent(await api<CurrentRunResp>(`/live/current-run`, token)) }catch(e:any){ setErr(e.message) }
  }

  async function refreshPoints(){
    if(!runId) return
    try{
      setP1(await api<PointResp>(`/live/points/P1`, token))
      setP2(await api<PointResp>(`/live/points/P2`, token))
      setP3(await api<PointResp>(`/live/points/P3`, token))
    }catch(e:any){ setErr(e.message) }
  }

  async function loadProducts(){
    try{
      const list = await api<Product[]>(`/products`, token)
      const published = list.filter(p => (p as any).publishedAtUtc)
      setProducts(published)
      if(!selectedProduct && published.length) setSelectedProduct(published[0].id)
    }catch(e:any){ setErr(e.message) }
  }

  async function loadIngredients(){
    try{ setAllIngredients(await api<any[]>(`/ingredients`, token)) }catch{ /* ignore */ }
  }

  async function loadReasons(){
    try{ setReasons(await api<Reason[]>(`/downtime-reasons`, token)) }catch{ /* ignore */ }
  }

  async function loadDefectTypes(){
    try{
      const list = await api<any[]>(`/defect-types`, token)
      const map: any = {}
      list.forEach(d => { map[d.code] = { label: d.label, category: d.category, severity: (d.severity ?? d.severityDefault ?? 2) } })
      setDefectTypes(map)
    }catch{ /* ignore */ }
  }

  // Core initial load
  useEffect(() => {
    refreshCurrent(); loadProducts(); loadReasons(); loadIngredients(); loadDefectTypes();
    const t = setInterval(refreshCurrent, 3000)
    return () => clearInterval(t)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Points refresh at 60s
  useEffect(() => {
    refreshPoints()
    const t = setInterval(refreshPoints, 60_000)
    return () => clearInterval(t)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [runId])

  // Realtime stream (SSE-over-fetch) for defects + prompts
  useEffect(() => {
    abortRef.current?.abort()
    abortRef.current = new AbortController()

    const url = `${apiBase()}/live/stream`
    startStream(url, token, (m) => {
      if(m.event !== 'snapshot' && m.event !== 'run') return
      const snap = m.data as StreamSnapshot
      // Cap high-frequency arrays to keep UI responsive.
      if(Array.isArray((snap as any).defects)) (snap as any).defects = (snap as any).defects.slice(0, 120)
      if(Array.isArray((snap as any).prompts)) (snap as any).prompts = (snap as any).prompts.slice(0, 20)
      if(Array.isArray((snap as any).alerts)) (snap as any).alerts = (snap as any).alerts.slice(0, 20)

      pendingStreamRef.current = snap
      if(streamTimerRef.current != null) return
      streamTimerRef.current = window.setTimeout(() => {
        streamTimerRef.current = null
        const s = pendingStreamRef.current
        pendingStreamRef.current = null
        if(s) setStreamState(s)
      }, 300)
    }, abortRef.current!.signal).catch(() => { /* ignore */ })

    return () => abortRef.current?.abort()
  }, [token])

  // Open mandatory prompts modal (priority: downtime > belt empty > changeover)
  useEffect(() => {
    if(!running) {
      activePromptIdRef.current = null
      setPromptModal({ open:false })
      return
    }
    const prompts = (stream.prompts ?? []) as any[]
    const open = prompts.find(p => p.type === 'DOWNTIME_REQUIRED')
      || prompts.find(p => p.type === 'BELT_EMPTY_REQUIRED')
      || prompts.find(p => p.type === 'CHANGEOVER_QUESTION')
    if(!open){
      activePromptIdRef.current = null
      setPromptModal({ open:false })
      return
    }

    const openId = String(open.id ?? '')

    // Only initialize defaults when a NEW prompt appears.
    if(activePromptIdRef.current !== openId){
      activePromptIdRef.current = openId
      setPromptModal({ open:true, prompt: open })
      setPromptReason('OTHER')
      setPromptChoice('NO')
      setPromptNewProduct(selectedProduct || (products[0]?.id ?? ''))
      return
    }

    // Keep prompt payload fresh without resetting user selection.
    setPromptModal(m => (m.open && (m.prompt?.id === openId)) ? ({ open:true, prompt: open }) : m)
  }, [stream.prompts, running, selectedProduct, products])

  // Keyboard shortcuts from app shell (F1/F2)
  useEffect(() => {
    const onAction = (ev: any) => {
      const type = ev?.detail?.type
      if(type === 'batch'){
        if(!running) return
        setBatchModal({ open:true, mode:'create' })
        setMixedAt(new Date())
        setAddedAt(null)
        setBatchReason('OTHER')
      }
      if(type === 'downtime'){
        const prompts = (stream.prompts ?? []) as any[]
        const p = prompts.find(x => x.type === 'DOWNTIME_REQUIRED')
          || prompts.find(x => x.type === 'BELT_EMPTY_REQUIRED')
          || prompts.find(x => x.type === 'CHANGEOVER_QUESTION')
        if(p) setPromptModal({ open:true, prompt: p })
      }
    }
    window.addEventListener('app-action', onAction as any)
    return () => window.removeEventListener('app-action', onAction as any)
  }, [running, stream.prompts])

  // Load mix sheet when modal opens
  useEffect(() => {
    if(!mixModal.open || !mixModal.batch?.id) return
    let alive = true
    setMixErr(null)
    api<any>(`/batches/${mixModal.batch.id}/mix-sheet`, token)
      .then(r => { if(alive) setMixModal(m => ({ ...m, data: r })) })
      .catch((e:any) => { if(alive) setMixErr(e.message) })
    return () => { alive = false }
  }, [mixModal.open, mixModal.batch?.id, token])

  // Chart: real avg/best from backend
  const [chartPoints, setChartPoints] = useState<any[]>([])
  useEffect(() => {
    if(!running) { setChartPoints([]); return }
    let alive = true
    async function loadChart(){
      try{
        const r = await api<any>(`/analytics/p3-volume`, token)
        if(alive) setChartPoints(r.points ?? [])
      }catch{ /* ignore */ }
    }
    loadChart()
    const t = setInterval(loadChart, 15_000)
    return () => { alive = false; clearInterval(t) }
  }, [running, token])

  async function startRun(){
    setErr(null); setBusy('startRun')
    try{
      await api(`/runs/start`, token, { method:'POST', body: JSON.stringify({ productId: selectedProduct }) })
      await refreshCurrent();
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  async function stopRun(){
    if(!runId) return
    setErr(null); setBusy('stopRun')
    try{
      await api(`/runs/${runId}/stop`, token, { method:'POST' })
      await refreshCurrent();
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  async function ackAlert(id: string){
    try{ await api('/alerts/' + id + '/ack', token, { method: 'POST', body: JSON.stringify({ note: null }) }) }catch{}
  }
  async function snoozeAlert(id: string, minutes: number){
    try{ await api('/alerts/' + id + '/snooze', token, { method: 'POST', body: JSON.stringify({ minutes }) }) }catch{}
  }
  async function closeAlert(id: string){
    try{ await api('/alerts/' + id + '/close', token, { method: 'POST' }) }catch{}
  }

  async function simStart(){
    setErr(null); setBusy('simStart')
    try{
      await api(`/sim/start`, token, { method:'POST', body: JSON.stringify({ productId: selectedProduct }) })
      await refreshCurrent()
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }
  async function simStop(){
    setErr(null); setBusy('simStop')
    try{ await api(`/sim/stop`, token, { method:'POST' }) }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  function openCreateBatch(){
    setBatchModal({ open:true, mode:'create' })
    setMixedAt(new Date(Date.now() - 50*60*1000))
    setAddedAt(null)
    setBatchReason('OTHER')
  }
  function openEditBatch(b:any){
    setBatchModal({ open:true, mode:'edit', batch: b })
    setMixedAt(b.mixedAtUtc ? new Date(b.mixedAtUtc) : null)
    setAddedAt(b.addedToLineAtUtc ? new Date(b.addedToLineAtUtc) : null)
    setBatchReason('OTHER')
  }

  async function saveBatch(){
    if(!runId) return
    setErr(null); setBusy('saveBatch')
    try{
      if(batchModal.mode==='create'){
        await api(`/runs/${runId}/batches`, token, {
          method:'POST',
          body: JSON.stringify({ mixedAtUtc: mixedAt ? toUtcISOString(mixedAt) : null, addedToLineAtUtc: addedAt ? toUtcISOString(addedAt) : null })
        })
      } else if(batchModal.batch?.id){
        await api(`/runs/batches/${batchModal.batch.id}/times`, token, {
          method:'PUT',
          body: JSON.stringify({ mixedAtUtc: mixedAt ? toUtcISOString(mixedAt) : null, addedToLineAtUtc: addedAt ? toUtcISOString(addedAt) : null, reasonCode: batchReason, comment: null })
        })
      }
      setBatchModal({ open:false, mode:'create' })
      await refreshCurrent()
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  function openDiscard(batchId: string){
    setDiscardModal({ open:true, batchId })
    setDiscardReason('OTHER')
  }

  async function confirmDiscard(){
    if(!discardModal.batchId) return
    setErr(null); setBusy('discard')
    try{
      await api(`/runs/batches/${discardModal.batchId}/discard`, token, {
        method:'POST',
        // Amount is computed server-side from the current product recipe total.
        body: JSON.stringify({ amountKg: 0, isPartial: false, reasonCode: discardReason, comment: null })
      })
      setDiscardModal({ open:false })
      await refreshCurrent()
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  async function resolvePrompt(){
    const p = promptModal.prompt
    if(!p) return
    setErr(null); setBusy('prompt')
    try{
      if(p.type === 'CHANGEOVER_QUESTION'){
        await api(`/prompts/${p.id}/resolve`, token, {
          method:'POST',
          body: JSON.stringify({ resolutionCode: promptChoice, reasonCode: (promptChoice==='NO' || promptChoice==='END') ? promptReason : null, comment: null, newProductId: promptChoice==='YES' ? promptNewProduct : null })
        })
      } else {
        await api(`/prompts/${p.id}/resolve`, token, {
          method:'POST',
          body: JSON.stringify({ resolutionCode: null, reasonCode: promptReason, comment: null, newProductId: null })
        })
      }

      // Allow prompt state to re-initialize if the same prompt remains open
      // (e.g., stream lag or server rejected). This avoids a stuck closed modal.
      activePromptIdRef.current = null
      setPromptModal({ open:false })
      // refreshCurrent is already polled; avoid an extra fetch here to reduce transient "Failed to fetch" noise.
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  const kpis = useMemo(() => {
    const defects = (stream.vis?.defects ?? (stream.defects ?? []).length)
    const visTotal = stream.vis?.total ?? 0
    const batches = running ? (current as any).run.batches.length : 0
    return { defects, visTotal, batches }
  }, [stream.defects, stream.vis, running, current])

  const defectsAll = (stream.defects ?? []) as any[]
  const defects = defectsAll.slice(0, 40)
  const alerts = ((stream.alerts ?? []) as any[]).slice(0, 3)
  const prompts = (stream.prompts ?? []) as any[]
  const batches = running ? (((current as any).run.batches ?? []) as any[]) : []
  const primaryPrompt = prompts.find(p => p.type === 'DOWNTIME_REQUIRED')
    || prompts.find(p => p.type === 'BELT_EMPTY_REQUIRED')
    || prompts.find(p => p.type === 'CHANGEOVER_QUESTION')
    || prompts[0]
  const activeStatus = !running ? 'Idle' : (((current as any).run.status === 'Draining' || stream.status === 'Draining') ? 'Draining' : 'Running')
  const attentionCount = alerts.length + prompts.length

  return (
    <div className="grid livePhase1Page">
      <section className={`liveHeroBand ${running ? 'is-running' : 'is-idle'}`}>
        <div className="liveHeroBand__top">
          <div className="liveHeroBand__identity">
            <div className={`liveHeroBand__signal ${running ? 'is-active' : ''}`} aria-hidden="true">
              <span />
            </div>
            <div className="liveHeroBand__copy">
              <div className="liveHeroBand__eyebrow">Live production</div>
              <div className="liveHeroBand__title">
                {running ? `${(current as any).run.product.code} • ${(current as any).run.product.name}` : 'No active run'}
              </div>
              <div className="liveHeroBand__meta">
                <Pill tone={running ? 'good' : 'neutral'}>{activeStatus}</Pill>
                {running ? (
                  <>
                    <span>Started {fmtLocal((current as any).run.startUtc)}</span>
                    <span>Run {String((current as any).run.runId).slice(0, 8)}…</span>
                    <span>Elapsed {elapsedSince((current as any).run.startUtc)}</span>
                  </>
                ) : (
                  <span>Select a product to begin a new production run.</span>
                )}
              </div>
            </div>
          </div>

          <div className="actions liveHeroBand__actions">
            <select value={selectedProduct} onChange={e => setSelectedProduct(e.target.value)}>
              {products.map(p => <option key={p.id} value={p.id}>{p.code} — {p.name}</option>)}
            </select>

            <button className="primary" disabled={busy!==null || running} onClick={startRun}><Icon name="bolt" /> Start Run</button>
            <button className="danger" disabled={busy!==null || !running} onClick={stopRun}>End Production</button>
            <button disabled={busy!==null || !running} onClick={openCreateBatch}>+ Batch</button>

            {role === 'Admin' && (
              <>
                <button disabled={busy!==null} onClick={simStart}>Sim Start</button>
                <button disabled={busy!==null} onClick={simStop}>Sim Stop</button>
              </>
            )}
          </div>
        </div>

        <div className="liveHeroBand__statusRow">
          {running && ((current as any).run.status === 'Draining' || stream.status === 'Draining') && (
            <Pill tone="info">Draining WIP</Pill>
          )}
          {running && ((current as any).run.productionEndUtc || stream.productionEndUtc) && (
            <span className="liveHeroBand__statusNote">Production ended {fmtLocal((current as any).run.productionEndUtc || stream.productionEndUtc)}</span>
          )}
          {running && (current as any).run.greyZoneEndUtc && (
            <Pill tone="warn">Grey zone until {fmtLocal((current as any).run.greyZoneEndUtc)}</Pill>
          )}
        </div>
      </section>

      {err && <div className="livePhase1Error">{err}</div>}

      <section className="liveKpiRail">
        <div className={`liveKpiCard ${kpis.defects > 0 ? 'is-warn' : ''}`}>
          <div className="liveKpiCard__label">VIS defects</div>
          <div className="liveKpiCard__value tabular">{kpis.defects}</div>
          <div className="liveKpiCard__sub">{stream.vis ? `${Math.round((stream.vis.defectRate ?? 0) * 1000) / 10}% rate` : 'Live window'}</div>
        </div>
        <div className="liveKpiCard">
          <div className="liveKpiCard__label">Batches</div>
          <div className="liveKpiCard__value tabular">{kpis.batches}</div>
          <div className="liveKpiCard__sub">Current run batch count</div>
        </div>
        <div className={`liveKpiCard ${prompts.length > 0 ? 'is-critical' : ''}`}>
          <div className="liveKpiCard__label">Prompts open</div>
          <div className="liveKpiCard__value tabular">{prompts.length}</div>
          <div className="liveKpiCard__sub">Mandatory operator actions</div>
        </div>
        <div className={`liveKpiCard ${alerts.length > 0 ? 'is-critical' : ''}`}>
          <div className="liveKpiCard__label">Alerts</div>
          <div className="liveKpiCard__value tabular">{(stream.alerts ?? []).length}</div>
          <div className="liveKpiCard__sub">Persisted live line alerts</div>
        </div>
      </section>

      <section className={`liveAttentionZone ${attentionCount > 0 ? 'is-active' : ''}`}>
        <div className="liveAttentionZone__header">
          <div className="liveAttentionZone__title">Critical zone</div>
          <div className="liveAttentionZone__count">{attentionCount > 0 ? `${attentionCount} item${attentionCount === 1 ? '' : 's'} need attention` : 'No active prompts or alerts'}</div>
        </div>
        <div className="liveAttentionZone__body">
          <div className="liveAttentionPrompt">
            <div className="liveSectionLabel">Prompt priority</div>
            {primaryPrompt ? (
              <>
                <div className="liveAttentionPrompt__title">{promptLabel(primaryPrompt.type)}</div>
                <div className="liveAttentionPrompt__text">Mandatory prompts stay in the current workflow. Reopen the active prompt here if the operator needs it again.</div>
                <button className="btn" onClick={() => setPromptModal({ open:true, prompt: primaryPrompt })}>Open active prompt</button>
              </>
            ) : (
              <div className="liveAttentionPrompt__empty">No mandatory prompt is waiting right now.</div>
            )}
          </div>

          <div className="liveAttentionAlerts">
            <div className="liveSectionLabel">Latest alerts</div>
            {alerts.length === 0 ? (
              <div className="liveAttentionPrompt__empty">No active alerts.</div>
            ) : (
              <div className="liveAttentionAlerts__list">
                {alerts.map((a:any) => (
                  <div key={a.id} className={`liveAlertRow liveAlertRow--${alertTone(a.severity)}`}>
                    <div className="liveAlertRow__copy">
                      <div className="liveAlertRow__title">
                        <Pill tone={alertTone(a.severity)}>{a.severity}</Pill>
                        <b>{a.title}</b>
                      </div>
                      <div className="hint">{a.message}</div>
                    </div>
                    <div className="row liveAlertRow__actions">
                      <button className="btn" onClick={() => ackAlert(a.id)}>ACK</button>
                      <button className="btn" onClick={() => snoozeAlert(a.id, 10)}>Snooze 10m</button>
                      <button className="btn danger" onClick={() => closeAlert(a.id)}>Close</button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </section>

      <div className="liveTopGrid">
        <section className="card liveChartCard">
          <div className="cardHeader">
            <h2>Current vs Avg last 10 vs Best last 10</h2>
            <span className="badge">Metric: P3 Volume (L) • Best = lowest OOT rate</span>
          </div>
          <div style={{ height: 170 }}>
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={chartPoints}>
                <CartesianGrid stroke="rgba(148,163,184,0.20)" vertical={false} />
                <XAxis dataKey="idx" tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
                <YAxis tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
                <Tooltip content={<PremiumTooltip labelPrefix="idx" />} />
                <Line type="monotone" dataKey="current" name="Current" dot={false} stroke={ChartColors.current} strokeWidth={2.5} />
                <Line type="monotone" dataKey="avg10" name="Avg last 10" dot={false} stroke={ChartColors.avg} strokeWidth={2.2} strokeDasharray="6 4" />
                <Line type="monotone" dataKey="best10" name="Best last 10" dot={false} stroke={ChartColors.best} strokeWidth={2.2} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="card liveBatchPanel">
          <div className="cardHeader">
            <h2>Batches</h2>
            <span className="badge">MixedAt + AddedToLine + Proofing</span>
          </div>
          {!running ? (
            <div className="liveBatchEmpty">Start a run to manage batches.</div>
          ) : batches.length === 0 ? (
            <div className="liveBatchEmpty">No batches added yet for this run.</div>
          ) : (
            <div className="liveBatchList">
              {batches.map((b:any) => (
                <div key={b.id} className={`liveBatchRow ${b.disposition != null ? 'is-discarded' : ''}`}>
                  <div className="liveBatchRow__head">
                    <div className="liveBatchRow__number">Batch #{b.batchNumber}</div>
                    <div className="row" style={{ gap: 8 }}>
                      {b.proofingActualMinutes != null && (
                        <Pill tone={proofTone(b.proofingActualMinutes)}>{b.proofingActualMinutes}m proof</Pill>
                      )}
                      {b.disposition != null && <Pill tone="bad">Discarded</Pill>}
                    </div>
                  </div>
                  <div className="liveBatchRow__stats">
                    <div className="liveBatchStat">
                      <span>Mixed</span>
                      <strong>{fmtLocal(b.mixedAtUtc)}</strong>
                    </div>
                    <div className="liveBatchStat">
                      <span>Added</span>
                      <strong>{fmtLocal(b.addedToLineAtUtc)}</strong>
                    </div>
                    <div className="liveBatchStat">
                      <span>Proof</span>
                      <strong>{b.proofingActualMinutes != null ? `${b.proofingActualMinutes} min` : '—'}</strong>
                    </div>
                  </div>
                  <div className="actions liveBatchRow__actions">
                    <button onClick={() => openEditBatch(b)}>Edit</button>
                    <button onClick={() => setMixModal({ open:true, batch: b })}>Recipe</button>
                    {b.disposition == null && (
                      <button className="danger" onClick={() => openDiscard(b.id)}>Discard</button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>

      <div className="card">
        <div className="cardHeader"><h2>P1</h2><span className="badge">Last row • refresh 60s</span></div>
        <PieceTiles items={p1?.pieces ?? []} mode="row" />
      </div>

      <div className="card">
        <div className="cardHeader"><h2>P2</h2><div className="badges"><span className="badge">Last row • refresh 60s</span><span className="badge">Numărare: stânga → dreapta</span></div></div>
        <PieceTiles items={p2?.pieces ?? []} mode="row" />
      </div>

      <div className="card">
        <div className="cardHeader"><h2>VIS — Defects (live only)</h2><span className="badge">No image storage</span></div>
        <div className="row" style={{ justifyContent:'space-between', marginBottom: 10 }}>
          <div className="row" style={{ gap: 8, flexWrap:'wrap' }}>
            <Pill tone="info">Total: {stream.vis?.total ?? '—'}</Pill>
            <Pill tone="bad">Defects: {stream.vis?.defects ?? defects.length}</Pill>
            <Pill tone="good">Good: {stream.vis?.good ?? '—'}</Pill>
            <Pill tone="neutral">Defect rate: {stream.vis ? `${Math.round((stream.vis.defectRate ?? 0)*1000)/10}%` : 'Estimated'}</Pill>
          </div>
          <div style={{ fontSize: 12, color:'var(--muted)' }}>Window: last 10 minutes</div>
        </div>
        <div className="defects">
          {defects.map((d:any) => (
            <div className="defectItem" key={d.id}>
              <AuthImage imageTokenId={d.imageTokenId} />
              <div className="defectMeta">
                <div className="t">
                  {(defectTypes[d.defectType]?.label ?? d.defectType)}
                  <span className="pill pill-info" style={{ marginLeft: 8, padding: '2px 8px' }}>
                    {defectTypes[d.defectType]?.category ?? 'OTHER'}
                  </span>
                </div>
                <div className="s">{fmtLocal(d.tsUtc)}</div>
                <div className="s">conf: {Number(d.confidence).toFixed(2)}</div>
              </div>
            </div>
          ))}
        </div>
      </div>

      <div className="card">
        <div className="cardHeader"><h2>P3</h2><span className="badge">Refresh 60s</span></div>
        <PieceTiles items={p3?.pieces ?? []} mode="flow" />
      </div>

      {/* Batch modal */}
      <Modal
        open={batchModal.open}
        title={batchModal.mode==='create' ? 'Add Batch' : `Edit Batch #${batchModal.batch?.batchNumber}`}
        subtitle="Minimal typing: quick time buttons + optional override reason"
        actions={
          <>
            <button onClick={() => setBatchModal({ open:false, mode:'create' })}>Cancel</button>
            <button className="primary" disabled={busy!==null} onClick={saveBatch}>Save</button>
          </>
        }
      >
        <div className="row" style={{ justifyContent:'space-between' }}>
          <div style={{ flex:1 }}>
            <div style={{ fontSize: 12, color:'var(--muted)' }}>Mixed at</div>
            <div style={{ fontWeight: 900 }}>{mixedAt ? mixedAt.toLocaleString() : '—'}</div>
            <div className="row" style={{ marginTop: 8 }}>
              <UiChip onClick={() => setMixedAt(new Date())}>Now</UiChip>
              <UiChip onClick={() => setMixedAt(new Date(Date.now() - 15*60*1000))}>-15m</UiChip>
              <UiChip onClick={() => setMixedAt(new Date(Date.now() - 30*60*1000))}>-30m</UiChip>
              <UiChip onClick={() => setMixedAt(new Date(Date.now() - 60*60*1000))}>-60m</UiChip>
            </div>
          </div>
          <div style={{ flex:1 }}>
            <div style={{ fontSize: 12, color:'var(--muted)' }}>Added to line</div>
            <div style={{ fontWeight: 900 }}>{addedAt ? addedAt.toLocaleString() : '— (auto-fill)'}</div>
            <div className="row" style={{ marginTop: 8 }}>
              <UiChip onClick={() => setAddedAt(new Date())}>NOW</UiChip>
              <UiChip onClick={() => setAddedAt(null)}>Auto</UiChip>
            </div>
          </div>
        </div>
        <div style={{ marginTop: 14 }}>
          <div style={{ fontSize: 12, color:'var(--muted)' }}>Override reason (audit)</div>
          <div className="row" style={{ marginTop: 8 }}>
            {reasons.slice(0,7).map(r => (
              <UiChip key={r.code} selected={batchReason===r.code} onClick={() => setBatchReason(r.code)}>{r.label}</UiChip>
            ))}
            <UiChip selected={batchReason==='OTHER'} onClick={() => setBatchReason('OTHER')}>Other</UiChip>
          </div>
        </div>
      </Modal>

      {/* Discard modal */}
      <Modal
        open={discardModal.open}
        title="Discard batch (Mix Scrap)"
        subtitle="Adds to Waste (Scrap) + value loss (cost/unit) — no typing needed"
        actions={
          <>
            <button onClick={() => setDiscardModal({ open:false })}>Cancel</button>
            <button className="danger" disabled={busy!==null} onClick={confirmDiscard}>Discard</button>
          </>
        }
      >
        <div style={{ fontSize: 12, color:'var(--muted)' }}>
          Discard always scraps the <strong>full batch amount</strong> based on the <strong>current recipe total</strong> (kg per batch).
        </div>
        <div style={{ marginTop: 14 }}>
          <div style={{ fontSize: 12, color:'var(--muted)' }}>Reason</div>
          <div className="row" style={{ marginTop: 8 }}>
            {reasons.slice(0,7).map(r => (
              <UiChip key={r.code} selected={discardReason===r.code} onClick={() => setDiscardReason(r.code)}>{r.label}</UiChip>
            ))}
            <UiChip selected={discardReason==='OTHER'} onClick={() => setDiscardReason('OTHER')}>Other</UiChip>
          </div>
        </div>
      </Modal>

      {/* Mandatory prompts modal */}
      <Modal
        open={promptModal.open}
        title={promptModal.prompt?.type === 'DOWNTIME_REQUIRED' ? 'Downtime > 60s — select reason' :
          (promptModal.prompt?.type === 'BELT_EMPTY_REQUIRED' ? 'Belt empty — select reason' : 'Changeover detected — new product?')}
        subtitle="Mandatory to continue • one-tap"
        actions={
          <>
            {promptModal.prompt?.type === 'CHANGEOVER_QUESTION' ? (
              <button className="primary" disabled={busy==='prompt' || (promptChoice==='YES' && !promptNewProduct)} onClick={resolvePrompt}>Confirm</button>
            ) : (
              <button className="primary" disabled={busy==='prompt' || !promptReason} onClick={resolvePrompt}>Save</button>
            )}
          </>
        }
      >
        {err && <div style={{ color:'#b91c1c', fontSize: 12, marginBottom: 10 }}>{err}</div>}
        {promptModal.prompt?.type === 'CHANGEOVER_QUESTION' ? (
          <>
            <div className="row" style={{ marginBottom: 10 }}>
              <UiChip selected={promptChoice==='YES'} onClick={() => setPromptChoice('YES')}>YES — start new product</UiChip>
              <UiChip selected={promptChoice==='NO'} onClick={() => setPromptChoice('NO')}>NO — keep same run</UiChip>
              <UiChip selected={promptChoice==='END'} onClick={() => setPromptChoice('END')}>END — production finished</UiChip>
            </div>
            {promptChoice==='YES' ? (
              <div>
                <div style={{ fontSize: 12, color:'var(--muted)' }}>Select new product</div>
                <select value={promptNewProduct} onChange={e => setPromptNewProduct(e.target.value)} style={{ marginTop: 8, width:'100%', padding: 12, borderRadius: 14, border: '1px solid var(--border)' }}>
                  {products.map(p => <option key={p.id} value={p.id}>{p.code} — {p.name}</option>)}
                </select>
                <div style={{ marginTop: 8, fontSize: 12, color:'var(--muted)' }}>
                  System will close current run, start a new run, and mark a WIP grey zone.
                </div>
              </div>
            ) : (
              <div>
                <div style={{ fontSize: 12, color:'var(--muted)' }}>{promptChoice==='END' ? 'Reason (optional but recommended)' : 'Why the gap?'}</div>
                <div className="row" style={{ marginTop: 8 }}>
                  {reasons.slice(0,7).map(r => (
                    <UiChip key={r.code} selected={promptReason===r.code} onClick={() => setPromptReason(r.code)}>{r.label}</UiChip>
                  ))}
                  <UiChip selected={promptReason==='OTHER'} onClick={() => setPromptReason('OTHER')}>Other</UiChip>
                </div>
              </div>
            )}
          </>
        ) : (
          <>
            <div style={{ fontSize: 12, color:'var(--muted)' }}>Reason</div>
            <div className="row" style={{ marginTop: 8 }}>
              {reasons.slice(0,8).map(r => (
                <UiChip key={r.code} selected={promptReason===r.code} onClick={() => setPromptReason(r.code)}>{r.label}</UiChip>
              ))}
              <UiChip selected={promptReason==='OTHER'} onClick={() => setPromptReason('OTHER')}>Other</UiChip>
            </div>
          </>
        )}
      </Modal>

      {/* Mix sheet modal */}
      <Modal
        open={mixModal.open}
        title={mixModal.batch ? `Batch #${mixModal.batch.batchNumber} — Mix Sheet` : 'Mix Sheet'}
        subtitle="View standard recipe, adjust quantities, add ingredients. Totals in kg vs standard."
        actions={
          <>
            <button onClick={() => setMixModal({ open:false })}>Close</button>
            <button className="primary" disabled={busy!==null || !mixModal.data} onClick={async () => {
              if(!mixModal.batch?.id || !mixModal.data) return
              setBusy('mixsave'); setMixErr(null)
              try{
                const lines = (mixModal.data.lines ?? []).map((l:any) => ({
                  ingredientId: l.ingredient.ingredientId,
                  quantity: Number(l.actual.quantity),
                  unit: l.actual.unit,
                  isRemoved: !!l.isRemoved,
                  reasonCode: null,
                  comment: null
                }))
                await api(`/batches/${mixModal.batch.id}/mix-sheet`, token, { method:'PUT', body: JSON.stringify({ lines, reasonCode: 'OTHER', comment: null }) })
                const r = await api<any>(`/batches/${mixModal.batch.id}/mix-sheet`, token)
                setMixModal(m => ({ ...m, data: r }))
              }catch(e:any){ setMixErr(e.message) }
              finally{ setBusy(null) }
            }}>Save</button>
          </>
        }
      >
        {mixErr && <div style={{ color:'#b91c1c', fontSize: 12 }}>{mixErr}</div>}
        {!mixModal.data ? (
          <div style={{ color:'var(--muted)', fontSize: 12 }}>Loading…</div>
        ) : (
          <>
            <div className="row" style={{ justifyContent:'space-between', marginBottom: 10 }}>
              <div className="pill">Standard: <strong>{mixModal.data.totals.standardKg} kg</strong></div>
              <div className="pill">Actual: <strong>{mixModal.data.totals.actualKg} kg</strong></div>
              <div className="pill">Δ: <strong>{mixModal.data.totals.deltaKg} kg</strong> ({(mixModal.data.totals.deltaPct*100).toFixed(1)}%)</div>
            </div>

            <table className="table">
              <thead>
                <tr>
                  <th>Ingredient</th>
                  <th>Standard</th>
                  <th>Actual</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {(mixModal.data.lines ?? []).map((l:any) => (
                  <tr key={l.id} style={{ opacity: l.isRemoved ? 0.5 : 1 }}>
                    <td>
                      <div style={{ fontWeight: 800 }}>{l.ingredient.code}</div>
                      <div style={{ fontSize: 12, color:'var(--muted)' }}>{l.ingredient.name}</div>
                      {l.isAdded && <Pill tone="info">Added</Pill>}
                    </td>
                    <td>{l.standard ? `${l.standard.quantity} ${l.standard.unit}` : '—'}</td>
                    <td>
                      <div className="row" style={{ gap: 8, flexWrap:'nowrap' }}>
                        <UiChip onClick={() => {
                          const q = Math.max(0, Number(l.actual.quantity) - 0.5)
                          setMixModal(m => {
                            const d = { ...m.data }
                            d.lines = d.lines.map((x:any) => x.id===l.id ? ({ ...x, actual: { ...x.actual, quantity: q } }) : x)
                            return { ...m, data: d }
                          })
                        }}>-0.5</UiChip>
                        <input
                          value={l.actual.quantity}
                          onChange={e => {
                            const q = Number(e.target.value)
                            setMixModal(m => {
                              const d = { ...m.data }
                              d.lines = d.lines.map((x:any) => x.id===l.id ? ({ ...x, actual: { ...x.actual, quantity: q } }) : x)
                              return { ...m, data: d }
                            })
                          }}
                          type="number"
                          step={0.1}
                          style={{ width: 110, padding: 10, borderRadius: 12, border:'1px solid var(--border)' }}
                        />
                        <UiChip onClick={() => {
                          const q = Number(l.actual.quantity) + 0.5
                          setMixModal(m => {
                            const d = { ...m.data }
                            d.lines = d.lines.map((x:any) => x.id===l.id ? ({ ...x, actual: { ...x.actual, quantity: q } }) : x)
                            return { ...m, data: d }
                          })
                        }}>+0.5</UiChip>
                        <span className="pill">{l.actual.unit}</span>
                      </div>
                    </td>
                    <td style={{ textAlign:'right' }}>
                      <button onClick={() => {
                        setMixModal(m => {
                          const d = { ...m.data }
                          d.lines = d.lines.map((x:any) => x.id===l.id ? ({ ...x, isRemoved: !x.isRemoved }) : x)
                          return { ...m, data: d }
                        })
                      }}>{l.isRemoved ? 'Restore' : 'Remove'}</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div style={{ marginTop: 10 }}>
              <div style={{ fontSize: 12, color:'var(--muted)' }}>Add ingredient</div>
              <div className="row" style={{ marginTop: 6 }}>
                <select id="addIng" style={{ padding: 10, borderRadius: 14, border: '1px solid var(--border)' }}>
                  {allIngredients.map(i => <option key={i.id} value={i.id}>{i.code} — {i.name}</option>)}
                </select>
                <button onClick={() => {
                  const sel = (document.getElementById('addIng') as HTMLSelectElement | null)
                  if(!sel) return
                  const ingId = sel.value
                  const ing = allIngredients.find(i => i.id === ingId)
                  if(!ing) return
                  setMixModal(m => {
                    const d = { ...m.data }
                    // if already exists, just un-remove
                    const ex = d.lines.find((x:any) => x.ingredient.ingredientId === ingId)
                    if(ex){
                      d.lines = d.lines.map((x:any) => x.ingredient.ingredientId === ingId ? ({ ...x, isRemoved:false }) : x)
                    } else {
                      d.lines = [...d.lines, {
                        id: `tmp-${ingId}`,
                        ingredient: { ingredientId: ingId, code: ing.code, name: ing.name, defaultUnit: ing.defaultUnit },
                        standard: null,
                        actual: { quantity: 0, unit: ing.defaultUnit ?? 'kg' },
                        isAdded: true,
                        isRemoved: false,
                        reasonCode: null,
                        comment: null
                      }]
                    }
                    return { ...m, data: d }
                  })
                }}>+ Add</button>
                <span className="pill pill-info">Tip: use kg/g only</span>
              </div>
            </div>
          </>
        )}
      </Modal>
    </div>
  )
}

function PieceTiles({ items, mode }:{ items:any[], mode:'row'|'flow' }){
  const maxH = useMemo(() => {
    const hs = items.map(x => Number(x.heightMm)).filter(n => Number.isFinite(n))
    return hs.length ? Math.max(...hs) : 1
  }, [items])

  if(!items.length) return <div style={{ color:'var(--muted)', fontSize: 12 }}>No data yet.</div>

  const display = mode==='row' ? items.slice(0,6) : items.slice(0,21)

  return (
    <div className="tiles">
      {display.map((x:any, i:number) => {
        const volL = Number(x.volumeMm3) / 1_000_000
        const h = Number(x.heightMm)
        const barPct = clamp((h / maxH) * 100, 8, 100)
        return (
          <div className="tile" key={x.pieceUid ?? i}>
            <div className="tileTop">
              <span>{x.rowIndex != null ? `Row ${x.rowIndex}` : 'Flow'}</span>
              <span className="posBadge">{x.posInRow != null ? `L→R #${x.posInRow}` : ''}</span>
            </div>
            <div className="tileBox">
              <div className="tileBar" style={{ height: `${barPct}%` }} />
            </div>
            <div className="tileMeta">
              <div><strong>{volL.toFixed(3)} L</strong> <span className="m">vol</span></div>
              <div className="m">W {Number(x.widthMm).toFixed(1)} • L {Number(x.lengthMm).toFixed(1)} • H {Number(x.heightMm).toFixed(1)} mm</div>
              <div className="m">Est {Number(x.estimatedWeightG).toFixed(1)} g ({x.weightConfidence})</div>
            </div>
          </div>
        )
      })}
    </div>
  )
}