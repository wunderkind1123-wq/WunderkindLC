import { api } from '../client'

/**
 * Pul oqimi 2.0 · P&L 2.0 (`docs/finance-reports/`). Server: `FinanceReports2Controller` (`api/admin/finance/v2`).
 * ⚠️ Turlar server DTO'lari bilan MAYDONMA-MAYDON mos (`CashFlowReport`, `PnlEngine`).
 */
const BASE = '/admin/finance/v2'

// ============================ Pul oqimi ============================

export interface CfLine {
  rowKey: string
  name: string
  data: number[]
  total: number
}
export interface CfSection {
  key: 'operating' | 'investing' | 'financing'
  title: string
  in: CfLine[]
  out: CfLine[]
  totalIn: number[]
  totalOut: number[]
  net: number[]
}
export interface CfReconciliation {
  cashboxBalanceNow: number
  reportBalanceNow: number
  difference: number
  applicable: boolean
}
export interface CashFlowYear {
  year: number
  currency: string
  months: string[]
  currentMonth: number
  initialBalance: number
  sections: CfSection[]
  startBalances: number[]
  endBalances: number[]
  netCashFlow: number[]
  reconciliation: CfReconciliation
  firstYear: number
}
export interface CfMonthLine {
  rowKey: string
  name: string
  amount: number
}
export interface CfMonthSection {
  key: string
  title: string
  cashIn: { total: number; categories: CfMonthLine[] }
  cashOut: { total: number; categories: CfMonthLine[] }
  net: number
}
export interface CashFlowMonth {
  year: number
  month: number
  monthName: string
  startBalance: number
  endBalance: number
  sections: CfMonthSection[]
  netCashFlow: number
  reconciliation: CfReconciliation
  firstYear: number
}
export interface CfTx {
  id: string
  date: string
  amount: number
  direction: string
  cashbox: string | null
  method: string | null
  person: string | null
  note: string | null
  createdBy: string | null
}
export interface CfTxPage {
  rowKey: string
  name: string
  total: number
  sum: number
  page: number
  pageSize: number
  items: CfTx[]
}

export async function getCashFlowYear(year: number): Promise<CashFlowYear> {
  const { data } = await api.get<CashFlowYear>(`${BASE}/cash-flow`, { params: { year } })
  return data
}
export async function getCashFlowMonth(year: number, month: number): Promise<CashFlowMonth> {
  const { data } = await api.get<CashFlowMonth>(`${BASE}/cash-flow`, { params: { year, month } })
  return data
}
export async function getCashFlowTransactions(p: {
  year: number
  month?: number
  rowKey: string
  page: number
  pageSize?: number
}): Promise<CfTxPage> {
  const { data } = await api.get<CfTxPage>(`${BASE}/cash-flow/transactions`, { params: { pageSize: 50, ...p } })
  return data
}
/** .xlsx — ekrandagi jadval AYNAN shunday. */
export async function downloadCashFlow(year: number, month?: number): Promise<void> {
  const { data } = await api.get<Blob>(`${BASE}/cash-flow/export`, {
    params: month ? { year, month } : { year },
    responseType: 'blob',
  })
  const url = URL.createObjectURL(data)
  const a = document.createElement('a')
  a.href = url
  a.download = month ? `pul-oqimi-${year}-${String(month).padStart(2, '0')}.xlsx` : `pul-oqimi-${year}.xlsx`
  a.click()
  URL.revokeObjectURL(url)
}

// ============================ P&L ============================

export interface PlanCur {
  planned: number
  current: number
}
export interface DiscountType {
  key: string
  type: 'catalog' | 'lesson' | 'transaction'
  label: string
  studentsCount: number
  planned: number
  amount: number
}
export interface ExpenseLine {
  id: string
  name: string
  typeId: string | null
  group: string
  planned: number
  current: number
  paid: number
  isUnplanned: boolean
  isCorrected: boolean
  isPayroll: boolean
  templates: { id: string; name: string; planned: number; current: number; isCorrected: boolean; hasAccrual: boolean }[]
}
export interface ExpenseGroup {
  name: string
  planned: number
  current: number
  paid: number
  lineCount: number
}
export interface PnlBranch {
  branchId: string
  branchName: string
  students: { planned: number; current: number; joined: number; left: number }
  revenue: {
    gross: PlanCur
    discount: PlanCur & { byType: DiscountType[] }
    refunds: PlanCur
    net: PlanCur
    otherRevenue: PlanCur & { lines: { key: string; name: string; amount: number }[] }
    perStudentNet: PlanCur
    discountRate: PlanCur
    netFlooredBy: number
  }
  cash: {
    collected: number
    forThisMonth: number
    forPriorMonths: number
    forFutureMonths: number
    uncollected: number
    collectionRate: number | null
  }
  expenses: {
    planned: number
    current: number
    paid: number
    unpaid: number
    hasExpenseData: boolean
    groups: ExpenseGroup[]
    lines: ExpenseLine[]
  }
  result: {
    profitPlanned: number
    profitCurrent: number
    marginPlanned: number | null
    marginCurrent: number | null
    profitPerStudentPlanned: number
    profitPerStudentCurrent: number
  }
  periodCollection: {
    owedForPeriod: number
    collectedForPeriod: number
    outstandingForPeriod: number
    collectionRateForPeriod: number | null
    studentsExpected: number
    studentsPaid: number
    studentsPartial: number
    studentsUnpaid: number
  }
  reconciliation: { snapshotNet: number; changesSum: number; computedNet: number; unexplained: number; snapshotIsEstimated: boolean }
}
export interface CashBridge {
  profit: number
  unpaidExpense: number
  expensePaid: number
  uncollectedRevenue: number
  priorDebtCollected: number
  advancesCollected: number
  outsidePnl: number
  cashDelta: number
  actualCashDelta: number
  difference: number
  priorDebtCollectedAvailable: boolean
  cashDeltaIsComplete: boolean
}
export interface Pnl {
  period: { year: number; month: number; monthName: string; from: string; asOf: string; daysInMonth: number }
  asOf: string
  isForecast: boolean
  isCurrentMonth: boolean
  currency: string
  revenueSource: 'charges' | 'tariff'
  planIsEstimated: boolean
  total: PnlBranch
  cashBridge: CashBridge
}

export async function getPnl(year: number, month: number, asOf?: string): Promise<Pnl> {
  const { data } = await api.get<Pnl>(`${BASE}/pnl`, { params: { year, month, asOf } })
  return data
}

export interface DrillRow {
  title: string
  subtitle: string | null
  date: string | null
  amount: number
  amount2: number | null
  studentId: string | null
}
export interface Drill {
  metric: string
  title: string
  description: string
  amountLabel: string
  amount2Label: string | null
  planned: number
  current: number
  total: number
  sum: number
  page: number
  pageSize: number
  items: DrillRow[]
}
export async function getPnlDrilldown(p: { year: number; month: number; asOf?: string; metric: string; page: number }): Promise<Drill> {
  const { data } = await api.get<Drill>(`${BASE}/pnl/drilldown`, { params: { pageSize: 50, ...p } })
  return data
}

export interface PnlYearMonth {
  month: number
  name: string
  isForecast: boolean
  isCurrent: boolean
  hasData: boolean
  students: number
  gross: number
  discount: number
  refunds: number
  otherRevenue: number
  net: number
  expense: number
  collected: number
  profit: number
  margin: number | null
  discountRate: number | null
  plan: { gross: number; discount: number; net: number; expense: number; profit: number; isEstimated: boolean }
}
export interface PnlYear {
  year: number
  currentMonth: number
  months: PnlYearMonth[]
  totals: {
    students: number
    gross: number
    discount: number
    refunds: number
    otherRevenue: number
    net: number
    expense: number
    collected: number
    profit: number
    margin: number | null
    discountRate: number | null
    planExpense: number
    planProfit: number
    bestMonth: { month: number; profit: number } | null
    worstMonth: { month: number; profit: number } | null
  }
}
export async function getPnlYearly(year: number): Promise<PnlYear> {
  const { data } = await api.get<PnlYear>(`${BASE}/pnl/yearly`, { params: { year } })
  return data
}

// ============================ Sozlamalar ============================

export type CashFlowActivity = 'operating' | 'investing' | 'financing' | 'excluded'
export type PnlTreatment =
  | 'tuitionRevenue'
  | 'otherRevenue'
  | 'otherRevenueContra'
  | 'expense'
  | 'salary'
  | 'salaryAdvance'
  | 'revenueContra'
  | 'nonCash'
  | 'excluded'

export interface CategoryRow {
  id: string
  key: string
  name: string
  baseName: string
  direction: 'income' | 'expense'
  typeId: string | null
  category: string | null
  kind: string | null
  cashFlowActivity: CashFlowActivity
  pnlTreatment: PnlTreatment
  pnlGroup: string | null
  sortOrder: number
  isAutoClassified: boolean
  autoReason: string
  count: number
  last12Months: number
  updatedAt: string
  updatedBy: string | null
}
export async function getFinanceCategories(): Promise<CategoryRow[]> {
  const { data } = await api.get<CategoryRow[]>(`${BASE}/categories`)
  return data
}
export async function updateFinanceCategory(
  id: string,
  p: { displayName?: string; cashFlowActivity?: CashFlowActivity; pnlTreatment?: PnlTreatment; pnlGroup?: string; sortOrder?: number },
): Promise<void> {
  await api.put(`${BASE}/categories/${id}`, p)
}
export async function reclassifyFinanceCategories(): Promise<{ changed: number }> {
  const { data } = await api.post<{ changed: number }>(`${BASE}/categories/reclassify`)
  return data
}

export interface PlannedTemplate {
  id: string
  name: string
  typeId: string | null
  category: string
  typeName: string
  amount: number
  frequency: string
  startDate: string
  endDate: string
  status: 'active' | 'inactive'
  reminderDay: number | null
  note: string | null
  createdAt: string
  createdBy: string | null
}
export interface PlannedTemplatePayload {
  name: string
  typeId: string | null
  category: string | null
  amount: number
  startDate: string
  endDate: string
  status: 'active' | 'inactive'
  reminderDay: number | null
  note: string
}
export async function getPlannedTemplates(): Promise<PlannedTemplate[]> {
  const { data } = await api.get<PlannedTemplate[]>(`${BASE}/planned-templates`)
  return data
}
export async function createPlannedTemplate(p: PlannedTemplatePayload): Promise<PlannedTemplate> {
  const { data } = await api.post<PlannedTemplate>(`${BASE}/planned-templates`, p)
  return data
}
export async function updatePlannedTemplate(id: string, p: PlannedTemplatePayload): Promise<PlannedTemplate> {
  const { data } = await api.put<PlannedTemplate>(`${BASE}/planned-templates/${id}`, p)
  return data
}
export async function deletePlannedTemplate(id: string): Promise<void> {
  await api.delete(`${BASE}/planned-templates/${id}`)
}
export async function correctPlannedAccrual(templateId: string, month: string, correctedAmount: number | null, note: string): Promise<void> {
  await api.put(`${BASE}/planned-templates/${templateId}/accruals/${month}`, { correctedAmount, note })
}
