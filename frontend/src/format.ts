const currency = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const quantity = new Intl.NumberFormat('en-US', { maximumFractionDigits: 6 })
const percent = new Intl.NumberFormat('en-US', { style: 'percent', minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: 'exceptZero' })
const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'medium' })

export const fmtMoney = (value: number) => currency.format(value)
export const fmtQty = (value: number) => quantity.format(value)
export const fmtPct = (value: number) => percent.format(value)
export const fmtDate = (iso: string) => dateTime.format(new Date(iso))

export const fmtSignedMoney = (value: number) => (value > 0 ? '+' : '') + currency.format(value)

/** CSS class for gain/loss colouring. */
export const pnlClass = (value: number) => (value > 0 ? 'gain' : value < 0 ? 'loss' : '')
