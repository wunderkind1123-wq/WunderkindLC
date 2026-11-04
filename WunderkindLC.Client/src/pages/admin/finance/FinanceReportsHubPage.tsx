import { useSearchParams } from 'react-router-dom'
import {
  IconCalendarStats,
  IconChartDonut3,
  IconPresentationAnalytics,
  IconTargetArrow,
  type Icon,
} from '@tabler/icons-react'
import { tabFromUrl } from '@/lib/tabParam'
import { cn } from '@/lib/utils'
import { FinanceReportsPage } from './FinanceReportsPage'
import { IncomeExpensePage } from './IncomeExpensePage'
import { FinanceAnalyticsPage } from './FinanceAnalyticsPage'
import { IncomePlanPage } from './IncomePlanPage'

/** Tab kalitlari — manzildagi `?tab=` qiymati (eski sahifa manzillari shularga yo'naltiriladi, `App.tsx`). */
const FINANCE_REPORT_TABS = ['umumiy', 'kirim-chiqim', 'kalendar', 'tushum-rejasi'] as const
type FinanceReportTab = (typeof FINANCE_REPORT_TABS)[number]

const TABS: { key: FinanceReportTab; label: string; hint: string; icon: Icon }[] = [
  { key: 'umumiy', label: 'Umumiy', hint: "Kirim, chiqim, qoldiq va kunlik grafik", icon: IconPresentationAnalytics },
  { key: 'kirim-chiqim', label: 'Kirim-chiqim', hint: "Toifalar bo'yicha taqsimot", icon: IconChartDonut3 },
  { key: 'kalendar', label: 'Kalendar', hint: "Kunma-kun farq va qoldiq", icon: IconCalendarStats },
  { key: 'tushum-rejasi', label: 'Tushum rejasi', hint: "Oyda kutilayotgan tushum", icon: IconTargetArrow },
]

/**
 * Moliya → «Moliya hisobotlari» — egasining talabi (2026-11-04): ilgari to'rtta alohida sahifa bo'lgan
 * «Moliya hisobotlari», «Kirim chiqim», «Moliya analitikasi» (kalendar) va «Tushum rejasi» BITTA sahifada, tablar bilan.
 *
 * ⚠️ Yangi hisob YO'Q — har tab avvalgi sahifa komponentining O'ZI (ma'lumot manbalari o'zgarmagan).
 * ⚠️ Tab manzildan HAR RENDERDA o'qiladi (`FinancePage` naqshi, `reports.md` §4): eski manzillar
 * (`/admin/moliya/kirim-chiqim` va h.k.) `?tab=` bilan shu sahifaga yo'naltiriladi. Tab almashtirish
 * `replace` bilan — "orqaga" tugmasi tablar orasida aylanmaydi.
 */
export function FinanceReportsHubPage() {
  const [params, setParams] = useSearchParams()
  const tab = tabFromUrl(FINANCE_REPORT_TABS, 'umumiy', `?${params.toString()}`)

  const select = (key: FinanceReportTab) => {
    // Faqat `tab` qoladi: «Kirim-chiqim» o'z `type/from/to` parametrlarini manzilda saqlaydi —
    // boshqa tabga o'tganda ular ortiqcha bo'lib qolmasin.
    setParams(key === 'umumiy' ? {} : { tab: key }, { replace: true })
  }

  return (
    <div className="space-y-3">
      <div>
        <h1 className="text-[18px] font-semibold text-black">Moliya hisobotlari</h1>
        <p className="text-[13px] text-[#6b7280]">Markazning kirim-chiqimi, kunlik qoldig'i va oylik tushum rejasi — bir joyda</p>
      </div>

      <div role="tablist" aria-label="Moliya hisobotlari" className="grid grid-cols-2 gap-2 lg:grid-cols-4">
        {TABS.map((t) => {
          const active = t.key === tab
          const Ico = t.icon
          return (
            <button
              key={t.key}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => select(t.key)}
              className={cn(
                'flex items-center gap-3 rounded-xl border bg-white p-3 text-left shadow-[0_1px_2px_rgba(0,0,0,0.05)] transition-colors',
                active ? 'border-brand-500 bg-brand-50' : 'border-[#dbe0e6] hover:border-slate-300 hover:bg-slate-50',
              )}
            >
              <span
                className={cn(
                  'flex h-10 w-10 shrink-0 items-center justify-center rounded-lg',
                  active ? 'bg-brand-600 text-white' : 'bg-[#f0f2f2] text-[#6b7280]',
                )}
              >
                <Ico className="h-5 w-5" />
              </span>
              <span className="min-w-0">
                <span className={cn('block text-[14px] font-semibold', active ? 'text-brand-700' : 'text-black')}>{t.label}</span>
                <span className="block truncate text-[12px] text-[#6b7280]">{t.hint}</span>
              </span>
            </button>
          )
        })}
      </div>

      <div role="tabpanel">
        {tab === 'umumiy' && <FinanceReportsPage />}
        {tab === 'kirim-chiqim' && <IncomeExpensePage />}
        {tab === 'kalendar' && <FinanceAnalyticsPage />}
        {tab === 'tushum-rejasi' && <IncomePlanPage />}
      </div>
    </div>
  )
}
