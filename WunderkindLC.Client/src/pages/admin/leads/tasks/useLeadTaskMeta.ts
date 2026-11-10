import { useEffect, useState } from 'react'
import { getLeadTaskMeta, type LeadTaskMeta } from '@/api/services/leadTasks'

/** Zaxira turlar — server javob bermasa forma baribir ishlasin (yagona manba serverdagi `LeadTasks.Types`). */
export const FALLBACK_TASK_TYPES: LeadTaskMeta['types'] = [
  { key: 'call', label: "Qo'ng'iroq" },
  { key: 'meeting', label: 'Uchrashuv' },
  { key: 'message', label: 'Xabar yozish' },
  { key: 'other', label: 'Boshqa' },
]

/** Topshiriq turlari va mas'ul tanlovi. Ikkilamchi ma'lumot — xato bo'lsa zaxira turlar bilan ishlaydi. */
export function useLeadTaskMeta(): LeadTaskMeta {
  const [meta, setMeta] = useState<LeadTaskMeta>({ types: FALLBACK_TASK_TYPES, people: [], meId: null })
  useEffect(() => {
    let alive = true
    getLeadTaskMeta()
      .then((m) => {
        if (alive) setMeta({ ...m, types: m.types.length ? m.types : FALLBACK_TASK_TYPES })
      })
      // Ikkilamchi o'qish (tanlov ro'yxati) — zaxira bilan davom etadi (`error-visibility.md` §4).
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [])
  return meta
}
