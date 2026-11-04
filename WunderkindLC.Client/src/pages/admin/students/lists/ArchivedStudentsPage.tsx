import { Navigate } from 'react-router-dom'
import { getArchivedStudents } from '@/api/services/students'
import { CardTabs } from '@/components/ui/CardTabs'
import { usePerm } from '@/lib/permissions'
import { LeadsPage } from '@/pages/admin/leads/LeadsPage'
import {
  StudentListBoard,
  balanceColumn,
  dateColumn,
  groupColumn,
  moderatorColumn,
  nameColumn,
  phoneColumn,
} from './StudentListBoard'

/**
 * O'quvchilar → «Arxiv o'quvchilar» (edutizim `/students/archive`) — IKKI tab:
 * - «O'quvchilar» (`/admin/students/arxiv`, `students.list`) — arxivlangan o'quvchilar;
 * - «Lidlar» (`/admin/students/arxiv/lidlar`, `leads.list`) — ARXIVLANGAN (yo'qotilgan) lidlar (egasi, 2026-11-04:
 *   «lidlar bo'limidagi arxiv kerak emas, o'quvchilar bo'limidagi arxivga qo'shilishi kerak»). Lid CRM'dan o'chmaydi —
 *   sahifasidagi «Tiklash» uni «Buyurtmalar»ga qaytaradi (`crm-leads.md` §5.5).
 * ⚠️ Barcha turdagi arxiv (guruh, o'qituvchi, O'CHIRILGAN lid ...) bizdagi «Arxiv (barcha)» sahifasida
 * qoladi — u "Future" menyusida, chunki edutizimda unday sahifa yo'q.
 */
export function ArchivedStudentsPage({ tab = 'students' }: { tab?: 'students' | 'leads' } = {}) {
  const { can } = usePerm()
  const canStudents = can('students.list', 'view')
  const canLeads = can('leads.list', 'view')
  // Faqat lid operatori menyudan kelsa — o'zi ko'ra oladigan tabga.
  if (tab === 'students' && !canStudents && canLeads) return <Navigate to="/admin/students/arxiv/lidlar" replace />

  const tabs = (
    <CardTabs
      className="mb-4"
      items={[
        { label: "O'quvchilar", to: '/admin/students/arxiv', end: true, hidden: !canStudents },
        { label: 'Lidlar', to: '/admin/students/arxiv/lidlar', hidden: !canLeads },
      ]}
    />
  )

  if (tab === 'leads') {
    return (
      <>
        {tabs}
        <LeadsPage key="archive" archive />
      </>
    )
  }

  return (
    <>
      {tabs}
      <StudentListBoard
        load={getArchivedStudents}
        columns={[
          nameColumn,
          phoneColumn,
          balanceColumn,
          { ...groupColumn, header: 'Arxivlangan guruh' },
          dateColumn('archived', 'Arxivlangan sana', (s) => s.archivedAt ?? ''),
          {
            key: 'reason',
            header: 'Sabab',
            render: (s) => s.archiveReason || <span className="text-[#9ca3af]">—</span>,
          },
          moderatorColumn,
        ]}
      />
    </>
  )
}
