import { useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { IconChevronDown, IconChevronRight, IconInfoCircle, IconPencil, IconPlus } from '@tabler/icons-react'
import { correctPlannedAccrual, type ExpenseLine, type Pnl } from '@/api/services/financeV2'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { MoneyInput } from '@/components/ui/NumberInput'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn } from '@/lib/utils'
import { delta, money, pct, toneCls } from './financeV2Format'

/**
 * Rejali xarajatlar tabi (faqat ko'rish + tuzatish). Shablonlar Moliya → Ma'lumotlar → «Rejali xarajat shablonlari» da.
 * Raqamlar P&L jadvalidagi xarajat bo'limi bilan AYNAN bir xil (server `PnlEngine.Compute`).
 */
export function PnlPlannedTab({ p, onChanged }: { p: Pnl; onChanged: () => void }) {
  const { can } = usePerm()
  const canEdit = can('finance.main', 'edit')
  const [correcting, setCorrecting] = useState<{ id: string; name: string; planned: number; current: number } | null>(null)
  const ex = p.total.expenses
  const month = `${p.period.year}-${String(p.period.month).padStart(2, '0')}`

  const templateLines = ex.lines.filter((l) => !l.isUnplanned && !l.isPayroll)
  const templateCount = templateLines.reduce((a, l) => a + l.templates.length, 0)
  const planned = templateLines.reduce((a, l) => a + l.planned, 0)
  const accrued = templateLines.reduce((a, l) => a + l.current, 0)
  const unplanned = ex.lines.filter((l) => l.isUnplanned).reduce((a, l) => a + l.current, 0)
  const corrected = templateLines.reduce((a, l) => a + l.templates.filter((t) => t.isCorrected).length, 0)
  const groups = [...new Set(ex.lines.map((l) => l.group))]

  return (
    <div className="space-y-3">
      <div className="flex items-start gap-2 rounded-xl border border-[#bfdbfe] bg-[#eff6ff] px-3 py-2 text-[12px] text-[#1e40af]">
        <IconInfoCircle className="mt-0.5 h-4 w-4 shrink-0" />
        <span>
          Xarajat to'langanmi-yo'qmi — farqi yo'q: shablon kiritilgan bo'lsa, tizim har oyning 1-sanasida uni P&L ga hisoblab qo'yadi. Shu turdagi
          haqiqiy chiqimlar shablonning «to'langan» qismi bo'ladi (ikki marta sanalmaydi). Ish haqi maosh reestridan avtomatik olinadi. Hisob-faktura
          boshqacha kelsa — «Tuzatish».
        </span>
      </div>

      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 xl:grid-cols-4">
        <Card title="Oylik reja" value={money(planned)} sub={`${templateLines.length} ta yo'nalish · ${templateCount} ta shablon`} />
        <Card
          title="Hisoblangan"
          value={money(accrued)}
          sub={<span className={toneCls[delta(accrued - planned, false).tone]}>rejadan {delta(accrued - planned, false).text}</span>}
        />
        <Card title="Rejasiz xarajat" value={money(unplanned)} sub={`jami xarajatning ${pct(ex.current ? (unplanned / ex.current) * 100 : null)}`} />
        <Card title="Rejadan chetlashgan" value={String(corrected)} sub="tuzatilgan shablonlar" />
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-[13px] font-bold">
          Xarajat shablonlari — {String(p.period.month).padStart(2, '0')}.{p.period.year}
        </span>
        {canEdit && (
          <Link
            to="/admin/moliya/rejali-xarajat-shablonlari"
            className="inline-flex min-h-[31px] items-center gap-1.5 rounded-lg bg-brand-600 px-3 text-xs font-medium text-white hover:bg-brand-700"
          >
            <IconPlus className="h-4 w-4" /> Shablon qo'shish
          </Link>
        )}
      </div>

      <div className="overflow-auto rounded-xl border border-[#dbe0e6] bg-white">
        <table className="w-full min-w-[720px] border-collapse text-[13px]">
          <thead>
            <tr className="bg-[#f5f6f7] text-[11.5px] font-semibold uppercase text-[#374151]">
              <th className="px-4 py-2.5 text-left">Nomi</th>
              <th className="px-3 py-2.5 text-right">Jami reja</th>
              <th className="px-3 py-2.5 text-right">Hisoblangan</th>
              <th className="px-3 py-2.5 text-right">To'langan</th>
              <th className="px-3 py-2.5 text-right">Farq</th>
              <th className="px-3 py-2.5 text-left">Holat</th>
            </tr>
          </thead>
          <tbody>
            {groups.length === 0 && (
              <tr>
                <td colSpan={6} className="px-4 py-8 text-center text-[#6b7280]">
                  Bu oy uchun xarajat yo'q. Shablon qo'shing — u har oy avtomatik hisoblanadi.
                </td>
              </tr>
            )}
            {groups.map((g) => {
              const lines = ex.lines.filter((l) => l.group === g)
              return [
                <tr key={`g-${g}`} className="bg-[#fafafa]">
                  <td colSpan={6} className="border-y border-[#eceff1] px-4 py-1.5 text-[11.5px] font-bold uppercase tracking-wide text-[#374151]">
                    {g} <span className="font-semibold text-[#9ca3af]">· {lines.length} ta</span>
                  </td>
                </tr>,
                ...lines.map((l) => <LineRow key={l.id} l={l} canEdit={canEdit} onCorrect={setCorrecting} />),
              ]
            })}
            <tr className="bg-[#fdeceb] font-bold">
              <td className="px-4 py-2.5">Jami</td>
              <td className="px-3 text-right tabular-nums">{money(ex.planned)}</td>
              <td className="px-3 text-right tabular-nums">{money(ex.current)}</td>
              <td className="px-3 text-right tabular-nums">{money(ex.paid)}</td>
              <td className={cn('px-3 text-right tabular-nums', toneCls[delta(ex.current - ex.planned, false).tone])}>{delta(ex.current - ex.planned, false).text}</td>
              <td />
            </tr>
          </tbody>
        </table>
      </div>

      {correcting && (
        <CorrectionModal
          month={month}
          target={correcting}
          onClose={() => setCorrecting(null)}
          onSaved={() => {
            setCorrecting(null)
            onChanged()
          }}
        />
      )}
    </div>
  )
}

function Card({ title, value, sub }: { title: string; value: string; sub?: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white p-3 shadow-[0_1px_2px_rgba(0,0,0,0.04)]">
      <div className="text-[12px] font-semibold text-[#6b7280]">{title}</div>
      <div className="truncate text-[20px] font-bold tabular-nums">{value}</div>
      {sub && <div className="text-[11.5px] text-[#6b7280]">{sub}</div>}
    </div>
  )
}

function LineRow({
  l,
  canEdit,
  onCorrect,
}: {
  l: ExpenseLine
  canEdit: boolean
  onCorrect: (t: { id: string; name: string; planned: number; current: number }) => void
}) {
  const [open, setOpen] = useState(false)
  const many = l.templates.length > 1
  const d = delta(l.current - l.planned, false)
  const status = l.isPayroll ? 'Maosh reestri' : l.isUnplanned ? 'Rejasiz' : l.templates.every((t) => t.hasAccrual) ? 'Faol' : 'Hisoblanmagan'
  return (
    <>
      <tr className="border-b border-[#f0f2f2]">
        <td className="px-4 py-2">
          <div className="flex flex-wrap items-center gap-1.5">
            {many ? (
              <button type="button" onClick={() => setOpen(!open)} className="inline-flex items-center gap-1 font-medium">
                {open ? <IconChevronDown className="h-4 w-4" /> : <IconChevronRight className="h-4 w-4" />}
                {l.name}
                <span className="text-[11px] text-[#9ca3af]">({l.templates.length} ta)</span>
              </button>
            ) : (
              <span className="font-medium">{l.templates.length === 1 && l.templates[0].name !== l.name ? `${l.name} — ${l.templates[0].name}` : l.name}</span>
            )}
            {l.isUnplanned && <Badge cls="bg-[#fef3c7] text-[#92400e]">REJASIZ</Badge>}
            {l.isCorrected && !l.isPayroll && <Badge cls="bg-[#e0e7ff] text-[#3730a3]">TUZATILGAN</Badge>}
            {!many && l.templates.length === 1 && canEdit && (
              <button
                type="button"
                onClick={() => onCorrect(l.templates[0])}
                className="ml-1 inline-flex items-center gap-0.5 rounded border border-[#dbe0e6] px-1.5 py-[1px] text-[11px] text-brand-600 hover:bg-[#eef2ff]"
              >
                <IconPencil className="h-3 w-3" /> Tuzatish
              </button>
            )}
          </div>
        </td>
        <td className="px-3 text-right tabular-nums text-[#4b5563]">{money(l.planned)}</td>
        <td className="px-3 text-right font-medium tabular-nums">{money(l.current)}</td>
        <td className="px-3 text-right tabular-nums text-[#4b5563]">{money(l.paid)}</td>
        <td className={cn('px-3 text-right font-semibold tabular-nums', l.isUnplanned ? 'text-[#9ca3af]' : toneCls[d.tone])}>{l.isUnplanned ? '—' : d.text}</td>
        <td className="px-3">
          <span
            className={cn(
              'rounded-full px-2 py-0.5 text-[11px] font-semibold',
              status === 'Faol' ? 'bg-[#e8f8ee] text-[#15803d]' : status === 'Rejasiz' ? 'bg-[#fef3c7] text-[#92400e]' : status === 'Maosh reestri' ? 'bg-[#eef2ff] text-brand-600' : 'bg-[#f0f2f2] text-[#6b7280]',
            )}
          >
            {status}
          </span>
        </td>
      </tr>
      {open &&
        l.templates.map((t) => (
          <tr key={t.id} className="border-b border-[#f0f2f2] bg-[#fafafa] text-[12.5px]">
            <td className="py-1.5 pl-11 pr-4 text-[#4b5563]">
              {l.name} — {t.name}
              {t.isCorrected && <Badge cls="ml-1 bg-[#e0e7ff] text-[#3730a3]">TUZATILGAN</Badge>}
              {canEdit && (
                <button type="button" onClick={() => onCorrect(t)} className="ml-2 text-[11px] text-brand-600 hover:underline">
                  Tuzatish
                </button>
              )}
            </td>
            <td className="px-3 text-right tabular-nums text-[#6b7280]">{money(t.planned)}</td>
            <td className="px-3 text-right tabular-nums">{money(t.current)}</td>
            <td />
            <td className={cn('px-3 text-right tabular-nums', toneCls[delta(t.current - t.planned, false).tone])}>{delta(t.current - t.planned, false).text}</td>
            <td />
          </tr>
        ))}
    </>
  )
}

function Badge({ cls, children }: { cls: string; children: React.ReactNode }) {
  return <span className={cn('rounded px-1.5 py-[1px] text-[9.5px] font-bold tracking-wide', cls)}>{children}</span>
}

function CorrectionModal({
  month,
  target,
  onClose,
  onSaved,
}: {
  month: string
  target: { id: string; name: string; planned: number; current: number }
  onClose: () => void
  onSaved: () => void
}) {
  const [amount, setAmount] = useState<number | null>(target.current !== target.planned ? target.current : null)
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const busy = useRef(false)

  const save = async (value: number | null) => {
    if (busy.current) return
    busy.current = true
    setSaving(true)
    setError(null)
    try {
      await correctPlannedAccrual(target.id, month, value, note)
      onSaved()
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      busy.current = false
      setSaving(false)
    }
  }

  return (
    <Modal open onClose={onClose} size="sm" title={`Tuzatish — ${target.name}`}>
      <div className="space-y-3">
        <p className="text-[12.5px] text-[#4b5563]">
          Reja: <b>{money(target.planned)} so'm</b>. Hisob-faktura bo'yicha haqiqiy summani kiriting — P&L shu oy uchun shuni oladi. O'tgan oylar
          o'zgarmaydi.
        </p>
        <MoneyInput label="Haqiqiy summa" value={amount} onChange={setAmount} />
        <Input label="Izoh (ixtiyoriy)" value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} />
        {error && <p className="text-[13px] text-red-600">{error}</p>}
        <div className="flex flex-wrap justify-between gap-2 pt-1">
          {target.current !== target.planned ? (
            <Button type="button" variant="secondary" disabled={saving} onClick={() => save(null)}>
              Tuzatishni olib tashlash
            </Button>
          ) : (
            <span />
          )}
          <div className="flex gap-2">
            <Button type="button" variant="secondary" onClick={onClose}>
              Orqaga
            </Button>
            <Button type="button" disabled={saving || amount === null} onClick={() => save(amount)}>
              {saving ? 'Saqlanmoqda…' : 'Saqlash'}
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  )
}
