import React, { useEffect, useMemo, useState } from 'react'
import { api } from '../components/api'
import { Pill } from '../components/ui'
import { BarChart, Bar, CartesianGrid, Tooltip, XAxis, YAxis, ResponsiveContainer, LineChart, Line, Legend } from 'recharts'
import { PremiumTooltip, ChartColors } from '../components/charts'

type LiveOee = {
  running: boolean
  runId: string
  window: { fromUtc: string, plannedEndUtc: string, countsEndUtc: string }
  totalOee: number
  segments: {
    segmentId: number
    availability: number
    performance: number
    quality: number
    oee: number
    waterfall: { availabilityLossMin: number, performanceLossUnits: number, qualityLossUnits: number }
    counts: { actualUnits: number, idealUnits: number, goodUnits: number, defectUnits: number, ootUnits: number, prelineScrapUnits: number }
    monetization: { speedLossValue: number, speedLossCost: number, prelineScrapValue: number }
  }[]
  extras: {
    giveawayKg: number
    prelineScrapUnits: number
    prelineScrapValue: number
    processLoss: { avgWeightP1_g: number, avgWeightP3_g: number, delta_g: number }
  }
}

export default function OeePage({ token }:{ token: string }){
  const [data, setData] = useState<LiveOee | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [windowMinutes, setWindowMinutes] = useState(15)

  useEffect(() => {
    let alive = true
    const tick = async () => {
      try{
        const r = await api<LiveOee>(`/oee/live?windowMinutes=${windowMinutes}`, token)
        if(alive){ setData(r); setErr(null) }
      }catch(e:any){
        if(alive) setErr(e?.message || 'Failed to load OEE')
      }
    }
    tick()
    const t = setInterval(tick, 5000)
    return () => { alive=false; clearInterval(t) }
  }, [token, windowMinutes])

  const wfRows = useMemo(() => {
    if(!data?.segments) return []
    return data.segments.map(s => ({
      segment: `S${s.segmentId}`,
      availabilityLossMin: s.waterfall.availabilityLossMin,
      performanceLossUnits: s.waterfall.performanceLossUnits,
      qualityLossUnits: s.waterfall.qualityLossUnits,
    }))
  }, [data])

  const apqRows = useMemo(() => {
    if(!data?.segments) return []
    return data.segments.map(s => ({
      segment: `S${s.segmentId}`,
      availability: s.availability,
      performance: s.performance,
      quality: s.quality,
      oee: s.oee,
    }))
  }, [data])

  return (
    <div className="grid2">
      <div className="card">
        <div className="cardTitle">Live OEE</div>
        <div className="row" style={{ justifyContent:'space-between', marginTop: 6 }}>
          <div className="row">
            <Pill tone="info">Window</Pill>
            {[15,30,60].map(m => (
              <button key={m} className={`chip ${windowMinutes===m ? 'chip-selected':''}`} onClick={()=>setWindowMinutes(m)}>{m}m</button>
            ))}
          </div>
          <div className="row">
            {data?.running ? <Pill tone="good">RUNNING</Pill> : <Pill tone="warn">NO RUN</Pill>}
            {data?.running && <Pill tone="neutral">Total OEE {data.totalOee.toFixed(3)}</Pill>}
          </div>
        </div>
        {err && <div className="error">{err}</div>}
        {data?.running && (
          <div className="kpiGrid" style={{ marginTop: 12 }}>
            {data.segments.map(s => (
              <div key={s.segmentId} className="kpiCard">
                <div className="kpiLabel">Segment {s.segmentId}</div>
                <div className="kpiValue">{s.oee.toFixed(3)}</div>
                <div className="kpiSub">A {s.availability.toFixed(3)} · P {s.performance.toFixed(3)} · Q {s.quality.toFixed(3)}</div>
                <div className="kpiSub">Loss €{s.monetization.speedLossValue.toFixed(2)} · Scrap €{s.monetization.prelineScrapValue.toFixed(2)}</div>
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="card">
        <div className="cardTitle">Waterfall losses (per segment)</div>
        <div className="hint">Availability (minutes), Performance/Quality (units) — computed from encoder + measurements + defects + mix scrap.</div>
        <div style={{ height: 320, marginTop: 10 }}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={wfRows}>
              <CartesianGrid stroke="rgba(148,163,184,0.20)" vertical={false} />
              <XAxis dataKey="segment" tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
              <YAxis tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
              <Tooltip content={<PremiumTooltip />} />
              <Legend />
              <Bar dataKey="availabilityLossMin" name="Avail loss (min)" fill={ChartColors.warn} radius={[8,8,0,0]} />
              <Bar dataKey="performanceLossUnits" name="Perf loss (units)" fill={ChartColors.current} radius={[8,8,0,0]} />
              <Bar dataKey="qualityLossUnits" name="Quality loss (units)" fill={ChartColors.danger} radius={[8,8,0,0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div className="card">
        <div className="cardTitle">APQ overview</div>
        <div className="hint">Each segment: Availability, Performance, Quality → OEE.</div>
        <div style={{ height: 320, marginTop: 10 }}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={apqRows}>
              <CartesianGrid stroke="rgba(148,163,184,0.20)" vertical={false} />
              <XAxis dataKey="segment" tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
              <YAxis domain={[0,1]} tick={{ fill: 'var(--muted)', fontSize: 11 }} axisLine={{ stroke: 'var(--border)' }} tickLine={false} />
              <Tooltip content={<PremiumTooltip />} />
              <Legend />
              <Line dataKey="availability" name="Availability" dot={false} stroke={ChartColors.best} strokeWidth={2.2} />
              <Line dataKey="performance" name="Performance" dot={false} stroke={ChartColors.current} strokeWidth={2.2} />
              <Line dataKey="quality" name="Quality" dot={false} stroke={ChartColors.warn} strokeWidth={2.2} strokeDasharray="6 4" />
              <Line dataKey="oee" name="OEE" dot={false} stroke={ChartColors.avg} strokeWidth={2.6} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div className="card">
        <div className="cardTitle">Extras (separate KPIs)</div>
        {data?.running ? (
          <div className="grid3" style={{ marginTop: 10 }}>
            <div className="mini">
              <div className="miniLabel">Giveaway (est.)</div>
              <div className="miniValue">{data.extras.giveawayKg.toFixed(3)} kg</div>
            </div>
            <div className="mini">
              <div className="miniLabel">Pre-line scrap</div>
              <div className="miniValue">{data.extras.prelineScrapUnits.toFixed(2)} u</div>
              <div className="miniSub">€{data.extras.prelineScrapValue.toFixed(2)}</div>
            </div>
            <div className="mini">
              <div className="miniLabel">Process loss (est.)</div>
              <div className="miniValue">{data.extras.processLoss.delta_g.toFixed(2)} g</div>
              <div className="miniSub">P1 {data.extras.processLoss.avgWeightP1_g.toFixed(1)}g → P3 {data.extras.processLoss.avgWeightP3_g.toFixed(1)}g</div>
            </div>
          </div>
        ) : (
          <div className="hint" style={{ marginTop: 10 }}>Start a run (or simulator) to see KPIs.</div>
        )}
      </div>
    </div>
  )
}
