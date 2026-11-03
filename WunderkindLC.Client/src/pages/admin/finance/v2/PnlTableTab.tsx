import { useState, type ReactNode } from 'react'
import { IconAlertTriangle, IconChevronDown, IconChevronRight, IconInfoCircle } from '@tabler/icons-react'
import type { Pnl } from '@/api/services/financeV2'
import { cn } from '@/lib/utils'
import { PnlDrillModal } from './PnlDrillModal'
import { delta, dm, dmy, money, pct, toneCls } from './financeV2Format'

type Drill = { metric: string; label: string; goodWhenUp: boolean }

/** P&L jadvali tabi: 6 KPI karta, xulosa, bo'limli jadval (Reja · Joriy · Farq) va «P&L → Pul oqimi» ko'prigi. */
export function PnlTableTab({ p }: { p: Pnl }) {
  const [drill, setDrill] = useState<Drill | null>(null)
  const t = p.total
  const r = t.revenue
  const m01 = p.period.from
  const open = (metric: string, label: string, goodWhenUp = true) => setDrill({ metric, label, goodWhenUp })

  return (
    <div className="space-y-3">
      {p.planIsEstimated && !p.isForecast && (
        <div className="flex items-start gap-2 rounded-xl border border-[#fde68a] bg-[#fffbeb] px-3 py-2 text-[12px] text-[#92400e]">
          <IconAlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            «Reja» ustuni o'quvchilarning oylik tariflaridan (oy 1-kunidagi faol a'zoliklar × narx − chegirma) hisoblandi — bu oy uchun
            oy boshi holati hali saqlanmagan.
          </span>
        </div>
      )}
      {p.isForecast && (
        <div className="flex items-start gap-2 rounded-xl border border-[#bfdbfe] bg-[#eff6ff] px-3 py-2 text-[12px] text-[#1e40af]">
          <IconInfoCircle className="mt-0.5 h-4 w-4 shrink-0" />
          Kelajak oy — «Joriy» ustuni prognoz (= reja): hozir faol a'zoliklar va tariflar bo'yicha.
        </div>
      )}

      <KpiCards p={p} onOpen={open} />

      <div className="rounded-xl border border-[#bfdbfe] bg-[#eff6ff] px-3 py-2.5 text-[12.5px] leading-relaxed text-[#1e3a8a]">
        <b>
          {p.period.monthName} {p.period.year} · {dmy(p.asOf)} holatiga.
        </b>{' '}
        Oy boshida {t.students.planned} ta o'quvchidan {money(r.net.planned)} so'm sof kutuv kutilgan edi; hozir {t.students.current} ta o'quvchi
        va kutuv {money(r.net.current)} so'm — {delta(r.net.current - r.net.planned).text}
        {r.net.planned ? ` (${pct(((r.net.current - r.net.planned) / r.net.planned) * 100, 2)})` : ''}. Formula: yalpi kutuv − chegirma − qaytarishlar
        = sof kutuv; sof kutuv + boshqa daromad − xarajat = foyda. Kassa fakti P&L natijasiga kirmaydi.
      </div>

      <div className="overflow-auto rounded-xl border border-[#dbe0e6] bg-white">
        <table className="w-full min-w-[640px] border-collapse text-[13px] text-black">
          <thead>
            <tr className="bg-[#f5f6f7] text-[12px] font-semibold uppercase">
              <th className="sticky left-0 z-10 h-11 min-w-[280px] border-b border-[#e0e0e0] bg-[#f5f6f7] px-4 text-left">Ko'rsatkich</th>
              <th className="h-11 min-w-[150px] border-b border-[#e0e0e0] px-3 text-right">
                Reja · {dm(m01)}
                {p.planIsEstimated && <div className="text-[9.5px] font-bold tracking-wide text-[#b45309]">TARIF BO'YICHA</div>}
              </th>
              <th className="h-11 min-w-[150px] border-b border-[#e0e0e0] px-3 text-right">Joriy · {dm(p.asOf)}</th>
              <th className="h-11 min-w-[150px] border-b border-[#e0e0e0] px-3 text-right">Farq</th>
            </tr>
          </thead>
          <tbody>
            <Section title="O'quvchilar" />
            <Row label="O'quvchilar soni (faol)" plan={t.students.planned} cur={t.students.current} count onClick={() => open('students', "O'quvchilar soni")} />
            <Row label="Yangi qabul qilinganlar" cur={t.students.joined} count indent onClick={() => open('joined', 'Yangi qabul qilinganlar')} />
            <Row label="Ketgan o'quvchilar" cur={t.students.left} count indent goodWhenUp={false} onClick={() => open('left', "Ketgan o'quvchilar", false)} />

            <Section title="Kutilayotgan tushum" tone="blue" />
            <Row label="Yalpi kutilayotgan tushum" plan={r.gross.planned} cur={r.gross.current} tone="blue" onClick={() => open('gross', 'Yalpi kutilayotgan tushum')} />
            <Expandable
              label="Chegirma (−)"
              plan={r.discount.planned}
              cur={r.discount.current}
              tone="pink"
              goodWhenUp={false}
              onClick={() => open('discount', 'Chegirma', false)}
              items={r.discount.byType.map((x) => ({
                key: x.key,
                label: `${x.label}${x.studentsCount ? ` · ${x.studentsCount} ta` : ''}`,
                plan: x.type === 'catalog' ? x.planned : null,
                cur: x.amount,
                onClick: () => open(`discountType:${x.key}`, x.label, false),
              }))}
            />
            <Row label="Qaytarishlar (−)" plan={r.refunds.planned} cur={r.refunds.current} goodWhenUp={false} onClick={() => open('refunds', 'Qaytarishlar', false)} />
            <Row label="Sof kutuv (NTR)" plan={r.net.planned} cur={r.net.current} tone="green" strong onClick={() => open('net', 'Sof kutuv')} />
            <Expandable
              label="Boshqa daromad (kitob, mock …)"
              plan={r.otherRevenue.planned}
              cur={r.otherRevenue.current}
              onClick={() => open('otherRevenue', 'Boshqa daromad')}
              items={r.otherRevenue.lines.map((l) => ({ key: l.key, label: l.name, plan: null, cur: l.amount, onClick: () => open('otherRevenue', 'Boshqa daromad') }))}
            />
            <Row label="O'rtacha 1 o'quvchi (sof)" plan={r.perStudentNet.planned} cur={r.perStudentNet.current} indent />
            <Row label="Chegirma darajasi" plan={r.discountRate.planned} cur={r.discountRate.current} percent indent goodWhenUp={false} />

            <Section title="Kassa (ma'lumot uchun)" tone="gray" />
            <Row label="To'lashi kerak o'quvchilar" cur={t.periodCollection.studentsExpected} count />
            <Row
              label="To'lagan · qisman · to'lamagan"
              text={`${t.periodCollection.studentsPaid} · ${t.periodCollection.studentsPartial} · ${t.periodCollection.studentsUnpaid}`}
              indent
            />
            <Row label="Shu oy uchun kelgan" cur={t.periodCollection.collectedForPeriod} onClick={() => open('collectedForPeriod', 'Shu oy uchun kelgan')} />
            <Row label="Yig'ilish darajasi (shu oy uchun)" cur={t.periodCollection.collectionRateForPeriod ?? 0} percent indent />
            <Row
              label="Shu oy uchun qoldiq"
              cur={t.periodCollection.outstandingForPeriod}
              goodWhenUp={false}
              onClick={() => open('outstanding', 'Shu oy uchun qoldiq', false)}
            />
            <Row label="Kassaga tushgan pul (shu oyda)" cur={t.cash.collected} onClick={() => open('cashCollected', 'Kassaga tushgan pul')} />
            <Row label="— shundan eski qarzdan" cur={t.cash.forPriorMonths} indent onClick={() => open('priorDebt', 'Eski qarzdan yig\'ildi')} />
            <Row label="— shundan oldindan (keyingi oylar)" cur={t.cash.forFutureMonths} indent onClick={() => open('advances', 'Oldindan to\'lovlar')} />

            <Section title="Xarajatlar" tone="red" />
            {!t.expenses.hasExpenseData && (
              <tr>
                <td colSpan={4} className="border-b border-[#eceff1] px-4 py-2 text-[12px] text-[#b45309]">
                  Xarajat kiritilmagan — foyda to'liq emas. Rejali xarajatlar: Moliya → Ma'lumotlar → «Rejali xarajat shablonlari».
                </td>
              </tr>
            )}
            {t.expenses.groups.map((g) => (
              <Expandable
                key={g.name}
                label={`${g.name} · ${g.lineCount} ta`}
                plan={g.planned}
                cur={g.current}
                goodWhenUp={false}
                onClick={() => open(`expenseGroup:${g.name}`, g.name, false)}
                items={t.expenses.lines
                  .filter((l) => l.group === g.name)
                  .map((l) => ({
                    key: l.id,
                    label: l.name,
                    badges: [l.isUnplanned ? 'REJASIZ' : null, l.isCorrected && !l.isPayroll ? 'TUZATILGAN' : null].filter(Boolean) as string[],
                    plan: l.planned,
                    cur: l.current,
                    note: l.paid ? `to'landi ${money(l.paid)}` : undefined,
                    onClick: () => open(`expenseLine:${l.id}`, l.name, false),
                  }))}
              />
            ))}
            <Row label="Jami xarajat" plan={t.expenses.planned} cur={t.expenses.current} tone="red" strong goodWhenUp={false} />

            <Section title="Natija" tone="yellow" />
            <Row label="Sof foyda / zarar" plan={t.result.profitPlanned} cur={t.result.profitCurrent} tone="yellow" strong />
            <Row label="Foyda marjasi" plan={t.result.marginPlanned} cur={t.result.marginCurrent} percent indent />
            <Row label="1 o'quvchiga foyda" plan={t.result.profitPerStudentPlanned} cur={t.result.profitPerStudentCurrent} indent />
          </tbody>
        </table>
      </div>

      {!p.isForecast && <CashBridgeCard p={p} onOpen={open} />}

      {drill && (
        <PnlDrillModal
          year={p.period.year}
          month={p.period.month}
          asOf={p.asOf}
          metric={drill.metric}
          label={drill.label}
          goodWhenUp={drill.goodWhenUp}
          onClose={() => setDrill(null)}
        />
      )}
    </div>
  )
}

// ============================ KPI ============================

function Kpi({
  title,
  value,
  sub,
  badge,
  badgeTone = 'neutral',
  progress,
  onClick,
}: {
  title: string
  value: string
  sub?: ReactNode
  badge?: string
  badgeTone?: 'good' | 'bad' | 'neutral'
  progress?: number | null
  onClick?: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="flex min-w-0 flex-col gap-1 rounded-xl border border-[#dbe0e6] bg-white p-3 text-left shadow-[0_1px_2px_rgba(0,0,0,0.04)] transition hover:border-brand-600/40 hover:shadow-md"
    >
      <div className="flex items-center justify-between gap-2">
        <span className="truncate text-[12px] font-semibold text-[#6b7280]">{title}</span>
        {badge && (
          <span
            className={cn(
              'shrink-0 rounded-full px-2 py-0.5 text-[11px] font-bold',
              badgeTone === 'good' ? 'bg-[#e8f8ee] text-[#15803d]' : badgeTone === 'bad' ? 'bg-[#fdeceb] text-[#dc2626]' : 'bg-[#eef2ff] text-brand-600',
            )}
          >
            {badge}
          </span>
        )}
      </div>
      <span className="truncate text-[20px] font-bold tabular-nums text-black">{value}</span>
      {progress !== undefined && progress !== null && (
        <div className="h-1.5 w-full overflow-hidden rounded-full bg-[#eef2f7]">
          <div className="h-full rounded-full bg-[#22c55e]" style={{ width: `${Math.max(0, Math.min(100, progress))}%` }} />
        </div>
      )}
      {sub && <span className="text-[11.5px] leading-snug text-[#6b7280]">{sub}</span>}
    </button>
  )
}

function KpiCards({ p, onOpen }: { p: Pnl; onOpen: (metric: string, label: string, goodWhenUp?: boolean) => void }) {
  const t = p.total
  const r = t.revenue
  const pc = t.periodCollection
  const expD = delta(t.expenses.current - t.expenses.planned, false)
  const profD = delta(t.result.profitCurrent - t.result.profitPlanned, true)
  return (
    <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-6">
      <Kpi
        title="O'quvchilar soni"
        value={String(t.students.current)}
        badge={`${pc.studentsPaid} to'lagan`}
        badgeTone="good"
        sub={`${pc.studentsPaid} ta to'lagan · ${pc.studentsPartial} ta qisman · ${pc.studentsUnpaid} ta to'lamagan`}
        onClick={() => onOpen('students', "O'quvchilar soni")}
      />
      <Kpi
        title="Fakt kutilayotgan tushum"
        value={money(r.net.current)}
        badge={pct(pc.collectionRateForPeriod, 0)}
        badgeTone={(pc.collectionRateForPeriod ?? 0) >= 90 ? 'good' : 'neutral'}
        progress={pc.collectionRateForPeriod}
        sub={`${money(pc.collectedForPeriod)} keldi · qoldiq ${money(pc.outstandingForPeriod)}`}
        onClick={() => onOpen('net', 'Sof kutuv')}
      />
      <Kpi
        title="Chegirma"
        value={money(r.discount.current)}
        badge={pct(r.discountRate.current)}
        badgeTone="neutral"
        sub={`yalpi kutuvdan · reja ${pct(r.discountRate.planned)}`}
        onClick={() => onOpen('discount', 'Chegirma', false)}
      />
      <Kpi
        title="Kassaga yig'ildi"
        value={money(t.cash.collected)}
        sub="kassa fakti · davrga bog'lanmagan (P&L ga kirmaydi)"
        onClick={() => onOpen('cashCollected', 'Kassaga tushgan pul')}
      />
      <Kpi
        title="Jami xarajat"
        value={t.expenses.hasExpenseData ? money(t.expenses.current) : 'Ma\'lumot yo\'q'}
        badge={t.expenses.hasExpenseData ? expD.text : undefined}
        badgeTone={expD.tone}
        sub={t.expenses.hasExpenseData ? `rejadan farq · to'lanmagan ${money(t.expenses.unpaid)}` : 'Xarajat kiritilmagan'}
      />
      <Kpi
        title="Sof foyda / zarar"
        value={money(t.result.profitCurrent)}
        badge={profD.text}
        badgeTone={profD.tone}
        sub={`marja ${pct(t.result.marginCurrent)} · rejadan farq`}
      />
    </div>
  )
}

// ============================ Jadval qatorlari ============================

type RowTone = 'blue' | 'pink' | 'green' | 'red' | 'yellow' | 'gray'
const rowBg: Record<RowTone, string> = {
  blue: 'bg-[#eef4ff]',
  pink: 'bg-[#fdf2f8]',
  green: 'bg-[#e8f8ee]',
  red: 'bg-[#fdeceb]',
  yellow: 'bg-[#fff6e5]',
  gray: 'bg-[#f5f6f7]',
}
const sectionCls: Record<RowTone, string> = {
  blue: 'text-[#1d4ed8] border-l-4 border-l-[#3b82f6]',
  pink: 'text-[#be185d] border-l-4 border-l-[#ec4899]',
  green: 'text-[#15803d] border-l-4 border-l-[#22c55e]',
  red: 'text-[#b91c1c] border-l-4 border-l-[#ef4444]',
  yellow: 'text-[#a16207] border-l-4 border-l-[#eab308]',
  gray: 'text-[#374151] border-l-4 border-l-[#9ca3af]',
}

function Section({ title, tone = 'gray' }: { title: string; tone?: RowTone }) {
  return (
    <tr>
      <td colSpan={4} className={cn('sticky left-0 h-9 border-b border-[#e5e7eb] bg-white px-4 text-[11.5px] font-bold uppercase tracking-wide', sectionCls[tone])}>
        {title}
      </td>
    </tr>
  )
}

function fmtVal(v: number | null | undefined, count?: boolean, percent?: boolean) {
  if (v === null || v === undefined) return '—'
  if (percent) return pct(v)
  if (count) return String(v)
  return money(v)
}

function DeltaCell({ plan, cur, count, percent, goodWhenUp }: { plan: number | null | undefined; cur: number | null | undefined; count?: boolean; percent?: boolean; goodWhenUp: boolean }) {
  if (plan === null || plan === undefined || cur === null || cur === undefined) return <td className="border-b border-[#eceff1] px-3 text-right text-[#9ca3af]">—</td>
  const d = delta(cur - plan, goodWhenUp)
  const text = percent ? (Math.round((cur - plan) * 100) === 0 ? '= 0' : `${cur > plan ? '▲ +' : '▼ −'}${pct(Math.abs(cur - plan))}`) : count ? d.text.replace(/\s/g, '') : d.text
  return <td className={cn('whitespace-nowrap border-b border-[#eceff1] px-3 text-right font-semibold tabular-nums', toneCls[d.tone])}>{text}</td>
}

function Row({
  label,
  plan,
  cur,
  text,
  tone,
  strong,
  indent,
  count,
  percent,
  goodWhenUp = true,
  onClick,
}: {
  label: ReactNode
  plan?: number | null
  cur?: number | null
  text?: string
  tone?: RowTone
  strong?: boolean
  indent?: boolean
  count?: boolean
  percent?: boolean
  goodWhenUp?: boolean
  onClick?: () => void
}) {
  const bg = tone ? rowBg[tone] : 'bg-white'
  return (
    <tr onClick={onClick} className={cn(bg, onClick && 'cursor-pointer hover:brightness-[0.97]')}>
      <td className={cn('sticky left-0 z-[1] h-10 border-b border-[#eceff1] px-4', bg, strong && 'font-bold', indent && 'pl-9 text-[#4b5563]')}>{label}</td>
      <td className={cn('whitespace-nowrap border-b border-[#eceff1] px-3 text-right tabular-nums text-[#4b5563]', strong && 'font-semibold')}>
        {text ? '—' : fmtVal(plan, count, percent)}
      </td>
      <td className={cn('whitespace-nowrap border-b border-[#eceff1] px-3 text-right tabular-nums', strong && 'font-bold', (cur ?? 0) < 0 && 'text-[#dc2626]')}>
        {text ?? fmtVal(cur, count, percent)}
      </td>
      {text ? <td className="border-b border-[#eceff1]" /> : <DeltaCell plan={plan} cur={cur} count={count} percent={percent} goodWhenUp={goodWhenUp} />}
    </tr>
  )
}

function Expandable({
  label,
  plan,
  cur,
  tone,
  goodWhenUp = true,
  onClick,
  items,
}: {
  label: string
  plan: number
  cur: number
  tone?: RowTone
  goodWhenUp?: boolean
  onClick?: () => void
  items: { key: string; label: string; badges?: string[]; plan: number | null; cur: number; note?: string; onClick?: () => void }[]
}) {
  const [open, setOpen] = useState(false)
  const bg = tone ? rowBg[tone] : 'bg-white'
  return (
    <>
      <tr className={cn(bg)}>
        <td className={cn('sticky left-0 z-[1] h-10 border-b border-[#eceff1] px-4', bg)}>
          <button type="button" onClick={() => setOpen(!open)} className="inline-flex items-center gap-1 font-medium" disabled={items.length === 0}>
            {items.length > 0 ? open ? <IconChevronDown className="h-4 w-4" /> : <IconChevronRight className="h-4 w-4" /> : <span className="inline-block w-4" />}
            {label}
          </button>
        </td>
        <td onClick={onClick} className="cursor-pointer whitespace-nowrap border-b border-[#eceff1] px-3 text-right tabular-nums text-[#4b5563]">
          {money(plan)}
        </td>
        <td onClick={onClick} className="cursor-pointer whitespace-nowrap border-b border-[#eceff1] px-3 text-right font-medium tabular-nums">
          {money(cur)}
        </td>
        <DeltaCell plan={plan} cur={cur} goodWhenUp={goodWhenUp} />
      </tr>
      {open &&
        items.map((it) => (
          <tr key={it.key} onClick={it.onClick} className={cn('bg-[#fafafa]', it.onClick && 'cursor-pointer hover:bg-[#f3f4f6]')}>
            <td className="sticky left-0 z-[1] h-9 border-b border-[#f0f2f2] bg-[#fafafa] py-1 pl-11 pr-4 text-[12.5px] text-[#4b5563]">
              <span className="mr-1.5">{it.label}</span>
              {it.badges?.map((b) => (
                <span
                  key={b}
                  className={cn(
                    'mr-1 rounded px-1.5 py-[1px] text-[9.5px] font-bold tracking-wide',
                    b === 'REJASIZ' ? 'bg-[#fef3c7] text-[#92400e]' : 'bg-[#e0e7ff] text-[#3730a3]',
                  )}
                >
                  {b}
                </span>
              ))}
              {it.note && <span className="ml-1 text-[11px] text-[#9ca3af]">· {it.note}</span>}
            </td>
            <td className="whitespace-nowrap border-b border-[#f0f2f2] px-3 text-right text-[12.5px] tabular-nums text-[#6b7280]">
              {it.plan === null ? '—' : money(it.plan)}
            </td>
            <td className="whitespace-nowrap border-b border-[#f0f2f2] px-3 text-right text-[12.5px] tabular-nums">{money(it.cur)}</td>
            <DeltaCell plan={it.plan} cur={it.cur} goodWhenUp={goodWhenUp} />
          </tr>
        ))}
    </>
  )
}

// ============================ Ko'prik ============================

function CashBridgeCard({ p, onOpen }: { p: Pnl; onOpen: (metric: string, label: string, goodWhenUp?: boolean) => void }) {
  const b = p.cashBridge
  const step = (label: string, value: number, sign: '+' | '−' | '=', hint?: string, onClick?: () => void) => (
    <div
      onClick={onClick}
      className={cn(
        'flex items-center justify-between gap-3 border-b border-[#f0f2f2] px-4 py-2 text-[13px]',
        sign === '=' && 'bg-[#fff6e5] font-bold',
        onClick && 'cursor-pointer hover:bg-[#f9fafb]',
      )}
    >
      <div className="min-w-0">
        <span className="mr-2 inline-block w-4 text-center font-bold text-[#6b7280]">{sign}</span>
        {label}
        {hint && <div className="pl-6 text-[11px] font-normal text-[#9ca3af]">{hint}</div>}
      </div>
      <span className={cn('shrink-0 tabular-nums', value < 0 && 'text-[#dc2626]')}>{money(value)}</span>
    </div>
  )
  return (
    <div className="overflow-hidden rounded-xl border border-[#dbe0e6] bg-white">
      <div className="border-b border-[#e5e7eb] bg-[#f5f6f7] px-4 py-2.5">
        <div className="text-[13px] font-bold">P&L → Pul oqimi ko'prigi</div>
        <div className="text-[11.5px] text-[#6b7280]">«Foydadaman-u, pulim qayerda?» — foyda bilan kassadagi pul o'zgarishi orasidagi farq qayerdan kelgani.</div>
      </div>
      {step('Sof foyda', b.profit, '+')}
      {step(
        b.unpaidExpense >= 0 ? "To'lanmagan xarajat (majburiyat o'sdi)" : "Oldindan to'langan xarajat (avans va h.k.)",
        b.unpaidExpense,
        '+',
        `hisoblangan xarajat − shu oyda to'langani · to'landi ${money(b.expensePaid)}`,
      )}
      {step("Yig'ilmagan tushum", b.uncollectedRevenue, '−', 'shu oy hisobi − shu oy uchun kelgan to\'lovlar', () => onOpen('outstanding', 'Shu oy uchun qoldiq', false))}
      {step("Eski qarzdan yig'ildi", b.priorDebtCollected, '+', "o'tgan oylar hisobiga kelgan to'lovlar", () => onOpen('priorDebt', "Eski qarzdan yig'ildi"))}
      {step("Oldindan to'lovlar (keyingi oylar)", b.advancesCollected, '+', undefined, () => onOpen('advances', "Oldindan to'lovlar"))}
      {step("P&L ga kirmagan pul harakati", b.outsidePnl, '+', 'xato tuzatish, jarima, moliyalashtirish va h.k.')}
      {step("Pul o'zgarishi (kassa + hisob raqam)", b.cashDelta, '=')}
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-2 text-[12px] text-[#4b5563]">
        <span>
          Haqiqiy pul o'zgarishi (Pul oqimi hisobotidan, shu oralig'da): <b className="text-black">{money(b.actualCashDelta)}</b>
        </span>
        {Math.round(b.difference) === 0 ? (
          <span className="font-semibold text-[#15803d]">✓ Farq 0 — ko'prik to'liq yopildi</span>
        ) : (
          <span className="font-semibold text-[#b45309]">
            Farq {money(b.difference)} — «Hisobot toifalari»da pul oqimidan chiqarilgan tur bor
          </span>
        )}
      </div>
    </div>
  )
}
