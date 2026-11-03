import { useSearchParams } from 'react-router-dom'
import { getPnl, getPnlYearly } from '@/api/services/financeV2'
import { Loader } from '@/components/ui/Loader'
import { useAsync } from '@/hooks/useAsync'
import { cn } from '@/lib/utils'
import { PnlPlannedTab } from './PnlPlannedTab'
import { PnlTableTab } from './PnlTableTab'
import { PnlYearlyTab } from './PnlYearlyTab'
import { MONTHS, PRESETS, dmy, presetPeriod, todayIso, yearRange, type PresetKey } from './financeV2Format'

type Tab = 'table' | 'yearly' | 'planned'
const TABS: { key: Tab; label: string }[] = [
  { key: 'table', label: 'P&L jadvali' },
  { key: 'yearly', label: "Yillik ko'rinish" },
  { key: 'planned', label: 'Rejali xarajatlar' },
]

/**
 * Moliya → Hisobotlar → «Moliya hisobotlari (P&L) 2.0 BETA» (`/admin/moliya/pnl-2`). Hisoblanma (accrual) asosida: oylik
 * hisoblar − chegirma − qaytarishlar = sof kutuv; + boshqa daromad − xarajat (shablonlar, maosh reestri, rejasiz) = foyda.
 * Barcha filtrlar manzilda (`?year&month&asOf&tab&ymode`). Server: `GET /admin/finance/v2/pnl` (+ `/yearly`, `/drilldown`).
 */
export function Pnl2Page() {
  const [params, setParams] = useSearchParams()
  const now = new Date()
  const year = Number(params.get('year')) || now.getFullYear()
  const month = Number(params.get('month')) || now.getMonth() + 1
  const asOf = params.get('asOf') || ''
  const tab = (TABS.find((t) => t.key === params.get('tab'))?.key ?? 'table') as Tab
  const ymode = params.get('ymode') === 'plan' ? 'plan' : 'fact'

  const set = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(patch)) {
      if (v === null || v === '') next.delete(k)
      else next.set(k, v)
    }
    setParams(next, { replace: true })
  }

  const applyPreset = (key: PresetKey) => {
    const pp = presetPeriod(key)
    set({ year: String(pp.year), month: String(pp.month), asOf: pp.asOf, tab: key === 'thisYear' ? 'yearly' : tab === 'yearly' ? 'table' : tab })
  }

  const pnl = useAsync(() => (tab === 'yearly' ? Promise.resolve(null) : getPnl(year, month, asOf || undefined)), [year, month, asOf, tab])
  const yearly = useAsync(() => (tab === 'yearly' ? getPnlYearly(year) : Promise.resolve(null)), [year, tab])

  const monthStart = `${year}-${String(month).padStart(2, '0')}-01`
  const monthEndDay = new Date(year, month, 0).getDate()
  const monthEnd = `${year}-${String(month).padStart(2, '0')}-${String(monthEndDay).padStart(2, '0')}`
  const today = todayIso()
  const maxAsOf = monthEnd < today ? monthEnd : today >= monthStart ? today : monthEnd
  const shownAsOf = pnl.data?.asOf ?? (asOf || maxAsOf)

  return (
    <div className="space-y-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <h1 className="text-[18px] font-semibold text-black">Moliya hisobotlari (P&L)</h1>
          <span className="rounded-full bg-[#eef2ff] px-2 py-0.5 text-[11px] font-bold text-brand-600">2.0</span>
          <span className="rounded-full bg-[#fef3c7] px-2 py-0.5 text-[10px] font-bold tracking-wide text-[#92400e]">BETA</span>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <select
            value=""
            onChange={(e) => e.target.value && applyPreset(e.target.value as PresetKey)}
            className="h-[34px] rounded-lg border border-black/25 bg-white px-3 text-[13px] text-black outline-none focus:border-brand-600"
            aria-label="Tez tanlov"
          >
            <option value="">Tez tanlov…</option>
            {PRESETS.map((p) => (
              <option key={p.key} value={p.key}>
                {p.label}
              </option>
            ))}
          </select>
          <select
            value={year}
            onChange={(e) => set({ year: e.target.value, asOf: null })}
            className="h-[34px] rounded-lg border border-black/25 bg-white px-3 text-[13px] text-black outline-none focus:border-brand-600"
            aria-label="Yil"
          >
            {yearRange(2026).map((v) => (
              <option key={v} value={v}>
                {v}
              </option>
            ))}
          </select>
          {tab !== 'yearly' && (
            <>
              <select
                value={month}
                onChange={(e) => set({ month: e.target.value, asOf: null })}
                className="h-[34px] rounded-lg border border-black/25 bg-white px-3 text-[13px] text-black outline-none focus:border-brand-600"
                aria-label="Oy"
              >
                {MONTHS.map((m, i) => (
                  <option key={m} value={i + 1}>
                    {m}
                  </option>
                ))}
              </select>
              <label className="flex items-center gap-1.5 text-[12px] text-[#4b5563]">
                holatiga
                <input
                  type="number"
                  min={1}
                  max={Number(maxAsOf.slice(8, 10))}
                  value={Number(shownAsOf.slice(8, 10))}
                  onChange={(e) => {
                    const dd = Math.max(1, Math.min(Number(maxAsOf.slice(8, 10)), Number(e.target.value) || 1))
                    set({ asOf: `${monthStart.slice(0, 8)}${String(dd).padStart(2, '0')}` })
                  }}
                  className="h-[34px] w-[64px] rounded-lg border border-black/25 bg-white px-2 text-center text-[13px] outline-none focus:border-brand-600"
                  aria-label="Holat kuni"
                />
                -kun
              </label>
            </>
          )}
        </div>
      </div>

      {tab !== 'yearly' && (
        <p className="text-[12px] text-[#6b7280]">
          Hisobot <b className="text-black">{dmy(shownAsOf)}</b> holatiga — oy boshidan shu sanagacha hisoblanadi. Katakchani bosing — tafsilot ochiladi.
        </p>
      )}

      <div className="flex gap-1 overflow-x-auto border-b border-[#e5e7eb]">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => set({ tab: t.key === 'table' ? null : t.key })}
            className={cn(
              'whitespace-nowrap border-b-2 px-3 py-2 text-[13px] font-semibold transition-colors',
              tab === t.key ? 'border-brand-600 text-brand-600' : 'border-transparent text-[#6b7280] hover:text-black',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'yearly' ? (
        yearly.error ? (
          <ErrorBox message={yearly.error} onRetry={yearly.refetch} />
        ) : !yearly.data ? (
          <Loader className="min-h-[240px]" />
        ) : (
          <PnlYearlyTab y={yearly.data} mode={ymode} onMode={(m) => set({ ymode: m === 'fact' ? null : m })} />
        )
      ) : pnl.error ? (
        <ErrorBox message={pnl.error} onRetry={pnl.refetch} />
      ) : !pnl.data ? (
        <Loader className="min-h-[240px]" />
      ) : tab === 'planned' ? (
        <PnlPlannedTab p={pnl.data} onChanged={pnl.refetch} />
      ) : (
        <PnlTableTab p={pnl.data} />
      )}
    </div>
  )
}

function ErrorBox({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="rounded-xl border border-[#fecaca] bg-[#fef2f2] px-4 py-3 text-[13px] text-[#b91c1c]">
      {message}{' '}
      <button type="button" onClick={onRetry} className="ml-2 font-semibold underline">
        Qayta urinish
      </button>
    </div>
  )
}
