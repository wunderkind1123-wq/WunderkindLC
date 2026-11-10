import { Link } from 'react-router-dom'
import type { HrSalesKind } from '@/types/hrTasks'
import { cn } from '@/lib/utils'

/**
 * LC: SOTUV topshirig'i belgisi — «Topshiriqlar» ro'yxatlarida lid va sinov darsi topshiriqlari boshqa rangda
 * ajralib tursin (egasi, 2026-11-10). `lead` — lid sahifasidan qo'yilgan «keyingi qadam» (lid nomi havola bo'ladi),
 * `trial` — sinov darsi ro'yxati. Oddiy topshiriqda hech narsa chizilmaydi.
 */
export function SalesBadge({
  kind,
  leadId,
  leadName,
  linkToLead = true,
  className,
}: {
  kind?: HrSalesKind | null
  leadId?: string | null
  leadName?: string | null
  /** Xodim lid sahifasini ocha olmasa (masalan o'qituvchi) — havolasiz. */
  linkToLead?: boolean
  className?: string
}) {
  if (!kind) return null
  const chip = (
    <span
      className={cn(
        'inline-flex shrink-0 items-center gap-1 rounded-md bg-orange-50 px-1.5 py-px text-[11px] font-semibold text-orange-700 ring-1 ring-orange-200',
        className,
      )}
      title={kind === 'lead' ? `Sotuv: lid topshirig'i${leadName ? ` — ${leadName}` : ''}` : 'Sotuv: sinov darsi ro‘yxati'}
    >
      {kind === 'lead' ? 'Sotuv · lid' : 'Sotuv · sinov darsi'}
    </span>
  )
  if (kind === 'lead' && leadId && linkToLead)
    return (
      <Link to={`/admin/leads/${leadId}`} onClick={(e) => e.stopPropagation()} className="shrink-0 hover:opacity-80">
        {chip}
      </Link>
    )
  return chip
}

/** Sotuv topshirig'i qatorining chap chekka chizig'i (ro'yxatda ko'zga tashlansin). */
export const SALES_ROW_ACCENT = 'border-l-[3px] border-l-orange-400'
