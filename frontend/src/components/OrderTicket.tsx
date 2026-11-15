import { useEffect, useState, type FormEvent } from 'react'
import type { Instrument, OrderSide, OrderType } from '../api'
import { fmtMoney, fmtQty } from '../format'

export interface OrderDraft {
  side: OrderSide
  type: OrderType
  quantity: number
  limitPrice?: number
}

interface Props {
  instrument: Instrument | null
  availableCash: number
  availableQuantity: number
  onSubmit: (draft: OrderDraft) => Promise<void>
}

export function OrderTicket({ instrument, availableCash, availableQuantity, onSubmit }: Props) {
  const [side, setSide] = useState<OrderSide>('Buy')
  const [type, setType] = useState<OrderType>('Market')
  const [qty, setQty] = useState('')
  const [limit, setLimit] = useState('')
  const [busy, setBusy] = useState(false)

  // Start the limit field at the current price whenever the instrument changes.
  useEffect(() => {
    if (instrument) setLimit(instrument.lastPrice.toFixed(2))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only on symbol change, not every tick
  }, [instrument?.symbol])

  const quantity = Number(qty)
  const validQty = qty !== '' && Number.isFinite(quantity) && quantity > 0
  const limitPrice = Number(limit)
  const validLimit = type === 'Market' || (limit !== '' && Number.isFinite(limitPrice) && limitPrice > 0)
  const price = type === 'Limit' && validLimit ? limitPrice : (instrument?.lastPrice ?? 0)
  const estimate = validQty ? quantity * price : 0
  const maxBuy = price > 0 ? Math.floor((availableCash / price) * 1e6) / 1e6 : 0

  const willRest =
    type === 'Limit' && instrument && validLimit &&
    (side === 'Buy' ? instrument.lastPrice > limitPrice : instrument.lastPrice < limitPrice)

  const warning =
    !validQty || !instrument
      ? null
      : side === 'Buy' && estimate > availableCash
        ? 'Exceeds available cash — the order will be rejected.'
        : side === 'Sell' && quantity > availableQuantity
          ? 'Exceeds your available holdings — the order will be rejected.'
          : null

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!validQty || !validLimit || !instrument) return
    setBusy(true)
    try {
      await onSubmit({ side, type, quantity, limitPrice: type === 'Limit' ? limitPrice : undefined })
      setQty('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="panel">
      <div className="panel-head">
        <h2>Trade {instrument?.symbol ?? ''}</h2>
        {instrument && <span className="num muted">{fmtMoney(instrument.lastPrice)}</span>}
      </div>

      <div className="segmented sides">
        {(['Buy', 'Sell'] as const).map((s) => (
          <button key={s} className={side === s ? `active ${s.toLowerCase()}` : ''} aria-pressed={side === s} onClick={() => setSide(s)}>
            {s}
          </button>
        ))}
      </div>

      <form onSubmit={submit} className="stack">
        <div className="segmented compact" role="tablist" aria-label="Order type">
          {(['Market', 'Limit'] as const).map((t) => (
            <button type="button" key={t} role="tab" aria-selected={type === t} className={type === t ? 'active' : ''} onClick={() => setType(t)}>
              {t}
            </button>
          ))}
        </div>

        {type === 'Limit' && (
          <label className="field">
            <span>Limit price (USD)</span>
            <input type="number" inputMode="decimal" min="0" step="0.01" value={limit} onChange={(e) => setLimit(e.target.value)} />
            <span className="hint">
              {side === 'Buy' ? 'Buys only at this price or lower.' : 'Sells only at this price or higher.'}
            </span>
          </label>
        )}

        <label className="field">
          <span>Quantity</span>
          <input type="number" inputMode="decimal" min="0" step="any" placeholder="0" value={qty} onChange={(e) => setQty(e.target.value)} />
        </label>
        <div className="quick">
          {side === 'Buy' ? (
            <button type="button" className="chip" disabled={maxBuy <= 0} onClick={() => setQty(String(maxBuy))}>
              Max {fmtQty(maxBuy)}
            </button>
          ) : (
            <button type="button" className="chip" disabled={availableQuantity <= 0} onClick={() => setQty(String(availableQuantity))}>
              All {fmtQty(availableQuantity)}
            </button>
          )}
        </div>

        <dl className="kv">
          <dt>Estimated {side === 'Buy' ? 'cost' : 'proceeds'}</dt>
          <dd className="num">{fmtMoney(estimate)}</dd>
          <dt>{side === 'Buy' ? 'Available cash' : 'Available to sell'}</dt>
          <dd className="num">{side === 'Buy' ? fmtMoney(availableCash) : fmtQty(availableQuantity)}</dd>
        </dl>

        {warning && <p className="warn">{warning}</p>}
        {!warning && willRest && validQty && (
          <p className="info">The order will wait until the price reaches {fmtMoney(limitPrice)}.</p>
        )}

        <button
          type="submit"
          className={`btn block ${side === 'Buy' ? 'buy' : 'sell'}`}
          disabled={!validQty || !validLimit || !instrument || busy}
        >
          {busy ? 'Sending…' : `${side} ${instrument?.symbol ?? ''}${type === 'Limit' ? ' (limit)' : ''}`}
        </button>
      </form>
    </section>
  )
}
