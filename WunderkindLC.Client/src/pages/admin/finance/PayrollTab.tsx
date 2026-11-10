import { useEffect, useRef, useState } from 'react'
import { ChevronLeft, ChevronRight, Download, Hourglass, HandCoins, Pencil, Plus, RefreshCw, Scale, Trash2, Users, Wallet } from 'lucide-react'
import {
  addSalaryAdjustment,
  deleteSalaryAdjustment,
  getPayroll,
  getPayrollPeople,
  getSalaryAdjustments,
  setStaffSalary,
  type Payroll,
  type PayrollPerson,
  type PayrollRow,
  type SalaryAdjustment,
  type SalaryAdjustmentKind,
} from '@/api/services/financeOps'
import { createTransaction, isPendingExpense } from '@/api/services/finance'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { MoneyInput } from '@/components/ui/NumberInput'
import { DateInput } from '@/components/ui/DateInput'
import { formatMonth } from '@/config/constants'
import { useCashboxOptions, showCashboxPicker, cashboxOptionLabel } from '@/hooks/useCashboxOptions'
import { currentMonth, shiftMonth, todayIso } from '@/lib/month'
import { newRequestId } from '@/lib/requestId'
import { apiErrorMessage, cn, exportToCsv, formatMoney } from '@/lib/utils'
import { toast } from '@/lib/toast'

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
 * «To'lash» — odatdagi maosh chiqimi (chegaradan katta bo'lsa tasdiq kutadi).
 */
export function PayrollTab() {
  const cur = currentMonth()
  const [month, setMonth] = useState(cur)
  const [data, setData] = useState<Payroll | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  const [paying, setPaying] = useState<PayrollRow | null>(null)
  const [editing, setEditing] = useState<PayrollRow | null>(null)
  const [picking, setPicking] = useState(false)
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
                <th className="text-right">Amal</th>
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
                  <td className="text-right">
                    <div className="inline-flex gap-1">
                      {staff && r.source !== 'kpi' && r.source !== 'kpi-pending' && (
                        <Button variant="secondary" className="!px-2.5 !py-1 text-xs" onClick={() => setEditing(r)}>
                          <Pencil className="h-3.5 w-3.5" /> Oylik
                        </Button>
                      )}
                      {staff && (
                        <Button variant="secondary" className="!px-2.5 !py-1 text-xs" onClick={() => setAdjusting(r)}>
                          <Scale className="h-3.5 w-3.5" /> Bonus / jarima
                        </Button>
                      )}
                      <Button className="!px-2.5 !py-1 text-xs" onClick={() => setPaying(r)}>
                        <HandCoins className="h-3.5 w-3.5" /> To'lash
                      </Button>
                    </div>
                  </td>
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
        <Button variant="secondary" onClick={() => setPicking(true)}>
          <Plus className="h-4 w-4" /> Xodim oyligi
        </Button>
        <Button variant="secondary" onClick={() => void exportStaff()} disabled={!data || exporting}>
          <Download className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda...' : 'Xodimlar hisoboti (Excel)'}
        </Button>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

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
            hisobga tushadi.
          </p>
        </div>
      )}

      {paying && (
        <PayModal row={paying} month={month} onClose={() => setPaying(null)} onDone={() => { setPaying(null); setTick((n) => n + 1) }} />
      )}
      {picking && (
        <PersonPickModal
          onClose={() => setPicking(false)}
          onPick={(p) => {
            setPicking(false)
            const row = data?.staff.find((r) => r.id === p.id)
            setEditing(row ?? {
              kind: 'staff', id: p.id, name: p.fullName, role: p.position || p.role, source: 'none',
              accrued: 0, advance: 0, final: 0, other: 0, paid: 0, remaining: 0, status: 'none',
              fixedAmount: null, fixedFrom: null,
            })
          }}
        />
      )}
      {editing && (
        <SalaryModal row={editing} month={month} onClose={() => setEditing(null)} onDone={() => { setEditing(null); setTick((n) => n + 1) }} />
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

/** Maosh to'lovi — avans yoki yakuniy; odatdagi maosh chiqimi (`POST finance/transactions`). */
function PayModal({ row: initialRow, month: initialMonth, onClose, onDone }: { row: PayrollRow; month: string; onClose: () => void; onDone: () => void }) {
  // «QAYSI OY UCHUN» (2026-11-10, egasi: «oktabr oyligini noyabrda kiritdim — noyabr chiqimi bo'lib qoldi»): reestrda ochiq
  // turgan oy — faqat standart; foydalanuvchi aniq tanlaydi. Oy almashsa o'sha oy hisobi (hisoblangan/berilgan/qoldiq) qayta olinadi.
  const [month, setMonth] = useState(initialMonth)
  const [row, setRow] = useState<PayrollRow>(initialRow)
  const [rowLoading, setRowLoading] = useState(false)
  const monthOptions = Array.from({ length: 6 }, (_, i) => shiftMonth(currentMonth(), -i))
  if (!monthOptions.includes(initialMonth)) monthOptions.push(initialMonth)
  const [amount, setAmount] = useState<number | null>(initialRow.remaining > 0 ? initialRow.remaining : null)
  const pickMonth = async (m: string) => {
    setMonth(m)
    setRowLoading(true)
    setError('')
    try {
      const reg = await getPayroll(m)
      const next = [...reg.teachers, ...reg.staff].find((x) => x.kind === initialRow.kind && x.id === initialRow.id)
      const r = next ?? { ...initialRow, accrued: 0, paid: 0, remaining: 0, advance: 0, final: 0, other: 0, pending: 0 }
      setRow(r)
      setAmount(r.remaining > 0 ? r.remaining : null)
    } catch (e) {
      setError(apiErrorMessage(e, "Bu oy hisobini yuklab bo'lmadi"))
    } finally {
      setRowLoading(false)
    }
  }
  // Tur tanlanmaguncha summaga qarab: qoldiqni TO'LIQ yopsa — «yakuniy», kamroq bo'lsa — «avans».
  const [kindPicked, setKind] = useState<'advance' | 'final' | null>(null)
  const kind: 'advance' | 'final' = kindPicked ?? (row.remaining > 0 && (amount ?? 0) >= row.remaining ? 'final' : 'advance')
  const inFlight = useRef(false)
  const [date, setDate] = useState(todayIso())
  const [note, setNote] = useState('')
  const [cashbox, setCashbox] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [requestId] = useState(newRequestId)
  const cashboxes = useCashboxOptions(true)
  const cashboxId = cashbox ?? cashboxes?.defaultId ?? ''

  const submit = async () => {
    if (!amount || amount <= 0 || inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setError('')
    try {
      const res = await createTransaction({
        date, direction: 'expense', category: 'salary', amount, month,
        teacherId: row.kind === 'teacher' ? row.id : undefined,
        staffUserId: row.kind === 'staff' ? row.id : undefined,
        salaryPayKind: kind,
        note: note.trim() || `${row.name} — ${formatMonth(month)} maoshi (${kind === 'advance' ? 'avans' : 'yakuniy'})`,
        cashboxId: cashboxId || undefined,
        requestId,
      })
      if (isPendingExpense(res)) toast.info('Tasdiq kutilmoqda', res.message)
      else toast.success("Maosh berildi", formatMoney(amount))
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e, "To'lov qilib bo'lmadi"))
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  const input = 'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm outline-none focus:border-brand-400'
  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title={`Maosh berish — ${row.name}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor</Button>
          <Button onClick={() => void submit()} disabled={busy || rowLoading || !amount || amount <= 0}>{busy ? 'Saqlanmoqda...' : 'Berish'}</Button>
        </>
      }
    >
      <div className="space-y-3">
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">Qaysi oy uchun (maosh oyi)</span>
          <select value={month} onChange={(e) => void pickMonth(e.target.value)} disabled={busy || rowLoading} className={input}>
            {monthOptions.map((m) => <option key={m} value={m}>{formatMonth(m)}</option>)}
          </select>
          <span className="mt-1 block text-xs text-slate-400">
            To'lov shu oy maoshi hisobiga va moliyaviy hisobotda (P&L) shu oy xarajatiga yoziladi; «Sana» — pul berilgan kun.
          </span>
        </label>
        <p className={cn('text-sm text-slate-500', rowLoading && 'opacity-50')}>
          {formatMonth(month)}: hisoblangan {formatMoney(row.accrued)}, berilgan {formatMoney(row.paid)}, qoldiq {formatMoney(Math.max(0, row.remaining))}.
        </p>
        {(row.pending ?? 0) > 0 && (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
            Tasdiq kutayotgan to'lov bor: {formatMoney(row.pending ?? 0)}. Uni qayta kiritmang — mas'ul tasdiqlagach hisobga tushadi.
          </p>
        )}
        <MoneyInput label="Summa" value={amount} onChange={setAmount} />
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">To'lov turi</span>
          <select value={kind} onChange={(e) => setKind(e.target.value as 'advance' | 'final')} className={input}>
            <option value="advance">Avans</option>
            <option value="final">Yakuniy (oylik)</option>
          </select>
        </label>
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">Sana (berilgan kun)</span>
          <DateInput value={date} onChange={(e) => setDate(e.target.value)} />
        </label>
        {showCashboxPicker(cashboxes) && (
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Kassa</span>
            <select value={cashboxId} onChange={(e) => setCashbox(e.target.value)} className={input}>
              {cashboxes.items.map((c) => <option key={c.id} value={c.id}>{cashboxOptionLabel(c)}</option>)}
            </select>
          </label>
        )}
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">Izoh</span>
          <input value={note} onChange={(e) => setNote(e.target.value)} className={input} />
        </label>
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}

/** Xodimning qat'iy oyligi — tanlangan oydan boshlab (o'tgan oylar o'zgarmaydi). */
function SalaryModal({ row, month, onClose, onDone }: { row: PayrollRow; month: string; onClose: () => void; onDone: () => void }) {
  const [amount, setAmount] = useState<number | null>(row.fixedAmount)
  const [from, setFrom] = useState(month)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const submit = async () => {
    if (amount === null || busy) return
    setBusy(true)
    setError('')
    try {
      await setStaffSalary(row.id, amount, from)
      toast.success('Oylik saqlandi', formatMoney(amount))
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e, "Oylikni saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title={`Oylik — ${row.name}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor</Button>
          <Button onClick={() => void submit()} disabled={busy || amount === null}>{busy ? 'Saqlanmoqda...' : 'Saqlash'}</Button>
        </>
      }
    >
      <div className="space-y-3">
        <MoneyInput label="Oylik summa" value={amount} onChange={setAmount} />
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">Qaysi oydan boshlab</span>
          <input type="month" value={from} onChange={(e) => e.target.value && setFrom(e.target.value)}
            className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 font-mono text-sm outline-none focus:border-brand-400" />
        </label>
        <p className="text-xs text-slate-500">
          Tanlangan oydan boshlab amal qiladi (oldingi oylar o'zgarmaydi; o'tgan oydan boshlashni faqat superadmin qila oladi).
          KPI profili bor xodimning okladi KPI bo'limida kiritiladi.
        </p>
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}

/** «Xodim oyligi» — reestrda hali qatori yo'q akkaunt (masalan admin/superadmin) uchun oylik kiritish. */
function PersonPickModal({ onClose, onPick }: { onClose: () => void; onPick: (p: PayrollPerson) => void }) {
  const [people, setPeople] = useState<PayrollPerson[] | null>(null)
  const [error, setError] = useState('')
  const [q, setQ] = useState('')
  useEffect(() => {
    let active = true
    getPayrollPeople()
      .then((p) => { if (active) setPeople(p) })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")) })
    return () => { active = false }
  }, [])
  const shown = (people ?? []).filter((p) => p.fullName.toLowerCase().includes(q.trim().toLowerCase()))
  return (
    <Modal open onClose={onClose} size="sm" title="Kimning oyligi" footer={<Button variant="secondary" onClick={onClose}>Yopish</Button>}>
      <input
        value={q}
        onChange={(e) => setQ(e.target.value)}
        placeholder="Qidirish..."
        className="mb-2 w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm outline-none focus:border-brand-400"
      />
      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      {!people && !error ? <Loader /> : (
        <div className="max-h-72 divide-y divide-slate-100 overflow-y-auto rounded-xl border border-slate-100">
          {shown.map((p) => (
            <button key={p.id} type="button" onClick={() => onPick(p)} className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left hover:bg-slate-50">
              <span className="truncate text-sm text-slate-700">{p.fullName}</span>
              <span className="shrink-0 text-xs text-slate-400">{p.position || p.role}</span>
            </button>
          ))}
          {shown.length === 0 && <p className="px-3 py-4 text-center text-sm text-slate-400">Topilmadi</p>}
        </div>
      )}
    </Modal>
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
