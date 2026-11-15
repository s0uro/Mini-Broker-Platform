// Typed client for the MiniBroker API. Shapes mirror src/MiniBroker.Api/Contracts.cs.

export type OrderSide = 'Buy' | 'Sell'
export type OrderType = 'Market' | 'Limit'
export type OrderStatus = 'Pending' | 'Filled' | 'Rejected' | 'Cancelled'
export type TransactionType = 'Deposit' | 'Withdrawal' | 'Buy' | 'Sell'
export type HistoryRange = '15m' | '1h' | '1d'

export interface User {
  id: string
  email: string
  displayName: string
}

export interface AuthSession {
  token: string
  expiresAt: string
  user: User
}

export interface Position {
  symbol: string
  quantity: number
  reservedQuantity: number
  averagePrice: number
}

export interface Account {
  id: string
  name: string
  cashBalance: number
  reservedCash: number
  availableCash: number
  createdAt: string
  positions: Position[]
}

export interface Instrument {
  symbol: string
  name: string
  lastPrice: number
  /** Fraction vs. 24h ago (0.012 = +1.2%), null when there is no history yet. */
  changePercent: number | null
}

export interface PricePoint {
  timestamp: string
  price: number
}

export interface Order {
  id: string
  accountId: string
  symbol: string
  type: OrderType
  side: OrderSide
  quantity: number
  limitPrice: number | null
  executedPrice: number | null
  status: OrderStatus
  rejectionReason: string | null
  createdAt: string
  closedAt: string | null
}

export interface Transaction {
  id: string
  type: TransactionType
  amount: number
  balanceAfter: number
  symbol: string | null
  quantity: number | null
  price: number | null
  orderId: string | null
  createdAt: string
}

export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

// In production the frontend may be hosted separately from the API; set VITE_API_BASE_URL at build time.
const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

let authToken: string | null = null
let onUnauthorized: (() => void) | null = null

export function setAuthToken(token: string | null) {
  authToken = token
}

/** Called when an authenticated request gets 401 (expired/invalid token). */
export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (authToken) headers.Authorization = `Bearer ${authToken}`

  let response: Response
  try {
    response = await fetch(`${baseUrl}${path}`, { ...init, headers: { ...headers, ...init?.headers } })
  } catch {
    throw new ApiError('Cannot reach the server. Is the API running?', 0)
  }

  if (response.status === 401 && authToken) {
    onUnauthorized?.()
    throw new ApiError('Your session has expired. Please sign in again.', 401)
  }
  if (!response.ok) {
    throw new ApiError(await readError(response), response.status)
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

async function readError(response: Response): Promise<string> {
  if (response.status === 401) return 'Incorrect email or password.'
  if (response.status === 429) return 'Too many attempts. Please wait a minute and try again.'
  const text = await response.text()
  if (!text) return `Request failed (${response.status})`
  try {
    const body: unknown = JSON.parse(text)
    if (typeof body === 'string') return body
    if (body && typeof body === 'object') {
      const problem = body as { detail?: string; title?: string }
      return problem.detail ?? problem.title ?? `Request failed (${response.status})`
    }
  } catch {
    // not JSON
  }
  return text
}

const send = <T>(method: string, path: string, body?: unknown) =>
  request<T>(path, { method, body: body === undefined ? undefined : JSON.stringify(body) })

export const api = {
  // Auth
  register: (email: string, password: string, displayName: string) =>
    send<AuthSession>('POST', '/api/auth/register', { email, password, displayName }),
  login: (email: string, password: string) => send<AuthSession>('POST', '/api/auth/login', { email, password }),

  // Market data
  listInstruments: () => request<Instrument[]>('/api/instruments'),
  history: (symbol: string, range: HistoryRange) =>
    request<PricePoint[]>(`/api/instruments/${encodeURIComponent(symbol)}/history?range=${range}`),
  updatePrice: (symbol: string, price: number) =>
    send<Instrument>('PUT', `/api/instruments/${encodeURIComponent(symbol)}/price`, { price }),

  // Watchlist
  watchlist: () => request<string[]>('/api/watchlist'),
  watch: (symbol: string) => send<void>('PUT', `/api/watchlist/${encodeURIComponent(symbol)}`),
  unwatch: (symbol: string) => send<void>('DELETE', `/api/watchlist/${encodeURIComponent(symbol)}`),

  // Accounts
  listAccounts: () => request<Account[]>('/api/accounts'),
  openAccount: (name: string) => send<Account>('POST', '/api/accounts', { name }),
  getAccount: (id: string) => request<Account>(`/api/accounts/${id}`),
  deposit: (id: string, amount: number) => send<Account>('POST', `/api/accounts/${id}/deposits`, { amount }),
  withdraw: (id: string, amount: number) => send<Account>('POST', `/api/accounts/${id}/withdrawals`, { amount }),
  transactions: (id: string) => request<Transaction[]>(`/api/accounts/${id}/transactions?take=200`),

  // Orders
  listOrders: (accountId: string) => request<Order[]>(`/api/accounts/${accountId}/orders`),
  placeOrder: (accountId: string, order: { symbol: string; side: OrderSide; type: OrderType; quantity: number; limitPrice?: number }) =>
    send<Order>('POST', `/api/accounts/${accountId}/orders`, order),
  cancelOrder: (accountId: string, orderId: string) =>
    send<Order>('DELETE', `/api/accounts/${accountId}/orders/${orderId}`),
}
