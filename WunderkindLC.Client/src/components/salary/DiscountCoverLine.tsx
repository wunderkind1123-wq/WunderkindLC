import { formatMoney } from '@/lib/utils'

/**
 * «Chegirma qoplagani uchun ushlanma: −X so'm» (2026-11-03) — ustoz QOPLAYDIGAN chegirmalar (`StudentDiscount.TeacherCovers`)
 * bo'yicha shu oy maoshidan ushlangan markaz ulushi. Har o'quvchi alohida qator: chegirma × ulush% = ushlanma.
 * O'qituvchi profili, o'qituvchi kabineti va `TeacherSalaryDetailModal` da.
 */
export function DiscountCoverLine({
  amount,
  items,
  className,
}: {
  amount: number
  items?: { studentName: string; groupName: string; discount: number; ratePct: number; amount: number }[] | null
  className?: string
}) {
  if (amount <= 0) return null
  return (
    <div className={className}>
      <div className="rounded-lg bg-amber-50 px-3 py-2.5 text-xs text-amber-900">
        <p className="font-semibold">Chegirma qoplagani uchun ushlanma: −{formatMoney(amount)}</p>
        <p className="mt-0.5 text-[11px] text-amber-800/80">Ustoz qoplaydigan chegirmalarning markaz ulushi (maosh to'liq tarifdan hisoblangan).</p>
        {items && items.length > 0 && (
          <ul className="mt-1 space-y-0.5">
            {items.map((f, i) => (
              <li key={i} className="flex justify-between gap-3">
                <span className="min-w-0">
                  {f.studentName}
                  {f.groupName ? ` · ${f.groupName}` : ''} — chegirma {formatMoney(f.discount)} × {f.ratePct}%
                </span>
                <span className="shrink-0 font-mono">−{formatMoney(f.amount)}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
