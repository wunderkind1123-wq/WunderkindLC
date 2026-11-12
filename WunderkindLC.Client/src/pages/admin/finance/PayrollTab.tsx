import { useEffect, useRef, useState } from 'react'
import { ChevronLeft, ChevronRight, Download, Hourglass, HandCoins, RefreshCw, Scale, Trash2, Users, Wallet } from 'lucide-react'
import {
  addSalaryAdjustment,
  deleteSalaryAdjustment,
  getPayroll,
  getSalaryAdjustments,
  type Payroll,
  type PayrollRow,
  type SalaryAdjustment,
  type SalaryAdjustmentKind,
} from '@/api/services/financeOps'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { MoneyInput } from '@/components/ui/NumberInput'
import { formatMonth } from '@/config/constants'
import { currentMonth, shiftMonth } from '@/lib/month'
import { apiErrorMessage, cn, exportToCsv, formatMoney } from '@/lib/utils'

const SOURCE_LABEL: Record<string, string> = {
  ledger: "O'qituvchi maoshi",
  kpi: 'KPI (tasdiqlangan)',
  'kpi-pending': 'KPI — oklad (oy tasdiqlanmagan)',
  fixed: 'Qat\'iy oylik',
  none: 'Oylik kiritilmagan',
}

const STATUS: Record<string, { tone: 'green' | 'amber' | 'red' | 'default'; label: string }> = {
  paid: { tone: 'green', label: "To'langan" },
  partial: { tone: 'amber', label: 'Qisman' },
  unpaid: { tone: 'red', label: "To'lanmagan" },
  none: { tone: 'default', label: '—' },
}

const money = (n: number) => (n ? formatMoney(n) : '—')

/**
 * Moliya → «Maosh reestri» (SPEC-backlog-2026-10-17 F3 K3; egasining qarori: fiks + KPI, avans / yakuniy). Bir oy
 * uchun o'qituvchilar (maosh hisobidan) va xodimlar (KPI yoki qat'iy oylik): hisoblangan, avans, yakuniy, qoldiq.
 * 2026-11-12 (egasi): bu sahifada TO'LOV va oylik belgilash YO'Q — oylik Kassa → «Yangi amal» (maosh chiqimi) orqali
 * beriladi, xodimning qat'iy oyligi xodim kartasida kiritiladi. Bu yerda faqat hisob + bonus/jarima.
 */
export function PayrollTab() {
  const cur = currentMonth()
  const [month, setMonth] = useState(cur)
  const [data, setData] = useState<Payroll | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  const [adjusting, setAdjusting] = useState<PayrollRow | null>(null)
  const [exporting, setExporting] = useState(false)

  /** Hisobot: xodimlar oyligi (oylik · bonus · jarima · hisoblangan · berilgan · qoldiq) + har bonus/jarima sababi bilan. */
  const exportStaff = async () => {
    if (!data || exporting) return
    setExporting(true)
    try {
      const items = await getSalaryAdjustments(month)
      const byUser = new Map<string, SalaryAdjustment[]>()
      for (const a of items) byUser.set(a.userId, [...(byUser.get(a.userId) ?? []), a])
      const reasons = (id: string, kind: SalaryAdjustmentKind) =>
        (byUser.get(id) ?? []).filter((a) => a.kind === kind).map((a) => `${a.reason} (${formatMoney(a.amount)})`).join('; ')
      exportToCsv(
        `xodimlar-oyligi-${month}.csv`,
        ['F.I.Sh', 'Lavozim', 'Oylik', 'Bonus', 'Bonus sabablari', 'Jarima', 'Jarima sabablari', 'Hisoblangan', 'Berilgan', 'Qoldiq'],
        data.staff.map((r) => [
          r.name, r.role, String(r.base ?? r.accrued), String(r.bonus ?? 0), reasons(r.id, 'bonus'),
          String(r.fine ?? 0), reasons(r.id, 'fine'), String(r.accrued), String(r.paid), String(Math.max(0, r.remaining)),
        ]),
      )
    } catch (e) {
      setError(apiErrorMessage(e, "Hisobotni yuklab bo'lmadi"))
    } finally {
      setExporting(false)
    }
  }

  useEffect(() => {
    let active = true
    setLoading(true)
    getPayroll(month)
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Reestrni yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [month, tick])

  const table = (title: string, rows: PayrollRow[], staff: boolean) => (
    <Card title={title} sub={`${rows.length} kishi · ${formatMonth(month)}`} tight>
      {rows.length === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">Ro'yxat bo'sh</p>
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>F.I.Sh</th>
                {staff && (
                  <>
                    <th className="num">Oylik</th>
                    <th className="num">Bonus</th>
                    <th className="num">Jarima</th>
                  </>
                )}
                <th className="num">Hisoblangan</th>
                <th className="num">Avans</th>
                <th className="num">Yakuniy</th>
                <th className="num">Qoldiq</th>
                <th>Holat</th>
                {staff && <th className="text-right">Amal</th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td>
                    <div className="font-medium text-slate-700">{r.name}</div>
                    <div className="text-xs text-slate-400">{r.role} · {SOURCE_LABEL[r.source] ?? r.source}</div>
                  </td>
                  {staff && (
                    <>
                      <td className="num font-mono tabular-nums">{money(r.base ?? r.accrued)}</td>
                      <td className="num font-mono tabular-nums text-emerald-600">{r.bonus ? `+${formatMoney(r.bonus)}` : '—'}</td>
                      <td className="num font-mono tabular-nums text-rose-600">{r.fine ? `−${formatMoney(r.fine)}` : '—'}</td>
                    </>
                  )}
                  <td className="num font-mono font-semibold tabular-nums">{money(r.accrued)}</td>
                  <td className="num font-mono tabular-nums">{money(r.advance)}</td>
                  <td className="num font-mono tabular-nums">
                    {money(r.final)}
                    {r.other > 0 && <div className="text-xs text-slate-400" title="Turi belgilanmagan to'lovlar">+{formatMoney(r.other)}</div>}
                  </td>
                  <td className={cn('num font-mono font-semibold tabular-nums', r.remaining > 0 ? 'text-rose-600' : 'text-slate-500')}>
                    {money(Math.max(0, r.remaining))}
                  </td>
                  <td>
                    <Badge tone={STATUS[r.status].tone}>{STATUS[r.status].label}</Badge>
                    {(r.pending ?? 0) > 0 && (
                      <div className="mt-0.5 inline-flex items-center gap-1 text-xs font-medium text-amber-600" title="Chegaradan katta to'lov — mas'ul tasdiqlagach «berilgan»ga tushadi">
                        <Hourglass className="h-3 w-3" /> Tasdiq kutmoqda: {formatMoney(r.pending ?? 0)}
                      </div>
                    )}
                  </td>
                  {staff && (
                    <td className="text-right">
                      <Button variant="secondary" className="!px-2.5 !py-1 text-xs" onClick={() => setAdjusting(r)}>
                        <Scale className="h-3.5 w-3.5" /> Bonus / jarima
                      </Button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )

  return (
    <div className="space-y-4">
      <div className="toolbar flex-wrap">
        <span className="text-sm font-medium text-slate-600">Oy:</span>
        <Button variant="secondary" onClick={() => setMonth((m) => shiftMonth(m, -1))} aria-label="Oldingi oy">
          <ChevronLeft className="h-4 w-4" />
        </Button>
        <span className="min-w-28 text-center text-sm font-semibold text-slate-700">{formatMonth(month)}</span>
        <Button variant="secondary" onClick={() => setMonth((m) => shiftMonth(m, 1))} disabled={month >= cur} aria-label="Keyingi oy">
          <ChevronRight className="h-4 w-4" />
        </Button>
        <Button variant="secondary" onClick={() => setTick((n) => n + 1)} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
        <Button variant="secondary" onClick={() => void exportStaff()} disabled={!data || exporting}>
          <Download className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda...' : 'Xodimlar hisoboti (Excel)'}
        </Button>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {/* 2026-11-12 (egasi): oylik FAQAT kassadan beriladi — bu sahifa kim qancha olishini ko'rsatadi. */}
      <p className="rounded-lg bg-sky-50 px-3 py-2 text-sm text-sky-800">
        Oylik <b>Moliya → Kassa → «Yangi amal» → Chiqim → Maosh</b> orqali beriladi (o'qituvchi yoki xodimni tanlab, «qaysi oy
        uchun» bilan). Bu yerda — kim qancha olishi, berilgani va qoldig'i.
      </p>

      {!data ? (error ? null : <Loader />) : (
        <div className={cn('space-y-4', loading && 'opacity-60')}>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <StatCard label="Hisoblangan maosh" value={formatMoney(data.accrued)} icon={Wallet}
              hint={(data.bonus ?? 0) || (data.fine ?? 0)
                ? `Xodimlar: bonus +${formatMoney(data.bonus ?? 0)} · jarima −${formatMoney(data.fine ?? 0)}`
                : undefined} />
            <StatCard label="Avans berilgan" value={formatMoney(data.advance)} icon={HandCoins} iconBg="bg-sky-50" iconColor="text-sky-600" />
            <StatCard label="Jami berilgan" value={formatMoney(data.paid)} icon={HandCoins} iconBg="bg-indigo-50" iconColor="text-indigo-600"
              hint={`Yakuniy: ${formatMoney(data.final)}`} />
            <StatCard label="Berilishi kerak" value={formatMoney(data.remaining)} icon={Users} iconBg="bg-rose-50" iconColor="text-rose-600" />
          </div>
          {table("O'qituvchilar", data.teachers, false)}
          {table('Xodimlar (administrator, kassir, operator)', data.staff, true)}
          <p className="text-xs text-slate-400">
            O'qituvchi maoshi — foiz/qat'iy, ushlanma va jarimalar bilan (o'qituvchi profilidagi hisob). Xodim: KPI profili bo'lsa —
            tasdiqlangan KPI oyi (oklad + bonus − jarima), aks holda qat'iy oylik; bunga «Bonus / jarima»da qo'lda kiritilganlar
            qo'shiladi (hisoblangan = oylik + bonus − jarima). Chiqim tasdig'i chegarasidan katta to'lov mas'ul tasdiqlagach
            hisobga tushadi. Xodimning qat'iy oyligi — «Xodimlar» bo'limida (xodim kartasi).
          </p>
        </div>
      )}

      {adjusting && (
        <AdjustmentsModal
          key={`${adjusting.id}:${month}`}
          row={adjusting}
          month={month}
          onClose={() => setAdjusting(null)}
          onChanged={() => setTick((n) => n + 1)}
        />
      )}
    </div>
  )
}

/**
 * «Bonus / jarima» — xodim oyligiga QO'LDA (2026-11-10, egasi): har biri summa + SABAB bilan. Hisoblangan = oylik (qat'iy
 * yoki KPI) + bonuslar − jarimalar. Ruxsat serverda (o'z oyligiga / superadminga / eski oyga — faqat superadmin).
 */
function AdjustmentsModal({ row, month, onClose, onChanged }: {
  row: PayrollRow
  month: string
  onClose: () => void
  onChanged: () => void
}) {
  const [items, setItems] = useState<SalaryAdjustment[] | null>(null)
  const [loadError, setLoadError] = useState('')
  const [kind, setKind] = useState<SalaryAdjustmentKind>('bonus')
  const [amount, setAmount] = useState<number | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const inFlight = useRef(false)

  useEffect(() => {
    let active = true
    getSalaryAdjustments(month, row.id)
      .then((d) => { if (active) setItems(d) })
      .catch((e) => { if (active) setLoadError(apiErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")) })
    return () => { active = false }
  }, [month, row.id])

  const list = items ?? []
  const bonus = list.filter((a) => a.kind === 'bonus').reduce((s, a) => s + a.amount, 0)
  const fine = list.filter((a) => a.kind === 'fine').reduce((s, a) => s + a.amount, 0)
  const base = row.base ?? row.accrued
  const total = Math.max(0, base + bonus - fine)

  const add = async () => {
    if (!amount || amount <= 0 || reason.trim().length < 3 || inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setError('')
    try {
      const created = await addSalaryAdjustment(row.id, { kind, amount, reason: reason.trim(), month })
      setItems((cur) => [...(cur ?? []), created])
      setAmount(null)
      setReason('')
      onChanged()
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  const remove = async (a: SalaryAdjustment) => {
    if (busy || !confirm(`${a.kind === 'bonus' ? 'Bonus' : 'Jarima'} (${formatMoney(a.amount)} — ${a.reason}) o'chirilsinmi?`)) return
    setBusy(true)
    setError('')
    try {
      await deleteSalaryAdjustment(a.id)
      setItems((cur) => (cur ?? []).filter((x) => x.id !== a.id))
      onChanged()
    } catch (e) {
      setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const input = 'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm outline-none focus:border-brand-400'
  return (
    <Modal open onClose={onClose} size="md" title={`Bonus va jarima — ${row.name}`}
      footer={<Button variant="secondary" onClick={onClose}>Yopish</Button>}>
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-2 rounded-xl bg-slate-50 p-3 text-sm sm:grid-cols-4">
          <div><p className="text-xs text-slate-400">Oylik</p><p className="font-semibold tabular-nums">{formatMoney(base)}</p></div>
          <div><p className="text-xs text-slate-400">Bonus</p><p className="font-semibold tabular-nums text-emerald-600">+{formatMoney(bonus)}</p></div>
          <div><p className="text-xs text-slate-400">Jarima</p><p className="font-semibold tabular-nums text-rose-600">−{formatMoney(fine)}</p></div>
          <div><p className="text-xs text-slate-400">Jami ({formatMonth(month)})</p><p className="font-bold tabular-nums">{formatMoney(total)}</p></div>
        </div>
        {row.source === 'none' && (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-700">
            Bu xodimga hali oylik belgilanmagan — avval «Oylik» tugmasi bilan qat'iy summani kiriting.
          </p>
        )}

        {loadError && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{loadError}</p>}
        {!items && !loadError ? <Loader /> : list.length > 0 && (
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-100">
            {list.map((a) => (
              <li key={a.id} className="flex items-start gap-3 px-3 py-2 text-sm">
                <span className={cn('shrink-0 font-mono font-semibold tabular-nums', a.kind === 'bonus' ? 'text-emerald-600' : 'text-rose-600')}>
                  {a.kind === 'bonus' ? '+' : '−'}{formatMoney(a.amount)}
                </span>
                <span className="min-w-0 flex-1 break-words text-slate-700">
                  {a.reason}
                  {a.createdByName && <span className="block text-xs text-slate-400">{a.createdByName} · {a.createdAt.slice(0, 10)}</span>}
                </span>
                <button type="button" onClick={() => void remove(a)} disabled={busy}
                  className="shrink-0 rounded p-1 text-slate-400 hover:bg-rose-50 hover:text-rose-600" aria-label="O'chirish">
                  <Trash2 className="h-4 w-4" />
                </button>
              </li>
            ))}
          </ul>
        )}

        <div className="space-y-3 rounded-xl border border-slate-200 p-3">
          <p className="text-sm font-medium text-slate-700">Qo'shish ({formatMonth(month)} oyligiga)</p>
          <div className="flex gap-2">
            {(['bonus', 'fine'] as const).map((k) => (
              <button key={k} type="button" onClick={() => setKind(k)}
                className={cn('flex-1 rounded-lg px-3 py-2 text-sm font-medium ring-1 transition',
                  kind === k
                    ? k === 'bonus' ? 'bg-emerald-50 text-emerald-700 ring-emerald-300' : 'bg-rose-50 text-rose-700 ring-rose-300'
                    : 'bg-white text-slate-600 ring-slate-200 hover:bg-slate-50')}>
                {k === 'bonus' ? '+ Bonus' : '− Jarima'}
              </button>
            ))}
          </div>
          <MoneyInput label="Summa" value={amount} onChange={setAmount} />
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Sabab</span>
            <input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={300} className={input}
              placeholder={kind === 'bonus' ? 'Masalan: reja bajarildi' : 'Masalan: 3 marta kechikish'} />
          </label>
          {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
          <Button onClick={() => void add()} disabled={busy || !amount || amount <= 0 || reason.trim().length < 3} className="w-full">
            {busy ? 'Saqlanmoqda...' : kind === 'bonus' ? "Bonusni qo'shish" : "Jarimani qo'shish"}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
