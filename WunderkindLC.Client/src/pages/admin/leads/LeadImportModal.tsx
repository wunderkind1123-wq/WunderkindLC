import { useEffect, useRef, useState } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { apiErrorMessage } from '@/lib/utils'
import {
  getLeadImportOperators,
  importLeadContacts,
  type LeadImportOperator,
  type LeadImportResult,
} from '@/api/services/leads'

/** Namuna fayl — Excel'da ochiladi (UTF-8 BOM, nuqtali vergul: o'zbekcha Excel ajratkichi). */
const TEMPLATE = '﻿Ism;Telefon;Izoh\nKarimov Ali;+998 90 123 45 67;Ingliz tili so\'radi\n'

function downloadTemplate() {
  const url = URL.createObjectURL(new Blob([TEMPLATE], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = 'kontaktlar-namuna.csv'
  a.click()
  URL.revokeObjectURL(url)
}

/**
 * CALL CENTER «IMPORT» (2026-11-25, amoCRM kabi): fayldagi kontaktlar TANLANGAN operatorga SHAXSIY lid bo'lib tushadi —
 * ularni faqat o'sha operator, direktor, superadmin va menejer (`leadContactsAll`) ko'radi. Bazada bor raqamlar
 * o'tkaziladi va natijada sababi bilan ko'rsatiladi. Server: `LeadImport`, `LeadVisibility`.
 */
export function LeadImportModal({ open, onClose, onImported }: {
  open: boolean
  onClose: () => void
  /** Kamida bitta kontakt qo'shilsa — ro'yxatni qayta yuklash uchun. */
  onImported: () => void
}) {
  const [operators, setOperators] = useState<LeadImportOperator[]>([])
  const [assignee, setAssignee] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState<LeadImportResult | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!open) return
    getLeadImportOperators()
      .then(setOperators)
      .catch((e) => setError(apiErrorMessage(e, "Operatorlar ro'yxatini yuklab bo'lmadi")))
  }, [open])

  const close = () => {
    setFile(null)
    setResult(null)
    setError('')
    setAssignee('')
    if (inputRef.current) inputRef.current.value = ''
    onClose()
  }

  const submit = async () => {
    if (busy || !file || !assignee) return
    setBusy(true)
    setError('')
    try {
      const r = await importLeadContacts(file, assignee)
      setResult(r)
      if (r.created > 0) onImported()
    } catch (e) {
      // Xato oynada qoladi, tanlangan fayl va operator saqlanadi (`error-visibility.md`).
      setError(apiErrorMessage(e, "Import bajarilmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={close}
      title="Kontaktlarni import qilish"
      size="md"
      footer={
        result ? (
          <Button onClick={close}>Yopish</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={close}>Bekor</Button>
            <Button onClick={submit} disabled={busy || !file || !assignee}>
              {busy ? 'Yuklanmoqda…' : 'Import qilish'}
            </Button>
          </>
        )
      }
    >
      {result ? (
        <div className="space-y-3 text-sm">
          <p className="rounded-lg bg-emerald-50 px-3 py-2 font-medium text-emerald-700">
            {result.created} ta kontakt {result.assigneeName} ga biriktirildi (jami {result.total} ta qator).
          </p>
          {result.skipped.length > 0 && (
            <div>
              <p className="mb-1.5 font-medium text-slate-700">O'tkazib yuborildi — {result.skipped.length} ta:</p>
              <div className="max-h-64 overflow-y-auto rounded-lg border border-slate-200">
                <table className="w-full text-xs">
                  <thead className="bg-slate-50 text-slate-500">
                    <tr>
                      <th className="px-2 py-1.5 text-left">Qator</th>
                      <th className="px-2 py-1.5 text-left">Ism</th>
                      <th className="px-2 py-1.5 text-left">Telefon</th>
                      <th className="px-2 py-1.5 text-left">Sabab</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {result.skipped.map((s) => (
                      <tr key={`${s.row}-${s.phone}`}>
                        <td className="px-2 py-1.5 font-mono text-slate-400">{s.row}</td>
                        <td className="px-2 py-1.5">{s.name || '—'}</td>
                        <td className="px-2 py-1.5 font-mono">{s.phone || '—'}</td>
                        <td className="px-2 py-1.5 text-amber-700">{s.reason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      ) : (
        <div className="space-y-4 text-sm">
          <p className="text-slate-600">
            Kontaktlar tanlangan operatorga <b>shaxsiy</b> lid bo'lib Call Center'ga tushadi. Ularni faqat shu operator,
            direktor, superadmin va menejer ko'radi. Bazada bor raqamlar qo'shilmaydi.
          </p>

          <label className="block">
            <span className="mb-1 block font-medium text-slate-600">Operator</span>
            <select
              value={assignee}
              onChange={(e) => setAssignee(e.target.value)}
              className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400"
            >
              <option value="">— Operatorni tanlang —</option>
              {operators.map((o) => (
                <option key={o.id} value={o.id}>{o.fullName}</option>
              ))}
            </select>
          </label>

          <div>
            <span className="mb-1 block font-medium text-slate-600">Fayl (.xlsx yoki .csv)</span>
            <input
              ref={inputRef}
              type="file"
              accept=".xlsx,.csv"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              className="block w-full text-sm text-slate-600 file:mr-3 file:rounded-lg file:border-0 file:bg-brand-50 file:px-3 file:py-2 file:text-sm file:font-medium file:text-brand-700 hover:file:bg-brand-100"
            />
            <p className="mt-1.5 text-xs text-slate-400">
              Ustunlar: <b>Ism</b> · <b>Telefon</b> · <b>Izoh</b> (ixtiyoriy). Birinchi qator sarlavha bo'lishi mumkin.
              Bir faylda 2000 tagacha.{' '}
              <button type="button" onClick={downloadTemplate} className="font-medium text-brand-600 hover:underline">
                Namuna faylni yuklab olish
              </button>
            </p>
          </div>

          {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-red-700">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
