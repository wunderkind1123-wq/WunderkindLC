import { formatNumber } from '@/lib/numberFormat'

/**
 * Pul oqimi 2.0 · P&L 2.0 — ko'rinish yordamchilari (komponentsiz fayl — react-refresh qoidasi).
 * Summalar `1 234 567` (bo'sh joy bilan, `lib/numberFormat` — yagona manba).
 */

export const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
] as const

export const MONTHS_SHORT = ['Yan', 'Fev', 'Mar', 'Apr', 'May', 'Iyn', 'Iyl', 'Avg', 'Sen', 'Okt', 'Noy', 'Dek'] as const

/** Summa: 1 234 567 (butun so'm). */
export function money(n: number | null | undefined): string {
  if (n === null || n === undefined) return '—'
  return formatNumber(Math.round(n))
}

/** Foiz: 84,29% (`null` → «—»). */
export function pct(n: number | null | undefined, digits = 1): string {
  if (n === null || n === undefined || !Number.isFinite(n)) return '—'
  return `${n.toFixed(digits).replace('.', ',')}%`
}

/**
 * Farq: «▲ +X» / «▼ −X» / «= 0». Rang yagona belgi EMAS — har doim strelka va ishora bor (rang ko'r foydalanuvchi ham
 * o'qiydi). `goodWhenUp`: daromad qatorlarida o'sish yaxshi (yashil), xarajatda — yomon (qizil).
 */
export function delta(n: number, goodWhenUp = true): { text: string; tone: 'good' | 'bad' | 'neutral' } {
  const r = Math.round(n)
  if (r === 0) return { text: '= 0', tone: 'neutral' }
  const up = r > 0
  return {
    text: `${up ? '▲ +' : '▼ −'}${formatNumber(Math.abs(r))}`,
    tone: up === goodWhenUp ? 'good' : 'bad',
  }
}

export const toneCls = { good: 'text-[#15803d]', bad: 'text-[#dc2626]', neutral: 'text-[#6b7280]' } as const

/** "2026-04-29" → "29.04.2026". */
export function dmy(iso: string | null | undefined): string {
  const d = (iso ?? '').slice(0, 10)
  if (d.length < 10) return '—'
  const [y, m, dd] = d.split('-')
  return `${dd}.${m}.${y}`
}

/** "2026-04-29" → "29.04". */
export function dm(iso: string): string {
  return `${iso.slice(8, 10)}.${iso.slice(5, 7)}`
}

/** Mahalliy bugungi sana "yyyy-MM-dd" (UTC emas). */
export function todayIso(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** Yillar tanlovi: birinchi amal yilidan joriy yil + 1 gacha (eng yangisi tepada). */
export function yearRange(firstYear: number, now = new Date().getFullYear()): number[] {
  const from = Math.min(firstYear || now, now)
  const out: number[] = []
  for (let y = now + 1; y >= from; y--) out.push(y)
  return out
}

/** Davr presetlari (P&L): backend faqat (oy, holat sanasi) qabul qiladi — oraliq boshlanishining oyi + tugashi oy ichiga qisiladi. */
export type PresetKey = 'today' | 'yesterday' | 'last7' | 'last30' | 'thisWeek' | 'lastWeek' | 'thisMonth' | 'lastMonth' | 'thisYear'

export const PRESETS: { key: PresetKey; label: string }[] = [
  { key: 'today', label: 'Bugun' },
  { key: 'yesterday', label: 'Kecha' },
  { key: 'last7', label: 'Oxirgi 7 kun' },
  { key: 'last30', label: 'Oxirgi 30 kun' },
  { key: 'thisWeek', label: 'Shu hafta' },
  { key: 'lastWeek', label: "O'tgan hafta" },
  { key: 'thisMonth', label: 'Shu oy' },
  { key: 'lastMonth', label: "O'tgan oy" },
  { key: 'thisYear', label: 'Shu yil' },
]

const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

/** Preset → { year, month, asOf } (asOf oy ichiga qisiladi, bugundan keyin bo'lmaydi). */
export function presetPeriod(key: PresetKey, now = new Date()): { year: number; month: number; asOf: string } {
  const day = (offset: number) => new Date(now.getFullYear(), now.getMonth(), now.getDate() + offset)
  let from: Date
  let to: Date
  const dow = (now.getDay() + 6) % 7 // Dushanba = 0
  switch (key) {
    case 'today': from = to = day(0); break
    case 'yesterday': from = to = day(-1); break
    case 'last7': from = day(-6); to = day(0); break
    case 'last30': from = day(-29); to = day(0); break
    case 'thisWeek': from = day(-dow); to = day(0); break
    case 'lastWeek': from = day(-dow - 7); to = day(-dow - 1); break
    case 'lastMonth': from = new Date(now.getFullYear(), now.getMonth() - 1, 1); to = new Date(now.getFullYear(), now.getMonth(), 0); break
    default: from = new Date(now.getFullYear(), now.getMonth(), 1); to = day(0)
  }
  const monthEnd = new Date(from.getFullYear(), from.getMonth() + 1, 0)
  const asOf = to > monthEnd ? monthEnd : to
  return { year: from.getFullYear(), month: from.getMonth() + 1, asOf: iso(asOf) }
}
