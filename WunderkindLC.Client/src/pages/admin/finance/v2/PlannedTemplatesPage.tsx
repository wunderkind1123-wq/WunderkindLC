import { useMemo, useRef, useState } from 'react'
import { IconPencil, IconTrash } from '@tabler/icons-react'
import { getTransactionTypes } from '@/api/services/finance'
import {
  createPlannedTemplate,
  deletePlannedTemplate,
  getPlannedTemplates,
  updatePlannedTemplate,
  type PlannedTemplate,
  type PlannedTemplatePayload,
} from '@/api/services/financeV2'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { DataTable } from '@/components/ui/list/DataTable'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { TintedIconButton } from '@/components/ui/list/TintedIconButton'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { MoneyInput, NumberInput } from '@/components/ui/NumberInput'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { useAsync } from '@/hooks/useAsync'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn } from '@/lib/utils'
import { dmy, money } from './financeV2Format'

/** Maosh/avans va pul harakati bo'lmagan turlar shablonga qo'yilmaydi (server ham rad etadi). */
const BLOCKED_CATEGORIES = new Set(['salary', 'penalty', 'refund', 'book'])

/**
 * Moliya → Ma'lumotlar → «Rejali xarajat shablonlari» (`/admin/moliya/rejali-xarajat-shablonlari`): oylik takrorlanadigan
 * xarajatlar (ijara, kommunal, soliq). Faol shablon har oyning 1-kunida TO'LIQ summada P&L 2.0 ga hisoblanadi; shu turdagi
 * haqiqiy chiqimlar uning «to'langan» qismi. Ish haqi bu yerda emas — maosh reestridan avtomatik.
 * ⚠️ Eski «Rejalashtirilgan xarajatlar» (bir martalik reja) bilan aralashtirmang — u alohida sahifa.
 */
export function PlannedTemplatesPage() {
  const { can } = usePerm()
  const canEdit = can('finance.main', 'create')
  const [tick, setTick] = useState(0)
  const res = useAsync(getPlannedTemplates, [tick])
  const types = useAsync(getTransactionTypes, [])
  const [q, setQ] = useState('')
  const [editing, setEditing] = useState<PlannedTemplate | null>(null)
  const [open, setOpen] = useState(false)

  const rows = useMemo(() => {
    const s = q.trim().toLowerCase()
    return (res.data ?? []).filter((r) => !s || r.name.toLowerCase().includes(s) || r.typeName.toLowerCase().includes(s))
  }, [res.data, q])
  const pg = usePagination(rows, 20)

  const remove = async (r: PlannedTemplate) => {
    if (!window.confirm(`«${r.name}» shablonini o'chirasizmi? O'tgan oylarning P&L hisobi o'zgarmaydi.`)) return
    try {
      await deletePlannedTemplate(r.id)
      setTick((t) => t + 1)
    } catch (err) {
      alert(apiErrorMessage(err, "O'chirib bo'lmadi"))
    }
  }

  if (res.error) return <p className="text-[13px] text-red-600">{res.error}</p>
  if (!res.data) return <Loader className="min-h-[240px]" />

  return (
    <div>
      <div className="mb-1 flex items-center justify-between">
        <h1 className="text-[18px] font-semibold text-black">Rejali xarajat shablonlari</h1>
      </div>
      <p className="mb-2.5 text-[12px] text-[#6b7280]">
        Oylik takrorlanadigan xarajatlar. Faol shablon har oyning 1-sanasida P&L 2.0 ga to'liq summada hisoblanadi (to'langan-to'lanmaganidan qat'i
        nazar); shu turdagi haqiqiy chiqimlar uning «to'langan» qismi bo'ladi. Ish haqi maosh reestridan avtomatik olinadi.
      </p>

      <ListToolbar
        addLabel={canEdit ? "Qo'shish" : undefined}
        onAdd={
          canEdit
            ? () => {
                setEditing(null)
                setOpen(true)
              }
            : undefined
        }
        left={
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Nomi yoki turi bo'yicha qidirish"
            className="h-[34px] w-full max-w-[320px] rounded-lg border border-black/25 bg-white px-3 text-[13px] outline-none focus:border-brand-600"
          />
        }
      />
      <div className="mb-2 flex justify-end">
        <TotalPill total={rows.length} />
      </div>

      <DataTable
        rows={pg.paged}
        rowKey={(r) => r.id}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        columns={[
          { key: 'name', header: 'Nomi', render: (r) => <span className="font-medium">{r.name}</span> },
          { key: 'amount', header: 'Summa (oyiga)', align: 'right', render: (r) => <span className="tabular-nums">{money(r.amount)}</span> },
          { key: 'type', header: 'Amaliyot turi', render: (r) => r.typeName },
          { key: 'start', header: 'Boshlanish sanasi', render: (r) => dmy(r.startDate) },
          { key: 'end', header: 'Tugash sanasi', render: (r) => (r.endDate ? dmy(r.endDate) : 'Muddatsiz') },
          {
            key: 'status',
            header: 'Holati',
            render: (r) => (
              <span className={cn('rounded-full px-2 py-0.5 text-[11px] font-semibold', r.status === 'active' ? 'bg-[#e8f8ee] text-[#15803d]' : 'bg-[#f0f2f2] text-[#6b7280]')}>
                {r.status === 'active' ? 'Faol' : 'Faol emas'}
              </span>
            ),
          },
          {
            key: 'actions',
            header: '',
            align: 'right',
            render: (r) =>
              canEdit ? (
                <div className="flex justify-end gap-1.5">
                  <TintedIconButton
                    label="Tahrirlash"
                    onClick={() => {
                      setEditing(r)
                      setOpen(true)
                    }}
                  >
                    <IconPencil className="h-4 w-4" />
                  </TintedIconButton>
                  <TintedIconButton label="O'chirish" className="border-[#e34a29]/20 bg-[#e34a29]/10 text-[#e34a29] hover:bg-[#e34a29]/15" onClick={() => remove(r)}>
                    <IconTrash className="h-4 w-4" />
                  </TintedIconButton>
                </div>
              ) : null,
          },
        ]}
        footer={<TablePagination {...pg} />}
      />

      {open && (
        <TemplateForm
          editing={editing}
          types={(types.data ?? []).filter((t) => t.direction === 'expense' && t.kind === 'chiqim' && !BLOCKED_CATEGORIES.has(t.baseCategory))}
          onClose={() => setOpen(false)}
          onSaved={() => {
            setOpen(false)
            setTick((t) => t + 1)
          }}
        />
      )}
    </div>
  )
}

function TemplateForm({
  editing,
  types,
  onClose,
  onSaved,
}: {
  editing: PlannedTemplate | null
  types: { id: string; name: string; baseCategory: string }[]
  onClose: () => void
  onSaved: () => void
}) {
  const [amount, setAmount] = useState<number | null>(editing?.amount ?? null)
  const [reminder, setReminder] = useState<number | null>(editing?.reminderDay ?? null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const busy = useRef(false)

  const submit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    if (busy.current) return
    const f = new FormData(e.currentTarget)
    if (amount === null || amount <= 0) {
      setError('Summani kiriting')
      return
    }
    const payload: PlannedTemplatePayload = {
      name: String(f.get('name') ?? '').trim(),
      typeId: String(f.get('typeId') ?? '') || null,
      category: null,
      amount,
      startDate: String(f.get('startDate') ?? ''),
      endDate: String(f.get('endDate') ?? ''),
      status: (String(f.get('status') ?? 'active') as 'active' | 'inactive'),
      reminderDay: reminder,
      note: String(f.get('note') ?? ''),
    }
    busy.current = true
    setSaving(true)
    setError(null)
    try {
      if (editing) await updatePlannedTemplate(editing.id, payload)
      else await createPlannedTemplate(payload)
      onSaved()
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      busy.current = false
      setSaving(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={editing ? 'Shablonni tahrirlash' : "Rejali xarajat qo'shish"}>
      <form onSubmit={submit} className="space-y-3">
        <Input name="name" label="Nomi" required defaultValue={editing?.name ?? ''} maxLength={200} placeholder="masalan: Bino ijarasi" />
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Select name="typeId" label="Tranzaksiya turi" required defaultValue={editing?.typeId ?? ''}>
            <option value="" disabled>
              Tanlang…
            </option>
            {types.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </Select>
          <MoneyInput label="Miqdor (oyiga)" required value={amount} onChange={setAmount} />
        </div>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Select name="frequency" label="Davriylik" defaultValue="monthly" disabled>
            <option value="monthly">Oylik</option>
          </Select>
          <Select name="status" label="Holati" defaultValue={editing?.status ?? 'active'}>
            <option value="active">Faol</option>
            <option value="inactive">Faol emas</option>
          </Select>
        </div>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Input name="startDate" label="Boshlanish sanasi" type="date" required defaultValue={editing?.startDate ?? ''} />
          <Input name="endDate" label="Tugash sanasi (ixtiyoriy)" type="date" defaultValue={editing?.endDate ?? ''} />
        </div>
        <NumberInput label="Eslatma kuni (1–28, ixtiyoriy)" value={reminder} onChange={setReminder} />
        <Textarea name="note" label="Izoh" rows={2} defaultValue={editing?.note ?? ''} />
        {editing && <p className="text-[12px] text-[#6b7280]">Summa o'zgarsa — faqat shu oydan boshlab; o'tgan oylarning hisobi o'zgarmaydi.</p>}
        {error && <p className="text-[13px] text-red-600">{error}</p>}
        <div className="flex justify-end gap-2 pt-1">
          <Button type="button" variant="secondary" onClick={onClose}>
            Orqaga
          </Button>
          <Button type="submit" disabled={saving}>
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
