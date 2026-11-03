import { useState } from 'react'
import { Link } from 'react-router-dom'
import { getPnlDrilldown } from '@/api/services/financeV2'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { useAsync } from '@/hooks/useAsync'
import { usePerm } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { delta, dm, dmy, money, toneCls } from './financeV2Format'

/** P&L katakchasi → tafsilot: reja · joriy · o'zgarish va unga ta'sir qilgan o'quvchilar/amallar. */
export function PnlDrillModal({
  year,
  month,
  asOf,
  metric,
  label,
  goodWhenUp = true,
  onClose,
}: {
  year: number
  month: number
  asOf: string
  metric: string
  label: string
  goodWhenUp?: boolean
  onClose: () => void
}) {
  const [page, setPage] = useState(1)
  const { can } = usePerm()
  const canOpenStudent = can('students.list', 'view')
  const res = useAsync(() => getPnlDrilldown({ year, month, asOf, metric, page }), [year, month, asOf, metric, page])
  const d = res.data
  const pages = d ? Math.max(1, Math.ceil(d.total / d.pageSize)) : 1
  const m01 = `${year}-${String(month).padStart(2, '0')}-01`
  const isCount = metric === 'students' || metric === 'joined' || metric === 'left'
  const fmt = (n: number) => (isCount ? String(n) : money(n))

  return (
    <Modal open onClose={onClose} size="xl" title={d?.title || label}>
      {res.error ? (
        <p className="text-[13px] text-red-600">{res.error}</p>
      ) : !d ? (
        <Loader className="min-h-[160px]" />
      ) : (
        <div className="space-y-3">
          <p className="text-[12.5px] text-[#4b5563]">{d.description}</p>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
            <div className="rounded-lg border border-[#e5e7eb] bg-[#f9fafb] px-3 py-2">
              <div className="text-[11px] font-semibold uppercase text-[#6b7280]">Reja · {dm(m01)}</div>
              <div className="text-[16px] font-bold tabular-nums">{fmt(d.planned)}</div>
            </div>
            <div className="rounded-lg border border-[#e5e7eb] bg-[#f9fafb] px-3 py-2">
              <div className="text-[11px] font-semibold uppercase text-[#6b7280]">Joriy · {dm(asOf)}</div>
              <div className="text-[16px] font-bold tabular-nums">{fmt(d.current)}</div>
            </div>
            <div className="rounded-lg border border-[#e5e7eb] bg-[#f9fafb] px-3 py-2">
              <div className="text-[11px] font-semibold uppercase text-[#6b7280]">O'zgarish</div>
              {(() => {
                const x = delta(d.current - d.planned, goodWhenUp)
                return <div className={cn('text-[16px] font-bold tabular-nums', toneCls[x.tone])}>{isCount ? x.text.replace(/\s/g, '') : x.text}</div>
              })()}
            </div>
          </div>

          {d.total === 0 ? (
            <p className="py-6 text-center text-[13px] text-[#6b7280]">Tanlangan sanagacha hodisa yo'q.</p>
          ) : (
            <>
              <div className="max-h-[55vh] overflow-auto rounded-lg border border-[#e5e7eb]">
                <table className="w-full border-collapse text-[12.5px]">
                  <thead className="sticky top-0 bg-[#f5f6f7] text-[11px] uppercase text-[#4b5563]">
                    <tr>
                      <th className="px-3 py-2 text-left">Nomi</th>
                      <th className="px-3 py-2 text-left">Tafsilot</th>
                      <th className="px-3 py-2 text-left">Sana</th>
                      <th className="px-3 py-2 text-right">{d.amountLabel}</th>
                      {d.amount2Label && <th className="px-3 py-2 text-right">{d.amount2Label}</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {d.items.map((r, i) => (
                      <tr key={`${r.title}-${i}`} className="border-t border-[#f0f2f2]">
                        <td className="px-3 py-1.5 font-medium">
                          {r.studentId && canOpenStudent ? (
                            <Link to={`/admin/students/${r.studentId}`} className="hover:text-brand-600 hover:underline">
                              {r.title}
                            </Link>
                          ) : (
                            r.title
                          )}
                        </td>
                        <td className="px-3 py-1.5 text-[#4b5563]">{r.subtitle || '—'}</td>
                        <td className="whitespace-nowrap px-3 py-1.5">{r.date ? dmy(r.date) : '—'}</td>
                        <td className="whitespace-nowrap px-3 py-1.5 text-right font-semibold tabular-nums">{money(r.amount)}</td>
                        {d.amount2Label && (
                          <td className="whitespace-nowrap px-3 py-1.5 text-right tabular-nums text-[#4b5563]">
                            {r.amount2 === null ? '—' : money(r.amount2)}
                          </td>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="flex flex-wrap items-center justify-between gap-2 text-[12px] text-[#6b7280]">
                <span>
                  Hodisalar soni: <b className="text-black">{d.total}</b> · jami <b className="text-black">{money(d.sum)} so'm</b> · Hisoblanma
                  manbasi: oylik hisoblar (MonthlyCharge) va kassa amallari
                </span>
                {pages > 1 && (
                  <span className="flex items-center gap-2">
                    <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="rounded border px-2 py-1 disabled:opacity-40">
                      ‹
                    </button>
                    {page} / {pages}
                    <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="rounded border px-2 py-1 disabled:opacity-40">
                      ›
                    </button>
                  </span>
                )}
              </div>
            </>
          )}
        </div>
      )}
    </Modal>
  )
}
