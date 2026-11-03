import type { AdminDashboard, TodayLessons } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { adminDashboardMock } from '../mock/dashboard'

/** Admin bosh sahifasi uchun barcha ma'lumotlar */
export async function getAdminDashboard(): Promise<AdminDashboard> {
  if (USE_MOCK) {
    await delay()
    return adminDashboardMock
  }
  const { data } = await api.get<AdminDashboard>('/admin/dashboard')
  return data
}

/** Darslar monitoringi — tanlangan sanada (default bugun) har guruh bo'yicha davomat/baho holati */
export async function getTodayLessons(date?: string): Promise<TodayLessons> {
  const { data } = await api.get<TodayLessons>('/admin/dashboard/today-lessons', {
    params: date ? { date } : undefined,
  })
  return data
}

/**
 * Bosh sahifaning 12 ta kartochkasi (server: `DashboardSummaryDto`, hisob — `DashboardSummary`).
 * "Ketganlar" va "birinchi to'lov" — JORIY oy (`month`, "yyyy-MM") bo'yicha.
 */
export interface DashboardSummary {
  month: string
  /** Ochiq (aylantirilmagan) lidlar. */
  orders: number
  /** Bugun yoki keyinroqqa belgilangan, natijasi hali yozilmagan sinov darsi bor lidlar. */
  firstLesson: number
  /** Sinovdagi (trial) a'zoligi bor o'quvchilar. */
  newStudents: number
  /** Faol (active) a'zoligi bor o'quvchilar. */
  activeStudents: number
  /** Shu oyda o'chirilgan (arxivga tushgan) lidlar. */
  ordersLeft: number
  /** Shu oyda kursdan ketgan, aktivlashmagan o'quvchilar. */
  newLeft: number
  /** Shu oyda kursdan ketgan, aktivlashgan o'quvchilar. */
  activeLeft: number
  /** Qarzdorlar — FAQAT aktiv o'quvchilar (boshqalari «Boshqa qarzdorlar» vidjetida). */
  debtors: number
  groups: number
  /** Birinchi o'quv to'lovi shu oyda bo'lganlar. */
  firstPayments: number
  /** Faqat muzlatilgan a'zoliklari qolgan o'quvchilar. */
  frozen: number
  /** Arxivlangan o'quvchilar. */
  archived: number
}

/** «Boshqa qarzdorlar» toifasi: guruhdan ketgan · arxivdagi · sinovdagi · muzlatilgan (server `DashboardSummary.OtherDebtKinds`). */
export type OtherDebtKind = 'left' | 'archived' | 'trial' | 'frozen'

export interface OtherDebtGroup {
  kind: OtherDebtKind
  label: string
  count: number
  total: number
}

export interface OtherDebtor {
  studentId: string
  fullName: string
  kind: OtherDebtKind
  /** Musbat qarz summasi (so'm). */
  debt: number
  archivedAt: string | null
}

/** Bosh sahifa «Boshqa qarzdorlar» vidjeti (server: `OtherDebtorsDto`). */
export interface OtherDebtors {
  /** Aktiv qarzdorlar — «Qarzdorlar» kartochkasi bilan bir xil ta'rif. */
  activeCount: number
  activeTotal: number
  groups: OtherDebtGroup[]
  /** Eng katta qarz tepada; `truncated` bo'lsa chegarada qirqilgan (sanoqlar to'liq). */
  items: OtherDebtor[]
  truncated: boolean
}

/** «Boshqa qarzdorlar» vidjeti — ruxsat `students.list` (javobda ism va summa bor). */
export async function getOtherDebtors(): Promise<OtherDebtors> {
  const { data } = await api.get<OtherDebtors>('/admin/dashboard/other-debtors')
  return data
}

/** Bosh sahifa kartochkalari. */
export async function getDashboardSummary(): Promise<DashboardSummary> {
  const { data } = await api.get<DashboardSummary>('/admin/dashboard/summary')
  return data
}
