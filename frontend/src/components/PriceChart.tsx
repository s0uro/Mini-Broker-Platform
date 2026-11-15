import { useEffect, useMemo, useRef, useState, type PointerEvent } from 'react'
import type { HistoryRange, PricePoint } from '../api'
import { fmtMoney } from '../format'
import { SkeletonChart } from './Skeleton'

const RANGES: { value: HistoryRange; label: string }[] = [
  { value: '15m', label: '15m' },
  { value: '1h', label: '1H' },
  { value: '1d', label: '1D' },
]

const HEIGHT = 260
const PAD = { top: 12, right: 64, bottom: 26, left: 8 }

interface Props {
  points: PricePoint[] | null
  range: HistoryRange
  onRangeChange: (range: HistoryRange) => void
}

/**
 * Single-series price line. Single series → no legend (the panel title names it); hover gives a
 * crosshair + tooltip; the line colour reflects direction over the selected range.
 */
export function PriceChart({ points, range, onRangeChange }: Props) {
  const wrapRef = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(640)
  const [hover, setHover] = useState<number | null>(null)

  // Track container width so the SVG renders crisp at any size.
  useEffect(() => {
    const el = wrapRef.current
    if (!el) return
    const ro = new ResizeObserver(([entry]) => setWidth(Math.max(240, Math.floor(entry.contentRect.width))))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const geom = useMemo(() => {
    if (!points || points.length < 2) return null
    const times = points.map((p) => new Date(p.timestamp).getTime())
    const prices = points.map((p) => p.price)
    const tMin = times[0]
    const tMax = times[times.length - 1]
    let pMin = Math.min(...prices)
    let pMax = Math.max(...prices)
    const pad = (pMax - pMin) * 0.08 || pMax * 0.01
    pMin -= pad
    pMax += pad

    const innerW = width - PAD.left - PAD.right
    const innerH = HEIGHT - PAD.top - PAD.bottom
    const x = (t: number) => PAD.left + ((t - tMin) / Math.max(1, tMax - tMin)) * innerW
    const y = (p: number) => PAD.top + (1 - (p - pMin) / (pMax - pMin)) * innerH

    const xs = times.map(x)
    const ys = prices.map(y)
    const line = xs.map((xv, i) => `${i === 0 ? 'M' : 'L'}${xv.toFixed(1)},${ys[i].toFixed(1)}`).join(' ')
    const area = `${line} L${xs[xs.length - 1].toFixed(1)},${PAD.top + innerH} L${xs[0].toFixed(1)},${PAD.top + innerH} Z`

    const ticks = niceTicks(pMin, pMax, 4).map((v) => ({ v, y: y(v) }))
    const timeTicks = [0, 0.25, 0.5, 0.75, 1].map((f) => {
      const t = tMin + f * (tMax - tMin)
      return { x: x(t), label: fmtAxisTime(t, range) }
    })
    const up = prices[prices.length - 1] >= prices[0]
    return { xs, ys, line, area, ticks, timeTicks, innerH, up, prices, times }
  }, [points, width, range])

  function onMove(e: PointerEvent<SVGSVGElement>) {
    if (!geom) return
    const rect = e.currentTarget.getBoundingClientRect()
    const px = e.clientX - rect.left
    // nearest point by x (xs is sorted)
    let lo = 0
    let hi = geom.xs.length - 1
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1
      if (geom.xs[mid] < px) lo = mid
      else hi = mid
    }
    setHover(Math.abs(geom.xs[lo] - px) < Math.abs(geom.xs[hi] - px) ? lo : hi)
  }

  const tone = geom?.up ? 'gain' : 'loss'
  const h = hover !== null && geom ? hover : null

  return (
    <div className="chart">
      <div className="chart-toolbar">
        <div className="segmented compact" role="tablist" aria-label="Chart range">
          {RANGES.map((r) => (
            <button
              key={r.value}
              role="tab"
              aria-selected={range === r.value}
              className={range === r.value ? 'active' : ''}
              onClick={() => onRangeChange(r.value)}
            >
              {r.label}
            </button>
          ))}
        </div>
      </div>

      <div ref={wrapRef} className="chart-canvas">
        {points === null ? (
          <SkeletonChart />
        ) : !geom ? (
          <div className="chart-empty muted">Not enough price history yet — it fills in as prices move.</div>
        ) : (
          <>
            <svg
              width={width}
              height={HEIGHT}
              role="img"
              aria-label={`Price chart, ${fmtMoney(geom.prices[0])} to ${fmtMoney(geom.prices[geom.prices.length - 1])}`}
              onPointerMove={onMove}
              onPointerLeave={() => setHover(null)}
            >
              <defs>
                <linearGradient id="chart-fill" x1="0" x2="0" y1="0" y2="1">
                  <stop offset="0%" className={`stop-${tone}`} stopOpacity="0.22" />
                  <stop offset="100%" className={`stop-${tone}`} stopOpacity="0" />
                </linearGradient>
              </defs>

              {geom.ticks.map((t) => (
                <g key={t.v}>
                  <line className="grid-line" x1={PAD.left} x2={width - PAD.right} y1={t.y} y2={t.y} />
                  <text className="axis-label" x={width - PAD.right + 8} y={t.y + 4}>
                    {fmtAxisPrice(t.v)}
                  </text>
                </g>
              ))}
              {geom.timeTicks.map((t, i) => (
                <text
                  key={i}
                  className="axis-label"
                  x={t.x}
                  y={HEIGHT - 6}
                  textAnchor={i === 0 ? 'start' : i === geom.timeTicks.length - 1 ? 'end' : 'middle'}
                >
                  {t.label}
                </text>
              ))}

              <path d={geom.area} fill="url(#chart-fill)" />
              <path d={geom.line} className={`chart-line ${tone}`} />

              {h !== null && (
                <g className="crosshair">
                  <line x1={geom.xs[h]} x2={geom.xs[h]} y1={PAD.top} y2={PAD.top + geom.innerH} />
                  <circle cx={geom.xs[h]} cy={geom.ys[h]} r={4.5} className={`dot ${tone}`} />
                </g>
              )}
            </svg>

            {h !== null && (
              <div
                className="chart-tooltip"
                style={{
                  left: Math.min(Math.max(geom.xs[h], 70), width - PAD.right - 70),
                  top: Math.max(4, geom.ys[h] - 58),
                }}
              >
                <div className="num tooltip-price">{fmtMoney(geom.prices[h])}</div>
                <div className="muted small">{new Date(geom.times[h]).toLocaleString()}</div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  )
}

function niceTicks(min: number, max: number, count: number): number[] {
  const span = max - min
  if (span <= 0) return [min]
  const raw = span / count
  const mag = 10 ** Math.floor(Math.log10(raw))
  const step = [1, 2, 2.5, 5, 10].map((m) => m * mag).find((s) => s >= raw) ?? raw
  const start = Math.ceil(min / step) * step
  const out: number[] = []
  for (let v = start; v <= max + 1e-9; v += step) out.push(Number(v.toFixed(6)))
  return out
}

const fmtAxisPrice = (v: number) => (v >= 1000 ? v.toFixed(0) : v.toFixed(2))

function fmtAxisTime(t: number, range: HistoryRange) {
  const d = new Date(t)
  return range === '1d'
    ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: range === '15m' ? '2-digit' : undefined })
}
