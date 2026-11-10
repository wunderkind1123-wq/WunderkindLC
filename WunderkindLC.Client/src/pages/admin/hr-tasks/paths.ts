/** HR → TOPSHIRIQLAR routes (docs/modules/hr-tasks.md §8). */
export const HRT_BASE = '/admin/hr/topshiriqlar'

export const hrtPaths = {
  dashboard: HRT_BASE,
  tasks: `${HRT_BASE}/royxat`,
  create: (employeeId?: string) =>
    `${HRT_BASE}/royxat/yangi${employeeId ? `?employeeId=${encodeURIComponent(employeeId)}` : ''}`,
  /** Recurring tasks for many employees (batch) — opens with the "Takroriy" switch on. */
  createRecurring: `${HRT_BASE}/royxat/yangi?takroriy=1`,
  templates: `${HRT_BASE}/takroriy`,
  employees: `${HRT_BASE}/xodimlar`,
  reports: `${HRT_BASE}/hisobotlar`,
  settings: `${HRT_BASE}/sozlamalar`,
  /** For every HR employee — their own tasks (`/api/hr/me/*`). */
  myTasks: '/admin/hr/mening-topshiriqlarim',
}

/** `yyyy-MM-dd` + n kun (UTC kalendar arifmetikasi — mintaqadan qat'i nazar). */
export function hrAddDays(day: string, n: number): string {
  const [y, m, d] = day.split('-').map(Number)
  const t = new Date(Date.UTC(y ?? 1970, (m ?? 1) - 1, (d ?? 1) + n))
  const pad = (x: number): string => String(x).padStart(2, '0')
  return `${t.getUTCFullYear()}-${pad(t.getUTCMonth() + 1)}-${pad(t.getUTCDate())}`
}

/** LC: «Kelgusi» oynasi — bugundan necha kun oldinga (lid topshiriqlari odatda ertaga/indinga qo'yiladi). */
export const HRT_UPCOMING_DAYS = 30

/**
 * LC (egasi, 2026-11-10): «lidga topshiriq qo'ydim, Topshiriqlarda ko'rinmayapti» va «kim nima ish qilayotgani rahbarda
 * ko'rinsin». Davr tugmalari (LMS'dagidek) faqat BUGUNDAN ORQAGA sanaydi — ertangi topshiriq hech bir tabda chiqmasdi.
 * Bu havola HALI BAJARILMAGAN ishlarni ochadi: KECHIKKANLAR (oxirgi 30 kun) + kelgusi 30 kun, holat — ochiq/qaytarilgan.
 * ⚠️ Faqat «bugundan oldinga» oyna kechikkan topshiriqlarni yashirardi — rahbar aynan ularni ko'rishi kerak.
 * Lid sahifasidan va rahbarning «Mening topshiriqlarim» yo'naltirishidan.
 */
export function hrtOpenWorkLink(today: string): string {
  const search = new URLSearchParams({
    dateFrom: hrAddDays(today, -HRT_UPCOMING_DAYS),
    dateTo: hrAddDays(today, HRT_UPCOMING_DAYS),
  })
  search.append('status', 'open')
  search.append('status', 'returned')
  return `${hrtPaths.tasks}?${search.toString()}`
}

/** Link to the task list (from the dashboard and reports). */
export function hrtTasksLink(params: Record<string, string | null | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) if (value) search.set(key, value)
  const qs = search.toString()
  return qs ? `${hrtPaths.tasks}?${qs}` : hrtPaths.tasks
}
