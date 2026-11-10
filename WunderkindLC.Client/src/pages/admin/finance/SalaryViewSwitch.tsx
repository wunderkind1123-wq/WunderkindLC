import { Link } from 'react-router-dom'
import { cn } from '@/lib/utils'

/**
 * «Oylik chiqarish» — BITTA sahifa (egasi, 2026-11-10: «maosh reestri bilan oylik chiqarish bitta page'da bo'lsin»).
 * Moliya'ning ikki tabi bir sahifa bo'lib ko'rinadi:
 * - `payroll` — OY bo'yicha hammasi (o'qituvchi + xodim): oylik, bonus/jarima, avans/yakuniy, to'lash (`PayrollTab`);
 * - `teachers` — o'qituvchilar maoshining DAVR bo'yicha batafsil hisobi (ushlanmalar, jarimalar, oy tafsiloti).
 * Menyuda bitta band («Oylik chiqarish» → `payroll`).
 */
export function SalaryViewSwitch({ tab }: { tab: 'payroll' | 'teachers' }) {
  const items = [
    { key: 'payroll', label: 'Oy bo‘yicha — hammasi', hint: 'o‘qituvchi + xodim, bonus/jarima, to‘lash' },
    { key: 'teachers', label: 'O‘qituvchilar — davr hisobi', hint: 'ushlanmalar va jarimalar bilan' },
  ] as const
  return (
    <div className="mb-4 flex flex-wrap gap-2" role="tablist" aria-label="Oylik chiqarish">
      {items.map((it) => (
        <Link
          key={it.key}
          role="tab"
          aria-selected={tab === it.key}
          to={`/admin/finance?tab=${it.key}`}
          className={cn(
            'rounded-xl px-4 py-2 text-sm ring-1 transition',
            tab === it.key
              ? 'bg-brand-600 font-semibold text-white ring-brand-600'
              : 'bg-white text-slate-600 ring-slate-200 hover:bg-slate-50',
          )}
        >
          {it.label}
          <span className={cn('block text-xs font-normal', tab === it.key ? 'text-white/80' : 'text-slate-400')}>{it.hint}</span>
        </Link>
      ))}
    </div>
  )
}
