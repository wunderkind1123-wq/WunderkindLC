import { useRef, useState } from 'react'
import { IconPencil, IconRefresh } from '@tabler/icons-react'
import {
  getFinanceCategories,
  reclassifyFinanceCategories,
  updateFinanceCategory,
  type CashFlowActivity,
  type CategoryRow,
  type PnlTreatment,
} from '@/api/services/financeV2'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { NumberInput } from '@/components/ui/NumberInput'
import { useAsync } from '@/hooks/useAsync'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn } from '@/lib/utils'
import { money } from './financeV2Format'

const ACTIVITY_LABEL: Record<CashFlowActivity, string> = {
  operating: 'Operatsion',
  investing: 'Investitsiya',
  financing: 'Moliyalashtirish',
  excluded: 'Pul oqimiga kirmaydi',
}

const TREATMENT_LABEL: Record<PnlTreatment, string> = {
  tuitionRevenue: "O'quvchi to'lovi (kutuv orqali)",
  otherRevenue: 'Boshqa daromad',
  otherRevenueContra: 'Boshqa daromaddan ayiriladi (qaytarish)',
  expense: 'Xarajat',
  salary: 'Ish haqi (maosh reestridan)',
  salaryAdvance: "Avans (P&L ga kirmaydi)",
  revenueContra: 'Qaytarish (sof kutuvdan ayiriladi)',
  nonCash: "Pul harakati emas (chegirma, vaucher, jarima)",
  excluded: "P&L ga kirmaydi",
}

const INCOME_TREATMENTS: PnlTreatment[] = ['tuitionRevenue', 'otherRevenue', 'nonCash', 'excluded']
const EXPENSE_TREATMENTS: PnlTreatment[] = ['expense', 'salary', 'salaryAdvance', 'revenueContra', 'otherRevenueContra', 'nonCash', 'excluded']

/**
 * Moliya → Ma'lumotlar → «Hisobot toifalari» (`/admin/moliya/hisobot-toifalari`): har kirim/chiqim turi Pul oqimi 2.0 da
 * qaysi faoliyatga, P&L 2.0 da qaysi o'ringa tushadi. Tizim yangi turlarni qoida bilan O'ZI tasniflaydi («Avto» belgisi);
 * qo'lda saqlangan yozuvni qayta tasniflash o'zgartirmaydi. Mavjud hisob-kitobga (balans, maosh, kassa) TA'SIR QILMAYDI.
 */
export function FinanceCategoriesPage() {
  const { can } = usePerm()
  const canEdit = can('finance.main', 'edit')
  const [tick, setTick] = useState(0)
  const res = useAsync(getFinanceCategories, [tick])
  const [editing, setEditing] = useState<CategoryRow | null>(null)
  const [confirm, setConfirm] = useState(false)
  const [info, setInfo] = useState<string | null>(null)

  if (res.error) return <p className="text-[13px] text-red-600">{res.error}</p>
  if (!res.data) return <Loader className="min-h-[240px]" />
  const rows = res.data

  return (
    <div className="space-y-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-[18px] font-semibold text-black">Hisobot toifalari</h1>
        {canEdit && (
          <Button variant="secondary" onClick={() => setConfirm(true)}>
            <IconRefresh className="h-4 w-4" /> Qayta avtomatik tasniflash
          </Button>
        )}
      </div>
      <p className="text-[12px] text-[#6b7280]">
        Har kirim/chiqim turi «Pul oqimi 2.0» da qaysi faoliyatga va «P&L 2.0» da qaysi qatorga tushishi. Yangi tur qo'shilsa tizim uni o'zi
        tasniflaydi («Avto»). Bu sozlama faqat hisobotlarga ta'sir qiladi — balans, maosh va kassa hisobi o'zgarmaydi.
      </p>
      {info && <p className="text-[12.5px] font-medium text-[#15803d]">{info}</p>}

      {(['income', 'expense'] as const).map((dir) => {
        const list = rows.filter((r) => r.direction === dir)
        return (
          <div key={dir} className="overflow-auto rounded-xl border border-[#dbe0e6] bg-white">
            <div className={cn('px-4 py-2 text-[12px] font-bold uppercase tracking-wide', dir === 'income' ? 'bg-[#e8f8ee] text-[#15803d]' : 'bg-[#fdeceb] text-[#b91c1c]')}>
              {dir === 'income' ? 'Kirim turlari' : 'Chiqim turlari'} · {list.length} ta
            </div>
            <table className="w-full min-w-[860px] border-collapse text-[13px]">
              <thead>
                <tr className="bg-[#f5f6f7] text-[11px] font-semibold uppercase text-[#4b5563]">
                  <th className="px-4 py-2 text-left">Nomi</th>
                  <th className="px-3 py-2 text-left">Pul oqimi faoliyati</th>
                  <th className="px-3 py-2 text-left">P&L dagi o'rni</th>
                  <th className="px-3 py-2 text-left">P&L guruhi</th>
                  <th className="px-3 py-2 text-right">Amallar</th>
                  <th className="px-3 py-2 text-right">12 oy summasi</th>
                  <th className="px-3 py-2" />
                </tr>
              </thead>
              <tbody>
                {list.map((r) => (
                  <tr key={r.id} className="border-t border-[#f0f2f2]">
                    <td className="px-4 py-2">
                      <div className="flex flex-wrap items-center gap-1.5">
                        <span className="font-medium">{r.name}</span>
                        {r.isAutoClassified ? (
                          <span title={r.autoReason} className="cursor-help rounded bg-[#eef2ff] px-1.5 py-[1px] text-[9.5px] font-bold text-brand-600">
                            AVTO
                          </span>
                        ) : (
                          <span title={`Qo'lda: ${r.updatedBy ?? ''}`} className="rounded bg-[#f0f2f2] px-1.5 py-[1px] text-[9.5px] font-bold text-[#4b5563]">
                            QO'LDA
                          </span>
                        )}
                      </div>
                      {r.name !== r.baseName && <div className="text-[11px] text-[#9ca3af]">{r.baseName}</div>}
                      {!r.typeId && <div className="text-[11px] text-[#9ca3af]">tizim toifasi (turi yo'q amallar)</div>}
                    </td>
                    <td className="px-3 py-2">
                      <span className={cn('rounded-full px-2 py-0.5 text-[11px] font-semibold', r.cashFlowActivity === 'excluded' ? 'bg-[#f0f2f2] text-[#6b7280]' : 'bg-[#eef4ff] text-[#1d4ed8]')}>
                        {ACTIVITY_LABEL[r.cashFlowActivity]}
                      </span>
                    </td>
                    <td className="px-3 py-2 text-[12.5px]">{TREATMENT_LABEL[r.pnlTreatment] ?? r.pnlTreatment}</td>
                    <td className="px-3 py-2 text-[12.5px] text-[#4b5563]">{r.pnlGroup || '—'}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{r.count}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{money(r.last12Months)}</td>
                    <td className="px-3 py-2 text-right">
                      {canEdit && (
                        <button type="button" onClick={() => setEditing(r)} className="rounded p-1 text-brand-600 hover:bg-[#eef2ff]" aria-label="Tahrirlash">
                          <IconPencil className="h-4 w-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
                {list.length === 0 && (
                  <tr>
                    <td colSpan={7} className="px-4 py-6 text-center text-[#6b7280]">
                      Tur yo'q.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )
      })}

      {editing && (
        <EditModal
          row={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            setTick((t) => t + 1)
          }}
        />
      )}
      {confirm && (
        <ReclassifyModal
          onClose={() => setConfirm(false)}
          onDone={(n) => {
            setConfirm(false)
            setInfo(n ? `${n} ta yozuv qayta tasniflandi.` : "O'zgarish yo'q — barcha avtomatik yozuvlar qoidaga mos.")
            setTick((t) => t + 1)
          }}
        />
      )}
    </div>
  )
}

function EditModal({ row, onClose, onSaved }: { row: CategoryRow; onClose: () => void; onSaved: () => void }) {
  const [activity, setActivity] = useState<CashFlowActivity>(row.cashFlowActivity)
  const [treatment, setTreatment] = useState<PnlTreatment>(row.pnlTreatment)
  const [name, setName] = useState(row.name === row.baseName ? '' : row.name)
  const [group, setGroup] = useState(row.pnlGroup ?? '')
  const [order, setOrder] = useState<number | null>(row.sortOrder)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const busy = useRef(false)
  const treatments = row.direction === 'income' ? INCOME_TREATMENTS : EXPENSE_TREATMENTS

  const save = async () => {
    if (busy.current) return
    busy.current = true
    setSaving(true)
    setError(null)
    try {
      await updateFinanceCategory(row.id, { displayName: name, cashFlowActivity: activity, pnlTreatment: treatment, pnlGroup: group, sortOrder: order ?? 0 })
      onSaved()
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      busy.current = false
      setSaving(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={row.baseName}>
      <div className="space-y-3">
        {row.isAutoClassified && <p className="rounded-lg bg-[#eef2ff] px-3 py-2 text-[12px] text-brand-600">Avtomatik: {row.autoReason}</p>}
        <Input label="Hisobotdagi nomi (bo'sh — tur nomi)" value={name} onChange={(e) => setName(e.target.value)} placeholder={row.baseName} maxLength={200} />
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Select label="Pul oqimi faoliyati" value={activity} onChange={(e) => setActivity(e.target.value as CashFlowActivity)}>
            {(Object.keys(ACTIVITY_LABEL) as CashFlowActivity[]).map((k) => (
              <option key={k} value={k}>
                {ACTIVITY_LABEL[k]}
              </option>
            ))}
          </Select>
          <Select label="P&L dagi o'rni" value={treatment} onChange={(e) => setTreatment(e.target.value as PnlTreatment)}>
            {treatments.map((k) => (
              <option key={k} value={k}>
                {TREATMENT_LABEL[k]}
              </option>
            ))}
          </Select>
        </div>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Input label="P&L guruhi (ixtiyoriy)" value={group} onChange={(e) => setGroup(e.target.value)} placeholder="masalan: Bino xarajatlari" maxLength={120} />
          <NumberInput label="Tartib" value={order} onChange={setOrder} />
        </div>
        {activity === 'excluded' && (
          <p className="text-[12px] text-[#b45309]">
            Diqqat: bu turdagi amallar kassa qoldig'ini o'zgartiradi, lekin pul oqimiga kirmaydi — hisobot kassa qoldig'idan farq qiladi.
          </p>
        )}
        {error && <p className="text-[13px] text-red-600">{error}</p>}
        <div className="flex justify-end gap-2 pt-1">
          <Button type="button" variant="secondary" onClick={onClose}>
            Orqaga
          </Button>
          <Button type="button" disabled={saving} onClick={save}>
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}

function ReclassifyModal({ onClose, onDone }: { onClose: () => void; onDone: (n: number) => void }) {
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const busy = useRef(false)
  const run = async () => {
    if (busy.current) return
    busy.current = true
    setSaving(true)
    setError(null)
    try {
      const r = await reclassifyFinanceCategories()
      onDone(r.changed)
    } catch (err) {
      setError(apiErrorMessage(err, "Bajarib bo'lmadi"))
    } finally {
      busy.current = false
      setSaving(false)
    }
  }
  return (
    <Modal open onClose={onClose} size="sm" title="Qayta avtomatik tasniflash">
      <div className="space-y-3">
        <p className="text-[13px] text-[#374151]">
          Faqat «AVTO» belgili yozuvlar qoidalar bo'yicha qayta tasniflanadi. Qo'lda saqlangan («QO'LDA») yozuvlarga tegilmaydi.
        </p>
        {error && <p className="text-[13px] text-red-600">{error}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="button" disabled={saving} onClick={run}>
            {saving ? 'Bajarilmoqda…' : 'Tasniflash'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
