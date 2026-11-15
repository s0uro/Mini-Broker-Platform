import type { CSSProperties } from 'react'

/** A shimmering placeholder block shown while data loads. */
export function Skeleton({ width = '100%', height = 14, radius = 6, style }: {
  width?: number | string
  height?: number | string
  radius?: number
  style?: CSSProperties
}) {
  return <span className="skeleton" aria-hidden style={{ width, height, borderRadius: radius, ...style }} />
}

export function SkeletonStats({ count = 4 }: { count?: number }) {
  return (
    <section className="summary" aria-busy="true" aria-label="Loading account summary">
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="stat">
          <Skeleton width="45%" height={11} />
          <Skeleton width="70%" height={24} style={{ marginTop: 8 }} />
        </div>
      ))}
    </section>
  )
}

export function SkeletonRows({ rows = 5, cols = 4 }: { rows?: number; cols?: number }) {
  return (
    <div className="skeleton-rows" aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }, (_, r) => (
        <div key={r} className="skeleton-row" style={{ gridTemplateColumns: `1.4fr repeat(${cols - 1}, 1fr)` }}>
          {Array.from({ length: cols }, (_, c) => (
            <Skeleton key={c} width={c === 0 ? '70%' : '55%'} height={12} style={c > 0 ? { justifySelf: 'end' } : undefined} />
          ))}
        </div>
      ))}
    </div>
  )
}

export function SkeletonChart() {
  return (
    <div className="skeleton-chart" aria-busy="true" aria-label="Loading chart">
      <Skeleton height="100%" radius={8} />
    </div>
  )
}

export function SkeletonPanel({ title, rows = 5, cols = 4 }: { title?: string; rows?: number; cols?: number }) {
  return (
    <section className="panel">
      {title ? <h2>{title}</h2> : <Skeleton width={120} height={16} />}
      <SkeletonRows rows={rows} cols={cols} />
    </section>
  )
}

export function SkeletonForm() {
  return (
    <section className="panel" aria-busy="true">
      <Skeleton width={110} height={16} />
      <Skeleton height={36} radius={8} />
      <Skeleton width="30%" height={11} />
      <Skeleton height={36} radius={8} />
      <Skeleton width="30%" height={11} />
      <Skeleton height={36} radius={8} />
      <Skeleton height={42} radius={8} style={{ marginTop: 8 }} />
    </section>
  )
}

/** Full-page placeholder while the first account load is in flight. */
export function DashboardSkeleton() {
  return (
    <>
      <div className="account-title">
        <Skeleton width={180} height={26} />
      </div>
      <SkeletonStats />
      <div className="grid">
        <div className="col">
          <SkeletonPanel rows={8} cols={3} />
        </div>
        <div className="col">
          <section className="panel">
            <Skeleton width={160} height={20} />
            <SkeletonChart />
          </section>
          <SkeletonPanel rows={4} cols={5} />
        </div>
        <div className="col">
          <SkeletonForm />
        </div>
      </div>
    </>
  )
}
