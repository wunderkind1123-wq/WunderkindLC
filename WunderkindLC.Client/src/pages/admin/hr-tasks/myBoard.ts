import { hrIsOverdue, hrTashkentParts } from '@/components/hr-tasks/lib'
import type { HrTaskStatus } from '@/types/hrTasks'
import { hrAddDays } from './paths'

/**
 * LC (egasi, 2026-11-10): «Mening topshiriqlarim» — KANBAN doska. BELGILANGAN ustunlar (xodim qo'shmaydi/o'chirmaydi):
 * Kechikkan · Bugun · Ertaga · Keyinroq. Faqat HALI BAJARILMAGAN (ochiq / qaytarilgan) topshiriqlar — bajarilgani doskadan
 * darhol yo'qoladi (bazada qoladi: «Bajarilgan» tabi, KPI, hisobot, lid tarixi). Sana — Toshkent, «bugun» — serverniki.
 */
export type BoardColumnKey = 'overdue' | 'today' | 'tomorrow' | 'later'

export const BOARD_COLUMNS: { key: BoardColumnKey; label: string; empty: string }[] = [
  { key: 'overdue', label: 'Kechikkan', empty: 'Kechikkan topshiriq yo‘q 👍' },
  { key: 'today', label: 'Bugun', empty: 'Bugunga topshiriq yo‘q' },
  { key: 'tomorrow', label: 'Ertaga', empty: 'Ertaga uchun yo‘q' },
  { key: 'later', label: 'Keyinroq', empty: 'Keyingi kunlarga yo‘q' },
]

type BoardTask = { dueAt: string | null; status: HrTaskStatus; isOverdue?: boolean | null }

/** Bitta topshiriqning ustuni; bajarilgan/bekor qilingan — null (doskada YO'Q). Muddati yo'q — «Keyinroq». */
export function boardColumnOf(task: BoardTask, today: string, now: Date = new Date()): BoardColumnKey | null {
  if (task.status !== 'open' && task.status !== 'returned') return null
  if (task.isOverdue ?? hrIsOverdue(task, now)) return 'overdue'
  const day = hrTashkentParts(task.dueAt)?.date
  if (!day) return 'later'
  if (day <= today) return 'today' // bugun, hali vaqti bor
  if (day === hrAddDays(today, 1)) return 'tomorrow'
  return 'later'
}

/** Ustunlarga ajratish; har ustunda eng yaqin muddat tepada (kechikkanlarda — eng eskisi). */
export function groupBoard<T extends BoardTask>(tasks: readonly T[], today: string, now: Date = new Date()): Record<BoardColumnKey, T[]> {
  const out: Record<BoardColumnKey, T[]> = { overdue: [], today: [], tomorrow: [], later: [] }
  for (const t of tasks) {
    const key = boardColumnOf(t, today, now)
    if (key) out[key].push(t)
  }
  const at = (x: T) => (x.dueAt ? Date.parse(x.dueAt) : Number.NaN)
  const byDue = (a: T, b: T) => (Number.isNaN(at(a)) ? Infinity : at(a)) - (Number.isNaN(at(b)) ? Infinity : at(b)) || 0
  for (const key of Object.keys(out) as BoardColumnKey[]) out[key].sort(byDue)
  return out
}
