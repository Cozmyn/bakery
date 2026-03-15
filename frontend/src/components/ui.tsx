import React from 'react'

export function Pill({ children, tone = 'neutral' }: { children: React.ReactNode, tone?: 'neutral'|'good'|'warn'|'bad'|'info' }){
  return <span className={`pill pill-${tone}`}>{children}</span>
}

export function UiChip({ children, onClick, selected }: { children: React.ReactNode, onClick?: ()=>void, selected?: boolean }){
  return (
    <button className={`chip ${selected ? 'chip-selected' : ''}`} onClick={onClick}>
      {children}
    </button>
  )
}

export function Modal({ open, title, subtitle, children, actions }:{
  open: boolean
  title: string
  subtitle?: string
  children: React.ReactNode
  actions?: React.ReactNode
}){
  if(!open) return null
  return (
    <div className="modalOverlay" role="dialog" aria-modal="true">
      <div className="modalCard">
        <div className="modalHeader">
          <div>
            <div className="modalTitle">{title}</div>
            {subtitle && <div className="modalSubtitle">{subtitle}</div>}
          </div>
        </div>
        <div className="modalBody">{children}</div>
        {actions && <div className="modalActions">{actions}</div>}
      </div>
    </div>
  )
}


export function Skeleton({ height = 12, width = '100%', radius = 12 }:{
  height?: number
  width?: number | string
  radius?: number
}){
  return <div className="skeleton" style={{ height, width, borderRadius: radius }} />
}

export function SkeletonCard({ lines = 3 }:{ lines?: number }){
  return (
    <div style={{ display:'grid', gap: 10 }}>
      <Skeleton height={14} width="55%" />
      {Array.from({ length: lines }).map((_,i) => (
        <Skeleton key={i} height={12} width={i===lines-1 ? '70%' : '100%'} />
      ))}
    </div>
  )
}

export function Icon({ name }:{ name: 'live'|'admin'|'logout'|'bolt'|'alert'|'factory'|'chart'|'users'|'audit' }){
  // lightweight inline icons (no dependency)
  const common = { width:18, height:18, viewBox:'0 0 24 24', fill:'none', xmlns:'http://www.w3.org/2000/svg' }
  if(name==='live') return (
    <svg {...common}><path d="M4 12h6l2-8 2 16 2-8h4" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/></svg>
  )
  if(name==='admin') return (
    <svg {...common}><path d="M12 2l7 4v6c0 5-3 9-7 10-4-1-7-5-7-10V6l7-4z" stroke="currentColor" strokeWidth="2"/></svg>
  )
  if(name==='logout') return (
    <svg {...common}><path d="M10 17l-1 0a4 4 0 0 1-4-4V7a4 4 0 0 1 4-4h1" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
    <path d="M16 7l5 5-5 5" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
    <path d="M21 12H10" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/></svg>
  )
  if(name==='bolt') return (
    <svg {...common}><path d="M13 2L3 14h8l-1 8 11-14h-8l0-6z" stroke="currentColor" strokeWidth="2" strokeLinejoin="round"/></svg>
  )
  if(name==='alert') return (
    <svg {...common}><path d="M12 9v4" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
    <path d="M12 17h.01" stroke="currentColor" strokeWidth="3" strokeLinecap="round"/>
    <path d="M10.3 3.6l-8.4 14.5A2 2 0 0 0 3.6 21h16.8a2 2 0 0 0 1.7-2.9L13.7 3.6a2 2 0 0 0-3.4 0z" stroke="currentColor" strokeWidth="2"/></svg>
  )
  if(name==='chart') return (
    <svg {...common}>
      <path d="M4 19V5" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
      <path d="M4 19h16" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
      <path d="M7 16l3-4 3 2 4-6 2 3" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
    </svg>
  )
  if(name==='users') return (
    <svg {...common}>
      <path d="M17 21v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
      <path d="M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z" stroke="currentColor" strokeWidth="2"/>
      <path d="M21 21v-2a3 3 0 0 0-2-2.82" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
      <path d="M17 3.13a4 4 0 0 1 0 7.75" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
    </svg>
  )
  if(name==='audit') return (
    <svg {...common}>
      <path d="M9 5h10" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
      <path d="M9 9h10" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
      <path d="M9 13h10" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
      <path d="M5 5h.01" stroke="currentColor" strokeWidth="3" strokeLinecap="round"/>
      <path d="M5 9h.01" stroke="currentColor" strokeWidth="3" strokeLinecap="round"/>
      <path d="M5 13h.01" stroke="currentColor" strokeWidth="3" strokeLinecap="round"/>
      <path d="M7 21h13a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-1" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
      <path d="M5 5H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h1" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
    </svg>
  )
  // default: factory
  return (
    <svg {...common}><path d="M4 21V8l8-4 8 4v13" stroke="currentColor" strokeWidth="2"/><path d="M9 21v-7h6v7" stroke="currentColor" strokeWidth="2"/></svg>
  )
}
