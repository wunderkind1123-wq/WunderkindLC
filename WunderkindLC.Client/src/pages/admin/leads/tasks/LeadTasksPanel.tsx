import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { hrToday } from '@/components/hr-tasks/lib'
import { usePerm } from '@/lib/permissions'
import { hrtOpenWorkLink } from '@/pages/admin/hr-tasks/paths'
import { IconChecklist, IconPlus } from '@tabler/icons-react'
import {
  cancelLeadTask,
  completeLeadTask,
  createLeadTask,
  getLeadTasks,
  updateLeadTask,
  type LeadTask,
} from '@/api/services/leadTasks'
import { Button } from '@/components/ui/Button'
import { apiErrorMessage, cn } from '@/lib/utils'
import { dueText } from '@/lib/leadTaskDue'
import { LeadTaskForm } from './LeadTaskForm'
import { emptyTaskValues, toPayload, valuesFromTask } from './taskFormValues'
import { useLeadTaskMeta } from './useLeadTaskMeta'

/**
 * Lid sahifasidagi TOPSHIRIQLAR bloki (topshiriqning o'zi «Topshiriqlar» (HR) bo'limida — xodim uni
 * «Mening topshiriqlarim»da «Sotuv» belgisi bilan ko'radi, Telegram'ga «✅ Qabul qildim» bilan boradi) — lenta pastida (spec §6.3: ochiq topshiriqlar doim yozish maydonidan
 * tepada). Bajarish oqimi: natija (majburiy) → «Keyingi topshiriq» formasi → bitta so'rovda yopish va
 * keyingisini qo'yish (yoki «Keyingisiz yopish»).
 *
 * Optimistik EMAS — ro'yxat server javobidan keyin yangilanadi; xato forma ichida ko'rinadi.
 */
export function LeadTasksPanel({
  leadId,
  leadOpen,
  leadAssigneeId,
  canCreate,
  canEdit,
  onChanged,
}: {
  leadId: string
  /** Lid ochiqmi (aylantirilmagan, yo'qotilmagan) — yopiq lidga topshiriq qo'yilmaydi (server ham rad etadi). */
  leadOpen: boolean
  leadAssigneeId?: string | null
  canCreate: boolean
  canEdit: boolean
  /** Lid tarixi va kartochka qayta yuklansin (hodisa yozildi, mas'ul o'zgargan bo'lishi mumkin). */
  onChanged: () => void
}) {
  const meta = useLeadTaskMeta()
  const { can } = usePerm()
  const [tasks, setTasks] = useState<LeadTask[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)

  const load = useCallback(() => {
    getLeadTasks({ leadId, status: 'open' })
      .then((t) => {
        setTasks(t)
        setLoadError(null)
      })
      .catch((err) => setLoadError(apiErrorMessage(err, "Topshiriqlarni yuklab bo'lmadi")))
  }, [leadId])

  useEffect(() => {
    load()
  }, [load])

  const changed = () => {
    load()
    onChanged()
  }

  if (!leadOpen && tasks.length === 0 && !loadError) return null

  return (
    <div className="border-t border-[#eeeeee] bg-white px-3 py-2">
      <div className="mb-1.5 flex items-center gap-2">
        <IconChecklist className="h-4 w-4 text-brand-600" />
        <span className="text-[13px] font-semibold text-black">Topshiriqlar</span>
        {tasks.length === 0 && leadOpen && !adding && (
          <span className="text-[12px] font-medium text-[#ED6C02]">— keyingi qadam qo'yilmagan</span>
        )}
        {/* Topshiriqning o'zi «Topshiriqlar» bo'limida — xodim u yerda «Sotuv» belgisi bilan ko'radi. */}
        {/* Panel ruxsati bo'lsa — barcha xodimlarning BAJARILMAGAN topshiriqlari (kechikkan + kelgusi 30 kun,
            `hrtOpenWorkLink`); bo'lmasa — o'zining topshiriqlari. */}
        <Link
          to={can('hrTasks', 'view') ? hrtOpenWorkLink(hrToday()) : '/admin/hr/mening-topshiriqlarim'}
          className="ml-auto text-[12px] text-[#757575] hover:text-brand-600 hover:underline"
          title="Lid topshiriqlari «Topshiriqlar» bo'limida «Sotuv» belgisi bilan ko'rinadi"
        >
          Topshiriqlarda ko'rish ↗
        </Link>
        {leadOpen && canCreate && !adding && (
          <button
            type="button"
            onClick={() => setAdding(true)}
            className="inline-flex items-center gap-1 rounded-lg px-2 py-0.5 text-[12.5px] font-medium text-brand-600 hover:bg-brand-600/10"
          >
            <IconPlus className="h-3.5 w-3.5" /> Topshiriq qo'yish
          </button>
        )}
      </div>

      {loadError && <p className="mb-1 text-[12.5px] text-red-700">{loadError}</p>}

      <ul className="max-h-[260px] space-y-1.5 overflow-y-auto">
        {tasks.map((t) => (
          <LeadTaskItem key={t.id} task={t} meta={meta} leadOpen={leadOpen} canEdit={canEdit} onChanged={changed} />
        ))}
      </ul>

      {adding && (
        <div className="mt-2 rounded-lg border border-brand-600/30 bg-brand-600/[0.03] p-2">
          <LeadTaskForm
            meta={meta}
            initial={emptyTaskValues(leadAssigneeId ?? '')}
            submitLabel="Topshiriq qo'yish"
            onCancel={() => setAdding(false)}
            onSubmit={async (v) => {
              try {
                await createLeadTask({ ...toPayload(v), leadId })
              } catch (err) {
                throw new Error(apiErrorMessage(err, "Topshiriqni saqlab bo'lmadi"), { cause: err })
              }
              setAdding(false)
              changed()
            }}
          />
        </div>
      )}
    </div>
  )
}

type Mode = 'view' | 'complete' | 'edit'

function LeadTaskItem({
  task,
  meta,
  leadOpen,
  canEdit,
  onChanged,
}: {
  task: LeadTask
  meta: ReturnType<typeof useLeadTaskMeta>
  leadOpen: boolean
  canEdit: boolean
  onChanged: () => void
}) {
  const [mode, setMode] = useState<Mode>('view')
  const [result, setResult] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const resultOk = result.trim().length >= 3

  const finish = async (next: ReturnType<typeof toPayload> | null) => {
    try {
      await completeLeadTask(task.id, result.trim(), next)
    } catch (err) {
      throw new Error(apiErrorMessage(err, "Topshiriqni yopib bo'lmadi"), { cause: err })
    }
    onChanged()
  }

  const closeWithoutNext = async () => {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      await finish(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  const cancel = async () => {
    const reason = prompt('Topshiriqni bekor qilish sababi (ixtiyoriy):')
    if (reason === null) return
    try {
      await cancelLeadTask(task.id, reason)
      onChanged()
    } catch (err) {
      alert(apiErrorMessage(err, "Topshiriqni bekor qilib bo'lmadi"))
    }
  }

  return (
    <li
      className={cn(
        'rounded-lg border px-2.5 py-2 text-[12.5px]',
        task.overdue ? 'border-red-300 bg-red-50/40' : 'border-[#dbe0e6] bg-white',
      )}
    >
      <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
        <span className={cn('font-semibold', task.overdue ? 'text-red-700' : 'text-black')}>
          {dueText(task.dueAt, task.allDay)}
          {task.overdue && task.overdueDays > 0 && ` (${task.overdueDays} kun kechikdi)`}
        </span>
        <span className="rounded bg-orange-50 px-1.5 py-px text-[11px] font-semibold text-orange-700">{task.typeLabel}</span>
        {task.text && <span className="min-w-0 break-words text-[#333]">{task.text}</span>}
        <span className="ml-auto text-[11.5px] text-[#757575]">
          {task.createdByName}
          {task.responsibleName ? ` → ${task.responsibleName}` : ''}
        </span>
      </div>

      {canEdit && leadOpen && mode === 'view' && (
        <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
          <input
            value={result}
            onChange={(e) => setResult(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && resultOk) {
                e.preventDefault()
                setMode('complete')
              }
            }}
            maxLength={1000}
            placeholder="Natija: nima bo'ldi?"
            className="h-8 min-w-[180px] flex-1 rounded-lg border border-[#dbe0e6] bg-white px-3 text-[12.5px] outline-none focus:border-brand-600"
          />
          <Button
            onClick={() => setMode('complete')}
            disabled={!resultOk}
            title={resultOk ? undefined : 'Avval natijani yozing (kamida 3 belgi)'}
          >
            Bajarish
          </Button>
          <button
            type="button"
            onClick={() => setMode('edit')}
            className="rounded-lg px-2 py-1 text-[12px] text-brand-600 hover:bg-brand-600/10"
          >
            Muddatni o'zgartirish
          </button>
          <button
            type="button"
            onClick={() => void cancel()}
            className="rounded-lg px-2 py-1 text-[12px] text-[#757575] hover:bg-black/5"
          >
            Bekor qilish
          </button>
        </div>
      )}

      {mode === 'complete' && (
        <div className="mt-2 rounded-lg border border-emerald-200 bg-emerald-50/40 p-2">
          <p className="mb-1.5 text-[12.5px] text-[#333]">
            Natija: <b>{result.trim()}</b>. <span className="font-semibold">Keyingi topshiriq?</span>
          </p>
          <LeadTaskForm
            meta={meta}
            initial={{ ...emptyTaskValues(task.responsibleUserId ?? ''), type: task.type }}
            submitLabel="Bajarish va keyingisini qo'yish"
            onSubmit={async (v) => finish(toPayload(v))}
            onCancel={() => setMode('view')}
            cancelLabel="Orqaga"
          />
          <div className="mt-1.5 flex items-center gap-2 border-t border-emerald-200 pt-1.5">
            <button
              type="button"
              disabled={busy}
              onClick={() => void closeWithoutNext()}
              className="text-[12px] font-medium text-[#757575] hover:text-black hover:underline disabled:opacity-50"
            >
              Keyingi topshiriqsiz yopish
            </button>
            {error && <span className="text-[12px] text-red-700">{error}</span>}
          </div>
        </div>
      )}

      {mode === 'edit' && (
        <div className="mt-2 rounded-lg border border-[#dbe0e6] bg-[#fafafa] p-2">
          <LeadTaskForm
            meta={meta}
            initial={valuesFromTask(task)}
            submitLabel="Saqlash"
            onCancel={() => setMode('view')}
            onSubmit={async (v) => {
              try {
                await updateLeadTask(task.id, toPayload(v))
              } catch (err) {
                throw new Error(apiErrorMessage(err, "Topshiriqni saqlab bo'lmadi"), { cause: err })
              }
              setMode('view')
              onChanged()
            }}
          />
        </div>
      )}
    </li>
  )
}
