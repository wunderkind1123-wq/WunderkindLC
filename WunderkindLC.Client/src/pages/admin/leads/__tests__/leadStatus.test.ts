import { describe, expect, it } from 'vitest'
import { leadMatchesStatus } from '../leadLabels'

/**
 * «Buyurtmalar ro'yxati», «Birinchi darsga yozilganlar», o'quvchilar va «Arxiv (yo'qotilganlar)» KESISHMAYDI
 * (mijoz shikoyati, 2026-10-09: "lid Birinchi darsga o'tkazilganda oldingi bosqichdan o'chmayapti — ikkala bo'limda
 * qolib ketyapti"; 2026-11-04: "yo'qotilgan lidlar buyurtma bo'limidan arxivga o'tib ketishi kerak").
 */
describe('leadMatchesStatus', () => {
  const order = { convertedStudentId: null, firstLessonAt: null, lostAt: null }
  const firstLesson = { convertedStudentId: null, firstLessonAt: '2026-10-12T15:00', lostAt: null }
  const student = { convertedStudentId: 's1', firstLessonAt: null, lostAt: null }
  const lost = { convertedStudentId: null, firstLessonAt: null, lostAt: '2026-10-28T13:41:00Z' }

  it('buyurtmalar — faqat sinovga yozilmagan, aylantirilmagan va YO`QOTILMAGAN lid', () => {
    expect(leadMatchesStatus(order, 'lead')).toBe(true)
    expect(leadMatchesStatus(firstLesson, 'lead')).toBe(false)
    expect(leadMatchesStatus(student, 'lead')).toBe(false)
    expect(leadMatchesStatus(lost, 'lead')).toBe(false)
  })

  it('arxiv — faqat yo`qotilganlar', () => {
    expect(leadMatchesStatus(lost, 'lost')).toBe(true)
    for (const l of [order, firstLesson, student]) expect(leadMatchesStatus(l, 'lost')).toBe(false)
  })

  it('har lid AYNAN bitta bo`limda (buyurtma / birinchi dars / o`quvchi / arxiv)', () => {
    for (const l of [order, firstLesson, student, lost]) {
      const hits = ['lead', 'first', 'student', 'lost'].filter((s) => leadMatchesStatus(l, s))
      expect(hits).toHaveLength(1)
    }
  })

  it('«hammasi» va noma`lum qiymat — arxivdan (yo`qotilganlardan) TASHQARI hammasi', () => {
    for (const l of [order, firstLesson, student]) {
      expect(leadMatchesStatus(l, 'all')).toBe(true)
      expect(leadMatchesStatus(l, 'eskirgan')).toBe(true)
    }
    // 2026-11-10 (egasi): yo`qotilgan = arxiv — buyurtmalar ro`yxatida («Hammasi»da ham) turmaydi
    expect(leadMatchesStatus(lost, 'all')).toBe(false)
  })
})
