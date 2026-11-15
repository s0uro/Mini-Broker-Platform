import { useCallback, useEffect, useRef, useState } from 'react'
import { setAuthToken, setUnauthorizedHandler, type AuthSession } from './api'
import { AuthPage } from './components/AuthPage'
import { Dashboard } from './components/Dashboard'
import { Toasts, type Toast } from './components/Toasts'
import { loadJson, saveJson } from './storage'

const SESSION_KEY = 'minibroker.session'

function loadSession(): AuthSession | null {
  const session = loadJson<AuthSession | null>(SESSION_KEY, null)
  const valid = session && new Date(session.expiresAt).getTime() > Date.now() ? session : null
  setAuthToken(valid?.token ?? null) // before any child effect fires a request
  return valid
}

export default function App() {
  const [session, setSession] = useState<AuthSession | null>(loadSession)
  const [toasts, setToasts] = useState<Toast[]>([])
  const toastId = useRef(0)

  const notify = useCallback((kind: Toast['kind'], text: string) => {
    const id = ++toastId.current
    setToasts((t) => [...t.slice(-3), { id, kind, text }])
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 5000)
  }, [])

  const signIn = useCallback((next: AuthSession) => {
    setAuthToken(next.token)
    saveJson(SESSION_KEY, next)
    setSession(next)
  }, [])

  const signOut = useCallback(() => {
    setAuthToken(null)
    saveJson(SESSION_KEY, null)
    setSession(null)
  }, [])

  // Expired/revoked token on any request -> back to the sign-in screen.
  useEffect(() => {
    setUnauthorizedHandler(() => {
      signOut()
      notify('error', 'Your session has expired. Please sign in again.')
    })
    return () => setUnauthorizedHandler(null)
  }, [signOut, notify])

  // Also sign out when the token's lifetime ends while the tab is open.
  useEffect(() => {
    if (!session) return
    const ms = new Date(session.expiresAt).getTime() - Date.now()
    const timer = setTimeout(signOut, Math.max(0, ms))
    return () => clearTimeout(timer)
  }, [session, signOut])

  return (
    <>
      {session ? (
        <Dashboard key={session.user.id} session={session} onLogout={signOut} notify={notify} />
      ) : (
        <AuthPage onAuthenticated={signIn} />
      )}
      <Toasts toasts={toasts} onDismiss={(id) => setToasts((t) => t.filter((x) => x.id !== id))} />
    </>
  )
}
