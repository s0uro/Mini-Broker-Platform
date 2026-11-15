import { useState, type FormEvent } from 'react'

interface Props {
  onDeposit: (amount: number) => Promise<void>
  onWithdraw: (amount: number) => Promise<void>
}

type Mode = 'deposit' | 'withdraw'

export function CashPanel({ onDeposit, onWithdraw }: Props) {
  const [mode, setMode] = useState<Mode>('deposit')
  const [amount, setAmount] = useState('')
  const [busy, setBusy] = useState(false)

  const value = Number(amount)
  const valid = amount !== '' && Number.isFinite(value) && value > 0

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!valid) return
    setBusy(true)
    try {
      await (mode === 'deposit' ? onDeposit(value) : onWithdraw(value))
      setAmount('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="panel">
      <h2>Cash</h2>
      <div className="segmented" role="tablist">
        {(['deposit', 'withdraw'] as const).map((m) => (
          <button
            key={m}
            role="tab"
            aria-selected={mode === m}
            className={mode === m ? 'active' : ''}
            onClick={() => setMode(m)}
          >
            {m === 'deposit' ? 'Deposit' : 'Withdraw'}
          </button>
        ))}
      </div>
      <form onSubmit={submit} className="stack">
        <label className="field">
          <span>Amount (USD)</span>
          <input
            type="number"
            inputMode="decimal"
            min="0"
            step="0.01"
            placeholder="0.00"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
        </label>
        <div className="quick">
          {[100, 1000, 10000].map((q) => (
            <button type="button" key={q} className="chip" onClick={() => setAmount(String(q))}>
              {q.toLocaleString()}
            </button>
          ))}
        </div>
        <button type="submit" className="btn primary block" disabled={!valid || busy}>
          {busy ? 'Working…' : mode === 'deposit' ? 'Deposit' : 'Withdraw'}
        </button>
      </form>
    </section>
  )
}
