import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  api,
  ApiError,
  type Account,
  type AuthSession,
  type HistoryRange,
  type Instrument,
  type Order,
  type PricePoint,
  type Transaction,
} from '../api'
import { fmtMoney, fmtPct, fmtQty, pnlClass } from '../format'
import { loadJson, saveJson } from '../storage'
import { AccountBar } from './AccountBar'
import { ActivityTabs } from './ActivityTabs'
import { CashPanel } from './CashPanel'
import { Markets } from './Markets'
import { OrderTicket, type OrderDraft } from './OrderTicket'
import { PriceChart } from './PriceChart'
import { DashboardSkeleton, Skeleton, SkeletonForm } from './Skeleton'
import { Summary } from './Summary'
import type { Toast } from './Toasts'

const PRICE_POLL_MS = 3000
const ACCOUNT_POLL_MS = 4000
const CHART_POLL_MS = 5000

interface Props {
  session: AuthSession
  onLogout: () => void
  notify: (kind: Toast['kind'], text: string) => void
}

export function Dashboard({ session, onLogout, notify }: Props) {
  const activeKey = `minibroker.activeAccount.${session.user.id}`

  const [accounts, setAccounts] = useState<Account[] | null>(null)
  const [activeId, setActiveId] = useState<string | null>(() => loadJson<string | null>(activeKey, null))
  const [orders, setOrders] = useState<Order[] | null>(null)
  const [transactions, setTransactions] = useState<Transaction[] | null>(null)
  const [instruments, setInstruments] = useState<Instrument[] | null>(null)
  const [previousPrices, setPreviousPrices] = useState<Map<string, number>>(new Map())
  const [watchlist, setWatchlist] = useState<Set<string>>(new Set())
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null)
  const [range, setRange] = useState<HistoryRange>('1h')
  const [history, setHistory] = useState<PricePoint[] | null>(null)
  const [online, setOnline] = useState(true)

  const lastInstruments = useRef<Instrument[]>([])
  const pendingIds = useRef<Set<string> | null>(null)

  const reportError = useCallback(
    (err: unknown) => notify('error', err instanceof Error ? err.message : 'Something went wrong.'),
    [notify],
  )

  useEffect(() => saveJson(activeKey, activeId), [activeKey, activeId])

  // ---- Accounts list (once) ----
  useEffect(() => {
    let cancelled = false
    api
      .listAccounts()
      .then((list) => {
        if (cancelled) return
        setAccounts(list)
        setActiveId((current) => (current && list.some((a) => a.id === current) ? current : (list[0]?.id ?? null)))
      })
      .catch((err) => !cancelled && reportError(err))
    return () => {
      cancelled = true
    }
  }, [reportError])

  // ---- Market prices (poll) ----
  const refreshInstruments = useCallback(async () => {
    try {
      const list = await api.listInstruments()
      setPreviousPrices(new Map(lastInstruments.current.map((i) => [i.symbol, i.lastPrice])))
      lastInstruments.current = list
      setInstruments(list)
      setOnline(true)
    } catch (err) {
      if (!(err instanceof ApiError && err.status === 401)) setOnline(false)
    }
  }, [])

  useEffect(() => {
    void refreshInstruments()
    const timer = setInterval(() => void refreshInstruments(), PRICE_POLL_MS)
    return () => clearInterval(timer)
  }, [refreshInstruments])

  useEffect(() => {
    if (!selectedSymbol && instruments?.length) setSelectedSymbol(instruments[0].symbol)
  }, [instruments, selectedSymbol])

  // ---- Watchlist (once) ----
  useEffect(() => {
    api.watchlist().then((s) => setWatchlist(new Set(s)), reportError)
  }, [reportError])

  // ---- Active account details (poll: limit orders can fill in the background) ----
  const refreshActive = useCallback(
    async (id: string) => {
      try {
        const [acc, ords, txs] = await Promise.all([api.getAccount(id), api.listOrders(id), api.transactions(id)])
        setAccounts((list) => list?.map((a) => (a.id === acc.id ? acc : a)) ?? [acc])
        setOrders(ords)
        setTransactions(txs)

        // Announce limit orders that executed since the last refresh.
        const nowPending = new Set(ords.filter((o) => o.status === 'Pending').map((o) => o.id))
        if (pendingIds.current) {
          for (const o of ords) {
            if (pendingIds.current.has(o.id) && o.status === 'Filled') {
              notify('success', `Limit ${o.side.toLowerCase()} ${fmtQty(o.quantity)} ${o.symbol} filled at ${fmtMoney(o.executedPrice ?? 0)}.`)
            }
          }
        }
        pendingIds.current = nowPending
      } catch (err) {
        if (!(err instanceof ApiError && (err.status === 401 || err.status === 0))) reportError(err)
      }
    },
    [notify, reportError],
  )

  useEffect(() => {
    if (!activeId) return
    setOrders(null)
    setTransactions(null)
    pendingIds.current = null
    void refreshActive(activeId)
    const timer = setInterval(() => void refreshActive(activeId), ACCOUNT_POLL_MS)
    return () => clearInterval(timer)
  }, [activeId, refreshActive])

  // ---- Chart history ----
  useEffect(() => {
    if (!selectedSymbol) return
    let cancelled = false
    const load = () =>
      api.history(selectedSymbol, range).then(
        (points) => !cancelled && setHistory(points),
        () => undefined, // keep the last chart on transient errors
      )
    setHistory(null)
    void load()
    const timer = setInterval(load, CHART_POLL_MS)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [selectedSymbol, range])

  // ---- Derived ----
  const account = accounts?.find((a) => a.id === activeId) ?? null
  const prices = useMemo(() => new Map((instruments ?? []).map((i) => [i.symbol, i.lastPrice])), [instruments])
  const selected = instruments?.find((i) => i.symbol === selectedSymbol) ?? null
  const position = account?.positions.find((p) => p.symbol === selectedSymbol)
  const availableQuantity = position ? position.quantity - position.reservedQuantity : 0

  // ---- Actions ----
  async function openAccount(name: string) {
    try {
      const acc = await api.openAccount(name)
      setAccounts((list) => [...(list ?? []), acc])
      setActiveId(acc.id)
      notify('success', `Account “${acc.name}” opened.`)
    } catch (err) {
      reportError(err)
    }
  }

  async function moveCash(kind: 'deposit' | 'withdraw', amount: number) {
    if (!account) return
    try {
      const updated = kind === 'deposit' ? await api.deposit(account.id, amount) : await api.withdraw(account.id, amount)
      setAccounts((list) => list?.map((a) => (a.id === updated.id ? updated : a)) ?? null)
      notify('success', `${kind === 'deposit' ? 'Deposited' : 'Withdrew'} ${fmtMoney(amount)}.`)
      void refreshActive(account.id)
    } catch (err) {
      reportError(err)
    }
  }

  async function placeOrder(draft: OrderDraft) {
    if (!account || !selectedSymbol) return
    try {
      const order = await api.placeOrder(account.id, { symbol: selectedSymbol, ...draft })
      const what = `${draft.side} ${fmtQty(draft.quantity)} ${order.symbol}`
      if (order.status === 'Filled') notify('success', `${what} filled at ${fmtMoney(order.executedPrice ?? 0)}.`)
      else if (order.status === 'Pending') notify('success', `${what} placed — waiting for ${fmtMoney(order.limitPrice ?? 0)}.`)
      else notify('error', `Order rejected: ${order.rejectionReason ?? 'unknown reason'}`)
      await refreshActive(account.id)
    } catch (err) {
      reportError(err)
    }
  }

  async function cancelOrder(order: Order) {
    if (!account) return
    try {
      await api.cancelOrder(account.id, order.id)
      notify('success', `Cancelled ${order.side.toLowerCase()} order for ${order.symbol}.`)
      await refreshActive(account.id)
    } catch (err) {
      reportError(err)
      void refreshActive(account.id) // it may have filled meanwhile
    }
  }

  async function toggleWatch(symbol: string) {
    const watched = watchlist.has(symbol)
    // Optimistic update, rolled back on failure.
    setWatchlist((s) => {
      const next = new Set(s)
      if (watched) next.delete(symbol)
      else next.add(symbol)
      return next
    })
    try {
      await (watched ? api.unwatch(symbol) : api.watch(symbol))
    } catch (err) {
      setWatchlist((s) => {
        const next = new Set(s)
        if (watched) next.add(symbol)
        else next.delete(symbol)
        return next
      })
      reportError(err)
    }
  }

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <span className="logo" aria-hidden>◆</span>
          <span>Mini Broker</span>
          <span className={`status-dot ${online ? 'ok' : 'down'}`} title={online ? 'Connected' : 'API unreachable'} />
        </div>
        <AccountBar
          user={session.user}
          accounts={accounts}
          activeId={activeId}
          onSelect={setActiveId}
          onOpen={openAccount}
          onLogout={onLogout}
        />
      </header>

      {!online && <div className="banner">Cannot reach the server. Retrying…</div>}

      <main className="content">
        {accounts === null ? (
          <DashboardSkeleton />
        ) : !account ? (
          <FirstAccount name={session.user.displayName} onOpen={openAccount} />
        ) : (
          <>
            <div className="account-title">
              <h1>{account.name}</h1>
              <span className="muted small">Opened {new Date(account.createdAt).toLocaleDateString()}</span>
            </div>
            <Summary account={account} prices={prices} />

            <div className="grid">
              <div className="col">
                <Markets
                  instruments={instruments}
                  previousPrices={previousPrices}
                  watchlist={watchlist}
                  selected={selectedSymbol}
                  onSelect={setSelectedSymbol}
                  onToggleWatch={toggleWatch}
                />
              </div>

              <div className="col center">
                <section className="panel">
                  <div className="quote-head">
                    {selected ? (
                      <>
                        <div>
                          <div className="quote-symbol">
                            {selected.symbol}
                            <button
                              className={`star ${watchlist.has(selected.symbol) ? 'on' : ''}`}
                              aria-label={watchlist.has(selected.symbol) ? 'Remove from watchlist' : 'Add to watchlist'}
                              onClick={() => toggleWatch(selected.symbol)}
                            >
                              {watchlist.has(selected.symbol) ? '★' : '☆'}
                            </button>
                          </div>
                          <div className="muted small">{selected.name}</div>
                        </div>
                        <div className="quote-price">
                          <div className="num big">{fmtMoney(selected.lastPrice)}</div>
                          {selected.changePercent !== null && (
                            <div className={`num small ${pnlClass(selected.changePercent)}`}>{fmtPct(selected.changePercent)} 24h</div>
                          )}
                        </div>
                      </>
                    ) : (
                      <>
                        <Skeleton width={120} height={22} />
                        <Skeleton width={90} height={22} />
                      </>
                    )}
                  </div>
                  <PriceChart points={history} range={range} onRangeChange={setRange} />
                </section>

                <ActivityTabs
                  positions={account.positions}
                  prices={prices}
                  orders={orders}
                  transactions={transactions}
                  onSelectSymbol={setSelectedSymbol}
                  onCancelOrder={cancelOrder}
                />
              </div>

              <div className="col">
                {instruments === null ? (
                  <SkeletonForm />
                ) : (
                  <OrderTicket
                    instrument={selected}
                    availableCash={account.availableCash}
                    availableQuantity={availableQuantity}
                    onSubmit={placeOrder}
                  />
                )}
                <CashPanel onDeposit={(a) => moveCash('deposit', a)} onWithdraw={(a) => moveCash('withdraw', a)} />
              </div>
            </div>
          </>
        )}
      </main>
    </div>
  )
}

function FirstAccount({ name, onOpen }: { name: string; onOpen: (name: string) => Promise<void> }) {
  const [accountName, setAccountName] = useState('Main')
  const [busy, setBusy] = useState(false)
  return (
    <section className="welcome panel">
      <h1>Hi {name} 👋</h1>
      <p className="muted">Open your first trading account. You can deposit simulated cash and start trading right away.</p>
      <form
        className="inline-form"
        onSubmit={async (e) => {
          e.preventDefault()
          if (!accountName.trim()) return
          setBusy(true)
          try {
            await onOpen(accountName.trim())
          } finally {
            setBusy(false)
          }
        }}
      >
        <input aria-label="Account name" value={accountName} maxLength={100} onChange={(e) => setAccountName(e.target.value)} />
        <button className="btn primary" type="submit" disabled={busy || !accountName.trim()}>
          {busy ? 'Opening…' : 'Open account'}
        </button>
      </form>
    </section>
  )
}
