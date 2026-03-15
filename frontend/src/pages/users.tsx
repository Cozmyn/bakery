import React, { useEffect, useMemo, useState } from 'react'
import { api } from '../components/api'
import { UiChip, Modal, Pill, SkeletonCard } from '../components/ui'

type Props = { token: string, role: string }

type UserRow = {
  id: string
  email: string
  role: 'Admin'|'Operator'
  isActive: boolean
  createdAtUtc?: string|null
  createdBy?: string|null
  updatedAtUtc?: string|null
  updatedBy?: string|null
}

export default function UsersPage({ token, role }: Props){
  const [items, setItems] = useState<UserRow[]|null>(null)
  const [err, setErr] = useState<string|null>(null)
  const [includeInactive, setIncludeInactive] = useState(true)

  const [createOpen, setCreateOpen] = useState(false)
  const [email, setEmail] = useState('')
  const [userRole, setUserRole] = useState<'Admin'|'Operator'>('Operator')
  const [password, setPassword] = useState('')
  const [createdTempPw, setCreatedTempPw] = useState<string|null>(null)

  const [editId, setEditId] = useState<string|null>(null)
  const editUser = useMemo(() => items?.find(x => x.id === editId) ?? null, [items, editId])
  const [editRole, setEditRole] = useState<'Admin'|'Operator'>('Operator')
  const [editActive, setEditActive] = useState(true)
  const [resetPw, setResetPw] = useState('')

  useEffect(() => {
    if(role !== 'Admin') return
    let alive = true
    ;(async () => {
      try{
        setErr(null)
        const qs = includeInactive ? '?includeInactive=true' : '?includeInactive=false'
        const data = await api<UserRow[]>(`/admin/users${qs}`, token)
        if(alive) setItems(data)
      }catch(e:any){
        if(alive) setErr(e?.message ?? 'Failed to load users')
      }
    })()
    return () => { alive = false }
  }, [token, role, includeInactive])

  if(role !== 'Admin'){
    return (
      <div className="page">
        <div className="pageTitle">Users</div>
        <div className="card" style={{ padding: 18 }}>
          <div style={{ color:'var(--muted)' }}>Admin access required.</div>
        </div>
      </div>
    )
  }

  async function refresh(){
    const qs = includeInactive ? '?includeInactive=true' : '?includeInactive=false'
    const data = await api<UserRow[]>(`/admin/users${qs}`, token)
    setItems(data)
  }

  async function create(){
    try{
      setErr(null)
      const res = await api<any>('/admin/users', token, {
        method:'POST',
        body: JSON.stringify({ email, role: userRole, password: password.trim() ? password : null })
      })
      setCreatedTempPw(res.temporaryPassword ?? null)
      setEmail('')
      setPassword('')
      await refresh()
    }catch(e:any){
      setErr(e?.message ?? 'Failed to create user')
    }
  }

  async function openEdit(u: UserRow){
    setEditId(u.id)
    setEditRole(u.role)
    setEditActive(u.isActive)
    setResetPw('')
  }

  async function saveEdit(){
    if(!editId) return
    try{
      setErr(null)
      await api<any>(`/admin/users/${editId}`, token, {
        method:'PUT',
        body: JSON.stringify({
          role: editRole,
          isActive: editActive,
          password: resetPw.trim() ? resetPw : null
        })
      })
      setEditId(null)
      await refresh()
    }catch(e:any){
      setErr(e?.message ?? 'Failed to update user')
    }
  }

  return (
    <div className="page">
      <div className="pageHeader">
        <div>
          <div className="pageTitle">Users</div>
          <div className="pageSubtitle">Create operators/admins, deactivate accounts, reset passwords. All changes are audit logged.</div>
        </div>
        <div style={{ display:'flex', gap: 10, alignItems:'center' }}>
          <UiChip selected={includeInactive} onClick={() => setIncludeInactive(v => !v)}>
            {includeInactive ? 'Showing inactive' : 'Active only'}
          </UiChip>
          <UiChip onClick={() => { setCreatedTempPw(null); setCreateOpen(true) }}>+ Add user</UiChip>
        </div>
      </div>

      {err && <div className="warnBox">{err}</div>}

      <div className="card">
        {!items ? (
          <SkeletonCard lines={6} />
        ) : (
          <div className="table">
            <div className="tr th">
              <div>Email</div>
              <div>Role</div>
              <div>Status</div>
              <div>Updated</div>
              <div style={{ textAlign:'right' }}>Actions</div>
            </div>
            {items.map(u => (
              <div className="tr" key={u.id}>
                <div style={{ fontWeight: 700 }}>{u.email}</div>
                <div><Pill tone={u.role==='Admin' ? 'info' : 'neutral'}>{u.role}</Pill></div>
                <div><Pill tone={u.isActive ? 'good' : 'warn'}>{u.isActive ? 'Active' : 'Inactive'}</Pill></div>
                <div style={{ color:'var(--muted)', fontSize: 12 }}>{u.updatedAtUtc ? new Date(u.updatedAtUtc).toLocaleString() : '—'}</div>
                <div style={{ textAlign:'right' }}>
                  <button className="btn btn-ghost" onClick={() => openEdit(u)}>Edit</button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      <Modal
        open={createOpen}
        title="Add user"
        subtitle="Create a new Admin or Operator. If you leave password empty, the server generates a temporary password."
        actions={
          <div style={{ display:'flex', gap: 10, justifyContent:'flex-end' }}>
            <button className="btn btn-ghost" onClick={() => setCreateOpen(false)}>Close</button>
            <button className="btn" onClick={create}>Create</button>
          </div>
        }
      >
        <div className="formGrid">
          <label className="field">
            <div className="label">Email</div>
            <input value={email} onChange={e => setEmail(e.target.value)} placeholder="operator@plant.com" />
          </label>

          <label className="field">
            <div className="label">Role</div>
            <select value={userRole} onChange={e => setUserRole(e.target.value as any)}>
              <option value="Operator">Operator</option>
              <option value="Admin">Admin</option>
            </select>
          </label>

          <label className="field">
            <div className="label">Password (optional)</div>
            <input value={password} onChange={e => setPassword(e.target.value)} placeholder="Leave empty to auto-generate" />
          </label>

          {createdTempPw && (
            <div className="infoBox">
              <div style={{ fontWeight: 800 }}>Temporary password</div>
              <div style={{ marginTop: 6, fontFamily:'var(--mono)' }}>{createdTempPw}</div>
              <div style={{ marginTop: 6, color:'var(--muted)', fontSize: 12 }}>Copy it now. It won't be shown again.</div>
            </div>
          )}
        </div>
      </Modal>

      <Modal
        open={!!editId}
        title="Edit user"
        subtitle={editUser ? editUser.email : ''}
        actions={
          <div style={{ display:'flex', gap: 10, justifyContent:'flex-end' }}>
            <button className="btn btn-ghost" onClick={() => setEditId(null)}>Cancel</button>
            <button className="btn" onClick={saveEdit}>Save</button>
          </div>
        }
      >
        {editUser && (
          <div className="formGrid">
            <label className="field">
              <div className="label">Role</div>
              <select value={editRole} onChange={e => setEditRole(e.target.value as any)}>
                <option value="Operator">Operator</option>
                <option value="Admin">Admin</option>
              </select>
            </label>

            <label className="field">
              <div className="label">Status</div>
              <select value={editActive ? 'active' : 'inactive'} onChange={e => setEditActive(e.target.value === 'active')}>
                <option value="active">Active</option>
                <option value="inactive">Inactive</option>
              </select>
            </label>

            <label className="field">
              <div className="label">Reset password (optional)</div>
              <input value={resetPw} onChange={e => setResetPw(e.target.value)} placeholder="Set a new password" />
            </label>

            <div className="hintBox">
              <div style={{ fontWeight: 700 }}>Note</div>
              <div style={{ marginTop: 4, color:'var(--muted)', fontSize: 12 }}>
                Deactivating a user blocks login immediately. All changes are stored in the audit log.
              </div>
            </div>
          </div>
        )}
      </Modal>
    </div>
  )
}