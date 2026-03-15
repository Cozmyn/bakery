import React from 'react'

// Premium chart helpers (consistent tooltips across the app)

export function PremiumTooltip({
  active,
  payload,
  label,
  labelPrefix,
}: {
  active?: boolean
  payload?: any[]
  label?: any
  labelPrefix?: string
}) {
  if (!active || !payload?.length) return null

  const rows = payload
    .filter(p => p && p.value != null)
    .map(p => ({
      name: p.name ?? p.dataKey,
      value: typeof p.value === 'number' ? p.value : Number(p.value),
      color: p.color,
    }))

  return (
    <div className="chartTip">
      <div className="t">{labelPrefix ? `${labelPrefix} ${label}` : String(label)}</div>
      {rows.map((r, idx) => (
        <div key={idx} className="r">
          <span style={{ width: 10, height: 10, borderRadius: 4, background: r.color, display: 'inline-block', marginTop: 3 }} />
          <span style={{ minWidth: 90 }}>{r.name}</span>
          <span className="tabular" style={{ color: 'var(--text)', fontWeight: 900 }}>
            {Number.isFinite(r.value) ? r.value.toFixed(3) : '—'}
          </span>
        </div>
      ))}
    </div>
  )
}

export const ChartColors = {
  current: 'var(--accent)',
  avg: 'var(--accent2)',
  best: 'var(--good)',
  danger: 'var(--bad)',
  warn: 'var(--warn)',
  neutral: 'rgba(148,163,184,0.75)'
}
