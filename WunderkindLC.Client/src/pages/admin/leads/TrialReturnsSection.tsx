import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  applyLegacyTrialReturns,
  getLegacyTrialReturns,
  getTrialReturns,
  resolveTrialReturn,
  type LegacyTrialReturn,
  type TrialReturn,
} from '@/api/services/trialReturns'
import { DataTable, type DataColumn } from '@/components/ui/list/DataTable'
import { PhoneChip } from '@/components/ui/list/PhoneChip'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { useAuth } from '@/context/auth-context'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, formatDateTime, formatMoney } from '@/lib/utils'

/**
 * «SINOVDAN QAYTGANLAR» (2026-11-13, egasi: «sinovga qaytarilgan o'quvchi guruhda qolmasin va birinchi darsga o'tsin»).
 * «Sinovga qaytarish» bilan guruhdan chiqqan o'quvchilar: qaysi guruhdan, sabab, kim, qachon. Guruhga qayta qo'shilsa yoki
 * arxivlansa ro'yxatdan O'ZI chiqadi (server). Superadmin — eski (guruhida sinovda qolib ketgan) qaytarilganlarni o'tkazadi.
 */
export function TrialReturnsSection() {
  const { can } = usePerm()
  const isSuperAdmin = useAuth().user?.role === 'superadmin'
  const canEdit = can('leads.list', 'edit') || can('leads.callcenter', 'edit')
  const [rows, setRows] = useState<TrialReturn[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const [legacyOpen, setLegacyOpen] = useState(false)

  useEffect(() => {
    let active = true
    getTrialReturns()
      .then((r) => {
        if (active) {
          setRows(r)
          setError(null)
        }
      })
      // Sahifaning ASOSIY qismi — xato ko'rsatiladi (`error-visibility.md` §4).
      .catch((e) => active && setError(apiErrorMessage(e, "Sinovdan qaytganlarni yuklab bo'lmadi")))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [tick])

  const resolve = async (r: TrialReturn) => {
    if (!confirm(`${r.studentName} — ro'yxatdan olib tashlansinmi? (tarixda qoladi)`)) return
    try {
      await resolveTrialReturn(r.id)
      setRows((prev) => prev.filter((x) => x.id !== r.id))
    } catch (e) {
      alert(apiErrorMessage(e, "Ro'yxatdan olib tashlab bo'lmadi"))
    }
  }

  const columns: DataColumn<TrialReturn>[] = [
    {
      key: 'name',
      header: "O'quvchi",
      render: (r) => (
        <Link to={`/admin/students/${r.studentId}`} className="font-medium text-black hover:text-brand-600 hover:underline">
          {r.studentName}
        </Link>
      ),
    },
    { key: 'phone', header: 'Telefon', render: (r) => <PhoneChip phone={r.phone || r.fatherPhone || r.motherPhone} /> },
    {
      key: 'group',
      header: 'Qaysi guruhdan',
      render: (r) => (
        <span className="whitespace-nowrap">
          {r.groupName}
          {(r.teacherName || r.courseName) && (
            <span className="block text-[12px] text-[#757575]">{[r.courseName, r.teacherName].filter(Boolean).join(' · ')}</span>
          )}
        </span>
      ),
    },
    { key: 'reason', header: 'Sabab', render: (r) => r.reason || <span className="text-[#9ca3af]">—</span> },
    {
      key: 'when',
      header: 'Qachon / kim',
      render: (r) => (
        <span className="whitespace-nowrap">
          {formatDateTime(r.createdAt)}
          <span className="block text-[12px] text-[#757575]">{r.createdByName}</span>
        </span>
      ),
    },
    {
      key: 'canceled',
      header: 'Bekor qilingan hisob',
      render: (r) => (r.canceledAmount > 0 ? formatMoney(r.canceledAmount) : <span className="text-[#9ca3af]">—</span>),
    },
    {
      key: 'actions',
      header: '',
      render: (r) =>
        canEdit ? (
          <button
            type="button"
            onClick={() => void resolve(r)}
            className="whitespace-nowrap text-[12px] font-medium text-[#757575] hover:text-rose-600"
            title="Masalan o'quvchi umuman qaytmaydi — ro'yxatdan olib tashlanadi, tarixda qoladi"
          >
            Ro'yxatdan olish
          </button>
        ) : null,
    },
  ]

  return (
    <div className="mt-6">
      <div className="mb-2 flex flex-wrap items-center gap-2">
        <div>
          <p className="text-[15px] font-semibold text-black">Sinovdan qaytganlar</p>
          <p className="text-[12px] text-[#757575]">
            «Sinovga qaytarish» bilan guruhdan chiqqan o'quvchilar. Guruhga qayta qo'shilsa — ro'yxatdan o'zi chiqadi.
          </p>
        </div>
        <div className="ml-auto flex items-center gap-2">
          {isSuperAdmin && (
            <Button variant="secondary" onClick={() => setLegacyOpen(true)}>
              Eski qaytarilganlarni o'tkazish
            </Button>
          )}
          <TotalPill total={rows.length} />
        </div>
      </div>
      {error && <p className="mb-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      <DataTable
        rows={rows}
        columns={columns}
        rowKey={(r) => r.id}
        loading={loading}
        numbered
        emptyHint="Guruh sahifasida a'zo «⋮» → «Sinovga qaytarish» qilinganda shu yerga tushadi."
      />
      {legacyOpen && (
        <LegacyModal
          onClose={() => setLegacyOpen(false)}
          onDone={() => {
            setLegacyOpen(false)
            setTick((t) => t + 1)
          }}
        />
      )}
    </div>
  )
}

/** ESKI qaytarilganlar (guruhida sinovda qolib ketgan) — bir martalik o'tkazish, faqat superadmin. Pulga tegilmaydi. */
function LegacyModal({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const [items, setItems] = useState<LegacyTrialReturn[] | null>(null)
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let active = true
    getLegacyTrialReturns()
      .then((r) => {
        if (!active) return
        setItems(r)
        setPicked(new Set(r.map((x) => x.key)))
      })
      .catch((e) => active && setError(apiErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")))
    return () => {
      active = false
    }
  }, [])

  const toggle = (key: string) =>
    setPicked((p) => {
      const n = new Set(p)
      if (n.has(key)) n.delete(key)
      else n.add(key)
      return n
    })

  const apply = async () => {
    if (busy || picked.size === 0) return
    setBusy(true)
    setError(null)
    try {
      const r = await applyLegacyTrialReturns([...picked])
      alert(`${r.moved} ta o'quvchi «Sinovdan qaytganlar»ga o'tkazildi.`)
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e, "O'tkazib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title="Eski qaytarilganlarni o'tkazish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Yopish
          </Button>
          <Button onClick={() => void apply()} disabled={busy || picked.size === 0}>
            {busy ? "O'tkazilmoqda..." : `O'tkazish (${picked.size})`}
          </Button>
        </>
      }
    >
      <p className="mb-3 text-sm text-slate-600">
        Ilgari «sinovga qaytarilgan», lekin hamon guruhida sinov holatida turgan o'quvchilar. Belgilanganlar guruhdan chiqadi va
        «Sinovdan qaytganlar»ga o'tadi (asl sana, sabab va kim — tarixdan). <b>Pulga tegilmaydi</b>, ketish hisoblanmaydi.
      </p>
      {error && <p className="mb-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      {!items ? (
        error ? null : <Loader />
      ) : items.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">O'tkaziladigan eski yozuv yo'q.</p>
      ) : (
        <div className="max-h-[420px] divide-y divide-slate-100 overflow-y-auto rounded-xl border border-slate-100">
          {items.map((it) => (
            <label key={it.key} className="flex cursor-pointer items-start gap-3 px-3 py-2 text-sm hover:bg-slate-50">
              <input type="checkbox" className="mt-1" checked={picked.has(it.key)} onChange={() => toggle(it.key)} />
              <span className="min-w-0 flex-1">
                <span className="font-medium text-slate-800">{it.studentName}</span>
                <span className="block text-xs text-slate-500">
                  {it.groupName} · {formatDateTime(it.returnedAt)} · {it.returnedBy}
                  {it.reason ? ` · ${it.reason}` : ''}
                </span>
              </span>
            </label>
          ))}
        </div>
      )}
    </Modal>
  )
}
