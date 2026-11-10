import { describe, expect, it } from 'vitest'
import { dueIndicator, dueText, quickDueDays } from '../leadTaskDue'
import { toPayload } from '@/pages/admin/leads/tasks/taskFormValues'

// Seshanba, 2026-11-10 12:00 (mahalliy)
const NOW = new Date(2026, 10, 10, 12, 0)

describe('dueIndicator — kanban kartasidagi topshiriq belgisi (server `LeadTasks.Bucket` bilan bir xil)', () => {
  it("topshiriq yo'q — sariq «Topshiriq yo'q»", () => {
    expect(dueIndicator(null, false, NOW)).toEqual({ tone: 'none', text: "Topshiriq yo'q" })
  })
  it('kechikkan — kunlar soni bilan', () => {
    expect(dueIndicator('2026-11-07T10:00:00', false, NOW)).toEqual({ tone: 'overdue', text: '3 kun kechikdi' })
    expect(dueIndicator('2026-11-10T11:00:00', false, NOW)).toEqual({ tone: 'overdue', text: 'Kechikdi' })
  })
  it('bugun / ertaga / keyinroq', () => {
    expect(dueIndicator('2026-11-10T15:30:00', false, NOW)).toEqual({ tone: 'today', text: 'Bugun 15:30' })
    expect(dueIndicator('2026-11-11T09:00:00', false, NOW)).toEqual({ tone: 'tomorrow', text: 'Ertaga 09:00' })
    expect(dueIndicator('2026-11-20T09:00:00', false, NOW).tone).toBe('later')
  })
  it('butun kunlik topshiriq kun oxirigacha kechikmagan, vaqt ko\'rsatilmaydi', () => {
    expect(dueIndicator('2026-11-10T23:59:00', true, NOW)).toEqual({ tone: 'today', text: 'Bugun' })
  })
  it('buzuq sana — yolg\'on qizil emas', () => {
    expect(dueIndicator('buzuq', false, NOW).tone).toBe('later')
  })
})

describe('quickDueDays — tezkor muddat tugmalari', () => {
  it('Bugun · Ertaga · Hafta oxiri (shanba) · 1 hafta', () => {
    expect(quickDueDays(NOW).map((q) => q.day)).toEqual(['2026-11-10', '2026-11-11', '2026-11-14', '2026-11-17'])
  })
  it('shanba kuni «Hafta oxiri» — keyingi shanba', () => {
    expect(quickDueDays(new Date(2026, 10, 14, 9))[2].day).toBe('2026-11-21')
  })
})

describe('toPayload — forma qiymatidan so\'rov', () => {
  it('vaqt bilan va vaqtsiz (butun kun)', () => {
    const base = { type: 'call', text: ' Narx ', day: '2026-11-11', time: '10:00', allDay: false, responsibleUserId: '' }
    expect(toPayload(base)).toEqual({ type: 'call', text: 'Narx', dueAt: '2026-11-11T10:00', allDay: false, responsibleUserId: null })
    expect(toPayload({ ...base, allDay: true }).dueAt).toBe('2026-11-11')
    expect(toPayload({ ...base, time: '' }).allDay).toBe(true)
  })
  it('dueText', () => {
    expect(dueText('2026-11-11T10:05:00', false)).toBe('11.11.2026 10:05')
    expect(dueText('2026-11-11T23:59:00', true)).toBe('11.11.2026')
  })
})
