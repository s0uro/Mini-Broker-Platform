import { useMemo, useState } from 'react'
import type { Instrument } from '../api'
import { fmtMoney, fmtPct, pnlClass } from '../format'
import { SkeletonRows } from './Skeleton'

interface Props {
  instruments: Instrument[] | null
  previousPrices: Map<string, number>
  watchlist: Set<string>
  selected: string | null
  onSelect: (symbol: string) => void
  onToggleWatch: (symbol: string) => void
}

type Filter = 'all' | 'watchlist'

export function Markets({ instruments, previousPrices, watchlist, selected, onSelect, onToggleWatch }: Props) {
  const [filter, setFilter] = useState<Filter>('all')
  const [search, setSearch] = useState('')

  const rows = useMemo(() => {
    if (!instruments) return null
    const term = search.trim().toLowerCase()
    return instruments.filter(
      (i) =>
        (filter === 'all' || watchlist.has(i.symbol)) &&
        (!term || i.symbol.toLowerCase().includes(term) || i.name.toLowerCase().includes(term)),
    )
  }, [instruments, filter, search, watchlist])

  return (
    <section className="panel markets">
      <div className="panel-head">
        <h2>Markets</h2>
        <span className="live-badge">
          <span className="pulse" aria-hidden /> Live
        </span>
      </div>

      <input
        type="search"
        placeholder="Search symbol or name"
        aria-label="Search instruments"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <div className="segmented compact" role="tablist" aria-label="Filter instruments">
        <button role="tab" aria-selected={filter === 'all'} className={filter === 'all' ? 'active' : ''} onClick={() => setFilter('all')}>
          All
        </button>
        <button
          role="tab"
          aria-selected={filter === 'watchlist'}
          className={filter === 'watchlist' ? 'active' : ''}
          onClick={() => setFilter('watchlist')}
        >
          ★ Watchlist {watchlist.size > 0 && <span className="count">{watchlist.size}</span>}
        </button>
      </div>

      {rows === null ? (
        <SkeletonRows rows={9} cols={3} />
      ) : rows.length === 0 ? (
        <p className="empty">
          {filter === 'watchlist' && !search ? 'Your watchlist is empty. Tap ☆ next to a symbol to follow it.' : 'No matches.'}
        </p>
      ) : (
        <div className="market-list" role="listbox" aria-label="Instruments">
          {rows.map((i) => {
            const prev = previousPrices.get(i.symbol)
            const tick = prev === undefined || prev === i.lastPrice ? '' : i.lastPrice > prev ? 'tick-up' : 'tick-down'
            const watched = watchlist.has(i.symbol)
            return (
              <div
                key={i.symbol}
                role="option"
                aria-selected={selected === i.symbol}
                tabIndex={0}
                className={`market-row ${selected === i.symbol ? 'selected' : ''}`}
                onClick={() => onSelect(i.symbol)}
                onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && onSelect(i.symbol)}
              >
                <button
                  className={`star ${watched ? 'on' : ''}`}
                  aria-label={watched ? `Remove ${i.symbol} from watchlist` : `Add ${i.symbol} to watchlist`}
                  aria-pressed={watched}
                  onClick={(e) => {
                    e.stopPropagation()
                    onToggleWatch(i.symbol)
                  }}
                >
                  {watched ? '★' : '☆'}
                </button>
                <div className="market-name">
                  <div className="symbol">{i.symbol}</div>
                  <div className="muted small ellipsis">{i.name}</div>
                </div>
                <div className="market-price">
                  <span key={i.lastPrice} className={`num price ${tick}`}>
                    {fmtMoney(i.lastPrice)}
                  </span>
                  <span className={`num small change ${i.changePercent === null ? 'muted' : pnlClass(i.changePercent)}`}>
                    {i.changePercent === null ? '—' : fmtPct(i.changePercent)}
                  </span>
                </div>
              </div>
            )
          })}
        </div>
      )}
    </section>
  )
}
