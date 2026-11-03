import { useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { IconAlertTriangle, IconBell, IconCashBanknote, IconChecklist, IconPhoneCall } from '@tabler/icons-react'
import { getAbsentNoticeSummary } from '@/api/services/attendanceNotices'
import { getContactMeta } from '@/api/services/contacts'
import { getRedList } from '@/api/services/absences'
import { getOtherDebtors, type OtherDebtKind } from '@/api/services/dashboard'
import { useAsync } from '@/hooks/useAsync'
import { usePerm } from '@/lib/permissions'
import { cn, formatMoney } from '@/lib/utils'

/**
 * Bosh sahifa vidjetlari (CRM Phase 5, `lib/homeWidgets.ts`) — kichik bloklar. Har biri O'Z ma'lumotini
 * o'zi yuklaydi va server darvozasidan o'tadi (vidjet sozlamasi ma'lumot OCHMAYDI). O'qish xatosi blok
 * ichida qisqa matn bilan chiqadi — bosh sahifaning qolgani buzilmaydi.
 */

function BlockCard({
  title,
  icon: Icon,
  to,
  linkLabel,
  children,
}: {
  title: string
  icon: typeof IconChecklist
  to?: string
  linkLabel?: string
  children: ReactNode
}) {
  return (
    <section className="rounded-xl border border-[#DBE0E6] bg-white p-3 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 text-[15px] font-semibold text-black">
          <Icon size={20} stroke={2} className="text-[#3D68FF]" />
          {title}
        </h2>
        {to && (
          <Link to={to} className="text-[12px] font-medium text-[#3D68FF] hover:underline">
            {linkLabel}
          </Link>
        )}
      </div>
      {children}
    </section>
  )
}

/**
 * «Davomat xabarlari» (CRM U3b): bugungi «kelmadi» navbati — tasdiq kutayotgan, to'xtatilgan, rejalashtirilgan va
 * yuborilganlar soni + nazorat sahifasiga havola. Ruxsat `students.notices` (server ham shu bilan darvozalaydi).
 */
export function AbsentNoticesBlock() {
  const summary = useAsync(() => getAbsentNoticeSummary(), [])
  const c = summary.data?.counts
  const n = (v: number | undefined) => (summary.loading ? '…' : (v ?? 0))
  return (
    <BlockCard title="Davomat xabarlari" icon={IconBell} to="/admin/nazorat/davomat-xabarlari" linkLabel="Nazoratga o'tish">
      {summary.error ? (
        <p className="text-[12px] text-red-600">Davomat xabarlarini yuklab bo'lmadi: {summary.error}</p>
      ) : (
        <div className="flex flex-wrap gap-2 text-[12px] font-semibold">
          <span
            className={cn(
              'rounded-full px-3 py-1.5 text-[14px]',
              (c?.awaiting ?? 0) > 0 ? 'bg-amber-50 text-amber-700' : 'bg-[#EEF2FF] text-[#3D68FF]',
            )}
          >
            Tasdiq kutmoqda: {n(c?.awaiting)}
          </span>
          <span className="rounded-full bg-violet-50 px-2.5 py-1.5 text-violet-700">To'xtatilgan: {n(c?.held)}</span>
          <span className="rounded-full bg-sky-50 px-2.5 py-1.5 text-sky-700">
            Rejalashtirilgan: {n((c?.pending ?? 0) + (c?.approved ?? 0))}
          </span>
          <span className="rounded-full bg-emerald-50 px-2.5 py-1.5 text-emerald-700">Yuborildi: {n(c?.sent)}</span>
        </div>
      )}
    </BlockCard>
  )
}

/**
 * «Qizil ro'yxat» (CRM U3d): bitta guruhda ketma-ket SABABSIZ dars qoldirganlar — eng uzun seriyalar, top 10.
 * Ruxsat `students.attendance`; o'quvchiga havola faqat `students.list` bo'lsa (aks holda sahifa "ruxsat yo'q" derdi).
 */
export function RedListBlock() {
  const { can } = usePerm()
  const canOpenStudent = can('students.list', 'view')
  const list = useAsync(() => getRedList({ min: 3 }), [])
  const rows = [...(list.data ?? [])]
    .sort((a, b) => b.streak - a.streak || b.lastDate.localeCompare(a.lastDate))
    .slice(0, 10)
  return (
    <BlockCard title="Qizil ro'yxat" icon={IconAlertTriangle} to="/admin/students/davomat?tab=qizil" linkLabel="Barchasi">
      {list.error ? (
        <p className="text-[12px] text-red-600">Qizil ro'yxatni yuklab bo'lmadi: {list.error}</p>
      ) : list.loading ? (
        <p className="text-[12px] text-[#6B7280]">Yuklanmoqda…</p>
      ) : rows.length === 0 ? (
        <p className="text-[12px] text-[#6B7280]">Ketma-ket 3 va undan ko'p dars qoldirgan o'quvchi yo'q.</p>
      ) : (
        <ul className="space-y-1">
          {rows.map((r) => (
            <li
              key={`${r.studentId}:${r.groupId}`}
              className="flex items-center justify-between gap-2 rounded-lg border border-[#F0F2F2] px-2 py-1.5 text-[13px]"
            >
              <span className="flex min-w-0 items-center gap-2">
                <span
                  className={cn(
                    'shrink-0 rounded-full px-1.5 py-0.5 text-[11px] font-bold',
                    r.level >= 5 ? 'bg-rose-100 text-rose-700' : 'bg-amber-100 text-amber-700',
                  )}
                >
                  {r.level >= 5 ? '5+' : '3+'}
                </span>
                {canOpenStudent ? (
                  <Link to={`/admin/students/${r.studentId}`} className="truncate text-black hover:underline">
                    {r.studentName}
                  </Link>
                ) : (
                  <span className="truncate text-black">{r.studentName}</span>
                )}
                <span className="truncate text-[11px] text-[#6B7280]">{r.groupName}</span>
              </span>
              <span className="shrink-0 text-[11px] font-semibold text-rose-600">{r.streak} dars</span>
            </li>
          ))}
        </ul>
      )}
    </BlockCard>
  )
}

const DEBT_KIND_STYLE: Record<OtherDebtKind, string> = {
  left: 'bg-rose-50 text-rose-700',
  archived: 'bg-slate-100 text-slate-700',
  trial: 'bg-violet-50 text-violet-700',
  frozen: 'bg-sky-50 text-sky-700',
}

/** Yopiq holatda ko'rsatiladigan qatorlar soni. */
const OTHER_DEBTORS_PREVIEW = 10

/**
 * «Boshqa qarzdorlar»: aktiv bo'lmagan o'quvchilardagi qarz — guruhdan ketgan, arxivdagi, sinovdagi, muzlatilgan.
 * Aktiv qarzdorlar «Qarzdorlar» kartochkasida; bu yerda — qolgani, ya'ni hech bir qarz ko'zdan yo'qolmaydi.
 * Toifa chipi bosilsa ro'yxat o'sha toifaga toraytiriladi. Ruxsat `students.list` (server ham shu bilan).
 */
export function OtherDebtorsBlock() {
  const res = useAsync(() => getOtherDebtors(), [])
  const [kind, setKind] = useState<OtherDebtKind | null>(null)
  const [expanded, setExpanded] = useState(false)
  const data = res.data
  const labels = new Map((data?.groups ?? []).map((g) => [g.kind, g.label]))
  const rows = (data?.items ?? []).filter((r) => !kind || r.kind === kind)
  const shown = expanded ? rows : rows.slice(0, OTHER_DEBTORS_PREVIEW)
  const totalCount = (data?.groups ?? []).reduce((s, g) => s + g.count, 0)
  const totalSum = (data?.groups ?? []).reduce((s, g) => s + g.total, 0)

  return (
    <BlockCard title="Boshqa qarzdorlar" icon={IconCashBanknote}>
      {res.error ? (
        <p className="text-[12px] text-red-600">Qarzdorlarni yuklab bo'lmadi: {res.error}</p>
      ) : res.loading || !data ? (
        <p className="text-[12px] text-[#6B7280]">Yuklanmoqda…</p>
      ) : (
        <div className="space-y-2">
          <div className="flex flex-wrap gap-2 text-[12px] font-semibold">
            <button
              type="button"
              onClick={() => setKind(null)}
              className={cn(
                'rounded-full px-3 py-1.5 text-left',
                kind === null ? 'bg-[#3D68FF] text-white' : 'bg-[#EEF2FF] text-[#3D68FF]',
              )}
            >
              Hammasi: {totalCount} ta · {formatMoney(totalSum)}
            </button>
            {data.groups.map((g) => (
              <button
                key={g.kind}
                type="button"
                onClick={() => setKind(kind === g.kind ? null : g.kind)}
                className={cn(
                  'rounded-full px-2.5 py-1.5 text-left',
                  DEBT_KIND_STYLE[g.kind],
                  kind === g.kind && 'ring-2 ring-current',
                )}
              >
                {g.label}: {g.count} ta · {formatMoney(g.total)}
              </button>
            ))}
          </div>
          <p className="text-[11px] text-[#6B7280]">
            Aktiv o'quvchilardagi qarz («Qarzdorlar» kartochkasi): {data.activeCount} ta · {formatMoney(data.activeTotal)}
          </p>
          {rows.length === 0 ? (
            <p className="text-[12px] text-[#6B7280]">Bu toifada qarzdor yo'q.</p>
          ) : (
            <>
              <ul className="space-y-1">
                {shown.map((r) => (
                  <li
                    key={r.studentId}
                    className="flex items-center justify-between gap-2 rounded-lg border border-[#F0F2F2] px-2 py-1.5 text-[13px]"
                  >
                    <span className="flex min-w-0 items-center gap-2">
                      <span className={cn('shrink-0 rounded-full px-1.5 py-0.5 text-[11px] font-semibold', DEBT_KIND_STYLE[r.kind])}>
                        {labels.get(r.kind) ?? r.kind}
                      </span>
                      <Link to={`/admin/students/${r.studentId}`} className="truncate text-black hover:underline">
                        {r.fullName}
                      </Link>
                    </span>
                    <span className="shrink-0 text-[12px] font-semibold text-rose-600">{formatMoney(r.debt)}</span>
                  </li>
                ))}
              </ul>
              {rows.length > OTHER_DEBTORS_PREVIEW && (
                <button
                  type="button"
                  onClick={() => setExpanded((v) => !v)}
                  className="text-[12px] font-medium text-[#3D68FF] hover:underline"
                >
                  {expanded ? "Qisqartirish" : `Barchasini ko'rsatish (${rows.length})`}
                </button>
              )}
              {data.truncated && expanded && (
                <p className="text-[11px] text-[#6B7280]">Ro'yxatda eng katta {data.items.length} ta qarz; sanoqlar to'liq.</p>
              )}
            </>
          )}
        </div>
      )}
    </BlockCard>
  )
}

/** «Bog'lanish kerak — bugun»: bugun qilinishi kerak (muddati o'tgan + bugungi + sanasiz) soni. */
export function ContactsTodayBlock() {
  const meta = useAsync(() => getContactMeta(), [])
  const due = meta.data?.due
  return (
    <BlockCard title="Bog'lanish kerak — bugun" icon={IconPhoneCall} to="/admin/students/boglanish" linkLabel="Navbatga o'tish">
      {meta.error ? (
        <p className="text-[12px] text-red-600">Navbatni yuklab bo'lmadi: {meta.error}</p>
      ) : (
        <div className="flex flex-wrap gap-2 text-[12px] font-semibold">
          <span className="rounded-full bg-[#EEF2FF] px-3 py-1.5 text-[14px] text-[#3D68FF]">
            Bugun qilish kerak: {meta.loading ? '…' : (due?.todo ?? 0)}
          </span>
          {(due?.overdue ?? 0) > 0 && (
            <span className="rounded-full bg-rose-50 px-2.5 py-1.5 text-rose-700">Muddati o'tgan: {due!.overdue}</span>
          )}
          {(due?.today ?? 0) > 0 && (
            <span className="rounded-full bg-amber-50 px-2.5 py-1.5 text-amber-700">Bugun: {due!.today}</span>
          )}
        </div>
      )}
    </BlockCard>
  )
}
