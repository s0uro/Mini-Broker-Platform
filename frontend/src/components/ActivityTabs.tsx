import { useState } from 'react'
import type { Order, Position, Transaction } from '../api'
import { fmtDate, fmtMoney, fmtPct, fmtQty, fmtSignedMoney, pnlClass } from '../format'
import { SkeletonRows } from './Skeleton'

type Tab = 'positions' | 'open' | 'orders' | 'transactions'

interface Props {
  positions: Position[]
  prices: Map<string, number>
  orders: Order[] | null
  transactions: Transaction[] | null
  onSelectSymbol: (symbol: string) => void
  onCancelOrder: (order: Order) => Promise<void>
}

export function ActivityTabs({ positions, prices, orders, transactions, onSelectSymbol, onCancelOrder }: Props) {
  const [tab, setTab] = useState<Tab>('positions')
  const openOrders = orders?.filter((o) => o.status === 'Pending') ?? null

  const tabs: { id: Tab; label: string; count?: number }[] = [
    { id: 'positions', label: 'Positions', count: positions.length },
    { id: 'open', label: 'Open orders', count: openOrders?.length },
    { id: 'orders', label: 'Order history' },
    { id: 'transactions', label: 'Transactions' },
  ]

  return (
    <section className="panel activity">
      <div className="tabs" role="tablist" aria-label="Account activity">
        {tabs.map((t) => (
          <button key={t.id} role="tab" aria-selected={tab === t.id} className={tab === t.id ? 'active' : ''} onClick={() => setTab(t.id)}>
            {t.label}
            {!!t.count && <span className="count">{t.count}</span>}
          </button>
        ))}
      </div>

      <div className="table-scroll">
        {tab === 'positions' && <PositionsTable positions={positions} prices={prices} onSelect={onSelectSymbol} />}
        {tab === 'open' && <OpenOrdersTable orders={openOrders} onCancel={onCancelOrder} />}
        {tab === 'orders' && <OrdersTable orders={orders} />}
        {tab === 'transactions' && <TransactionsTable transactions={transactions} />}
      </div>
    </section>
  )
}

function PositionsTable({ positions, prices, onSelect }: { positions: Position[]; prices: Map<string, number>; onSelect: (s: string) => void }) {
  if (positions.length === 0) return <p className="empty">No positions yet. Buy something from the trade panel.</p>
  return (
    <table className="table">
      <thead>
        <tr>
          <th>Symbol</th>
          <th className="right">Qty</th>
          <th className="right">Avg cost</th>
          <th className="right">Last</th>
          <th className="right">Value</th>
          <th className="right">P&L</th>
        </tr>
      </thead>
      <tbody>
        {positions.map((p) => {
          const last = prices.get(p.symbol) ?? p.averagePrice
          const value = p.quantity * last
          const pnl = value - p.quantity * p.averagePrice
          const pct = p.averagePrice > 0 ? last / p.averagePrice - 1 : 0
          return (
            <tr key={p.symbol} className="clickable" onClick={() => onSelect(p.symbol)}>
              <td className="symbol">{p.symbol}</td>
              <td className="right num">
                {fmtQty(p.quantity)}
                {p.reservedQuantity > 0 && <div className="muted small">{fmtQty(p.reservedQuantity)} reserved</div>}
              </td>
              <td className="right num">{fmtMoney(p.averagePrice)}</td>
              <td className="right num">{fmtMoney(last)}</td>
              <td className="right num">{fmtMoney(value)}</td>
              <td className={`right num ${pnlClass(pnl)}`}>
                {fmtSignedMoney(pnl)}
                <div className="small">{fmtPct(pct)}</div>
              </td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}

function OpenOrdersTable({ orders, onCancel }: { orders: Order[] | null; onCancel: (o: Order) => Promise<void> }) {
  const [cancelling, setCancelling] = useState<string | null>(null)
  if (orders === null) return <SkeletonRows rows={3} cols={5} />
  if (orders.length === 0) return <p className="empty">No open orders. Limit orders wait here until their price is reached.</p>
  return (
    <table className="table">
      <thead>
        <tr>
          <th>Placed</th>
          <th>Side</th>
          <th>Symbol</th>
          <th className="right">Qty</th>
          <th className="right">Limit</th>
          <th className="right" aria-label="Actions" />
        </tr>
      </thead>
      <tbody>
        {orders.map((o) => (
          <tr key={o.id}>
            <td className="muted small nowrap">{fmtDate(o.createdAt)}</td>
            <td className={o.side === 'Buy' ? 'gain' : 'loss'}>{o.side}</td>
            <td className="symbol">{o.symbol}</td>
            <td className="right num">{fmtQty(o.quantity)}</td>
            <td className="right num">{o.limitPrice === null ? '—' : fmtMoney(o.limitPrice)}</td>
            <td className="right">
              <button
                className="btn small ghost danger"
                disabled={cancelling === o.id}
                onClick={async () => {
                  setCancelling(o.id)
                  try {
                    await onCancel(o)
                  } finally {
                    setCancelling(null)
                  }
                }}
              >
                {cancelling === o.id ? 'Cancelling…' : 'Cancel'}
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function OrdersTable({ orders }: { orders: Order[] | null }) {
  if (orders === null) return <SkeletonRows rows={5} cols={6} />
  if (orders.length === 0) return <p className="empty">No orders yet.</p>
  return (
    <table className="table">
      <thead>
        <tr>
          <th>Time</th>
          <th>Side</th>
          <th>Symbol</th>
          <th>Type</th>
          <th className="right">Qty</th>
          <th className="right">Price</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>
        {orders.map((o) => (
          <tr key={o.id}>
            <td className="muted small nowrap">{fmtDate(o.createdAt)}</td>
            <td className={o.side === 'Buy' ? 'gain' : 'loss'}>{o.side}</td>
            <td className="symbol">{o.symbol}</td>
            <td className="muted small">
              {o.type}
              {o.limitPrice !== null && ` @ ${fmtMoney(o.limitPrice)}`}
            </td>
            <td className="right num">{fmtQty(o.quantity)}</td>
            <td className="right num">{o.executedPrice === null ? '—' : fmtMoney(o.executedPrice)}</td>
            <td>
              <span className={`badge ${o.status.toLowerCase()}`} title={o.rejectionReason ?? undefined}>
                {o.status}
              </span>
              {o.rejectionReason && <div className="muted small">{o.rejectionReason}</div>}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

const TX_LABEL: Record<Transaction['type'], string> = {
  Deposit: 'Deposit',
  Withdrawal: 'Withdrawal',
  Buy: 'Buy',
  Sell: 'Sell',
}

function TransactionsTable({ transactions }: { transactions: Transaction[] | null }) {
  if (transactions === null) return <SkeletonRows rows={6} cols={4} />
  if (transactions.length === 0) return <p className="empty">No transactions yet. Deposit some cash to get started.</p>
  return (
    <table className="table">
      <thead>
        <tr>
          <th>Time</th>
          <th>Type</th>
          <th>Details</th>
          <th className="right">Amount</th>
          <th className="right">Balance</th>
        </tr>
      </thead>
      <tbody>
        {transactions.map((t) => (
          <tr key={t.id}>
            <td className="muted small nowrap">{fmtDate(t.createdAt)}</td>
            <td>
              <span className={`tx-type tx-${t.type.toLowerCase()}`}>{TX_LABEL[t.type]}</span>
            </td>
            <td className="small">
              {t.symbol ? (
                <>
                  <span className="symbol">{t.symbol}</span>{' '}
                  <span className="muted">
                    {fmtQty(t.quantity ?? 0)} @ {fmtMoney(t.price ?? 0)}
                  </span>
                </>
              ) : (
                <span className="muted">Cash</span>
              )}
            </td>
            <td className={`right num ${pnlClass(t.amount)}`}>{fmtSignedMoney(t.amount)}</td>
            <td className="right num muted">{fmtMoney(t.balanceAfter)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
