import { useState, type FormEvent } from 'react'
import type { Account, User } from '../api'

interface Props {
  user: User
  accounts: Account[] | null
  activeId: string | null
  onSelect: (id: string) => void
  onOpen: (name: string) => Promise<void>
  onLogout: () => void
}

export function AccountBar({ user, accounts, activeId, onSelect, onOpen, onLogout }: Props) {
  const [opening, setOpening] = useState(false)
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!name.trim()) return
    setBusy(true)
    try {
      await onOpen(name.trim())
      setName('')
      setOpening(false)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="account-bar">
      {accounts && accounts.length > 0 && !opening && (
        <label className="select-wrap">
          <span className="sr-only">Account</span>
          <select value={activeId ?? ''} onChange={(e) => onSelect(e.target.value)}>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
        </label>
      )}

      {opening ? (
        <form className="inline-form" onSubmit={submit}>
          <input
            autoFocus
            placeholder="Account name (e.g. Long-term)"
            value={name}
            maxLength={100}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === 'Escape' && setOpening(false)}
          />
          <button type="submit" className="btn primary" disabled={busy || !name.trim()}>
            {busy ? 'Opening…' : 'Open'}
          </button>
          <button type="button" className="btn ghost" onClick={() => setOpening(false)}>
            Cancel
          </button>
        </form>
      ) : (
        accounts && accounts.length > 0 && (
          <button className="btn" onClick={() => setOpening(true)}>
            + Account
          </button>
        )
      )}

      <div className="user-chip" title={user.email}>
        <span className="avatar" aria-hidden>
          {user.displayName.slice(0, 1).toUpperCase()}
        </span>
        <span className="user-name">{user.displayName}</span>
        <button className="btn ghost small" onClick={onLogout}>
          Sign out
        </button>
      </div>
    </div>
  )
}
