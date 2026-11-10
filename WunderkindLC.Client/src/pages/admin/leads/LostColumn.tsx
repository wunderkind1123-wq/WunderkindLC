import { Link } from 'react-router-dom'
import { useDroppable } from '@dnd-kit/core'
import { IconArchive, IconChartPie } from '@tabler/icons-react'
import { LOST_COLUMN_ID } from '@/api/services/leadLoss'
import { cn } from '@/lib/utils'

interface Props {
  /** Arxivdagi (yo'qotilgan) lidlar soni — havola matni uchun. */
  archivedCount: number
  /** Hisobot havolasi ko'rsatilsinmi (`leads.stats` ruxsati) */
  showReportLink: boolean
}

/**
 * KANBANNING DOIMIY «ARXIVGA» JOYI — har doim ENG OXIRIDA, foydalanuvchi uni tahrirlay/o'chira/ko'chira olmaydi
 * (bazada bunday `LeadStage` YO'Q — `LeadLoss` izohiga qarang).
 *
 * Egasi (2026-11-10): «yo'qotilgan deb belgilash = arxivga tushish, yo'qotilgan lidlar ro'yxatda turmasin». Shuning
 * uchun bu yerda KARTALAR CHIZILMAYDI: karta shu yerga tashlansa sabab oynasi ochiladi (holat jimgina o'zgarmaydi),
 * tasdiqlangach lid kanbandan yo'qoladi va O'quvchilar → «Arxiv» → «Lidlar» tabiga o'tadi (tiklash — o'sha yerdan,
 * lid sahifasidagi «Tiklash»).
 */
export function LostColumn({ archivedCount, showReportLink }: Props) {
  const { setNodeRef, isOver } = useDroppable({ id: LOST_COLUMN_ID })

  return (
    <div className="flex w-[350px] shrink-0 flex-col">
      <div className="relative px-2 pb-1.5 text-center">
        <p className="truncate text-[13px] font-bold uppercase text-[#be123c]">Arxivga</p>
        <Link to="/admin/students/arxiv/lidlar" className="text-[11px] text-[#757575] hover:text-brand-600 hover:underline">
          Arxivda {archivedCount} ta lid ↗
        </Link>
        {showReportLink && (
          <Link
            to="/admin/hisobotlar/yoqotilgan-lidlar"
            title="Yo'qotilgan (arxivlangan) lidlar hisoboti"
            aria-label="Yo'qotilgan (arxivlangan) lidlar hisoboti"
            className="absolute right-0 top-0 rounded-md p-1 text-[#757575] transition-colors hover:bg-[#F0F2F2] hover:text-black"
          >
            <IconChartPie className="h-3.5 w-3.5" />
          </Link>
        )}
      </div>
      <div className="h-0.5 w-full rounded-full bg-[#e11d48]" />

      <div
        ref={setNodeRef}
        className={cn(
          'mt-2 flex min-h-[200px] flex-1 flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-[#f3c4cf] bg-[#F7F0F2] p-3 text-center transition-shadow',
          isOver && 'shadow-[inset_0_0_0_2px_rgba(225,29,72,0.4)]',
        )}
      >
        <IconArchive className="h-6 w-6 text-[#e11d48]/70" />
        <p className="text-[12px] text-[#757575]">
          Kartani shu yerga tashlang — sabab so'raladi va lid <b>arxivga</b> o'tadi (ro'yxatdan yo'qoladi, tarixi saqlanadi).
        </p>
      </div>
    </div>
  )
}
