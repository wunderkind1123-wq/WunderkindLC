import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { PnlYear, PnlYearMonth } from '@/api/services/financeV2'
import { cn } from '@/lib/utils'
import { MONTHS, MONTHS_SHORT, delta, money, pct, toneCls } from './financeV2Format'

/**
 * Yillik ko'rinish: har oy P&L jadvali bilan AYNAN bir xil hisob (server `PnlEngine.Compute`, oy oxiri holatiga; joriy oy —
 * bugun). Kelajak oylar PROGNOZ. YILLIK — fakt oylar yig'indisi; marja = yillik foyda / yillik daromad.
 */
export function PnlYearlyTab({ y, mode, onMode }: { y: PnlYear; mode: 'fact' | 'plan'; onMode: (m: 'fact' | 'plan') => void }) {
  const tt = y.totals
  const revenue = tt.net + tt.otherRevenue
  const chart = y.months.map((m) => ({
    name: MONTHS_SHORT[m.month - 1],
    net: Math.round(m.net),
    expense: Math.round(m.expense),
    profit: Math.round(m.profit),
    forecast: m.isForecast,
  }))

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-6">
        <Card title="Yillik sof kutuv" value={money(tt.net)} sub={`yalpi ${money(tt.gross)}`} />
        <Card title="Yillik chegirma" value={money(tt.discount)} sub={`yalpidan ${pct(tt.discountRate)}`} />
        <Card
          title="Yillik xarajat"
          value={money(tt.expense)}
          sub={
            <span className={toneCls[delta(tt.expense - tt.planExpense, false).tone]}>rejadan {delta(tt.expense - tt.planExpense, false).text}</span>
          }
        />
        <Card title="Yillik foyda / zarar" value={money(tt.profit)} sub={`marja ${pct(tt.margin)} · daromad ${money(revenue)}`} tone={tt.profit < 0 ? 'bad' : 'good'} />
        <Card
          title="Eng foydali oy"
          value={tt.bestMonth ? MONTHS[tt.bestMonth.month - 1] : '—'}
          sub={tt.bestMonth ? `${money(tt.bestMonth.profit)} so'm` : "Ma'lumot yo'q"}
        />
        <Card
          title="Eng zararli oy"
          value={tt.worstMonth ? MONTHS[tt.worstMonth.month - 1] : '—'}
          sub={tt.worstMonth ? `${money(tt.worstMonth.profit)} so'm` : "Ma'lumot yo'q"}
        />
      </div>

      <div className="rounded-xl border border-[#dbe0e6] bg-white p-3">
        <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
          <span className="text-[13px] font-bold">Oylik sof kutuv, xarajat va foyda</span>
          <span className="text-[11.5px] text-[#6b7280]">Kelajak oylar — prognoz (jadvalda kursiv)</span>
        </div>
        <div className="h-[260px]">
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={chart} margin={{ top: 4, right: 8, left: 8, bottom: 0 }}>
              <CartesianGrid stroke="#eef2f7" vertical={false} />
              <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#6b7280' }} axisLine={false} tickLine={false} />
              <YAxis
                tick={{ fontSize: 11, fill: '#6b7280' }}
                axisLine={false}
                tickLine={false}
                width={56}
                tickFormatter={(v: number) => (Math.abs(v) >= 1e6 ? `${(v / 1e6).toFixed(0)} mln` : money(v))}
              />
              <Tooltip formatter={(v) => `${money(Number(v))} so'm`} contentStyle={{ fontSize: 12, borderRadius: 8 }} />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="net" name="Sof kutuv" fill="#0284c7" radius={[4, 4, 0, 0]} fillOpacity={0.9} />
              <Bar dataKey="expense" name="Xarajat" fill="#e11d48" radius={[4, 4, 0, 0]} fillOpacity={0.85} />
              <Line dataKey="profit" name="Sof foyda" stroke="#6366f1" strokeWidth={2.5} dot={{ r: 3 }} />
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div className="flex items-center gap-1 rounded-lg bg-[#eef2f7] p-1 text-[12px] font-semibold" style={{ width: 'fit-content' }}>
        {(['fact', 'plan'] as const).map((m) => (
          <button
            key={m}
            type="button"
            onClick={() => onMode(m)}
            className={cn('rounded-md px-3 py-1', mode === m ? 'bg-white text-brand-600 shadow-sm' : 'text-[#4b5563]')}
          >
            {m === 'fact' ? 'Fakt' : 'Reja / Fakt'}
          </button>
        ))}
      </div>

      <YearTable y={y} mode={mode} />
    </div>
  )
}

function Card({ title, value, sub, tone }: { title: string; value: string; sub?: React.ReactNode; tone?: 'good' | 'bad' }) {
  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white p-3 shadow-[0_1px_2px_rgba(0,0,0,0.04)]">
      <div className="text-[12px] font-semibold text-[#6b7280]">{title}</div>
      <div className={cn('truncate text-[20px] font-bold tabular-nums', tone === 'bad' ? 'text-[#dc2626]' : 'text-black')}>{value}</div>
      {sub && <div className="text-[11.5px] text-[#6b7280]">{sub}</div>}
    </div>
  )
}

type RowDef = {
  label: string
  get: (m: PnlYearMonth) => number | null
  plan?: (m: PnlYearMonth) => number | null
  total: number | null
  planTotal?: number | null
  tone?: string
  count?: boolean
  percent?: boolean
  goodWhenUp?: boolean
}

function YearTable({ y, mode }: { y: PnlYear; mode: 'fact' | 'plan' }) {
  const t = y.totals
  const rows: RowDef[] = [
    { label: "O'quvchilar soni", get: (m) => m.students, total: t.students, count: true },
    { label: 'Yalpi kutilayotgan tushum', get: (m) => m.gross, plan: (m) => m.plan.gross, total: t.gross, planTotal: sum(y, (m) => m.plan.gross) },
    { label: 'Chegirma (−)', get: (m) => m.discount, plan: (m) => m.plan.discount, total: t.discount, planTotal: sum(y, (m) => m.plan.discount), goodWhenUp: false },
    { label: 'Qaytarishlar (−)', get: (m) => m.refunds, total: t.refunds, goodWhenUp: false },
    { label: 'Boshqa daromad', get: (m) => m.otherRevenue, total: t.otherRevenue },
    { label: 'Fakt kutilayotgan tushum', get: (m) => m.net, plan: (m) => m.plan.net, total: t.net, planTotal: sum(y, (m) => m.plan.net), tone: 'bg-[#e8f8ee] font-bold' },
    { label: 'Jami xarajat', get: (m) => m.expense, plan: (m) => m.plan.expense, total: t.expense, planTotal: t.planExpense, tone: 'bg-[#fdeceb] font-bold', goodWhenUp: false },
    { label: 'Sof foyda / zarar', get: (m) => m.profit, plan: (m) => m.plan.profit, total: t.profit, planTotal: t.planProfit, tone: 'bg-[#fff6e5] font-bold' },
    { label: 'Foyda marjasi', get: (m) => m.margin, total: t.margin, percent: true },
    { label: 'Chegirma darajasi', get: (m) => m.discountRate, total: t.discountRate, percent: true, goodWhenUp: false },
  ]
  const fmt = (v: number | null, r: RowDef) => (v === null ? '—' : r.percent ? pct(v) : r.count ? String(v) : money(v))
  const planMode = mode === 'plan'

  return (
    <div className="overflow-auto rounded-xl border border-[#dbe0e6] bg-white">
      <table className="w-full border-collapse text-[12.5px] text-black">
        <thead>
          <tr className="bg-[#f5f6f7] text-[11.5px] font-semibold uppercase">
            <th rowSpan={planMode ? 2 : 1} className="sticky left-0 z-10 min-w-[220px] border-b border-[#e0e0e0] bg-[#f5f6f7] px-4 py-2 text-left">
              Ko'rsatkich
            </th>
            {y.months.map((m) => (
              <th
                key={m.month}
                colSpan={planMode ? 3 : 1}
                className={cn(
                  'min-w-[112px] border-b border-l border-[#e0e0e0] px-3 py-2 text-right',
                  m.isCurrent && 'bg-[#eef2ff] text-brand-600',
                  m.isForecast && 'text-[#9ca3af]',
                )}
              >
                {m.name}
                {m.isCurrent && <div className="text-[9px] font-bold tracking-wide">JORIY</div>}
                {m.isForecast && <div className="text-[9px] font-bold tracking-wide">PROGNOZ</div>}
              </th>
            ))}
            <th colSpan={planMode ? 3 : 1} className="min-w-[130px] border-b border-l border-[#e0e0e0] bg-[#eef2f7] px-3 py-2 text-right">
              Yillik
            </th>
          </tr>
          {planMode && (
            <tr className="bg-[#fafafa] text-[10px] uppercase text-[#6b7280]">
              {[...y.months.map((m) => m.month), 13].flatMap((k) =>
                ['Reja', 'Fakt', 'Farq'].map((h) => (
                  <th key={`${k}-${h}`} className="border-b border-[#e0e0e0] px-2 py-1 text-right font-semibold">
                    {h}
                  </th>
                )),
              )}
            </tr>
          )}
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.label} className={r.tone ?? ''}>
              <td className={cn('sticky left-0 z-[1] h-10 whitespace-nowrap border-b border-[#eceff1] px-4', r.tone ?? 'bg-white')}>{r.label}</td>
              {y.months.map((m) => {
                const v = r.get(m)
                if (!planMode)
                  return (
                    <td key={m.month} className={cn('whitespace-nowrap border-b border-l border-[#eceff1] px-3 text-right tabular-nums', m.isForecast && 'italic text-[#9ca3af]', (v ?? 0) < 0 && !m.isForecast && 'text-[#dc2626]')}>
                      {fmt(v, r)}
                    </td>
                  )
                const pv = r.plan ? r.plan(m) : null
                const d = pv !== null && v !== null ? delta(v - pv, r.goodWhenUp ?? true) : null
                return [
                  <td key={`${m.month}p`} className="whitespace-nowrap border-b border-l border-[#eceff1] px-2 text-right tabular-nums text-[#6b7280]">
                    {pv === null ? '—' : fmt(pv, r)}
                  </td>,
                  <td key={`${m.month}f`} className={cn('whitespace-nowrap border-b border-[#eceff1] px-2 text-right tabular-nums', m.isForecast && 'italic text-[#9ca3af]')}>
                    {fmt(v, r)}
                  </td>,
                  <td key={`${m.month}d`} className={cn('whitespace-nowrap border-b border-[#eceff1] px-2 text-right text-[11px] font-semibold tabular-nums', d ? toneCls[d.tone] : 'text-[#9ca3af]')}>
                    {d ? d.text : '—'}
                  </td>,
                ]
              })}
              {!planMode ? (
                <td className="whitespace-nowrap border-b border-l border-[#eceff1] bg-[#f8fafc] px-3 text-right font-bold tabular-nums">{fmt(r.total, r)}</td>
              ) : (
                [
                  <td key="tp" className="whitespace-nowrap border-b border-l border-[#eceff1] bg-[#f8fafc] px-2 text-right tabular-nums text-[#6b7280]">
                    {r.planTotal === undefined || r.planTotal === null ? '—' : fmt(r.planTotal, r)}
                  </td>,
                  <td key="tf" className="whitespace-nowrap border-b border-[#eceff1] bg-[#f8fafc] px-2 text-right font-bold tabular-nums">{fmt(r.total, r)}</td>,
                  <td key="td" className="whitespace-nowrap border-b border-[#eceff1] bg-[#f8fafc] px-2 text-right text-[11px] font-semibold tabular-nums">
                    {r.planTotal !== undefined && r.planTotal !== null && r.total !== null ? (
                      <span className={toneCls[delta(r.total - r.planTotal, r.goodWhenUp ?? true).tone]}>{delta(r.total - r.planTotal, r.goodWhenUp ?? true).text}</span>
                    ) : (
                      '—'
                    )}
                  </td>,
                ]
              )}
            </tr>
          ))}
        </tbody>
      </table>
      <p className="border-t border-[#eceff1] px-4 py-2 text-[11.5px] text-[#6b7280]">
        YILLIK — fakt oylar yig'indisi (prognoz kirmaydi). O'quvchilar — fakt oylar o'rtachasi. Prognoz: sof kutuv — oxirgi fakt oy, xarajat — oxirgi 3
        oy o'rtachasi. «Reja» — har oy boshidagi tariflar bo'yicha taxmin.
      </p>
    </div>
  )
}

function sum(y: PnlYear, f: (m: PnlYearMonth) => number): number {
  return y.months.filter((m) => !m.isForecast).reduce((a, m) => a + f(m), 0)
}
