/**
 * LID TOPSHIRIG'I muddati — sof yordamchilar (kanban indikatori, tezkor muddat tugmalari).
 * Server bilan BIR XIL qoida (`LeadTasks.Bucket` / `IsOverdue`): kechikkan = muddat HOZIRDAN oldin;
 * butun kunlik topshiriq (vaqt 23:59) kun oxirigacha kechikmagan. Buzuq sana — kechikmagan (yolg'on qizil yo'q).
 */

const pad = (n: number) => String(n).padStart(2, '0')

/** Date → mahalliy "yyyy-MM-dd". */
export function isoDay(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** "yyyy-MM-ddTHH:mm[:ss]" → mahalliy Date (null — buzuq). */
export function parseDue(iso: string | null | undefined): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(iso ?? '')
  if (!m) return null
  const d = new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5])
  return isNaN(d.getTime()) ? null : d
}

export type DueTone = 'none' | 'overdue' | 'today' | 'tomorrow' | 'later'

/** Kanban kartasidagi indikator: matn + rang turi. */
export function dueIndicator(
  dueAt: string | null | undefined, allDay: boolean | undefined, now: Date = new Date(),
): { tone: DueTone; text: string } {
  if (!dueAt) return { tone: 'none', text: "Topshiriq yo'q" }
  const due = parseDue(dueAt)
  if (!due) return { tone: 'later', text: 'Topshiriq bor' }
  const time = allDay ? '' : ` ${pad(due.getHours())}:${pad(due.getMinutes())}`
  if (due.getTime() < now.getTime()) {
    const days = Math.round(
      (new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime() -
        new Date(due.getFullYear(), due.getMonth(), due.getDate()).getTime()) / 86_400_000,
    )
    return { tone: 'overdue', text: days > 0 ? `${days} kun kechikdi` : 'Kechikdi' }
  }
  const today = isoDay(now)
  const tomorrow = isoDay(new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1))
  const day = isoDay(due)
  if (day === today) return { tone: 'today', text: `Bugun${time}` }
  if (day === tomorrow) return { tone: 'tomorrow', text: `Ertaga${time}` }
  return { tone: 'later', text: `${pad(due.getDate())}.${pad(due.getMonth() + 1)}${time}` }
}

export const dueToneColor: Record<DueTone, string> = {
  none: '#ED6C02',
  overdue: '#DE4141',
  today: '#00A35C',
  tomorrow: '#3D68FF',
  later: '#3D68FF',
}

/** "12.11.2026 15:00" (butun kun — faqat sana). */
export function dueText(dueAt: string, allDay: boolean): string {
  const d = parseDue(dueAt)
  if (!d) return dueAt
  const date = `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`
  return allDay ? date : `${date} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Tezkor muddat tugmalari (spec §6.3): sana "yyyy-MM-dd". */
export function quickDueDays(now: Date = new Date()): { key: string; label: string; day: string }[] {
  const at = (n: number) => isoDay(new Date(now.getFullYear(), now.getMonth(), now.getDate() + n))
  // Hafta oxiri — shu haftaning shanbasi (bugun shanba/yakshanba bo'lsa — keyingi shanba).
  const dow = now.getDay() // 0=Yak .. 6=Shan
  const toSat = dow === 6 ? 7 : (6 - dow + 7) % 7 || 7
  return [
    { key: 'today', label: 'Bugun', day: at(0) },
    { key: 'tomorrow', label: 'Ertaga', day: at(1) },
    { key: 'weekend', label: 'Hafta oxiri', day: at(toSat) },
    { key: 'week', label: '1 haftadan keyin', day: at(7) },
  ]
}
