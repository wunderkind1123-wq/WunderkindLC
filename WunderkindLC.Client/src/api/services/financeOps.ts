import { api } from '../client'

/**
 * CHIQIMLAR TASDIG'I va MAOSH REESTRI (SPEC-backlog-2026-10-17 F3). Shakl serverdagi `ExpenseRequestDto`,
 * `ExpenseApprovalSettingsDto`, `PayrollDto` bilan bir xil.
 */

export type ExpenseRequestStatus = 'pending' | 'approved' | 'rejected' | 'cancelled'

export interface ExpenseRequest {
  id: string
  status: ExpenseRequestStatus
  date: string
  category: string
  typeName: string | null
  amount: number
  note: string | null
  comment: string | null
  teacherName: string | null
  staffName: string | null
  month: string | null
  method: string | null
  cashboxId: string | null
  cashboxName: string | null
  salaryPayKind: 'advance' | 'final' | null
  createdAt: string
  createdBy: string | null
  decidedAt: string | null
  decidedBy: string | null
  decisionNote: string | null
  transactionId: string | null
  /** Joriy foydalanuvchi tasdiqlay/rad eta oladimi (tasdiqlovchi va kirituvchi emas). */
  canDecide: boolean
  isMine: boolean
}

export interface ExpenseApprovalSettings {
  /** 0 — tasdiq o'chiq. */
  threshold: number
  canApprove: boolean
  canEditThreshold: boolean
}

export async function getExpenseRequests(status?: ExpenseRequestStatus): Promise<ExpenseRequest[]> {
  const { data } = await api.get<ExpenseRequest[]>('/admin/finance/expense-requests', { params: { status } })
  return data
}

export async function getExpenseApprovalSettings(): Promise<ExpenseApprovalSettings> {
  const { data } = await api.get<ExpenseApprovalSettings>('/admin/finance/expense-requests/settings')
  return data
}

export async function saveExpenseApprovalThreshold(threshold: number): Promise<ExpenseApprovalSettings> {
  const { data } = await api.put<ExpenseApprovalSettings>('/admin/finance/expense-requests/settings', { threshold })
  return data
}

/** Tasdiqlash. `cashboxId` — tasdiqlovchi tanlagan kassa (so'rovdagisi arxivlangan bo'lsa shart); bo'sh — so'rovdagisi. */
export async function approveExpense(id: string, note?: string, cashboxId?: string): Promise<ExpenseRequest> {
  const { data } = await api.post<ExpenseRequest>(`/admin/finance/expense-requests/${encodeURIComponent(id)}/approve`, {
    note,
    cashboxId: cashboxId || undefined,
  })
  return data
}

export async function rejectExpense(id: string, note: string): Promise<ExpenseRequest> {
  const { data } = await api.post<ExpenseRequest>(`/admin/finance/expense-requests/${encodeURIComponent(id)}/reject`, { note })
  return data
}

export async function cancelExpense(id: string): Promise<ExpenseRequest> {
  const { data } = await api.post<ExpenseRequest>(`/admin/finance/expense-requests/${encodeURIComponent(id)}/cancel`)
  return data
}

/* ---------- Maosh reestri ---------- */

export type PayrollSource = 'ledger' | 'kpi' | 'kpi-pending' | 'fixed' | 'none'

export interface PayrollRow {
  kind: 'teacher' | 'staff'
  id: string
  name: string
  role: string
  source: PayrollSource
  accrued: number
  advance: number
  final: number
  /** Turi belgilanmagan (eski) to'lovlar. */
  other: number
  paid: number
  remaining: number
  status: 'paid' | 'partial' | 'unpaid' | 'none'
  fixedAmount: number | null
  fixedFrom: string | null
  /** Shu oy uchun TASDIQ KUTAYOTGAN maosh so'rovlari summasi (chegaradan katta to'lov). */
  pending?: number
  /** Xodim: asos (qat'iy oylik / KPI natijasi). Hisoblangan = asos + bonus − jarima (2026-11-10). */
  base?: number
  /** Qo'lda kiritilgan bonuslar jami (shu oy). */
  bonus?: number
  /** Qo'lda kiritilgan jarimalar jami (shu oy). */
  fine?: number
}

export interface Payroll {
  month: string
  accrued: number
  paid: number
  remaining: number
  advance: number
  final: number
  teachers: PayrollRow[]
  staff: PayrollRow[]
  /** Xodimlarga qo'lda bonus / jarima jami. */
  bonus?: number
  fine?: number
}

export async function getPayroll(month: string): Promise<Payroll> {
  const { data } = await api.get<Payroll>('/admin/finance/payroll', { params: { month } })
  return data
}

export interface PayrollPerson {
  id: string
  fullName: string
  role: string
  position: string
}

/** Oylik belgilash mumkin bo'lgan panel akkauntlari (reestrda hali qatori yo'q admin/superadmin ham). */
export async function getPayrollPeople(): Promise<PayrollPerson[]> {
  const { data } = await api.get<PayrollPerson[]>('/admin/finance/payroll/people')
  return data
}

export async function setStaffSalary(userId: string, amount: number, fromMonth: string, note?: string): Promise<void> {
  await api.put(`/admin/finance/payroll/staff/${encodeURIComponent(userId)}/salary`, { amount, fromMonth, note })
}

// ── Xodim oyligiga QO'LDA bonus / jarima (2026-11-10) ───────────────────────────────────────────────────

export type SalaryAdjustmentKind = 'bonus' | 'fine'

export interface SalaryAdjustment {
  id: string
  userId: string
  month: string
  kind: SalaryAdjustmentKind
  amount: number
  reason: string
  createdByName: string | null
  createdAt: string
}

/** Oydagi bonus/jarimalar; `userId` berilsa — faqat shu xodimniki. */
export async function getSalaryAdjustments(month: string, userId?: string): Promise<SalaryAdjustment[]> {
  const { data } = await api.get<SalaryAdjustment[]>('/admin/finance/payroll/adjustments', { params: { month, userId } })
  return data
}

export async function addSalaryAdjustment(
  userId: string,
  body: { kind: SalaryAdjustmentKind; amount: number; reason: string; month: string },
): Promise<SalaryAdjustment> {
  const { data } = await api.post<SalaryAdjustment>(`/admin/finance/payroll/staff/${encodeURIComponent(userId)}/adjustments`, body)
  return data
}

export async function deleteSalaryAdjustment(id: string): Promise<void> {
  await api.delete(`/admin/finance/payroll/adjustments/${encodeURIComponent(id)}`)
}
