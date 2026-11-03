import { useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router-dom'
import { IconAlertTriangle, IconCircleCheck, IconFileSpreadsheet } from '@tabler/icons-react'
import {
  downloadCashFlow,
  getCashFlowMonth,
  getCashFlowTransactions,
  getCashFlowYear,
  type CashFlowMonth,
  type CashFlowYear,
  type CfReconciliation,
} from '@/api/services/financeV2'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { useAsync } from '@/hooks/useAsync'
import { apiErrorMessage, cn } from '@/lib/utils'
import { MONTHS, dmy, money, yearRange } from './financeV2Format'

type Tone = 'income' | 'expense' | 'net' | 'section' | 'balance' | undefined

const toneRow: Record<NonNullable<Tone>, string> = {
  income: 'bg-[#e8f8ee] font-semibold',
  expense: 'bg-[#fdeceb] font-semibold',
  net: 'bg-[#fff6e5] font-semibold',
  section: 'bg-[#f5f6f7] font-bold uppercase text-[12px] tracking-wide text-[#374151]',
  balance: 'bg-white font-bold',
}

/**
 * Moliya → Hisobotlar → «Pul oqimi 2.0» (`/admin/moliya/pul-oqimi-2`). To'g'ridan-to'g'ri usul: haqiqiy kassa amallari
 * operatsion / investitsiya / moliyalashtirish faoliyatlariga bo'lingan (sozlama — «Hisobot toifalari»). Kassadan kassaga
 * ko'chirish va bekor qilingan amallar kirmaydi. Server: `GET /admin/finance/v2/cash-flow`.
 */
export function CashFlow2Page() {
  const [params, setParams] = useSearchParams()
  const nowYear = new Date().getFullYear()
  const year = Number(params.get('year')) || nowYear
  const month = Number(params.get('month')) || 0
  const [drill, setDrill] = useState<{ rowKey: string; name: string; month: number } | null>(null)
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const yearRes = useAsync(() => getCashFlowYear(year), [year])
  const monthRes = useAsync(() => (month ? getCashFlowMonth(year, month) : Promise.resolve(null)), [year, month])

  const setFilter = (y: number, m: number) => {
    const next = new URLSearchParams(params)
    next.set('year', String(y))
    if (m) next.set('month', String(m))
    else next.delete('month')
    setParams(next, { replace: true })
  }

  const onExport = async () => {
    setExporting(true)
    setExportError(null)
    try {
      await downloadCashFlow(year, month || undefined)
    } catch (err) {
      setExportError(apiErrorMessage(err, "Faylni yuklab bo'lmadi"))
    } finally {
      setExporting(false)
    }
  }

  const y = yearRes.data
  const years = yearRange(y?.firstYear ?? nowYear)

  return (
    <div className="space-y-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <h1 className="text-[18px] font-semibold text-black">Pul oqimi hisoboti</h1>
          <span className="rounded-full bg-[#eef2ff] px-2 py-0.5 text-[11px] font-bold text-brand-600">2.0</span>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <select
            value={year}
            onChange={(e) => setFilter(Number(e.target.value), month)}
            className="h-[34px] rounded-lg border border-black/25 bg-white px-3 text-[13px] text-black outline-none focus:border-brand-600"
            aria-label="Yil"
          >
            {years.map((v) => (
              <option key={v} value={v}>
                {v}
              </option>
            ))}
          </select>
          <select
            value={month}
            onChange={(e) => setFilter(year, Number(e.target.value))}
            className="h-[34px] rounded-lg border border-black/25 bg-white px-3 text-[13px] text-black outline-none focus:border-brand-600"
            aria-label="Oy"
          >
            <option value={0}>Butun yil</option>
            {MONTHS.map((m, i) => (
              <option key={m} value={i + 1}>
                {m}
              </option>
            ))}
          </select>
          <button
            type="button"
            onClick={onExport}
            disabled={exporting}
            className="inline-flex min-h-[34px] items-center gap-1.5 rounded-lg bg-brand-600 px-3 text-xs font-medium text-white transition-colors hover:bg-brand-700 disabled:opacity-60"
          >
            <IconFileSpreadsheet className="h-5 w-5" />
            {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
          </button>
        </div>
      </div>
      {exportError && <p className="text-[13px] text-red-600">{exportError}</p>}

      <p className="text-[12px] text-[#6b7280]">
        Haqiqiy kassa amallari, to'g'ridan-to'g'ri usulda. Kassadan kassaga ko'chirish va bekor qilingan amallar kirmaydi. Qatorlarning
        faoliyat turi — Moliya → Ma'lumotlar → «Hisobot toifalari». Katakchani bosing — amallar ro'yxati ochiladi.
      </p>

      {yearRes.error ? (
        <ErrorBox message={yearRes.error} onRetry={yearRes.refetch} />
      ) : !y ? (
        <Loader className="min-h-[240px]" />
      ) : (
        <>
          <ReconciliationBanner r={y.reconciliation} />
          {month ? (
            monthRes.error ? (
              <ErrorBox message={monthRes.error} onRetry={monthRes.refetch} />
            ) : !monthRes.data ? (
              <Loader className="min-h-[200px]" />
            ) : (
              <MonthTable m={monthRes.data} onDrill={(rowKey, name) => setDrill({ rowKey, name, month })} />
            )
          ) : (
            <YearTable y={y} onDrill={(rowKey, name, m) => setDrill({ rowKey, name, month: m })} />
          )}
        </>
      )}

      {drill && <TxDrillModal year={year} {...drill} onClose={() => setDrill(null)} />}
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

function ReconciliationBanner({ r }: { r: CfReconciliation }) {
  if (!r.applicable) return null
  if (Math.round(r.difference) === 0)
    return (
      <div className="flex items-center gap-2 rounded-xl border border-[#bbf7d0] bg-[#f0fdf4] px-3 py-2 text-[12px] text-[#166534]">
        <IconCircleCheck className="h-4 w-4 shrink-0" />
        Hisobot qoldig'i kassalar jami qoldig'iga teng: {money(r.cashboxBalanceNow)} so'm.
      </div>
    )
  return (
    <div className="flex items-start gap-2 rounded-xl border border-[#fde68a] bg-[#fffbeb] px-3 py-2 text-[12px] text-[#92400e]">
      <IconAlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
      <span>
        Kassa qoldig'i bilan <b>{money(Math.abs(r.difference))} so'm</b> farq bor (kassalarda {money(r.cashboxBalanceNow)}, hisobotda{' '}
        {money(r.reportBalanceNow)}). Sababi: «Hisobot toifalari»da pul oqimidan chiqarilgan («excluded») turlar yoki yil ichida
        qo'yilgan kassa boshlang'ich qoldig'i.
      </span>
    </div>
  )
}

function Cell({ value, onClick, muted, strong }: { value: number; onClick?: () => void; muted?: boolean; strong?: boolean }) {
  const text = value === 0 ? '0' : money(value)
  return (
    <td
      onClick={onClick && value !== 0 ? onClick : undefined}
      className={cn(
        'h-10 whitespace-nowrap border-b border-[#eceff1] px-3 text-right tabular-nums',
        muted && 'text-[#9ca3af]',
        value < 0 && !muted && 'text-[#dc2626]',
        strong && 'font-bold',
        onClick && value !== 0 && 'cursor-pointer hover:bg-[#eef2ff] hover:text-brand-600',
      )}
    >
      {text}
    </td>
  )
}

function YearTable({ y, onDrill }: { y: CashFlowYear; onDrill: (rowKey: string, name: string, month: number) => void }) {
  const isFuture = (i: number) => i + 1 > y.currentMonth
  const rows: ReactNode[] = []
  const line = (key: string, label: ReactNode, data: number[], total: number, tone: Tone, indent = false, drillKey?: string, name?: string) =>
    rows.push(
      <tr key={key} className={tone ? toneRow[tone] : ''}>
        <td
          className={cn(
            'sticky left-0 z-10 h-10 min-w-[260px] whitespace-nowrap border-b border-[#eceff1] px-4',
            tone ? toneRow[tone] : 'bg-white',
            indent && 'pl-9 font-normal text-[#4b5563]',
          )}
        >
          {label}
        </td>
        {data.map((v, i) => (
          <Cell
            key={i}
            value={v}
            muted={isFuture(i) && v === 0}
            onClick={drillKey ? () => onDrill(drillKey, name ?? '', i + 1) : undefined}
          />
        ))}
        <Cell value={total} strong onClick={drillKey ? () => onDrill(drillKey, name ?? '', 0) : undefined} />
      </tr>,
    )

  line('start', "Boshlang'ich qoldiq", y.startBalances, y.startBalances[0], 'balance')
  for (const s of y.sections) {
    if (s.in.length === 0 && s.out.length === 0) continue
    rows.push(
      <tr key={`h-${s.key}`}>
        <td colSpan={14} className={cn('sticky left-0 h-9 border-b border-[#e5e7eb] px-4', toneRow.section)}>
          {s.title}
        </td>
      </tr>,
    )
    line(`${s.key}-in`, 'Kirim — Jami', s.totalIn, s.totalIn.reduce((a, b) => a + b, 0), 'income')
    for (const l of s.in) line(`${s.key}-i-${l.rowKey}`, l.name, l.data, l.total, undefined, true, l.rowKey, l.name)
    line(`${s.key}-out`, 'Chiqim — Jami', s.totalOut, s.totalOut.reduce((a, b) => a + b, 0), 'expense')
    for (const l of s.out) line(`${s.key}-o-${l.rowKey}`, l.name, l.data, l.total, undefined, true, l.rowKey, l.name)
    line(`${s.key}-net`, 'Sof', s.net, s.net.reduce((a, b) => a + b, 0), 'net')
  }
  line('net', 'Sof pul oqimi', y.netCashFlow, y.netCashFlow.reduce((a, b) => a + b, 0), 'net')
  line('end', 'Yakuniy qoldiq', y.endBalances, y.endBalances[11], 'balance')

  const empty = y.sections.every((s) => s.in.length === 0 && s.out.length === 0)

  return (
    <div className="overflow-auto rounded-xl border border-[#dbe0e6] bg-white">
      <table className="w-full border-collapse text-[13px] font-medium text-black">
        <thead>
          <tr>
            <th className="sticky left-0 z-20 h-12 border-b border-[#e0e0e0] bg-[#f5f6f7] px-4 text-left text-[12px] font-semibold uppercase">
              Ko'rsatkich
            </th>
            {y.months.map((m, i) => (
              <th
                key={m}
                className={cn(
                  'h-12 min-w-[118px] border-b border-[#e0e0e0] px-3 text-right text-[12px] font-semibold uppercase',
                  i + 1 === y.currentMonth && y.year === new Date().getFullYear() ? 'bg-[#eef2ff] text-brand-600' : 'bg-white',
                  isFuture(i) && 'text-[#9ca3af]',
                )}
              >
                {m}
              </th>
            ))}
            <th className="h-12 min-w-[130px] border-b border-[#e0e0e0] bg-[#f5f6f7] px-3 text-right text-[12px] font-semibold uppercase">
              Jami
            </th>
          </tr>
        </thead>
        <tbody>{rows}</tbody>
      </table>
      {empty && <p className="px-4 py-6 text-center text-[13px] text-[#6b7280]">Bu yil uchun kassa amallari yo'q.</p>}
    </div>
  )
}

function MonthTable({ m, onDrill }: { m: CashFlowMonth; onDrill: (rowKey: string, name: string) => void }) {
  const row = (key: string, label: ReactNode, value: number, tone: Tone, indent = false, drill?: () => void) => (
    <tr key={key} className={tone ? toneRow[tone] : ''}>
      <td className={cn('h-10 border-b border-[#eceff1] px-4', indent && 'pl-9 font-normal text-[#4b5563]')}>{label}</td>
      <Cell value={value} onClick={drill} strong={!!tone} />
    </tr>
  )
  const body: ReactNode[] = [row('start', "Boshlang'ich qoldiq", m.startBalance, 'balance')]
  for (const s of m.sections) {
    if (s.cashIn.categories.length === 0 && s.cashOut.categories.length === 0) continue
    body.push(
      <tr key={`h-${s.key}`}>
        <td colSpan={2} className={cn('h-9 border-b border-[#e5e7eb] px-4', toneRow.section)}>
          {s.title}
        </td>
      </tr>,
    )
    body.push(row(`${s.key}-in`, 'Kirim — Jami', s.cashIn.total, 'income'))
    for (const c of s.cashIn.categories) body.push(row(`${s.key}-i-${c.rowKey}`, c.name, c.amount, undefined, true, () => onDrill(c.rowKey, c.name)))
    body.push(row(`${s.key}-out`, 'Chiqim — Jami', s.cashOut.total, 'expense'))
    for (const c of s.cashOut.categories) body.push(row(`${s.key}-o-${c.rowKey}`, c.name, c.amount, undefined, true, () => onDrill(c.rowKey, c.name)))
    body.push(row(`${s.key}-net`, 'Sof', s.net, 'net'))
  }
  body.push(row('net', 'Sof pul oqimi', m.netCashFlow, 'net'))
  body.push(row('end', 'Yakuniy qoldiq', m.endBalance, 'balance'))

  return (
    <div className="max-w-[720px] overflow-hidden rounded-xl border border-[#dbe0e6] bg-white">
      <table className="w-full border-collapse text-[13px] font-medium text-black">
        <thead>
          <tr>
            <th className="h-12 border-b border-[#e0e0e0] bg-[#f5f6f7] px-4 text-left text-[12px] font-semibold uppercase">
              {m.monthName} {m.year}
            </th>
            <th className="h-12 w-[180px] border-b border-[#e0e0e0] bg-[#f5f6f7] px-3 text-right text-[12px] font-semibold uppercase">
              Summa
            </th>
          </tr>
        </thead>
        <tbody>{body}</tbody>
      </table>
    </div>
  )
}

const METHOD: Record<string, string> = { cash: 'Naqd', card: 'Karta', bank: "Bank o'tkazmasi", transfer: "O'tkazma" }

function TxDrillModal({ year, month, rowKey, name, onClose }: { year: number; month: number; rowKey: string; name: string; onClose: () => void }) {
  const [page, setPage] = useState(1)
  const res = useAsync(() => getCashFlowTransactions({ year, month: month || undefined, rowKey, page }), [year, month, rowKey, page])
  const d = res.data
  const pages = d ? Math.max(1, Math.ceil(d.total / d.pageSize)) : 1
  return (
    <Modal open onClose={onClose} size="xl" title={`${name} — ${month ? `${MONTHS[month - 1]} ${year}` : `${year} yil`}`}>
      {res.error ? (
        <p className="text-[13px] text-red-600">{res.error}</p>
      ) : !d ? (
        <Loader className="min-h-[160px]" />
      ) : d.total === 0 ? (
        <p className="py-6 text-center text-[13px] text-[#6b7280]">Amallar yo'q.</p>
      ) : (
        <div className="space-y-2">
          <p className="text-[12px] text-[#6b7280]">
            {d.total} ta amal · jami <b className="text-black">{money(d.sum)} so'm</b>
          </p>
          <div className="max-h-[60vh] overflow-auto rounded-lg border border-[#e5e7eb]">
            <table className="w-full border-collapse text-[12.5px]">
              <thead className="sticky top-0 bg-[#f5f6f7] text-[11px] uppercase text-[#4b5563]">
                <tr>
                  <th className="px-3 py-2 text-left">Sana</th>
                  <th className="px-3 py-2 text-right">Summa</th>
                  <th className="px-3 py-2 text-left">Kassa</th>
                  <th className="px-3 py-2 text-left">Usul</th>
                  <th className="px-3 py-2 text-left">O'quvchi / xodim</th>
                  <th className="px-3 py-2 text-left">Izoh</th>
                  <th className="px-3 py-2 text-left">Kiritgan</th>
                </tr>
              </thead>
              <tbody>
                {d.items.map((t) => (
                  <tr key={t.id} className="border-t border-[#f0f2f2]">
                    <td className="whitespace-nowrap px-3 py-1.5">{dmy(t.date)}</td>
                    <td className={cn('whitespace-nowrap px-3 py-1.5 text-right font-semibold tabular-nums', t.direction === 'expense' ? 'text-[#dc2626]' : 'text-[#15803d]')}>
                      {t.direction === 'expense' ? '−' : '+'}
                      {money(t.amount)}
                    </td>
                    <td className="px-3 py-1.5">{t.cashbox ?? '—'}</td>
                    <td className="px-3 py-1.5">{t.method ? (METHOD[t.method] ?? t.method) : '—'}</td>
                    <td className="px-3 py-1.5">{t.person ?? '—'}</td>
                    <td className="max-w-[260px] truncate px-3 py-1.5 text-[#4b5563]" title={t.note ?? ''}>
                      {t.note || '—'}
                    </td>
                    <td className="px-3 py-1.5 text-[#4b5563]">{t.createdBy ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {pages > 1 && (
            <div className="flex items-center justify-end gap-2 text-[12px]">
              <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="rounded border px-2 py-1 disabled:opacity-40">
                ‹ Oldingi
              </button>
              <span>
                {page} / {pages}
              </span>
              <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="rounded border px-2 py-1 disabled:opacity-40">
                Keyingi ›
              </button>
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}
