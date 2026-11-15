import type { Account } from '../api'
import { fmtMoney, fmtPct, fmtSignedMoney, pnlClass } from '../format'

interface Props {
  account: Account
  prices: Map<string, number>
}

export function Summary({ account, prices }: Props) {
  let marketValue = 0
  let costBasis = 0
  for (const p of account.positions) {
    marketValue += p.quantity * (prices.get(p.symbol) ?? p.averagePrice)
    costBasis += p.quantity * p.averagePrice
  }
  const pnl = marketValue - costBasis
  const equity = account.cashBalance + marketValue

  return (
    <section className="summary">
      <Stat label="Total equity" value={fmtMoney(equity)} emphasis />
      <Stat
        label="Available cash"
        value={fmtMoney(account.availableCash)}
        sub={account.reservedCash > 0 ? `${fmtMoney(account.reservedCash)} reserved for orders` : undefined}
      />
      <Stat label="Holdings" value={fmtMoney(marketValue)} />
      <Stat
        label="Unrealized P&L"
        value={fmtSignedMoney(pnl)}
        sub={costBasis > 0 ? fmtPct(pnl / costBasis) : undefined}
        className={pnlClass(pnl)}
      />
    </section>
  )
}

function Stat({
  label,
  value,
  sub,
  emphasis,
  className = '',
}: {
  label: string
  value: string
  sub?: string
  emphasis?: boolean
  className?: string
}) {
  return (
    <div className={`stat ${emphasis ? 'stat-main' : ''}`}>
      <div className="stat-label">{label}</div>
      <div className={`stat-value num ${className}`}>{value}</div>
      {sub && <div className={`stat-sub num ${className || 'muted'}`}>{sub}</div>}
    </div>
  )
}
