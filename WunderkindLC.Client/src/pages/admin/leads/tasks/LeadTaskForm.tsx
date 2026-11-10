import { useState } from 'react'
import type { LeadTaskMeta } from '@/api/services/leadTasks'
import { Button } from '@/components/ui/Button'
import { Input, Select, Time24Input } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import { quickDueDays } from '@/lib/leadTaskDue'
import type { LeadTaskFormValues } from './taskFormValues'

/**
 * Topshiriq formasi — yangi topshiriq, «keyingi topshiriq» va muddatni o'zgartirish uchun BITTA forma.
 * Tezkor muddat tugmalari (spec §6.3): Bugun · Ertaga · Hafta oxiri · 1 haftadan keyin.
 * ⚠️ `onSubmit` Promise qaytaradi va xatoni YUQORIGA otadi — forma ochiq qoladi, xato shu yerda ko'rinadi.
 */
export function LeadTaskForm({
  meta,
  initial,
  submitLabel,
  onSubmit,
  onCancel,
  cancelLabel = 'Bekor qilish',
  compact,
}: {
  meta: LeadTaskMeta
  initial: LeadTaskFormValues
  submitLabel: string
  onSubmit: (v: LeadTaskFormValues) => Promise<void>
  onCancel?: () => void
  cancelLabel?: string
  compact?: boolean
}) {
  const [v, setV] = useState<LeadTaskFormValues>(initial)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const set = (patch: Partial<LeadTaskFormValues>) => setV((p) => ({ ...p, ...patch }))
  const quick = quickDueDays()

  const submit = async () => {
    if (saving) return
    if (!v.day) {
      setError('Muddatni tanlang.')
      return
    }
    setError(null)
    setSaving(true)
    try {
      await onSubmit(v)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className={cn('space-y-2', compact && 'text-[13px]')}>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-[150px_minmax(0,1fr)]">
        <Select value={v.type} onChange={(e) => set({ type: e.target.value })} aria-label="Topshiriq turi">
          {meta.types.map((t) => (
            <option key={t.key} value={t.key}>
              {t.label}
            </option>
          ))}
        </Select>
        <Input
          value={v.text}
          maxLength={500}
          onChange={(e) => set({ text: e.target.value })}
          placeholder="Nima qilish kerak? (ixtiyoriy)"
          aria-label="Topshiriq matni"
        />
      </div>

      <div className="flex flex-wrap items-center gap-1">
        {quick.map((q) => (
          <button
            key={q.key}
            type="button"
            onClick={() => set({ day: q.day })}
            className={cn(
              'rounded-full border px-2.5 py-0.5 text-[12px] font-medium transition-colors',
              v.day === q.day
                ? 'border-brand-600 bg-brand-600 text-white'
                : 'border-[#dbe0e6] bg-white text-[#333] hover:border-brand-600/60',
            )}
          >
            {q.label}
          </button>
        ))}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <div className="w-[150px]">
          <Input type="date" value={v.day} onChange={(e) => set({ day: e.target.value })} aria-label="Sana" />
        </div>
        {!v.allDay && <Time24Input value={v.time} onChange={(time) => set({ time })} />}
        <label className="inline-flex items-center gap-1.5 text-[12.5px] text-[#333]">
          <input type="checkbox" checked={v.allDay} onChange={(e) => set({ allDay: e.target.checked })} />
          Butun kun
        </label>
        {meta.people.length > 0 && (
          <div className="min-w-[180px] flex-1">
            <Select
              value={v.responsibleUserId}
              onChange={(e) => set({ responsibleUserId: e.target.value })}
              aria-label="Mas'ul"
            >
              <option value="">Mas'ul: lid mas'uli</option>
              {meta.people.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.id === meta.meId ? `${p.name} (men)` : p.name}
                </option>
              ))}
            </Select>
          </div>
        )}
      </div>

      {error && <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-1.5 text-[12.5px] text-red-700">{error}</p>}

      <div className="flex items-center gap-2">
        <Button onClick={submit} disabled={saving}>
          {saving ? 'Saqlanmoqda…' : submitLabel}
        </Button>
        {onCancel && (
          <Button variant="ghost" onClick={onCancel} disabled={saving}>
            {cancelLabel}
          </Button>
        )}
      </div>
    </div>
  )
}
