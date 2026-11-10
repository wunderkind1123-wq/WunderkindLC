import type { LeadTaskPayload } from '@/api/services/leadTasks'
import { quickDueDays } from '@/lib/leadTaskDue'

/** Topshiriq formasining qiymatlari (komponent faylidan ATAYIN ajratilgan — Vite fast-refresh qoidasi). */
export interface LeadTaskFormValues {
  type: string
  text: string
  day: string
  time: string
  allDay: boolean
  responsibleUserId: string
}

/** Forma qiymatlarini so'rov ma'lumotiga aylantiradi (vaqtsiz — butun kun). */
export function toPayload(v: LeadTaskFormValues): LeadTaskPayload {
  const allDay = v.allDay || !v.time
  return {
    type: v.type,
    text: v.text.trim(),
    dueAt: allDay ? v.day : `${v.day}T${v.time}`,
    allDay,
    responsibleUserId: v.responsibleUserId || null,
  }
}

/** Yangi topshiriq uchun boshlang'ich qiymat: ertaga, 10:00, qo'ng'iroq. */
export function emptyTaskValues(responsibleUserId = ''): LeadTaskFormValues {
  return {
    type: 'call',
    text: '',
    day: quickDueDays()[1].day,
    time: '10:00',
    allDay: false,
    responsibleUserId,
  }
}

/** Mavjud topshiriqdan forma qiymati (muddatni o'zgartirish). */
export function valuesFromTask(t: {
  type: string; text: string; dueAt: string; allDay: boolean; responsibleUserId?: string | null
}): LeadTaskFormValues {
  return {
    type: t.type,
    text: t.text,
    day: t.dueAt.slice(0, 10),
    time: t.allDay ? '10:00' : t.dueAt.slice(11, 16),
    allDay: t.allDay,
    responsibleUserId: t.responsibleUserId ?? '',
  }
}
