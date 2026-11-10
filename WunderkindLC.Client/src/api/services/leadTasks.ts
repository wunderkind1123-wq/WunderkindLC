import { api } from '../client'

/**
 * LID TOPSHIRIQLARI («keyingi qadam») — server `LeadTasksController` (`api/admin/lead-tasks`). Topshiriqning
 * o'zi «Topshiriqlar» (HR) bo'limida — xodimning «Mening topshiriqlarim»ida «Sotuv» belgisi bilan ko'rinadi va
 * Telegram'ga «✅ Qabul qildim» bilan boradi. Qoidalar serverda (`LeadTasks`): natija majburiy, faqat ochiq lidga,
 * lid yo'qotilsa/aylantirilsa ochiq topshiriqlar avtomatik bekor bo'ladi.
 */

export type LeadTaskStatus = 'open' | 'done' | 'cancelled'
export type LeadTaskBucket = 'overdue' | 'today' | 'tomorrow' | 'later'

export interface LeadTask {
  id: string
  leadId: string
  leadName: string
  leadPhone: string
  /** Lid bosqichining NOMI (joriy). */
  leadStage: string
  type: string
  typeLabel: string
  text: string
  /** Mahalliy ISO "yyyy-MM-ddTHH:mm:ss"; butun kunlik topshiriqda vaqt 23:59:00. */
  dueAt: string
  allDay: boolean
  responsibleUserId?: string | null
  responsibleName?: string | null
  status: LeadTaskStatus
  result?: string | null
  cancelReason?: string | null
  createdAt: string
  createdByName: string
  closedAt?: string | null
  closedByName?: string | null
  overdue: boolean
  overdueDays: number
  bucket: LeadTaskBucket
  /** Xodim «✅ Qabul qildim» bosganmi (Telegram yoki panel). */
  accepted: boolean
}

export interface LeadTaskPayload {
  leadId?: string
  type: string
  text: string
  /** "yyyy-MM-ddTHH:mm" yoki butun kun uchun "yyyy-MM-dd". */
  dueAt: string
  allDay: boolean
  responsibleUserId?: string | null
}

export interface LeadTaskMeta {
  types: { key: string; label: string }[]
  people: { id: string; name: string }[]
  meId?: string | null
}

export interface LeadTaskFilter {
  status?: LeadTaskStatus | 'all'
  leadId?: string
}

export async function getLeadTasks(filter: LeadTaskFilter = {}): Promise<LeadTask[]> {
  const params: Record<string, string> = {}
  for (const [k, v] of Object.entries(filter)) if (v) params[k] = String(v)
  const { data } = await api.get<LeadTask[]>('/admin/lead-tasks', { params })
  return data
}

export async function getLeadTaskMeta(): Promise<LeadTaskMeta> {
  const { data } = await api.get<LeadTaskMeta>('/admin/lead-tasks/meta')
  return data
}

export async function createLeadTask(payload: LeadTaskPayload): Promise<LeadTask> {
  const { data } = await api.post<LeadTask>('/admin/lead-tasks', payload)
  return data
}

export async function updateLeadTask(id: string, payload: LeadTaskPayload): Promise<LeadTask> {
  const { data } = await api.put<LeadTask>(`/admin/lead-tasks/${id}`, payload)
  return data
}

/** Bajarish: natija MAJBURIY; `next` — o'sha so'rovda keyingi topshiriq (xato bo'lsa hech biri saqlanmaydi). */
export async function completeLeadTask(
  id: string, result: string, next?: LeadTaskPayload | null,
): Promise<{ done: LeadTask; next?: LeadTask | null }> {
  const { data } = await api.put<{ done: LeadTask; next?: LeadTask | null }>(
    `/admin/lead-tasks/${id}/complete`, { result, next: next ?? null },
  )
  return data
}

export async function cancelLeadTask(id: string, reason?: string): Promise<LeadTask> {
  const { data } = await api.put<LeadTask>(`/admin/lead-tasks/${id}/cancel`, { reason: reason ?? null })
  return data
}
