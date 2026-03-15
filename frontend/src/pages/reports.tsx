import React, { useEffect, useMemo, useState } from 'react'
import { api, apiBase } from '../components/api'
import { UiChip, Pill, SkeletonCard } from '../components/ui'
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, BarChart, Bar, CartesianGrid, Legend } from 'recharts'
import { PremiumTooltip, ChartColors } from '../components/charts'

function fmtLocal(utc?: string | null){
  if(!utc) return '—'
  return new Date(utc).toLocaleString()
}

type RunList = {
  fromUtc: string
  toUtc: string
  runs: {
    id: string
    status: string
    startUtc: string
    productionEndUtc: string | null
    endUtc: string | null
    product: { productId: string, code: string, name: string }
    kpi: { p3Count: number, visTotal?: number, defects: number, visGood?: number, visDefectRate?: number, mixScrapUnits: number }
  }[]
}

type RunReport = {
  run: { id: string, status: string, startUtc: string, productionEndUtc: string | null, endUtc: string | null, product: { productId: string, code: string, name: string } }
  counts: { p1Count: number, p2Count: number, p3Count: number, visTotal?: number, defects: number, visGood?: number, visDefectRate?: number }
  oot: { p1: number, p2: number, p3: number }
  waste: { mixScrapUnits: number, mixScrapValue: number, mixScrapKg: number }
  batches: any[]
  oee: any
}

type Buckets20Report = { runId: string, bucketMinutes: number, buckets: any[] }

type PeriodResp = {
  tz: string
  group: string
  fromUtc: string
  toUtc: string
  buckets: {
    key: string
    label: string
    bucketStartUtc: string
    runCount: number
    oeeAvg: number
    defectTotal: number
    visTotal?: number
    defectRate: number
    mixScrapUnits: number
    giveawayKg: number
    topDowntime: { code: string, minutes: number }[]
    topDefects: { code: string, meta: any, count: number }[]
  }[]
}

type ParetoDowntime = { fromUtc: string, toUtc: string, items: { code: string, minutes: number, cost: number }[] }

type ParetoDefects = { fromUtc: string, toUtc: string, bucketMinutes: number, items: { code: string, label: string, category: string, severity: number, count: number, avgSpeedSeg3: number }[] }

type TimelineResp = {
  run: any
  bucketMinutes: number
  buckets: { bucketStartUtc: string, bucketEndUtc: string, seg3AvgSpeed: number, defects: number, p2GrowthDeltaHeightMm: number, stopMin: { s1:number, s2:number, s3:number, s4:number } }[]
  operatorEvents: { tsUtc: string, type: string, reasonCode?: string|null, comment?: string|null }[]
}

type InsightsResp = {
  run: any
  proofing: { min: number, max: number, batches: any[] }
  recipeVariance: any[]
  weightCalibration: { sampleCount: number, meanK: number, stdK: number }
  changeover: any
}

type ChangeoversResp = { fromUtc: string, toUtc: string, items: any[] }

type View = 'runs'|'trends'|'pareto'|'explain'|'insights'|'changeovers'

export default function ReportsPage({ token }:{ token: string, role: string }){
  const [view, setView] = useState<View>('runs')
  const [days, setDays] = useState(7)
  const [list, setList] = useState<RunList | null>(null)
  const [err, setErr] = useState<string | null>(null)

  const [openId, setOpenId] = useState<string | null>(null)
  const [report, setReport] = useState<RunReport | null>(null)
  const [loadingReport, setLoadingReport] = useState(false)

  const [buckets20, setBuckets20] = useState<Buckets20Report | null>(null)
  const [loadingBuckets20, setLoadingBuckets20] = useState(false)

  // trends
  const [group, setGroup] = useState<'shift'|'day'|'week'>('day')
  const [period, setPeriod] = useState<PeriodResp | null>(null)

  // pareto
  const [pd, setPd] = useState<ParetoDowntime | null>(null)
  const [pf, setPf] = useState<ParetoDefects | null>(null)

  // drill-down
  const [timeline, setTimeline] = useState<TimelineResp | null>(null)
  const [insights, setInsights] = useState<InsightsResp | null>(null)
  const [changeovers, setChangeovers] = useState<ChangeoversResp | null>(null)

  // defect labels for display
  const [defectMap, setDefectMap] = useState<Record<string, { label: string, category: string }>>({})

  useEffect(() => {
    api<any[]>(`/defect-types`, token)
      .then(list => {
        const m: any = {}
        list.forEach(d => m[d.code] = { label: d.label, category: d.category })
        setDefectMap(m)
      })
      .catch(() => {})
  }, [token])

  // Run list
  useEffect(() => {
    if(view !== 'runs') return
    let alive = true
    api<RunList>(`/reports/runs?days=${days}`, token)
      .then(r => { if(alive){ setList(r); setErr(null) } })
      .catch(e => { if(alive) setErr(e?.message || 'Failed to load runs') })
    return () => { alive = false }
  }, [token, days, view])

  // Run report
  useEffect(() => {
    if(view !== 'runs' || !openId) { setReport(null); return }
    let alive = true
    setLoadingReport(true)
    api<RunReport>(`/reports/run/${openId}?bucketMinutes=10`, token)
      .then(r => { if(alive){ setReport(r); setLoadingReport(false) } })
      .catch(e => { if(alive){ setErr(e?.message || 'Failed to load report'); setLoadingReport(false) } })
    return () => { alive = false }
  }, [token, openId, view])

  // 20-minute analytics buckets report (persisted)
  useEffect(() => {
    if(view !== 'runs' || !openId) { setBuckets20(null); return }
    let alive = true
    setLoadingBuckets20(true)
    api<Buckets20Report>(`/reports/run/${openId}/buckets20`, token)
      .then(r => { if(alive){ setBuckets20(r); setLoadingBuckets20(false) } })
      .catch(() => { if(alive){ setBuckets20(null); setLoadingBuckets20(false) } })
    return () => { alive = false }
  }, [token, openId, view])

  // Period trends
  useEffect(() => {
    if(view !== 'trends') return
    let alive = true
    api<PeriodResp>(`/reports/period?group=${group}&days=${days}&tz=Europe/Bucharest`, token)
      .then(r => { if(alive){ setPeriod(r); setErr(null) } })
      .catch(e => { if(alive) setErr(e?.message || 'Failed to load trends') })
    return () => { alive = false }
  }, [token, view, group, days])

  // Pareto
  useEffect(() => {
    if(view !== 'pareto') return
    let alive = true
    Promise.all([
      api<ParetoDowntime>(`/reports/pareto/downtime?days=${days}`, token),
      api<ParetoDefects>(`/reports/pareto/defects?days=${days}&bucketMinutes=10`, token)
    ]).then(([a,b]) => {
      if(!alive) return
      setPd(a); setPf(b); setErr(null)
    }).catch(e => { if(alive) setErr(e?.message || 'Failed to load pareto') })
    return () => { alive = false }
  }, [token, view, days])

  // Explain why
  useEffect(() => {
    if(view !== 'explain' || !openId) { setTimeline(null); return }
    let alive = true
    api<TimelineResp>(`/reports/run/${openId}/timeline?bucketMinutes=10`, token)
      .then(r => { if(alive){ setTimeline(r); setErr(null) } })
      .catch(e => { if(alive) setErr(e?.message || 'Failed to load timeline') })
    return () => { alive = false }
  }, [token, view, openId])

  // Insights
  useEffect(() => {
    if(view !== 'insights' || !openId) { setInsights(null); return }
    let alive = true
    api<InsightsResp>(`/reports/run/${openId}/insights`, token)
      .then(r => { if(alive){ setInsights(r); setErr(null) } })
      .catch(e => { if(alive) setErr(e?.message || 'Failed to load insights') })
    return () => { alive = false }
  }, [token, view, openId])

  // Changeovers
  useEffect(() => {
    if(view !== 'changeovers') return
    let alive = true
    api<ChangeoversResp>(`/reports/changeovers?days=${days}`, token)
      .then(r => { if(alive){ setChangeovers(r); setErr(null) } })
      .catch(e => { if(alive) setErr(e?.message || 'Failed to load changeovers') })
    return () => { alive = false }
  }, [token, view, days])

  const downloadCsv = async () => {
    if(!openId) return
    const url = `${apiBase()}/reports/run/${openId}/csv?bucketMinutes=10`
    const res = await fetch(url, { headers: { 'Authorization': `Bearer ${token}` } })
    const blob = await res.blob()
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = `run_${openId}_oee.csv`
    a.click()
    URL.revokeObjectURL(a.href)
  }

  const runs = list?.runs ?? []

  const trendData = useMemo(() => {
    const b = period?.buckets ?? []
    return b.map(x => ({
      label: x.label,
      oee: x.oeeAvg,
      defectRate: x.defectRate,
      mixScrap: x.mixScrapUnits,
      giveaway: x.giveawayKg
    }))
  }, [period])

  const paretoDowntimeData = useMemo(() => (pd?.items ?? []).slice(0,10).map(x => ({ name: x.code, minutes: x.minutes, cost: x.cost })), [pd])
  const paretoDefectData = useMemo(() => (pf?.items ?? []).slice(0,10).map(x => ({ name: defectMap[x.code]?.label ?? x.label, count: x.count })), [pf, defectMap])

  return (
    <div>
      <div className="row" style={{ justifyContent:'space-between', marginBottom: 12 }}>
        <div className="row">
          <Pill tone="info">View</Pill>
          <UiChip selected={view==='runs'} onClick={()=>setView('runs')}>Runs</UiChip>
          <UiChip selected={view==='trends'} onClick={()=>setView('trends')}>Shift/Day/Week trends</UiChip>
          <UiChip selected={view==='pareto'} onClick={()=>setView('pareto')}>Pareto</UiChip>
          <UiChip selected={view==='explain'} onClick={()=>setView('explain')}>Explain why</UiChip>
          <UiChip selected={view==='insights'} onClick={()=>setView('insights')}>Batch & recipe</UiChip>
          <UiChip selected={view==='changeovers'} onClick={()=>setView('changeovers')}>Changeovers</UiChip>
        </div>
        <div className="row">
          <Pill tone="info">Range</Pill>
          {[1,7,30,90].map(d => (
            <button key={d} className={`chip ${days===d ? 'chip-selected':''}`} onClick={()=>setDays(d)}>{d}d</button>
          ))}
        </div>
      </div>

      {err && <div className="error" style={{ marginBottom: 10 }}>{err}</div>}

      {view==='runs' && (
        <div className="grid2">
          <div className="card">
            <div className="cardTitle">Runs</div>
            <div className="hint" style={{ marginTop: 6 }}>Click a run to open the report</div>
            <div className="table" style={{ marginTop: 12 }}>
              {!list && <div style={{ padding: 12 }}><SkeletonCard lines={6} /></div>}
                            <div className="tr tr7 th">
                <div>Start (local)</div>
                <div>Product</div>
                <div>Status</div>
                <div>P3</div>
                <div>Defects</div>
                <div>Mix scrap (u)</div>
                <div></div>
              </div>
                {runs.map(r => (
                <div key={r.id} className="tr tr7">
                  <div>{new Date(r.startUtc).toLocaleString()}</div>
                  <div><b>{r.product.code}</b> <span className="muted">{r.product.name}</span></div>
                  <div><Pill tone={r.status==='Closed' ? 'neutral' : (r.status==='Running' ? 'good' : 'warn')}>{r.status}</Pill></div>
                  <div>{r.kpi.p3Count}</div>
                  <div>{r.kpi.defects} / {r.kpi.visTotal ?? '—'}<div className="miniSub">rate {(r.kpi.visDefectRate!=null)?`${Math.round(r.kpi.visDefectRate*1000)/10}%`:'est.'}</div></div>
                  <div>{r.kpi.mixScrapUnits.toFixed(2)}</div>
                  <div><button className="btn" onClick={()=>{ setOpenId(r.id); setView('runs') }}>Open</button></div>
                </div>
              ))}
            </div>
          </div>

          <div className="card">
            <div className="cardTitle">Run report</div>
            {!openId && <div className="hint" style={{ marginTop: 10 }}>Select a run from the list.</div>}
            {loadingReport && <div style={{ marginTop: 10 }}><SkeletonCard lines={6} /></div>}
            {report && (
              <>
                <div className="row" style={{ justifyContent:'space-between', marginTop: 10 }}>
                  <div>
                    <div style={{ fontWeight: 700 }}>{report.run.product.code} — {report.run.product.name}</div>
                    <div className="muted">Run {report.run.id}</div>
                  </div>
                  <div className="row">
                    <button className="btn" onClick={downloadCsv}>Download OEE CSV</button>
                    <button className="btn btnGhost" onClick={()=>setOpenId(null)}>Close</button>
                  </div>
                </div>

                <div className="grid3" style={{ marginTop: 12 }}>
                  <div className="mini">
                    <div className="miniLabel">Counts</div>
                    <div className="miniValue">P1 {report.counts.p1Count} · P2 {report.counts.p2Count} · P3 {report.counts.p3Count}</div>
                    <div className="miniSub">VIS defects {report.counts.defects} / {report.counts.visTotal ?? '—'} • rate {(report.counts.visDefectRate!=null)?`${Math.round(report.counts.visDefectRate*1000)/10}%`:'est.'}</div>
                  </div>
                  <div className="mini">
                    <div className="miniLabel">Out of tolerance</div>
                    <div className="miniValue">P1 {report.oot.p1} · P2 {report.oot.p2} · P3 {report.oot.p3}</div>
                    <div className="miniSub">Dims/Vol/Weight</div>
                  </div>
                  <div className="mini">
                    <div className="miniLabel">Mix scrap</div>
                    <div className="miniValue">{report.waste.mixScrapUnits.toFixed(2)} u</div>
                    <div className="miniSub">{report.waste.mixScrapKg.toFixed(3)} kg · €{report.waste.mixScrapValue.toFixed(2)}</div>
                  </div>
                </div>

                <div className="cardSub" style={{ marginTop: 14 }}>
                  <div className="cardTitle" style={{ fontSize: 14 }}>OEE (totals)</div>
                  <div className="row" style={{ justifyContent:'space-between' }}>
                    <div className="hint">Total OEE: <b>{report.oee.totals.totalOee.toFixed(3)}</b></div>
                    <div className="hint">Giveaway: {report.oee.totals.extras.giveawayKg.toFixed(3)} kg · Process loss Δ {report.oee.totals.extras.processLoss.delta_g.toFixed(2)} g</div>
                  </div>
                  <div className="table" style={{ marginTop: 8 }}>
                    <div className="tr tr9 th">
                      <div>Seg</div><div>A</div><div>P</div><div>Q</div><div>OEE</div>
                      <div>Avail loss (min)</div><div>Perf loss (u)</div><div>Qual loss (u)</div>
                      <div>Speed loss €</div>
                    </div>
                    {report.oee.totals.segments.map((s:any) => (
                      <div key={s.segmentId} className="tr tr9">
                        <div><b>S{s.segmentId}</b></div>
                        <div>{s.availability.toFixed(3)}</div>
                        <div>{s.performance.toFixed(3)}</div>
                        <div>{s.quality.toFixed(3)}</div>
                        <div><b>{s.oee.toFixed(3)}</b></div>
                        <div>{s.waterfall.availabilityLossMin.toFixed(2)}</div>
                        <div>{s.waterfall.performanceLossUnits.toFixed(2)}</div>
                        <div>{s.waterfall.qualityLossUnits.toFixed(2)}</div>
                        <div>€{s.monetization.speedLossValue.toFixed(2)}</div>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="cardSub" style={{ marginTop: 14 }}>
                  <div className="cardTitle" style={{ fontSize: 14 }}>Batches</div>
                  <div className="table" style={{ marginTop: 8 }}>
                    <div className="tr trBatch th">
                      <div>#</div><div>Status</div><div>Mixed</div><div>Added to line</div><div>Proofing</div><div>Disposition</div><div>Discard kg</div>
                    </div>
                    {report.batches.map((b:any) => (
                      <div key={b.id} className="tr trBatch">
                        <div>{b.batchNumber}</div>
                        <div>{b.status}</div>
                        <div>{b.mixedAtUtc ? new Date(b.mixedAtUtc).toLocaleString() : '-'}</div>
                        <div>{b.addedToLineAtUtc ? new Date(b.addedToLineAtUtc).toLocaleString() : '-'}</div>
                        <div>{b.proofingActualMinutes ?? '-'}</div>
                        <div>{b.disposition}</div>
                        <div>{b.discardAmountKg ?? '-'}</div>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="cardSub" style={{ marginTop: 14 }}>
                  <div className="cardTitle" style={{ fontSize: 14 }}>Analytics buckets (20 min)</div>
                  <div className="hint">Persisted rollups for this run (all buckets, not just the last 12).</div>

                  {loadingBuckets20 && <div style={{ marginTop: 10 }}><SkeletonCard lines={3} /></div>}
                  {(!loadingBuckets20 && (!buckets20 || (buckets20.buckets?.length ?? 0) === 0)) && (
                    <div className="hint" style={{ marginTop: 10 }}>No buckets available yet.</div>
                  )}

                  {buckets20 && (buckets20.buckets?.length ?? 0) > 0 && (
                    <div style={{ marginTop: 10, display:'grid', gap:10 }}>
                      {buckets20.buckets.map((b:any) => (
                        <div key={b.id} style={{ border: '1px solid var(--border)', borderRadius: 14, padding: 10 }}>
                          <div className="row" style={{ justifyContent:'space-between' }}>
                            <div><b>{new Date(b.bucketStartUtc).toLocaleTimeString([], {hour:'2-digit', minute:'2-digit'})}–{new Date(b.bucketEndUtc).toLocaleTimeString([], {hour:'2-digit', minute:'2-digit'})}</b>
                              <span className="muted"> · pieces {b.p1StartPieceSeq}…{b.p1EndPieceSeq}</span>
                            </div>
                            <div className="muted">P1 {b.p1PieceCount} · P2 {b.p2PieceCount} · defects {(b.vis?.totalDefects ?? 0)}</div>
                          </div>
                          <div className="muted" style={{ fontSize: 11, marginTop: 6 }}>VIS Pareto top: {(b.vis?.pareto ?? []).slice(0,3).map((x:any)=>`${x.defectType} (${x.count})`).join(', ') || '—'}</div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                <div className="row" style={{ marginTop: 12 }}>
                  <button className="btn" onClick={()=>setView('explain')}>Explain why</button>
                  <button className="btn" onClick={()=>setView('insights')}>Batch & recipe</button>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {view==='trends' && (
        <div className="card">
          <div className="row" style={{ justifyContent:'space-between' }}>
            <div>
              <div className="cardTitle">Trends</div>
              <div className="hint">Shift schedule default: 06-14 / 14-22 / 22-06 (local)</div>
            </div>
            <div className="row">
              <Pill tone="info">Group</Pill>
              <UiChip selected={group==='shift'} onClick={()=>setGroup('shift')}>Shift</UiChip>
              <UiChip selected={group==='day'} onClick={()=>setGroup('day')}>Day</UiChip>
              <UiChip selected={group==='week'} onClick={()=>setGroup('week')}>Week</UiChip>
            </div>
          </div>

          {!period && <div className="hint" style={{ marginTop: 12 }}>Loading…</div>}
          {period && (
            <>
              <div className="grid2" style={{ marginTop: 12 }}>
                <div className="cardSub">
                  <div className="cardTitle" style={{ fontSize: 14 }}>OEE & Defects</div>
                  <div style={{ height: 260, marginTop: 8 }}>
                    <ResponsiveContainer width="100%" height="100%">
                      <LineChart data={trendData}>
                        <CartesianGrid stroke="rgba(148,163,184,0.20)" vertical={false} />
                        <XAxis dataKey="label" hide />
                        <YAxis tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
                        <Tooltip content={<PremiumTooltip />} />
                        <Legend />
                        <Line type="monotone" dataKey="oee" name="OEE" dot={false} stroke={ChartColors.avg} strokeWidth={2.6} />
                        <Line type="monotone" dataKey="defectRate" name="Defect rate" dot={false} stroke={ChartColors.danger} strokeWidth={2.2} />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                </div>

                <div className="cardSub">
                  <div className="cardTitle" style={{ fontSize: 14 }}>Waste & Giveaway</div>
                  <div style={{ height: 260, marginTop: 8 }}>
                    <ResponsiveContainer width="100%" height="100%">
                      <LineChart data={trendData}>
                        <CartesianGrid stroke="rgba(148,163,184,0.20)" vertical={false} />
                        <XAxis dataKey="label" hide />
                        <YAxis tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
                        <Tooltip content={<PremiumTooltip />} />
                        <Legend />
                        <Line type="monotone" dataKey="mixScrap" name="Mix scrap (u)" dot={false} stroke={ChartColors.warn} strokeWidth={2.2} />
                        <Line type="monotone" dataKey="giveaway" name="Giveaway (kg)" dot={false} stroke={ChartColors.current} strokeWidth={2.2} strokeDasharray="6 4" />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                </div>
              </div>

              <div className="table" style={{ marginTop: 12 }}>
              {!list && <div style={{ padding: 12 }}><SkeletonCard lines={6} /></div>}
                              <div className="tr trTrend th">
                  <div>Bucket</div><div>Runs</div><div>OEE avg</div><div>Defect rate</div><div>Mix scrap (u)</div><div>Giveaway (kg)</div><div>Top downtime</div><div>Top defects</div>
                </div>
                {period.buckets.map(b => (
                  <div key={b.key} className="tr trTrend">
                    <div><b>{b.label}</b></div>
                    <div>{b.runCount}</div>
                    <div>{Number(b.oeeAvg).toFixed(3)}</div>
                    <div>{Number(b.defectRate).toFixed(3)}</div>
                    <div>{Number(b.mixScrapUnits).toFixed(2)}</div>
                    <div>{Number(b.giveawayKg).toFixed(3)}</div>
                    <div>{b.topDowntime.map(x => `${x.code} ${x.minutes.toFixed(1)}m`).join(', ') || '—'}</div>
                    <div>{b.topDefects.map(x => `${defectMap[x.code]?.label ?? x.code} (${x.count})`).join(', ') || '—'}</div>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
      )}

      {view==='pareto' && (
        <div className="grid2">
          <div className="card">
            <div className="cardTitle">Downtime Pareto</div>
            {!pd && <div className="hint" style={{ marginTop: 10 }}>Loading…</div>}
            {pd && (
              <>
                <div style={{ height: 260, marginTop: 8 }}>
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={paretoDowntimeData}>
                      <XAxis dataKey="name" hide />
                      <YAxis tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
                      <Tooltip content={<PremiumTooltip />} />
                      <Bar dataKey="minutes" name="Minutes" fill={ChartColors.warn} radius={[8,8,0,0]} />
                    </BarChart>
                  </ResponsiveContainer>
                </div>
                <div className="table" style={{ marginTop: 12 }}>
              {!list && <div style={{ padding: 12 }}><SkeletonCard lines={6} /></div>}
                                <div className="tr tr3 th"><div>Reason</div><div>Minutes</div><div>Cost</div></div>
                  {pd.items.slice(0,15).map(x => (
                    <div key={x.code} className="tr tr3"><div><b>{x.code}</b></div><div>{x.minutes.toFixed(2)}</div><div>€{x.cost.toFixed(2)}</div></div>
                  ))}
                </div>
              </>
            )}
          </div>

          <div className="card">
            <div className="cardTitle">Defect Pareto</div>
            {!pf && <div className="hint" style={{ marginTop: 10 }}>Loading…</div>}
            {pf && (
              <>
                <div style={{ height: 260, marginTop: 8 }}>
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={paretoDefectData}>
                      <XAxis dataKey="name" hide />
                      <YAxis tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
                      <Tooltip content={<PremiumTooltip />} />
                      <Bar dataKey="count" name="Defects" fill={ChartColors.danger} radius={[8,8,0,0]} />
                    </BarChart>
                  </ResponsiveContainer>
                </div>
                <div className="table" style={{ marginTop: 12 }}>
              {!list && <div style={{ padding: 12 }}><SkeletonCard lines={6} /></div>}
                                <div className="tr tr4 th"><div>Defect</div><div>Category</div><div>Count</div><div>Avg seg3 speed</div></div>
                  {pf.items.slice(0,15).map(x => (
                    <div key={x.code} className="tr tr4">
                      <div><b>{defectMap[x.code]?.label ?? x.label}</b></div>
                      <div>{x.category}</div>
                      <div>{x.count}</div>
                      <div>{Number(x.avgSpeedSeg3).toFixed(3)} m/s</div>
                    </div>
                  ))}
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {view==='explain' && (
        <div className="card">
          <div className="row" style={{ justifyContent:'space-between' }}>
            <div>
              <div className="cardTitle">Explain why</div>
              <div className="hint">Pick a run from Runs view, then come here for timeline drill-down.</div>
            </div>
            <div className="row">
              <button className="btn" onClick={()=>setView('runs')}>Back to runs</button>
            </div>
          </div>

          {!openId && <div className="hint" style={{ marginTop: 10 }}>No run selected.</div>}
          {openId && !timeline && <div className="hint" style={{ marginTop: 10 }}>Loading…</div>}
          {timeline && (
            <>
              <div className="grid2" style={{ marginTop: 12 }}>
                <div className="cardSub">
                  <div className="cardTitle" style={{ fontSize: 14 }}>Defects & Speed (seg3)</div>
                  <div style={{ height: 260, marginTop: 8 }}>
                    <ResponsiveContainer width="100%" height="100%">
                      <LineChart data={timeline.buckets.map(b => ({
                        t: b.bucketStartUtc,
                        defects: b.defects,
                        speed: b.seg3AvgSpeed,
                        growth: b.p2GrowthDeltaHeightMm
                      }))}>
                        <XAxis dataKey="t" hide />
                        <YAxis tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
                        <Tooltip content={<PremiumTooltip />} />
                        <Legend />
                        <Line type="monotone" dataKey="defects" name="Defects" dot={false} stroke={ChartColors.danger} strokeWidth={2.2} />
                        <Line type="monotone" dataKey="speed" name="Seg3 speed" dot={false} stroke={ChartColors.current} strokeWidth={2.2} />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                </div>
                <div className="cardSub">
                  <div className="cardTitle" style={{ fontSize: 14 }}>Stops & P2 growth delta</div>
                  <div style={{ height: 260, marginTop: 8 }}>
                    <ResponsiveContainer width="100%" height="100%">
                      <LineChart data={timeline.buckets.map(b => ({
                        t: b.bucketStartUtc,
                        stopS3: b.stopMin.s3,
                        growth: b.p2GrowthDeltaHeightMm
                      }))}>
                        <XAxis dataKey="t" hide />
                        <YAxis tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
                        <Tooltip content={<PremiumTooltip />} />
                        <Legend />
                        <Line type="monotone" dataKey="stopS3" name="Stop S3 (min)" dot={false} stroke={ChartColors.warn} strokeWidth={2.2} />
                        <Line type="monotone" dataKey="growth" name="P2 growth ΔH" dot={false} stroke={ChartColors.avg} strokeWidth={2.2} strokeDasharray="6 4" />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                </div>
              </div>

              <div className="cardSub" style={{ marginTop: 12 }}>
                <div className="cardTitle" style={{ fontSize: 14 }}>Operator / System events</div>
                <div className="table" style={{ marginTop: 8 }}>
                  <div className="tr tr4 th"><div>Time</div><div>Type</div><div>Reason</div><div>Comment</div></div>
                  {timeline.operatorEvents.slice(-50).reverse().map((e, idx) => (
                    <div key={idx} className="tr tr4">
                      <div>{new Date(e.tsUtc).toLocaleString()}</div>
                      <div><b>{e.type}</b></div>
                      <div>{e.reasonCode ?? '—'}</div>
                      <div className="muted">{e.comment ?? ''}</div>
                    </div>
                  ))}
                </div>
              </div>
            </>
          )}
        </div>
      )}

      {view==='insights' && (
        <div className="card">
          <div className="row" style={{ justifyContent:'space-between' }}>
            <div>
              <div className="cardTitle">Batch proofing + recipe variance</div>
              <div className="hint">Best-effort insights (no image retention, VIS has defect-only events).</div>
            </div>
            <div className="row">
              <button className="btn" onClick={()=>setView('runs')}>Back to runs</button>
            </div>
          </div>

          {!openId && <div className="hint" style={{ marginTop: 10 }}>No run selected.</div>}
          {openId && !insights && <div className="hint" style={{ marginTop: 10 }}>Loading…</div>}
          {insights && (
            <>
              <div className="grid3" style={{ marginTop: 12 }}>
                <div className="mini">
                  <div className="miniLabel">Proofing min/max</div>
                  <div className="miniValue">{insights.proofing.min}–{insights.proofing.max} min</div>
                  <div className="miniSub">Per product</div>
                </div>
                <div className="mini">
                  <div className="miniLabel">Weight calibration (k)</div>
                  <div className="miniValue">μ {insights.weightCalibration.meanK.toFixed(3)} · σ {insights.weightCalibration.stdK.toFixed(3)}</div>
                  <div className="miniSub">Samples {insights.weightCalibration.sampleCount}</div>
                </div>
                <div className="mini">
                  <div className="miniLabel">Changeover</div>
                  <div className="miniValue">{insights.changeover ? `${insights.changeover.minutes} min` : '—'}</div>
                  <div className="miniSub">Grey zone {insights.changeover?.greyZoneSec ?? '—'} sec</div>
                </div>
              </div>

              <div className="cardSub" style={{ marginTop: 12 }}>
                <div className="cardTitle" style={{ fontSize: 14 }}>Proofing compliance</div>
                <div className="table" style={{ marginTop: 8 }}>
                  <div className="tr tr4 th"><div>Batch</div><div>Proofing (min)</div><div>Status</div><div></div></div>
                  {insights.proofing.batches.map((b:any) => (
                    <div key={b.id} className="tr tr4">
                      <div><b>#{b.batchNumber}</b></div>
                      <div>{b.proofingActualMinutes ?? '—'}</div>
                      <div><Pill tone={b.status==='OK' ? 'good' : (b.status==='UNKNOWN' ? 'neutral' : 'warn')}>{b.status}</Pill></div>
                      <div></div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="cardSub" style={{ marginTop: 12 }}>
                <div className="cardTitle" style={{ fontSize: 14 }}>Recipe variance (per batch)</div>
                <div className="table" style={{ marginTop: 8 }}>
                  <div className="tr tr5 th"><div>Batch</div><div>Std kg</div><div>Actual kg</div><div>Δ kg</div><div>Top deltas</div></div>
                  {insights.recipeVariance.map((b:any) => {
                    const top = (b.lines ?? []).slice().sort((a:any,b:any)=>Math.abs(b.deltaKg)-Math.abs(a.deltaKg)).slice(0,3)
                    return (
                      <div key={b.batchId} className="tr tr5">
                        <div><b>#{b.batchNumber}</b></div>
                        <div>{b.totalStdKg.toFixed(3)}</div>
                        <div>{b.totalActKg.toFixed(3)}</div>
                        <div><b>{b.deltaKg.toFixed(3)}</b></div>
                        <div className="muted">{top.map((x:any)=>`${x.ingredient.code}: ${x.deltaKg.toFixed(3)}kg`).join(' · ')}</div>
                      </div>
                    )
                  })}
                </div>
              </div>
            </>
          )}
        </div>
      )}

      {view==='changeovers' && (
        <div className="card">
          <div className="cardTitle">Changeover performance</div>
          {!changeovers && <div className="hint" style={{ marginTop: 10 }}>Loading…</div>}
          {changeovers && (
            <div className="table" style={{ marginTop: 12 }}>
              {!list && <div style={{ padding: 12 }}><SkeletonCard lines={6} /></div>}
                            <div className="tr tr5 th"><div>From run</div><div>To run</div><div>End</div><div>Start</div><div>Duration</div></div>
              {changeovers.items.map((c:any, idx:number) => (
                <div key={idx} className="tr tr5">
                  <div className="muted">{String(c.fromRunId).slice(0,8)}</div>
                  <div className="muted">{String(c.toRunId).slice(0,8)}</div>
                  <div>{fmtLocal(c.fromEndUtc)}</div>
                  <div>{fmtLocal(c.toStartUtc)}</div>
                  <div><b>{c.durationMin} min</b> <span className="muted">grey {c.greyZoneSec}s</span></div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}