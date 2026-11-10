import { describe, expect, it } from 'vitest'
import { boardColumnOf, groupBoard } from '../myBoard'

/** «Mening topshiriqlarim» doskasi: Kechikkan · Bugun · Ertaga · Keyinroq; bajarilgani doskada YO'Q. */
describe('myBoard', () => {
  const today = '2026-11-10'
  // 2026-11-10 12:00 Toshkent (UTC+5)
  const now = new Date('2026-11-10T07:00:00Z')
  const t = (dueAt: string | null, status: 'open' | 'returned' | 'done' | 'cancelled' = 'open') => ({ dueAt, status })

  it('ustunlar muddat bo`yicha (Toshkent vaqti)', () => {
    expect(boardColumnOf(t('2026-11-10T10:00:00+05:00'), today, now)).toBe('overdue') // bugun, vaqti o`tgan
    expect(boardColumnOf(t('2026-11-09T18:00:00+05:00'), today, now)).toBe('overdue')
    expect(boardColumnOf(t('2026-11-10T18:00:00+05:00'), today, now)).toBe('today')
    expect(boardColumnOf(t('2026-11-11T00:30:00+05:00'), today, now)).toBe('tomorrow')
    expect(boardColumnOf(t('2026-11-12T09:00:00+05:00'), today, now)).toBe('later')
    expect(boardColumnOf(t(null), today, now)).toBe('later')
    expect(boardColumnOf(t('2026-11-09T18:00:00+05:00', 'returned'), today, now)).toBe('overdue')
  })

  it('bajarilgan va bekor qilingan — doskada YO`Q', () => {
    expect(boardColumnOf(t('2026-11-10T18:00:00+05:00', 'done'), today, now)).toBeNull()
    expect(boardColumnOf(t('2026-11-09T18:00:00+05:00', 'cancelled'), today, now)).toBeNull()
  })

  it('serverning isOverdue belgisi ustun', () => {
    expect(boardColumnOf({ ...t('2026-11-10T18:00:00+05:00'), isOverdue: true }, today, now)).toBe('overdue')
  })

  it('guruhlash: har ustunda eng yaqin muddat tepada', () => {
    const g = groupBoard(
      [t('2026-11-10T18:00:00+05:00'), t('2026-11-10T14:00:00+05:00'), t('2026-11-08T09:00:00+05:00'), t('2026-11-10T09:00:00+05:00', 'done')],
      today,
      now,
    )
    expect(g.today.map((x) => x.dueAt)).toEqual(['2026-11-10T14:00:00+05:00', '2026-11-10T18:00:00+05:00'])
    expect(g.overdue).toHaveLength(1)
    expect(g.tomorrow).toHaveLength(0)
  })
})
