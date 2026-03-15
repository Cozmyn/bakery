import React, { useEffect, useMemo, useState } from 'react'
import { api } from '../components/api'
import { UiChip, Pill, SkeletonCard } from '../components/ui'
import '../styles/industrialAnalytics.css'

type Props = { token: string, role: string }

type Tol = {
  widthMinMm?: number|null
  widthMaxMm?: number|null
  lengthMinMm?: number|null
  lengthMaxMm?: number|null
  heightMinMm?: number|null
  heightMaxMm?: number|null
  volumeMinMm3?: number|null
  volumeMaxMm3?: number|null
  weightMinG?: number|null
  weightMaxG?: number|null
}

type PerBreadResp = {
  running: boolean
  runId: string
  positions: number
  tolerances: { p1: Tol|null, p2: Tol|null }
  instances: {
    rowIndex: number
    p1TimeUtc: string
    p2TimeUtc?: string|null
    positions: number
    cells: {
      pos: number
      pieceSeqIndex?: number
      p1?: { widthMm:number, lengthMm:number, heightMm:number, weightG:number, volumeL:number } | null
      p2?: { widthMm:number, lengthMm:number, heightMm:number, weightG:number, volumeL:number } | null
    }[]
  }[]
}

type Bucket20Resp = {
  running: boolean
  runId: string
  bucketMinutes: number
  maxBuckets: number
  tolerances?: { p1: Tol|null, p2: Tol|null, p3: Tol|null }
  buckets: any[]
}

function fmtTime(utc?: string|null){
  if(!utc) return '—'
  const d = new Date(utc)
  // Always 24h (no AM/PM)
  return d.toLocaleTimeString('en-GB', { hour:'2-digit', minute:'2-digit', hour12:false })
}

function outState(v:number, min?: number|null, max?: number|null): 'low'|'high'|'ok'{
  if(typeof min === 'number' && v < min) return 'low'
  if(typeof max === 'number' && v > max) return 'high'
  return 'ok'
}

function MetricCompareLine({ label, p1, p2, s1, s2 }:{
  label:string,
  p1:string,
  p2:string,
  s1:'low'|'high'|'ok',
  s2:'low'|'high'|'ok'
}){
  const c1 = s1==='ok' ? 'var(--text)' : 'var(--bad)'
  const c2 = s2==='ok' ? 'var(--text)' : 'var(--bad)'
  return (
    <>
      <span className="muted" style={{ whiteSpace:'nowrap' }}>{label}</span>
      <span style={{ textAlign:'right', color: c1, whiteSpace:'nowrap' }}>{p1}</span>
      <span style={{ textAlign:'right', color: c2, whiteSpace:'nowrap' }}>{p2}</span>
    </>
  )
}

function arrow(state:'low'|'high'|'ok'){
  if(state==='low') return '↓'
  if(state==='high') return '↑'
  return ''
}

function hasBucketMeasure(v:any|null|undefined){
  return !!v && Number(v?.count ?? 0) > 0
}

function fmtMetricValue(v:any, digits:number, ready=true){
  if(!ready || v == null || Number.isNaN(Number(v))) return ''
  return Number(v).toFixed(digits)
}

function MetricCompareLineRange({ label, p1, p2, s1, s2 }:{
  label:string,
  p1:string,
  p2:string,
  s1:'low'|'high'|'ok',
  s2:'low'|'high'|'ok'
}){
  const show1 = !!p1
  const show2 = !!p2
  const bad1 = show1 && s1 !== 'ok'
  const bad2 = show2 && s2 !== 'ok'
  return (
    <>
      <span className="muted" style={{ whiteSpace:'nowrap' }}>{label}</span>
      <span
        style={{
          textAlign:'right',
          color: bad1 ? 'var(--bad)' : 'var(--text)',
          textDecoration: bad1 ? 'underline' : 'none',
          whiteSpace:'nowrap'
        }}
      >{show1 ? `${p1}${arrow(s1)}` : ''}</span>
      <span
        style={{
          textAlign:'right',
          color: bad2 ? 'var(--bad)' : 'var(--text)',
          textDecoration: bad2 ? 'underline' : 'none',
          whiteSpace:'nowrap'
        }}
      >{show2 ? `${p2}${arrow(s2)}` : ''}</span>
    </>
  )
}

function MetricCompareLineTriple({ label, p1, p2, p3, s1, s2, s3 }:{
  label:string,
  p1:string,
  p2:string,
  p3:string,
  s1:'low'|'high'|'ok',
  s2:'low'|'high'|'ok',
  s3:'low'|'high'|'ok'
}){
  const cell = (val:string, st:'low'|'high'|'ok') => {
    const show = !!val
    const bad = show && st !== 'ok'
    return (
      <span
        style={{
          textAlign:'right',
          color: bad ? 'var(--bad)' : 'var(--text)',
          textDecoration: bad ? 'underline' : 'none',
          whiteSpace:'nowrap'
        }}
      >{show ? `${val}${arrow(st)}` : ''}</span>
    )
  }
  return (
    <>
      <span className="muted" style={{ whiteSpace:'nowrap' }}>{label}</span>
      {cell(p1, s1)}
      {cell(p2, s2)}
      {cell(p3, s3)}
    </>
  )
}

function BucketPosCompareTile({
  pos,
  p1,
  p2,
  tol1,
  tol2,
}:{
  pos:number,
  p1:any|null|undefined,
  p2:any|null|undefined,
  tol1: Tol|null|undefined,
  tol2: Tol|null|undefined,
}){
  const w1 = p1 ? outState(p1.widthMm, tol1?.widthMinMm, tol1?.widthMaxMm) : 'ok'
  const l1 = p1 ? outState(p1.lengthMm, tol1?.lengthMinMm, tol1?.lengthMaxMm) : 'ok'
  const h1 = p1 ? outState(p1.heightMm, tol1?.heightMinMm, tol1?.heightMaxMm) : 'ok'
  const wt1 = p1 ? outState(p1.weightG, tol1?.weightMinG, tol1?.weightMaxG) : 'ok'
  const v1 = p1 ? outState(p1.volumeL * 1_000_000, tol1?.volumeMinMm3 as any, tol1?.volumeMaxMm3 as any) : 'ok'

  const w2 = p2 ? outState(p2.widthMm, tol2?.widthMinMm, tol2?.widthMaxMm) : 'ok'
  const l2 = p2 ? outState(p2.lengthMm, tol2?.lengthMinMm, tol2?.lengthMaxMm) : 'ok'
  const h2 = p2 ? outState(p2.heightMm, tol2?.heightMinMm, tol2?.heightMaxMm) : 'ok'
  const wt2 = p2 ? outState(p2.weightG, tol2?.weightMinG, tol2?.weightMaxG) : 'ok'
  const v2 = p2 ? outState(p2.volumeL * 1_000_000, tol2?.volumeMinMm3 as any, tol2?.volumeMaxMm3 as any) : 'ok'

  const anyBad = [w1,l1,h1,wt1,v1,w2,l2,h2,wt2,v2].some(s => s !== 'ok')

  const p1W = p1 ? Number(p1.widthMm).toFixed(1) : '—'
  const p1L = p1 ? Number(p1.lengthMm).toFixed(1) : '—'
  const p1H = p1 ? Number(p1.heightMm).toFixed(1) : '—'
  const p1Wt = p1 ? Number(p1.weightG).toFixed(1) : '—'
  const p1V = p1 ? Number(p1.volumeL).toFixed(3) : '—'

  const p2W = p2 ? Number(p2.widthMm).toFixed(1) : '—'
  const p2L = p2 ? Number(p2.lengthMm).toFixed(1) : '—'
  const p2H = p2 ? Number(p2.heightMm).toFixed(1) : '—'
  const p2Wt = p2 ? Number(p2.weightG).toFixed(1) : '—'
  const p2V = p2 ? Number(p2.volumeL).toFixed(3) : '—'

  const n1 = p1?.count ?? 0
  const n2 = p2?.count ?? 0

  return (
    <div className="tile" style={{ borderColor: anyBad ? 'rgba(239,68,68,0.55)' : undefined, padding: 8 }}>
      <div className="tileTop">
        <span style={{ color:'var(--text)' }}>#{pos}</span>
        <span className="muted" style={{ whiteSpace:'nowrap' }}>n {n1}/{n2}</span>
      </div>
      <div style={{
        marginTop: 6,
        display:'grid',
        gridTemplateColumns:'22px 1fr 1fr',
        columnGap: 6,
        rowGap: 3,
        lineHeight: 1.2,
      }}>
        <span />
        <span className="muted" style={{ textAlign:'right', whiteSpace:'nowrap' }}>P1</span>
        <span className="muted" style={{ textAlign:'right', whiteSpace:'nowrap' }}>P2</span>

        <MetricCompareLineRange label="W" p1={p1W} p2={p2W} s1={w1} s2={w2} />
        <MetricCompareLineRange label="L" p1={p1L} p2={p2L} s1={l1} s2={l2} />
        <MetricCompareLineRange label="H" p1={p1H} p2={p2H} s1={h1} s2={h2} />
        <MetricCompareLineRange label="Wt" p1={p1Wt} p2={p2Wt} s1={wt1} s2={wt2} />
        <MetricCompareLineRange label="Vol" p1={p1V} p2={p2V} s1={v1} s2={v2} />
      </div>
    </div>
  )
}

function BucketSingleTile({
  pos,
  v,
  tol,
  count,
  dimLabel,
}:{
  pos:number,
  v:any|null|undefined,
  tol: Tol|null|undefined,
  count:number,
  dimLabel: 'P3'
}){
  const w = v ? outState(v.widthMm, tol?.widthMinMm, tol?.widthMaxMm) : 'ok'
  const l = v ? outState(v.lengthMm, tol?.lengthMinMm, tol?.lengthMaxMm) : 'ok'
  const h = v ? outState(v.heightMm, tol?.heightMinMm, tol?.heightMaxMm) : 'ok'
  const wt = v ? outState(v.weightG, tol?.weightMinG, tol?.weightMaxG) : 'ok'
  const vol = v ? outState(v.volumeL * 1_000_000, tol?.volumeMinMm3 as any, tol?.volumeMaxMm3 as any) : 'ok'
  const anyBad = [w,l,h,wt,vol].some(s => s !== 'ok')

  const W = v ? Number(v.widthMm).toFixed(1) : '—'
  const L = v ? Number(v.lengthMm).toFixed(1) : '—'
  const H = v ? Number(v.heightMm).toFixed(1) : '—'
  const Wt = v ? Number(v.weightG).toFixed(1) : '—'
  const Vol = v ? Number(v.volumeL).toFixed(3) : '—'

  return (
    <div className="tile" style={{ borderColor: anyBad ? 'rgba(239,68,68,0.55)' : undefined, padding: 8, opacity: count>0 ? 1 : 0.55 }}>
      <div className="tileTop">
        <span style={{ color:'var(--text)' }}>#{pos}</span>
        <span className="muted" style={{ whiteSpace:'nowrap' }}>{dimLabel} n {count}</span>
      </div>
      <div style={{
        marginTop: 6,
        display:'grid',
        gridTemplateColumns:'22px 1fr',
        columnGap: 6,
        rowGap: 3,
        lineHeight: 1.2,
      }}>
        <span className="muted">W</span>
        <span style={{ textAlign:'right', color: w==='ok' ? 'var(--text)' : 'var(--bad)', textDecoration: w==='ok' ? 'none' : 'underline', whiteSpace:'nowrap' }}>{W}{arrow(w)}</span>
        <span className="muted">L</span>
        <span style={{ textAlign:'right', color: l==='ok' ? 'var(--text)' : 'var(--bad)', textDecoration: l==='ok' ? 'none' : 'underline', whiteSpace:'nowrap' }}>{L}{arrow(l)}</span>
        <span className="muted">H</span>
        <span style={{ textAlign:'right', color: h==='ok' ? 'var(--text)' : 'var(--bad)', textDecoration: h==='ok' ? 'none' : 'underline', whiteSpace:'nowrap' }}>{H}{arrow(h)}</span>
        <span className="muted">Wt</span>
        <span style={{ textAlign:'right', color: wt==='ok' ? 'var(--text)' : 'var(--bad)', textDecoration: wt==='ok' ? 'none' : 'underline', whiteSpace:'nowrap' }}>{Wt}{arrow(wt)}</span>
        <span className="muted">Vol</span>
        <span style={{ textAlign:'right', color: vol==='ok' ? 'var(--text)' : 'var(--bad)', textDecoration: vol==='ok' ? 'none' : 'underline', whiteSpace:'nowrap' }}>{Vol}{arrow(vol)}</span>
      </div>
    </div>
  )
}

function PieceCompareTile({
  pos,
  p1,
  p2,
  tol1,
  tol2,
}:{
  pos:number,
  p1: any|null|undefined,
  p2: any|null|undefined,
  tol1: Tol|null|undefined,
  tol2: Tol|null|undefined,
}){
  const p1Ready = !!p1
  const p2Ready = !!p2

  const w1 = p1Ready ? outState(p1.widthMm, tol1?.widthMinMm, tol1?.widthMaxMm) : 'ok'
  const l1 = p1Ready ? outState(p1.lengthMm, tol1?.lengthMinMm, tol1?.lengthMaxMm) : 'ok'
  const h1 = p1Ready ? outState(p1.heightMm, tol1?.heightMinMm, tol1?.heightMaxMm) : 'ok'
  const wt1 = p1Ready ? outState(p1.weightG, tol1?.weightMinG, tol1?.weightMaxG) : 'ok'
  const v1 = p1Ready ? outState(p1.volumeL * 1_000_000, tol1?.volumeMinMm3 as any, tol1?.volumeMaxMm3 as any) : 'ok'

  const w2 = p2Ready ? outState(p2.widthMm, tol2?.widthMinMm, tol2?.widthMaxMm) : 'ok'
  const l2 = p2Ready ? outState(p2.lengthMm, tol2?.lengthMinMm, tol2?.lengthMaxMm) : 'ok'
  const h2 = p2Ready ? outState(p2.heightMm, tol2?.heightMinMm, tol2?.heightMaxMm) : 'ok'
  const wt2 = p2Ready ? outState(p2.weightG, tol2?.weightMinG, tol2?.weightMaxG) : 'ok'
  const v2 = p2Ready ? outState(p2.volumeL * 1_000_000, tol2?.volumeMinMm3 as any, tol2?.volumeMaxMm3 as any) : 'ok'

  const anyBad = [w1,l1,h1,wt1,v1,w2,l2,h2,wt2,v2].some(s => s !== 'ok')

  const p1W = fmtMetricValue(p1?.widthMm, 1, p1Ready)
  const p1L = fmtMetricValue(p1?.lengthMm, 1, p1Ready)
  const p1H = fmtMetricValue(p1?.heightMm, 1, p1Ready)
  const p1Wt = fmtMetricValue(p1?.weightG, 1, p1Ready)
  const p1V = fmtMetricValue(p1?.volumeL, 3, p1Ready)

  const p2W = fmtMetricValue(p2?.widthMm, 1, p2Ready)
  const p2L = fmtMetricValue(p2?.lengthMm, 1, p2Ready)
  const p2H = fmtMetricValue(p2?.heightMm, 1, p2Ready)
  const p2Wt = fmtMetricValue(p2?.weightG, 1, p2Ready)
  const p2V = fmtMetricValue(p2?.volumeL, 3, p2Ready)

  return (
    <div className="tile" style={{ borderColor: anyBad ? 'rgba(239,68,68,0.55)' : undefined, padding: 8 }}>
      <div className="tileTop">
        <span style={{ color:'var(--text)' }}>#{pos}</span>
        <span className="muted">{p2 ? 'P2' : '…'}</span>
      </div>
      <div style={{
        marginTop: 6,
        display:'grid',
        gridTemplateColumns:'22px 1fr 1fr',
        columnGap: 6,
        rowGap: 3,
        lineHeight: 1.2,
      }}>
        <span />
        <span className="muted" style={{ textAlign:'right', whiteSpace:'nowrap' }}>P1</span>
        <span className="muted" style={{ textAlign:'right', whiteSpace:'nowrap' }}>P2</span>

        {/* Out-of-range must be obvious: red + underline + direction arrow (both P1 & P2) */}
        <MetricCompareLineRange label="W" p1={p1W} p2={p2W} s1={w1} s2={w2} />
        <MetricCompareLineRange label="L" p1={p1L} p2={p2L} s1={l1} s2={l2} />
        <MetricCompareLineRange label="H" p1={p1H} p2={p2H} s1={h1} s2={h2} />
        <MetricCompareLineRange label="Wt" p1={p1Wt} p2={p2Wt} s1={wt1} s2={wt2} />
        <MetricCompareLineRange label="Vol" p1={p1V} p2={p2V} s1={v1} s2={v2} />
      </div>
    </div>
  )
}

function BucketTripleTile({
  pos,
  p1,
  p2,
  p3,
  tol1,
  tol2,
  tol3,
}: {
  pos:number,
  p1:any|null|undefined,
  p2:any|null|undefined,
  p3:any|null|undefined,
  tol1: Tol|null|undefined,
  tol2: Tol|null|undefined,
  tol3: Tol|null|undefined,
}){
  const p1Ready = hasBucketMeasure(p1)
  const p2Ready = hasBucketMeasure(p2)
  const p3Ready = hasBucketMeasure(p3)

  const w1 = p1Ready ? outState(p1.widthMm, tol1?.widthMinMm, tol1?.widthMaxMm) : 'ok'
  const l1 = p1Ready ? outState(p1.lengthMm, tol1?.lengthMinMm, tol1?.lengthMaxMm) : 'ok'
  const h1 = p1Ready ? outState(p1.heightMm, tol1?.heightMinMm, tol1?.heightMaxMm) : 'ok'
  const wt1 = p1Ready ? outState(p1.weightG, tol1?.weightMinG, tol1?.weightMaxG) : 'ok'
  const v1 = p1Ready ? outState(p1.volumeL * 1_000_000, tol1?.volumeMinMm3 as any, tol1?.volumeMaxMm3 as any) : 'ok'

  const w2 = p2Ready ? outState(p2.widthMm, tol2?.widthMinMm, tol2?.widthMaxMm) : 'ok'
  const l2 = p2Ready ? outState(p2.lengthMm, tol2?.lengthMinMm, tol2?.lengthMaxMm) : 'ok'
  const h2 = p2Ready ? outState(p2.heightMm, tol2?.heightMinMm, tol2?.heightMaxMm) : 'ok'
  const wt2 = p2Ready ? outState(p2.weightG, tol2?.weightMinG, tol2?.weightMaxG) : 'ok'
  const v2 = p2Ready ? outState(p2.volumeL * 1_000_000, tol2?.volumeMinMm3 as any, tol2?.volumeMaxMm3 as any) : 'ok'

  const w3 = p3Ready ? outState(p3.widthMm, tol3?.widthMinMm, tol3?.widthMaxMm) : 'ok'
  const l3 = p3Ready ? outState(p3.lengthMm, tol3?.lengthMinMm, tol3?.lengthMaxMm) : 'ok'
  const h3 = p3Ready ? outState(p3.heightMm, tol3?.heightMinMm, tol3?.heightMaxMm) : 'ok'
  const wt3 = p3Ready ? outState(p3.weightG, tol3?.weightMinG, tol3?.weightMaxG) : 'ok'
  const v3 = p3Ready ? outState(p3.volumeL * 1_000_000, tol3?.volumeMinMm3 as any, tol3?.volumeMaxMm3 as any) : 'ok'

  const anyBad = [w1,l1,h1,wt1,v1,w2,l2,h2,wt2,v2,w3,l3,h3,wt3,v3].some(s => s !== 'ok')

  const p1W = fmtMetricValue(p1?.widthMm, 1, p1Ready)
  const p1L = fmtMetricValue(p1?.lengthMm, 1, p1Ready)
  const p1H = fmtMetricValue(p1?.heightMm, 1, p1Ready)
  const p1Wt = fmtMetricValue(p1?.weightG, 1, p1Ready)
  const p1V = fmtMetricValue(p1?.volumeL, 3, p1Ready)

  const p2W = fmtMetricValue(p2?.widthMm, 1, p2Ready)
  const p2L = fmtMetricValue(p2?.lengthMm, 1, p2Ready)
  const p2H = fmtMetricValue(p2?.heightMm, 1, p2Ready)
  const p2Wt = fmtMetricValue(p2?.weightG, 1, p2Ready)
  const p2V = fmtMetricValue(p2?.volumeL, 3, p2Ready)

  const p3W = fmtMetricValue(p3?.widthMm, 1, p3Ready)
  const p3L = fmtMetricValue(p3?.lengthMm, 1, p3Ready)
  const p3H = fmtMetricValue(p3?.heightMm, 1, p3Ready)
  const p3Wt = fmtMetricValue(p3?.weightG, 1, p3Ready)
  const p3V = fmtMetricValue(p3?.volumeL, 3, p3Ready)

  const n1 = p1?.count ?? 0
  const n2 = p2?.count ?? 0
  const n3 = p3?.count ?? 0

  return (
    <div className={`iaTileWrap ${anyBad ? 'iaTileErr' : ''}`.trim()}>
      <div className="tile" style={{ padding: 8 }}>
        <div className="tileTop">
          <span style={{ color:'var(--text)' }}>#{pos}</span>
          <span className="muted" style={{ whiteSpace:'nowrap' }}>n {n1}/{n2}/{n3}</span>
        </div>
        <div
          style={{
            marginTop: 6,
            display:'grid',
            gridTemplateColumns:'22px 1fr 1fr 1fr',
            columnGap: 6,
            rowGap: 3,
            lineHeight: 1.2,
          }}
        >
          <span />
          <span className="muted" style={{ textAlign:'right', whiteSpace:'nowrap' }}>P1</span>
          <span className="muted" style={{ textAlign:'right', whiteSpace:'nowrap' }}>P2</span>
          <span className="muted" style={{ textAlign:'right', whiteSpace:'nowrap' }}>P3</span>

          <MetricCompareLineTriple label="W" p1={p1W} p2={p2W} p3={p3W} s1={w1} s2={w2} s3={w3} />
          <MetricCompareLineTriple label="L" p1={p1L} p2={p2L} p3={p3L} s1={l1} s2={l2} s3={l3} />
          <MetricCompareLineTriple label="H" p1={p1H} p2={p2H} p3={p3H} s1={h1} s2={h2} s3={h3} />
          <MetricCompareLineTriple label="Wt" p1={p1Wt} p2={p2Wt} p3={p3Wt} s1={wt1} s2={wt2} s3={wt3} />
          <MetricCompareLineTriple label="Vol" p1={p1V} p2={p2V} p3={p3V} s1={v1} s2={v2} s3={v3} />
        </div>
      </div>
    </div>
  )
}

function ParetoTile({ pareto }:{ pareto:any[] }){
  const top = (pareto ?? []).slice(0, 8)
  const max = top.reduce((m, x) => Math.max(m, Number(x?.count ?? 0)), 0)
  return (
    <div className="tile iaParetoTile" style={{ padding: 8 }}>
      <div className="tileTop">
        <span style={{ color:'var(--text)' }}>VIS</span>
        <span className="muted">Pareto</span>
      </div>
      <div style={{ marginTop: 6, display:'grid', gap:6 }}>
        {top.length === 0 ? (
          <div className="muted">No defects.</div>
        ) : top.map((x:any) => {
          const n = Number(x?.count ?? 0)
          const w = max > 0 ? Math.round((n / max) * 100) : 0
          return (
            <div key={x?.defectType ?? JSON.stringify(x)} style={{ display:'grid', gridTemplateColumns:'1fr 70px 18px', gap:6, alignItems:'center' }}>
              <div style={{ overflow:'hidden', textOverflow:'ellipsis', whiteSpace:'nowrap' }}>{x.defectType}</div>
              <div className="iaBarBg"><div className="iaBarFill" style={{ width: `${w}%` }} /></div>
              <div className="muted" style={{ textAlign:'right' }}>{n}</div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

export default function AnalyticsPage({ token }: Props){
  const [tab, setTab] = useState<'per'|'buckets'>('per')

  // Subpage 1
  const [per, setPer] = useState<PerBreadResp | null>(null)
  const [perErr, setPerErr] = useState<string | null>(null)

  // Subpage 2
  const [b20, setB20] = useState<Bucket20Resp | null>(null)
  const [bErr, setBErr] = useState<string | null>(null)

  // load per-bread (fast)
  useEffect(() => {
    if(tab !== 'per') return
    let alive = true
    async function load(){
      const r = await api<PerBreadResp>(`/analytics/per-bread?sampleEveryMinutes=3&maxInstances=30`, token)
      if(!alive) return
      setPer(r)
      setPerErr(null)
    }
    load().catch(e => { if(alive) setPerErr(e?.message || 'Failed to load per-bread') })
    const t = setInterval(() => { load().catch(()=>{}) }, 15_000)
    return () => { alive = false; clearInterval(t) }
  }, [token, tab])

  // load buckets20 (slower)
  useEffect(() => {
    if(tab !== 'buckets') return
    let alive = true
    async function load(){
      const r = await api<Bucket20Resp>(`/analytics/buckets20?maxBuckets=12`, token)
      if(!alive) return
      setB20(r)
      setBErr(null)
    }
    load().catch(e => { if(alive) setBErr(e?.message || 'Failed to load buckets') })
    // Buckets should feel "live"; keep the polling interval reasonably small.
    const t = setInterval(() => { load().catch(()=>{}) }, 15_000)
    return () => { alive = false; clearInterval(t) }
  }, [token, tab])

  const positions = per?.positions ?? b20?.buckets?.[0]?.positions ?? 6

  const colsStyle = useMemo(() => ({ gridTemplateColumns: `repeat(${positions}, minmax(0,1fr))` }), [positions])

  return (
    <div className="page analyticsPage">
      <div className="pageHeader">
        <div>
          <div className="pageTitle">Analytics</div>
          <div className="pageSubtitle">Comparații side‑by‑side (fără delta). „Per pâine” aliniază P1↔P2 strict pe numărătoare/poziție. „Bucket 20 min” folosește setul fix de piese numărate în P1 pentru medii P1/P2 și Pareto VIS (pe același set de piese); doar P3 este pe timp, calculat din fereastra VIS a setului (VIS→P3), apoi distribuit random pe poziții (persistat).</div>
        </div>
        <div className="row">
          <Pill tone="info">View</Pill>
          <UiChip selected={tab==='per'} onClick={() => setTab('per')}>Per pâine (P1→P2)</UiChip>
          <UiChip selected={tab==='buckets'} onClick={() => setTab('buckets')}>Bucket medii (20 min)</UiChip>
        </div>
      </div>

      {tab==='per' && (
        <div className="card">
          <div className="cardHeader">
            <h2>Per pâine — P1 ↔ P2 (rolling max 30)</h2>
            <span className="badge">La fiecare 3 minute se adaugă o instanță P1 (un rând complet). P2 se completează când ajung aceleași piese (strict pe numărătoare).</span>
          </div>

          {perErr && <div className="error">{perErr}</div>}

          {!per ? (
            <SkeletonCard lines={10} />
          ) : !per.running ? (
            <div>No active run.</div>
          ) : (
            <div className="iaRoot" style={{ marginTop: 12 }}>
              <div className="iaPanel">
                <div className="iaToolbar">
                  <span className="iaTitle">P1 ↔ P2 · per pâine</span>
                  <span className="iaBadge">{per.instances.length} instanțe</span>
                  <span className="iaBadge">{per.positions} poziții</span>
                  <span className="muted">scroll pe pagină pentru istoric · pozițiile rămân afișate compact</span>
                </div>
                <div className="iaLegend">
                  <span className="muted">În fiecare „pâine” vezi P1 și P2 pe aceeași dimensiune, cu warning (roșu + săgeată) dacă depășește toleranțele din Master Data.</span>
                </div>
                <div className="iaList">
                  {per.instances.map((inst:any) => (
                    <div key={inst.rowIndex} className="iaRow">
                      <div className="iaFrozen">
                        <div className="iaFrozenTop">
                          <span className="mono iaFrozenMain">row {inst.rowIndex}</span>
                          <span className="iaBadge">{per.positions}</span>
                        </div>
                        <div className="iaFrozenSub">
                          <div className="iaStamp">
                            <span className="k">P1</span>
                            <span className="v">{fmtTime(inst.p1TimeUtc)}</span>
                          </div>
                          <div className="iaStamp">
                            <span className="k">P2</span>
                            <span className="v">{fmtTime(inst.p2TimeUtc ?? null)}</span>
                          </div>
                          {!inst.p2TimeUtc && <span className="iaBadge iaBadgeWarn">în așteptare</span>}
                        </div>
                      </div>
                      <div className="iaTiles">
                        {inst.cells.map((c:any) => (
                          <div key={`cmp-${inst.rowIndex}-${c.pos}`} className={`iaTileWrap ${c.p2 ? '' : 'iaPend'}`.trim()}>
                            <PieceCompareTile
                              pos={c.pos}
                              p1={c.p1}
                              p2={c.p2}
                              tol1={per.tolerances?.p1}
                              tol2={per.tolerances?.p2}
                            />
                          </div>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      {tab==='buckets' && (
        <div className="card">
          <div className="cardHeader">
            <h2>Bucket medii — 20 min (rolling 12)</h2>
            <span className="badge">Bucket‑urile apar și „în curs”. Medii P1/P2 și Pareto VIS sunt pe același set fix de piese (numărate în P1). P3 folosește fereastra de timp derivată din VIS (VIS→P3), apoi distribuie random pe poziții (seed persistat pentru rapoarte).</span>
          </div>

          {bErr && <div className="error">{bErr}</div>}

          {!b20 ? (
            <SkeletonCard lines={10} />
          ) : !b20.running ? (
            <div>No active run.</div>
          ) : (
            <div className="iaRoot" style={{ marginTop: 12 }}>
              <div className="iaPanel">
                <div className="iaToolbar">
                  <span className="iaTitle">Bucket medii · 20 min</span>
                  <span className="iaBadge">{b20.buckets.length} buckets</span>
                  <span className="iaBadge">{positions} poziții</span>
                  <span className="muted">VIS Pareto = pe același set de piese (nu pe timp) · P3 = pe timp (VIS→P3)</span>
                </div>
                <div className="iaLegend">
                  <span className="muted">În fiecare poziție vezi P1/P2/P3 side‑by‑side. Warning: roșu + subliniat + săgeată ↑/↓ dacă depășește toleranțele.</span>
                </div>
                <div className="iaList">
                  {b20.buckets.map((b:any) => {
                    const p1 = b.p1
                    const p2 = b.p2
                    const vis = b.vis
                    const p3 = b.p3
                    const p1Per = (p1?.perPos ?? []) as any[]
                    const p2Per = (p2?.perPos ?? []) as any[]
                    const p3Per = (p3?.perPos ?? []) as any[]
                    const pareto = (vis?.pareto ?? []) as any[]

                    const p1ByPos = new Map<number, any>()
                    const p2ByPos = new Map<number, any>()
                    const p3ByPos = new Map<number, any>()
                    p1Per.forEach(x => p1ByPos.set(x.pos, x))
                    p2Per.forEach(x => p2ByPos.set(x.pos, x))
                    p3Per.forEach(x => p3ByPos.set(x.pos, x))

                    const tol1 = b20.tolerances?.p1
                    const tol2 = b20.tolerances?.p2
                    const tol3 = b20.tolerances?.p3

                    const isFinal = !!b.isFinal

                    return (
                      <div key={b.id} className="iaRow">
                        <div className="iaFrozen">
                          <div className="iaFrozenTop">
                            <div style={{ display:'flex', flexDirection:'column', gap:4 }}>
                              <span className="mono iaFrozenMain">PCS {b.p1StartPieceSeq}-{b.p1EndPieceSeq}</span>
                              <span className="mono muted" style={{ whiteSpace:'nowrap' }}>P1/P2 {b.p1PieceCount}/{b.p2PieceCount}</span>
                            </div>
                            <span className={`iaBadge ${isFinal ? '' : 'iaBadgeWarn'}`.trim()}>{isFinal ? 'final' : 'LIVE'}</span>
                          </div>
                          <div className="iaFrozenSub">
                            <div className="iaStamp">
                              <span className="k">P1</span>
                              <span className="v">{fmtTime(b.bucketStartUtc)}→{fmtTime(b.bucketEndUtc)}</span>
                            </div>
                            <div className="iaStamp">
                              <span className="k">P2</span>
                              <span className="v">{fmtTime(p2?.p2FirstUtc)}→{fmtTime(p2?.p2LastUtc)}</span>
                            </div>
                            <div className="iaStamp">
                              <span className="k">VIS</span>
                              <span className="v">{fmtTime(vis?.visFirstUtc)}→{fmtTime(vis?.visLastUtc)}</span>
                            </div>
                            <div className="iaStamp">
                              <span className="k">P3</span>
                              <span className="v">{fmtTime(p3?.p3WindowStartUtc)}→{fmtTime(p3?.p3WindowEndUtc)}</span>
                            </div>
                          </div>
                        </div>

                        <div className="iaTiles">
                          {Array.from({ length: b.positions }, (_, i) => i + 1).map(pos => (
                            <BucketTripleTile
                              key={`btri-${b.id}-${pos}`}
                              pos={pos}
                              p1={p1ByPos.get(pos)}
                              p2={p2ByPos.get(pos)}
                              p3={p3ByPos.get(pos)}
                              tol1={tol1}
                              tol2={tol2}
                              tol3={tol3}
                            />
                          ))}

                          <div className="iaParetoWrap">
                            <ParetoTile pareto={pareto} />
                          </div>
                        </div>
                      </div>
                    )
                  })}
                </div>
              </div>
            </div>
          )}
        </div>
      )}

    </div>
  )
}
