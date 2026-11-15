# Mini Broker Platform

A small trading broker: users sign up, open accounts, deposit simulated cash, and trade
instruments with market and limit orders against a live (simulated) price feed.

**Stack:** .NET 10 · ASP.NET Core minimal APIs · EF Core · SQL Server · JWT auth · React 19 + TypeScript + Vite

## Features

- **Auth** — register / sign in with JWT; every account, order and transaction is scoped to its owner
- **Accounts** — multiple accounts per user, deposits & withdrawals, full cash ledger
- **Orders** — market orders fill immediately; limit orders reserve cash/holdings and fill automatically when the price reaches the limit; pending orders can be cancelled
- **Market data** — background price simulator (Development), 24h change %, price history for charts (15m / 1h / 1d)
- **Watchlist & search** per user
- **Frontend** — live dashboard with price chart, positions & P&L, open orders, order history, transactions, and skeleton loading states

## Project layout

| Path | Purpose |
|---|---|
| `src/MiniBroker.Domain` | Entities and business rules (`Account`, `Order`, `Position`, `CashTransaction`, `User`…) — no dependencies |
| `src/MiniBroker.Infrastructure` | EF Core `BrokerDbContext`, mappings, migrations |
| `src/MiniBroker.Api` | Endpoints, JWT auth, price service + market simulator |
| `tests/MiniBroker.Tests` | Domain unit tests + API integration tests (in-memory DB) |
| `frontend/` | React + TypeScript dashboard |

## Run locally

**Backend** (uses SQL Server LocalDB from `appsettings.Development.json`; migrates and seeds on startup):

```bash
dotnet run --project src/MiniBroker.Api --launch-profile http   # http://localhost:5283
```

**Frontend** (proxies `/api` to the backend):

```bash
cd frontend
npm install
npm run dev        # http://localhost:5173
```

**Or with Docker** (SQL Server + API in containers):

```bash
cp .env.example .env          # set a strong MSSQL_SA_PASSWORD
docker compose up -d --build  # API on http://localhost:8080
VITE_API_PROXY=http://localhost:8080 npm --prefix frontend run dev
```

## Configuration

| Setting | Notes |
|---|---|
| `ConnectionStrings:BrokerDb` | SQL Server connection string |
| `Jwt:SigningKey` | ≥ 32 bytes. Development uses a `DEV-ONLY…` key; the app **refuses to start** with it outside Development — set `Jwt__SigningKey` (e.g. from Key Vault) |
| `Market:SimulatorEnabled` | Random-walk price feed; on in Development, off by default |
| `Cors:AllowedOrigins` | Only needed when the frontend is hosted on another origin |
| `Database:MigrateOnStartup` | Apply EF migrations at startup |

## Tests

```bash
dotnet test MiniBroker.slnx
cd frontend && npm run build && npm run lint
```

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/MiniBroker.Infrastructure -s src/MiniBroker.Api -o Persistence/Migrations
```

## API

All routes except `/health` and `/api/auth/register|login` require `Authorization: Bearer <token>`.

| Method | Route | Description |
|---|---|---|
| POST | `/api/auth/register` | `{ email, password, displayName }` → token |
| POST | `/api/auth/login` | `{ email, password }` → token |
| GET | `/api/auth/me` | Current user |
| GET / POST | `/api/accounts` | List / open account `{ name }` |
| GET | `/api/accounts/{id}` | Balance (cash, reserved, available) and positions |
| POST | `/api/accounts/{id}/deposits` · `/withdrawals` | `{ amount }` |
| GET | `/api/accounts/{id}/transactions` | Cash ledger, newest first |
| POST | `/api/accounts/{id}/orders` | `{ symbol, side, quantity, type: Market\|Limit, limitPrice? }` |
| GET | `/api/accounts/{id}/orders?status=Pending` | Orders, newest first |
| DELETE | `/api/accounts/{id}/orders/{orderId}` | Cancel a pending order |
| GET | `/api/instruments?search=` | Instruments with last price and 24h change |
| GET | `/api/instruments/{symbol}/history?range=15m\|1h\|1d` | Price history (downsampled) |
| PUT | `/api/instruments/{symbol}/price` | Set a price manually (also triggers limit-order matching) |
| GET / PUT / DELETE | `/api/watchlist` · `/api/watchlist/{symbol}` | Watchlist |

Errors are returned as ProblemDetails: `400` invalid input, `401` not signed in, `404` not found
(including other users' accounts), `409` conflict, `429` too many login attempts.
