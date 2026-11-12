import { api } from '../client'

/**
 * «SINOVDAN QAYTGANLAR» (2026-11-13) — «Sinovga qaytarish» bilan guruhdan chiqqan o'quvchilar («Birinchi darsga
 * yozilganlar» sahifasida). Server: `TrialReturnsController`, qoidalar — `membership-periods.md` §12.
 */
export interface TrialReturn {
  id: string
  studentId: string
  studentName: string
  phone: string
  fatherPhone: string
  motherPhone: string
  groupId: string
  groupName: string
  teacherName: string
  courseName: string
  reason: string
  createdAt: string
  createdByName: string
  /** Joriy oy va keyingi oylardan bekor qilingan hisob (so'm). */
  canceledAmount: number
}

export async function getTrialReturns(): Promise<TrialReturn[]> {
  const { data } = await api.get<TrialReturn[]>('/admin/trial-returns')
  return data
}

/** Ro'yxatdan qo'lda olib tashlash (yozuv tarixda qoladi). */
export async function resolveTrialReturn(id: string): Promise<void> {
  await api.put(`/admin/trial-returns/${id}/resolve`)
}

/** ESKI (2026-11-13 gacha) qaytarilgan, hamon guruhida sinovda turganlar — FAQAT superadmin. */
export interface LegacyTrialReturn {
  key: string
  studentId: string
  studentName: string
  groupId: string
  groupName: string
  returnedAt: string
  returnedBy: string
  reason: string
}

export async function getLegacyTrialReturns(): Promise<LegacyTrialReturn[]> {
  const { data } = await api.get<LegacyTrialReturn[]>('/admin/trial-returns/legacy')
  return data
}

export async function applyLegacyTrialReturns(keys: string[]): Promise<{ moved: number }> {
  const { data } = await api.post<{ moved: number }>('/admin/trial-returns/legacy', { keys })
  return data
}
