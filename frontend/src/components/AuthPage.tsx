import { useState, type FormEvent } from 'react'
import { api, type AuthSession } from '../api'

interface Props {
  onAuthenticated: (session: AuthSession) => void
}

type Mode = 'login' | 'register'

export function AuthPage({ onAuthenticated }: Props) {
  const [mode, setMode] = useState<Mode>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const isRegister = mode === 'register'
  const canSubmit = email.trim() && password.length >= (isRegister ? 8 : 1) && (!isRegister || displayName.trim())

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!canSubmit) return
    setBusy(true)
    setError(null)
    try {
      const session = isRegister
        ? await api.register(email.trim(), password, displayName.trim())
        : await api.login(email.trim(), password)
      onAuthenticated(session)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  function switchMode(next: Mode) {
    setMode(next)
    setError(null)
  }

  return (
    <div className="auth-page">
      <section className="auth-card panel">
        <div className="brand auth-brand">
          <span className="logo" aria-hidden>◆</span>
          <span>Mini Broker</span>
        </div>
        <h1>{isRegister ? 'Create your account' : 'Welcome back'}</h1>
        <p className="muted">
          {isRegister ? 'Start trading with a simulated market in seconds.' : 'Sign in to see your portfolio.'}
        </p>

        <form className="stack" onSubmit={submit} noValidate>
          {isRegister && (
            <label className="field">
              <span>Name</span>
              <input
                autoComplete="name"
                value={displayName}
                maxLength={100}
                onChange={(e) => setDisplayName(e.target.value)}
              />
            </label>
          )}
          <label className="field">
            <span>Email</span>
            <input
              type="email"
              autoComplete="email"
              autoFocus
              value={email}
              maxLength={256}
              onChange={(e) => setEmail(e.target.value)}
            />
          </label>
          <label className="field">
            <span>Password</span>
            <input
              type="password"
              autoComplete={isRegister ? 'new-password' : 'current-password'}
              value={password}
              maxLength={128}
              onChange={(e) => setPassword(e.target.value)}
            />
            {isRegister && <span className="hint">At least 8 characters.</span>}
          </label>

          {error && (
            <p className="form-error" role="alert">
              {error}
            </p>
          )}

          <button type="submit" className="btn primary block" disabled={!canSubmit || busy}>
            {busy ? 'Please wait…' : isRegister ? 'Create account' : 'Sign in'}
          </button>
        </form>

        <p className="auth-switch muted">
          {isRegister ? 'Already have an account?' : 'New to Mini Broker?'}{' '}
          <button className="link" onClick={() => switchMode(isRegister ? 'login' : 'register')}>
            {isRegister ? 'Sign in' : 'Create an account'}
          </button>
        </p>
      </section>
    </div>
  )
}
