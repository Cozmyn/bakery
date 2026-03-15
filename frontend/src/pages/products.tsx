import React, { useEffect, useMemo, useState } from 'react'
import { api } from '../components/api'
import { Modal, Pill } from '../components/ui'

type Props = { token: string }

type Product = {
  id: string
  code: string
  name: string
  isActive: boolean
  publishedAtUtc?: string | null
  proofingMinMinutes?: number | null
  proofingMaxMinutes?: number | null
  nominalUnitWeightG_P3?: number | null
}

type Readiness = { canPublish: boolean, missing: string[] }

type Tolerance = {
  point: 'P1'|'P2'|'P3'
  widthMinMm?: number|null; widthMaxMm?: number|null
  lengthMinMm?: number|null; lengthMaxMm?: number|null
  heightMinMm?: number|null; heightMaxMm?: number|null
  volumeMinMm3?: number|null; volumeMaxMm3?: number|null
  weightMinG?: number|null; weightMaxG?: number|null
}

type Segment = { segmentId: number, lengthM: number, targetSpeedMps: number }

type Ingredient = { id: string, itemNumber: number, name: string, defaultUnit?: string | null }
type RecipeLineEdit = { rowId: string, ingredientId: string, itemNumber: string, ingredientName: string, quantityKg: string }

function uid(){
  try{
    // Browsers: stable unique ids without adding deps
    return (globalThis as any).crypto?.randomUUID?.() ?? `${Math.random().toString(16).slice(2)}${Date.now().toString(16)}`
  }catch{
    return `${Math.random().toString(16).slice(2)}${Date.now().toString(16)}`
  }
}

export default function ProductsPage({ token }: Props){
  const [products, setProducts] = useState<Product[]>([])
  const [selected, setSelected] = useState<string>('')
  const [readiness, setReadiness] = useState<Readiness | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const [ingredients, setIngredients] = useState<Ingredient[]>([])
  const [recipeLines, setRecipeLines] = useState<RecipeLineEdit[]>([])
  const [recipeVersion, setRecipeVersion] = useState<number | null>(null)
  const [newIngPrompt, setNewIngPrompt] = useState<{ lineIdx: number, code: string, name: string } | null>(null)
  const [deleteIngredientId, setDeleteIngredientId] = useState<string>('')
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null)

  const [createModal, setCreateModal] = useState(false)
  const [newCode, setNewCode] = useState('BREAD-NEW')
  const [newName, setNewName] = useState('New Product')

  const current = useMemo(() => products.find(p => p.id === selected) ?? null, [products, selected])

  const [edit, setEdit] = useState<any>({})
  const [dens, setDens] = useState({ densityP1: 0.75, densityP2: 0.60, densityP3: 0.55 })
  const [tols, setTols] = useState<Tolerance[]>([
    { point:'P1', widthMinMm:60, widthMaxMm:85, lengthMinMm:140, lengthMaxMm:180, heightMinMm:45, heightMaxMm:70, volumeMinMm3:250000, volumeMaxMm3:500000, weightMinG:470, weightMaxG:530 },
    { point:'P2', widthMinMm:60, widthMaxMm:85, lengthMinMm:140, lengthMaxMm:180, heightMinMm:50, heightMaxMm:80, volumeMinMm3:280000, volumeMaxMm3:560000, weightMinG:470, weightMaxG:530 },
    { point:'P3', widthMinMm:60, widthMaxMm:85, lengthMinMm:140, lengthMaxMm:180, heightMinMm:48, heightMaxMm:78, volumeMinMm3:270000, volumeMaxMm3:540000, weightMinG:470, weightMaxG:530 }
  ])
  const [segs, setSegs] = useState<Segment[]>([
    { segmentId: 1, lengthM: 12, targetSpeedMps: 0.8 },
    { segmentId: 2, lengthM: 8, targetSpeedMps: 0.8 },
    { segmentId: 3, lengthM: 15, targetSpeedMps: 0.8 },
    { segmentId: 4, lengthM: 10, targetSpeedMps: 0.8 }
  ])

  async function load(){
    setErr(null)
    try{
      const list = await api<Product[]>(`/products`, token)
      setProducts(list)
      if(!selected && list.length) setSelected(list[0].id)
    }catch(e:any){ setErr(e.message) }
  }

  async function loadIngredients(){
    try{
      const raw = await api<any[]>(`/ingredients`, token)
      const list: Ingredient[] = (raw ?? []).map((x:any) => ({
        id: x.id ?? x.Id,
        itemNumber: Number(x.itemNumber ?? x.ItemNumber ?? x.code ?? x.Code ?? 0),
        name: x.name ?? x.Name,
        defaultUnit: x.defaultUnit ?? x.DefaultUnit ?? null
      })).filter((x:any) => x.id && Number.isFinite(x.itemNumber) && x.itemNumber > 0 && x.name)
      setIngredients(list)
    }catch(e:any){
      // ignore (UI remains usable without ingredients list)
      setIngredients([])
    }
  }


function parseKg(v: any): number {
  if(v === null || v === undefined) return 0
  const s = String(v).trim().replace(',', '.')
  const n = Number(s)
  return Number.isFinite(n) ? n : 0
}


function onlyDigits(s: string){
  return (s || '').replace(/[^\d]/g, '')
}

function norm(s: string){ return (s || '').trim().toLowerCase() }

function matchIngredientItemNumber(itemNumberText: string): Ingredient | null {
  const c = onlyDigits(itemNumberText)
  if(!c) return null
  const n = Number(c)
  if(!Number.isFinite(n) || n <= 0) return null
  return ingredients.find(i => i.itemNumber === n) ?? null
}

function setLineItemNumberFromInput(lineIdx: number, input: string){
  const code = (input || '').trim()
  const m = matchIngredientItemNumber(code)
  setRecipeLines(s => {
    const copy = [...s]
    if(!copy[lineIdx]) return copy
    copy[lineIdx] = {
      ...copy[lineIdx],
      itemNumber: code,
      ingredientId: m ? m.id : '',
      ingredientName: m ? m.name : (copy[lineIdx].ingredientId ? '' : copy[lineIdx].ingredientName)
    }
    return copy
  })
}

function requestCreateIngredient(lineIdx: number){
  const code = (recipeLines[lineIdx]?.itemNumber || '').trim()
  if(!code) return
  if(matchIngredientItemNumber(code)) return
  const name = (recipeLines[lineIdx]?.ingredientName || '').trim()
  setNewIngPrompt({ lineIdx, code, name })
}

  async function loadRecipe(pid: string){
    try{
      const r = await api<any>(`/products/${pid}/recipe`, token)
      if(!r?.exists){
        setRecipeVersion(null)
        setRecipeLines([])
        return
      }
      setRecipeVersion(r.recipe?.version ?? null)
const lines: RecipeLineEdit[] = (r.recipe?.ingredients ?? []).map((x:any) => {
  const ingredientId = x.ingredient?.ingredientId ?? x.ingredientId ?? ''
  const found = ingredientId ? ingredients.find(i => i.id === ingredientId) : null
  const code = x.ingredient?.itemNumber ?? x.ingredient?.ItemNumber ?? found?.itemNumber ?? ''
  const name = x.ingredient?.name ?? x.ingredient?.Name ?? found?.name ?? ''
  return {
    rowId: ingredientId || uid(),
    ingredientId,
    itemNumber: String(code ?? ''),
    ingredientName: String(name ?? ''),
    quantityKg: String(x.quantity ?? '')
  }
})
setRecipeLines(lines)
    }catch(e:any){
      setRecipeVersion(null)
      setRecipeLines([])
    }
  }

  async function loadReadiness(pid: string){
    try{
      setReadiness(await api<Readiness>(`/products/${pid}/readiness`, token))
    }catch(e:any){ /* ignore */ }
  }

  useEffect(() => { load(); loadIngredients() }, [])
  useEffect(() => { if(selected) { loadReadiness(selected); loadRecipe(selected); setEdit({}); } }, [selected])

  async function create(){
    setNewCode('BREAD-NEW')
    setNewName('New Product')
    setCreateModal(true)
  }

  async function confirmCreate(){
    const code = (newCode || '').trim()
    const name = (newName || '').trim()
    if(!code || !name) return
    setBusy('create')
    setErr(null)
    try{
      const r = await api<{id:string}>(`/products`, token, { method:'POST', body: JSON.stringify({ code, name }) })
      setCreateModal(false)
      await load()
      setSelected((r as any).id ?? '')
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  async function saveBasics(){
    if(!current) return
    setBusy('basics')
    setErr(null)
    try{
      await api(`/products/${current.id}`, token, {
        method:'PUT',
        body: JSON.stringify({
          name: edit.name ?? current.name,
          proofingMinMinutes: Number(edit.proofingMinMinutes ?? current.proofingMinMinutes ?? 40),
          proofingMaxMinutes: Number(edit.proofingMaxMinutes ?? current.proofingMaxMinutes ?? 70),
          nominalUnitWeightG_P3: Number(edit.nominalUnitWeightG_P3 ?? current.nominalUnitWeightG_P3 ?? 500),
          costPerUnit: Number(edit.costPerUnit ?? 1.2),
          idealCycleTimeSec: Number(edit.idealCycleTimeSec ?? 2)
        })
      })
      await load()
      await loadReadiness(current.id)
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  async function saveDens(){
    if(!current) return
    setBusy('dens')
    setErr(null)
    try{
      await api(`/products/${current.id}/density-defaults`, token, {
        method:'PUT',
        body: JSON.stringify({ densityP1: dens.densityP1, densityP2: dens.densityP2, densityP3: dens.densityP3 })
      })
      await loadReadiness(current.id)
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  async function saveTols(){
    if(!current) return
    setBusy('tols')
    setErr(null)
    try{
      await api(`/products/${current.id}/tolerances`, token, { method:'PUT', body: JSON.stringify(tols) })
      await loadReadiness(current.id)
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  async function saveSegs(){
    if(!current) return
    setBusy('segs')
    setErr(null)
    try{
      await api(`/products/${current.id}/segments`, token, { method:'PUT', body: JSON.stringify(segs) })
      await loadReadiness(current.id)
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  async function publish(){
    if(!current) return
    setBusy('publish')
    setErr(null)
    try{
      await api(`/products/${current.id}/publish`, token, { method:'POST' })
      await load()
      await loadReadiness(current.id)
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

  async function saveRecipe(){
    if(!current) return
    setBusy('recipe')
    setErr(null)
    try{
      const payload = {
        version: null,
        ingredients: recipeLines
          .filter(l => l.ingredientId && parseKg(l.quantityKg) > 0)
          .map(l => ({ ingredientId: l.ingredientId, quantity: parseKg(l.quantityKg), unit: 'kg' }))
      }
      await api(`/products/${current.id}/recipe`, token, { method:'PUT', body: JSON.stringify(payload) })
      await loadReadiness(current.id)
      await loadRecipe(current.id)
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }

async function confirmCreateIngredient(){
  if(!newIngPrompt) return
  const { lineIdx } = newIngPrompt
  const code = (newIngPrompt.code || '').trim()
  const name = (newIngPrompt.name || '').trim()
  if(!code || !name) return
  const itemNumber = Number(onlyDigits(code))
  if(!Number.isFinite(itemNumber) || itemNumber <= 0) return
  setBusy('ingredient-create')
  setErr(null)
  try{
    const r = await api<{id:string}>(`/ingredients`, token, { method:'POST', body: JSON.stringify({ itemNumber, name, defaultUnit:'kg' }) })
    await loadIngredients()
    const newId = ((r as any).id ?? (r as any).Id) as string
    setRecipeLines(s => {
      const copy = [...s]
      if(copy[lineIdx]){
        copy[lineIdx] = { ...copy[lineIdx], ingredientId: newId, itemNumber: code, ingredientName: name }
      }
      return copy
    })
    setNewIngPrompt(null)
  }catch(e:any){ setErr(e.message) }
  finally{ setBusy(null) }
}

  async function deleteIngredient(id: string){
    setBusy('ingredient-delete')
    setErr(null)
    try{
      await api(`/ingredients/${id}`, token, { method:'DELETE' })
      await loadIngredients()
      setRecipeLines(s => s.map(l => l.ingredientId === id ? { ...l, ingredientId:'', itemNumber:'', ingredientName:'' } : l))
      if(deleteIngredientId === id) setDeleteIngredientId('')
      setConfirmDeleteId(null)
    }catch(e:any){ setErr(e.message) }
    finally{ setBusy(null) }
  }



  const totalBatchKg = useMemo(() => {
    return recipeLines.reduce((acc, l) => acc + parseKg(l.quantityKg), 0)
  }, [recipeLines])

  const canSaveRecipe = useMemo(() => {
    const valid = recipeLines.filter(l => l.ingredientId && parseKg(l.quantityKg) > 0)
    return valid.length > 0 && busy === null
  }, [recipeLines, busy])

  return (
    <>
      <Modal
        open={!!newIngPrompt}
        title="Register new ingredient?"
        subtitle="This will add the ingredient to master data (all products). Item # must be unique."
        actions={
          <>
            <button className="btn" disabled={busy!==null} onClick={() => setNewIngPrompt(null)}>No</button>
            <button className="primary" disabled={busy!==null || !(newIngPrompt?.code?.trim()) || !(newIngPrompt?.name?.trim())} onClick={confirmCreateIngredient}>Yes, register</button>
          </>
        }
      >
        <div style={{ display:'grid', gap: 10 }}>
          <div><b>Item #:</b> {newIngPrompt?.code}</div>
          <div>
            <div style={{ fontSize: 12, color:'var(--muted)', marginBottom: 6 }}>Ingredient name</div>
            <input
              value={newIngPrompt?.name ?? ''}
              onChange={e => setNewIngPrompt(s => s ? ({ ...s, name: e.target.value }) : s)}
              placeholder="Required"
              style={inp()}
            />
          </div>
          <div style={{ fontSize: 12, color:'var(--muted)' }}>Default unit: kg</div>
          {err && <div style={{ color:'#b91c1c', fontSize: 12 }}>{err}</div>}
        </div>
      </Modal>

      <Modal
        open={!!confirmDeleteId}
        title="Delete ingredient?"
        subtitle="This deletes the ingredient from master data. It will fail if used by any recipe."
        actions={
          <>
            <button className="btn" disabled={busy!==null} onClick={() => setConfirmDeleteId(null)}>No</button>
            <button className="danger" disabled={busy!==null} onClick={() => confirmDeleteId && deleteIngredient(confirmDeleteId)}>Yes, delete</button>
          </>
        }
      >
        <div>
          {confirmDeleteId
            ? (() => {
                const ing = ingredients.find(i => i.id === confirmDeleteId)
                return ing ? <div><b>{ing.itemNumber} — {ing.name}</b></div> : <div>Selected ingredient</div>
              })()
            : null}
        </div>
      </Modal>

      <Modal
        open={createModal}
        title="Create product"
        subtitle="Fast setup: code + name now, details later. No typing beyond essentials."
        actions={
          <>
            <button onClick={() => setCreateModal(false)}>Cancel</button>
            <button className="primary" disabled={busy!==null || !newCode.trim() || !newName.trim()} onClick={confirmCreate}>Create</button>
          </>
        }
      >
        <div style={{ display:'grid', gap: 10 }}>
          <label style={{ fontSize: 12, color:'var(--muted)' }}>Product code</label>
          <input value={newCode} onChange={e => setNewCode(e.target.value)} style={{ padding: 10, borderRadius: 12, border: '1px solid var(--border)' }} />
          <label style={{ fontSize: 12, color:'var(--muted)' }}>Product name</label>
          <input value={newName} onChange={e => setNewName(e.target.value)} style={{ padding: 10, borderRadius: 12, border: '1px solid var(--border)' }} />
          <div style={{ fontSize: 12, color:'var(--muted)' }}>Tip: use Copy-from later to speed up master data completion.</div>
        </div>
      </Modal>

<div className="grid">
      <div className="card">
        <div className="row" style={{ justifyContent:'space-between' }}>
          <div>
            <div style={{ fontSize: 12, color:'var(--muted)' }}>Products</div>
            <div style={{ fontSize: 18, fontWeight: 800 }}>Master Data</div>
          </div>
          <div className="actions">
            <button className="primary" disabled={busy!==null} onClick={create}>+ Create</button>
            <select value={selected} onChange={e => setSelected(e.target.value)} style={{ padding: 10, borderRadius: 12, border:'1px solid var(--border)' }}>
              {products.map(p => <option key={p.id} value={p.id}>{p.code} — {p.name}</option>)}
            </select>
          </div>
        </div>

        {err && <div style={{ marginTop: 10, color:'#b91c1c', fontSize: 12 }}>{err}</div>}

        {current && (
          <div style={{ marginTop: 12 }} className="split">
            <div className="card">
              <div className="cardHeader"><h2>Readiness Gate</h2><span className="badge">No run until published</span></div>
              <div className="row" style={{ justifyContent:'space-between' }}>
                <div className="badge">Published: {current.publishedAtUtc ? 'YES' : 'NO'}</div>
                <button className="primary" disabled={!readiness?.canPublish || busy!==null} onClick={publish}>Publish</button>
              </div>
              <div style={{ marginTop: 10, fontSize: 12, color:'var(--muted)' }}>Missing:</div>
              <ul style={{ margin: 0, paddingLeft: 18, fontSize: 12 }}>
                {(readiness?.missing ?? []).map(m => <li key={m}>{m}</li>)}
                {(readiness?.missing?.length ?? 0) === 0 && <li>Nothing — product is ready.</li>}
              </ul>
            </div>

            <div className="card">
              <div className="cardHeader"><h2>Basics</h2><span className="badge">Min typing</span></div>
              <div className="row">
                <Field label="Name" value={edit.name ?? current.name} onChange={v => setEdit((s:any)=>({ ...s, name:v }))} />
                <Field label="Proofing min (min)" type="number" value={edit.proofingMinMinutes ?? current.proofingMinMinutes ?? 40} onChange={v => setEdit((s:any)=>({ ...s, proofingMinMinutes:v }))} />
                <Field label="Proofing max (min)" type="number" value={edit.proofingMaxMinutes ?? current.proofingMaxMinutes ?? 70} onChange={v => setEdit((s:any)=>({ ...s, proofingMaxMinutes:v }))} />
                <Field label="Nominal weight P3 (g)" type="number" value={edit.nominalUnitWeightG_P3 ?? current.nominalUnitWeightG_P3 ?? 500} onChange={v => setEdit((s:any)=>({ ...s, nominalUnitWeightG_P3:v }))} />
                <Field label="Cost per unit" type="number" value={edit.costPerUnit ?? 1.2} onChange={v => setEdit((s:any)=>({ ...s, costPerUnit:v }))} />
                <Field label="Ideal cycle (sec)" type="number" value={edit.idealCycleTimeSec ?? 2} onChange={v => setEdit((s:any)=>({ ...s, idealCycleTimeSec:v }))} />
              </div>
              <div className="actions" style={{ marginTop: 10 }}>
                <button className="primary" disabled={busy!==null} onClick={saveBasics}>Save Basics</button>
              </div>
            </div>
          </div>
        )}

        {current && (
          <div style={{ marginTop: 12 }} className="grid">
            <div className="card">
              <div className="cardHeader"><h2>Densities (g/cm³)</h2><span className="badge">Used for estimated weight</span></div>
              <div className="row">
                <Field label="P1" type="number" value={dens.densityP1} onChange={v => setDens(s => ({ ...s, densityP1: Number(v) }))} />
                <Field label="P2" type="number" value={dens.densityP2} onChange={v => setDens(s => ({ ...s, densityP2: Number(v) }))} />
                <Field label="P3" type="number" value={dens.densityP3} onChange={v => setDens(s => ({ ...s, densityP3: Number(v) }))} />
              </div>
              <div className="actions" style={{ marginTop: 10 }}>
                <button className="primary" disabled={busy!==null} onClick={saveDens}>Save Densities</button>
              </div>
            </div>

            <div className="card">
              <div className="cardHeader"><h2>Tolerances (P1/P2/P3) incl. weight</h2><span className="badge">Gate for production</span></div>
              {tols.map((t, idx) => (
                <div key={t.point} style={{ border:'1px solid var(--border)', borderRadius:14, padding:10, marginBottom:10 }}>
                  <div className="row" style={{ justifyContent:'space-between' }}>
                    <div style={{ fontWeight:800 }}>{t.point}</div>
                    <span className="badge">mm / mm³ / g</span>
                  </div>
                  <div className="row" style={{ marginTop: 8 }}>
                    <Field label="W min" type="number" value={t.widthMinMm ?? ''} onChange={v => updTol(idx,'widthMinMm',v)} />
                    <Field label="W max" type="number" value={t.widthMaxMm ?? ''} onChange={v => updTol(idx,'widthMaxMm',v)} />
                    <Field label="L min" type="number" value={t.lengthMinMm ?? ''} onChange={v => updTol(idx,'lengthMinMm',v)} />
                    <Field label="L max" type="number" value={t.lengthMaxMm ?? ''} onChange={v => updTol(idx,'lengthMaxMm',v)} />
                    <Field label="H min" type="number" value={t.heightMinMm ?? ''} onChange={v => updTol(idx,'heightMinMm',v)} />
                    <Field label="H max" type="number" value={t.heightMaxMm ?? ''} onChange={v => updTol(idx,'heightMaxMm',v)} />
                  </div>
                  <div className="row" style={{ marginTop: 8 }}>
                    <Field label="Vol min" type="number" value={t.volumeMinMm3 ?? ''} onChange={v => updTol(idx,'volumeMinMm3',v)} />
                    <Field label="Vol max" type="number" value={t.volumeMaxMm3 ?? ''} onChange={v => updTol(idx,'volumeMaxMm3',v)} />
                    <Field label="Weight min" type="number" value={t.weightMinG ?? ''} onChange={v => updTol(idx,'weightMinG',v)} />
                    <Field label="Weight max" type="number" value={t.weightMaxG ?? ''} onChange={v => updTol(idx,'weightMaxG',v)} />
                  </div>
                </div>
              ))}
              <div className="actions">
                <button className="primary" disabled={busy!==null} onClick={saveTols}>Save Tolerances</button>
              </div>
            </div>

            <div className="card">
              <div className="cardHeader"><h2>Recipe (Ingredients per batch)</h2><span className="badge">kg • audit-ready versioning</span></div>
              <div className="row" style={{ justifyContent:'space-between', alignItems:'center', marginBottom: 8 }}>
                <Pill tone="info">Version: {recipeVersion ?? '—'}</Pill>
                <div className="actions">
                  <button className="btn" disabled={!current || busy!==null} onClick={() => current && loadRecipe(current.id)}>Reload</button>
                  <button className="primary" disabled={!canSaveRecipe} onClick={saveRecipe}>Save Recipe</button>
                </div>
              </div>

              <datalist id="ingredientOptions">
  {ingredients.map(i => (
    <option key={i.id} value={String(i.itemNumber)} label={i.name} />
  ))}
</datalist>

              <table className="table">
                <thead>
                  <tr><th style={{ width: 160 }}>Item #</th><th>Ingredient name</th><th style={{ width: 180 }}>Weight (kg)</th><th style={{ width: 90 }}></th></tr>
                </thead>
                <tbody>
                  {recipeLines.map((l, idx) => (
                                        <tr key={l.rowId}>
                                          <td>
                                            <input
                                              list="ingredientOptions"
                                              value={l.itemNumber}
                                              placeholder="Item #"
                                              inputMode="numeric"
                                              pattern="[0-9]*"
                                              onChange={e => setLineItemNumberFromInput(idx, onlyDigits(e.target.value))}
                                              onBlur={e => setLineItemNumberFromInput(idx, e.target.value)}
                                              onKeyDown={e => {
                                                if(e.key === 'Enter'){
                                                  e.preventDefault()
                                                  requestCreateIngredient(idx)
                                                }
                                              }}
                                              style={inp()}
                                            />
                                          </td>
                                          <td>
                                            <input
                                              value={l.ingredientName}
                                              placeholder="Ingredient name"
                                              onChange={e => updRecipeLine(idx, 'ingredientName', e.target.value)}
                                              style={inp()}
                                              disabled={!!l.ingredientId}
                                            />
                                          </td>
                                          <td>
                                            <input type="number" step="0.001" value={l.quantityKg} onChange={e => updRecipeLine(idx, 'quantityKg', e.target.value)} style={inp()} />
                                          </td>
                                          <td>
                                            <button className="btn" onClick={() => removeRecipeLine(idx)} disabled={busy!==null}>Remove</button>
                                          </td>
                                        </tr>
                  ))}
                </tbody>
              </table>

              <div className="row" style={{ justifyContent:'space-between', marginTop: 10, alignItems:'center' }}>
                <button className="btn" disabled={busy!==null} onClick={addRecipeLine}>+ Add ingredient</button>
                <Pill tone="success">Total: {totalBatchKg.toFixed(3)} kg / batch</Pill>
              </div>
              <div style={{ marginTop: 8, fontSize: 12, color:'var(--muted)' }}>Tip: set weights in kg. Saving creates a new recipe version for audit traceability.</div>

              <div className="row" style={{ gap: 10, alignItems:'end', marginTop: 12 }}>
                <div style={{ flex: 1 }}>
                  <div style={{ fontSize: 12, color: 'var(--muted)', marginBottom: 6 }}>Delete ingredient (global)</div>
                  <select value={deleteIngredientId} onChange={e => setDeleteIngredientId(e.target.value)} style={inp()}>
                    <option value="">Select…</option>
                    {ingredients.map(i => (
                      <option key={i.id} value={i.id}>{i.itemNumber} — {i.name}</option>
                    ))}
                  </select>
                </div>
                <button className="danger" disabled={!deleteIngredientId || busy!==null} onClick={() => setConfirmDeleteId(deleteIngredientId)}>Delete</button>
              </div>
            </div>

            <div className="card">
              <div className="cardHeader"><h2>Segments 1..4 (length & target speed)</h2><span className="badge">Used for OEE per segment</span></div>
              <table className="table">
                <thead>
                  <tr><th>Segment</th><th>Length (m)</th><th>Target speed (m/s)</th></tr>
                </thead>
                <tbody>
                  {segs.map((s, idx) => (
                    <tr key={s.segmentId}>
                      <td>{s.segmentId}</td>
                      <td><input value={s.lengthM} onChange={e => updSeg(idx,'lengthM',e.target.value)} style={inp()} /></td>
                      <td><input value={s.targetSpeedMps} onChange={e => updSeg(idx,'targetSpeedMps',e.target.value)} style={inp()} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <div className="actions">
                <button className="primary" disabled={busy!==null} onClick={saveSegs}>Save Segments</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
    </>
  )

  function updTol(i: number, key: keyof Tolerance, v: any){
    setTols(s => {
      const copy = [...s]
      ;(copy[i] as any)[key] = v === '' ? null : Number(v)
      return copy
    })
  }

  function updSeg(i: number, key: keyof Segment, v: any){
    setSegs(s => {
      const copy = [...s]
      ;(copy[i] as any)[key] = Number(v)
      return copy
    })
  }

  function addRecipeLine(){
    setRecipeLines(s => [...s, { rowId: uid(), ingredientId:'', itemNumber:'', ingredientName:'', quantityKg:'' }])
  }

  function removeRecipeLine(idx: number){
    setRecipeLines(s => s.filter((_, i) => i !== idx))
  }

  function updRecipeLine(idx: number, key: keyof RecipeLineEdit, v: string){
    setRecipeLines(s => {
      const copy = [...s]
      ;(copy[idx] as any)[key] = v
      return copy
    })
  }
}

function Field({ label, value, onChange, type='text' }:{ label:string, value:any, onChange:(v:any)=>void, type?:string }){
  return (
    <div style={{ minWidth: 170 }}>
      <div style={{ fontSize: 12, color: 'var(--muted)' }}>{label}</div>
      <input type={type} value={value} onChange={e => onChange(e.target.value)} style={inp()} />
    </div>
  )
}

function inp(): React.CSSProperties {
  return { width:'100%', padding:10, borderRadius:12, border:'1px solid var(--border)' }
}
