import { useState } from 'react'
import { BellRing, CheckCircle2, CornerUpLeft, FileText, Mic, Plus, RotateCcw, Search, TrendingUp, UserX } from 'lucide-react'
import {
  acceptMyTask,
  getMe,
  getMySummary,
  getMyDayTasks,
  getMyTasks,
  hrErrorMessage,
  hrIsNotEmployee,
  reopenMyTask,
  type HrMyTask,
} from '@/api/services/hrTasks'
import type { HrTaskStatus, Page } from '@/types/hrTasks'
import { HR_DEFAULT_PAGE_SIZE, HR_MAX_PAGE_SIZE } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Badge, CursorPager, EmptyState, PageHeader, ProgressBar, Skeleton, inputCls } from '@/components/hr-tasks/ui'
import { useCursorStack, useDebounced, useToast } from '@/components/hr-tasks/hooks'
import { useLoad } from '@/components/hr-tasks/useLoad'
import {
  hrFormatDateTime,
  hrFormatPercent,
  hrIsOverdue,
  hrMediaSrc,
  hrOverdueLabel,
  hrProgressPercent,
  hrProgressText,
  hrTashkentParts,
  hrTaskSourceLabel,
  hrTaskStatusLabel,
  hrTaskStatusTone,
  hrToday,
  hrVoiceDuration,
} from '@/components/hr-tasks/lib'
import { cn } from '@/lib/utils'
import { CompleteTaskModal, ProgressModal, SelfTaskModal } from './MyTaskModals'
import { DateStrip } from './DateStrip'
import { dayEmptyText, dayHeadline, daySummary, sortDayTasks } from './myDay'
import { SALES_ROW_ACCENT, SalesBadge } from './SalesBadge'
import { hrtOpenWorkLink } from './paths'
import { BOARD_COLUMNS, groupBoard, type BoardColumnKey } from './myBoard'
import { Navigate } from 'react-router-dom'
import { useAuth } from '@/context/auth-context'

export type MyTab = 'board' | 'open' | 'done' | 'all' | 'day'

const TABS: { key: MyTab; label: string }[] = [
  // LC (egasi, 2026-11-10): kanban — Kechikkan · Bugun · Ertaga · Keyinroq (`myBoard.ts`); bajarilgani doskadan yo'qoladi.
  { key: 'board', label: 'Doska' },
  { key: 'open', label: 'Bajarish kerak' },
  { key: 'done', label: 'Bajarilgan' },
  { key: 'all', label: 'Hammasi' },
  // The LMS Mini App's main screen (one day + week strip) — LC's Mini App is this page.
  { key: 'day', label: 'Kun bo‘yicha' },
]

/** `open` — includes `returned` (open again for the employee). */
function tabStatuses(tab: MyTab): HrTaskStatus[] {
  if (tab === 'open') return ['open', 'returned']
  if (tab === 'done') return ['done']
  return []
}

type Action = { kind: 'complete' | 'progress'; task: HrMyTask } | { kind: 'self' } | null

/**
 * "Mening topshiriqlarim" — the user's OWN HR tasks (`/api/hr/me/*`, the old
 * `HrMyTasksPage`). Managers are employees too and complete tasks from above here.
 * A user without an HR row (403 `hr_not_employee`) gets a calm explanation.
 *
 * `defaultTab` — the teacher portal (opened inside Telegram) starts on the day view, as the
 * LMS Mini App did; LC: the admin panel starts on the BOARD («Doska», egasi 2026-11-10) — LMS started on "Bajarish kerak".
 */
export function HrMyTasksPage({ defaultTab = 'board' }: { defaultTab?: MyTab } = {}) {
  const toast = useToast()
  const [today] = useState(() => hrToday())
  const me = useLoad('hr-me', getMe)
  const notEmployee = me.rawError !== null && hrIsNotEmployee(me.rawError)
  // LC (egasi, 2026-11-10): superadmin HR'da xodim EMAS (§4) — «Mening topshiriqlarim» unga «ro'yxatda yo'qsiz»
  // deyardi. Rahbar (superadmin / direktor = admin roli) uchun bu menyu BUTUN tashkilot topshiriqlariga olib boradi:
  // «kim nima ish qilayotgani» panel ro'yxatida (xodim ustuni, sotuv topshiriqlari — `SalesBadge`).
  const role = useAuth().user?.role
  const isLeader = role === 'superadmin' || role === 'admin'

  const [tab, setTab] = useState<MyTab>(defaultTab)
  const [pickedDay, setPickedDay] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const q = useDebounced(search.trim())
  const [version, setVersion] = useState(0)
  const filterKey = `${tab}|${q}`
  const pager = useCursorStack(filterKey)

  const ready = Boolean(me.data)
  // "Today" by the SERVER (a phone clock may be wrong), else Tashkent by this browser.
  const serverToday = me.data?.today?.slice(0, 10) || today
  const day = pickedDay ?? serverToday
  const dayList = useLoad(ready && tab === 'day' ? `my-day|${day}|${version}` : null, () => getMyDayTasks(day))
  // Doska — barcha OCHIQ topshiriqlar bitta so'rovda (sahifalashsiz; ko'pi bilan HR_MAX_PAGE_SIZE).
  const board = useLoad(ready && tab === 'board' ? `my-board|${q}|${version}` : null, () =>
    getMyTasks({ status: ['open', 'returned'], q, sort: 'due_at', limit: HR_MAX_PAGE_SIZE, withTotal: true }),
  )
  const list = useLoad(ready && tab !== 'day' && tab !== 'board' ? `my|${filterKey}|${pager.cursor ?? ''}|${version}` : null, () =>
    getMyTasks({
      status: tabStatuses(tab),
      q,
      // Open: nearest due date first; history: latest first.
      sort: tab === 'open' ? 'due_at' : '-due_at',
      limit: HR_DEFAULT_PAGE_SIZE,
      cursor: pager.cursor,
      withTotal: true,
    }),
  )
  const summary = useLoad(ready ? `my-summary|${version}` : null, getMySummary)

  const [action, setAction] = useState<Action>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  function done(message: string): void {
    toast.success(message)
    setAction(null)
    setVersion((v) => v + 1)
  }

  /** A self task due on another day — open that day, otherwise "I added it, it is not there". */
  function selfCreated(message: string, dueAt: string | null): void {
    const dueDay = hrTashkentParts(dueAt)?.date
    if (tab === 'day' && dueDay && dueDay !== day) setPickedDay(dueDay)
    done(message)
  }

  async function run(task: HrMyTask, fn: (id: string) => Promise<unknown>, message: string): Promise<void> {
    setBusyId(task.id)
    try {
      await fn(task.id)
      done(message)
    } catch (err) {
      toast.error(hrErrorMessage(err))
    } finally {
      setBusyId(null)
    }
  }

  if (me.loading) {
    return (
      <div className="space-y-4">
        <PageHeader title="Mening topshiriqlarim" description="Sizga berilgan topshiriqlar: tasdiqlash va bajarish" />
        <Skeleton className="h-40 w-full rounded-2xl" />
      </div>
    )
  }

  if (notEmployee && isLeader) return <Navigate to={hrtOpenWorkLink(today)} replace />

  if (notEmployee || !me.data) {
    return (
      <div className="space-y-4">
        <PageHeader title="Mening topshiriqlarim" description="Sizga berilgan topshiriqlar: tasdiqlash va bajarish" />
        <Card>
          {notEmployee ? (
            <EmptyState
              icon={<UserX className="h-5 w-5" />}
              title="Siz HR xodimlari ro‘yxatida yo‘qsiz"
              hint="Topshiriqlar HR bo‘limida xodim sifatida qo‘shilgan foydalanuvchilarga beriladi. Kerak bo‘lsa, rahbaringizdan sizni «Xodimlar» ro‘yxatiga qo‘shishini so‘rang."
            />
          ) : (
            <EmptyState
              title="Ma’lumotni yuklab bo‘lmadi"
              hint={me.error ?? undefined}
              action={
                <Button variant="secondary" onClick={me.reload}>
                  Qayta urinish
                </Button>
              }
            />
          )}
        </Card>
      </div>
    )
  }

  const page = list.data ?? list.previous
  const items = page?.items ?? []
  const total = page?.total
  const pagerSummary = total !== null && total !== undefined ? `Jami ${total} ta topshiriq` : `${items.length} ta ko‘rsatilmoqda`
  // The day list keeps the previous day's rows (dimmed) only while the SAME view reloads.
  const dayPage = dayList.data ?? (dayList.previous?.date === day ? dayList.previous : null)
  const dayItems = sortDayTasks(dayPage?.items ?? [])
  const s = summary.data
  const percent = s ? (s.percent ?? (s.total > 0 ? (s.done / s.total) * 100 : 0)) : 0

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Mening topshiriqlarim"
        description={[me.data.fullName, me.data.positionName].filter(Boolean).join(' · ') || 'Sizga berilgan topshiriqlar: tasdiqlash va bajarish'}
        actions={
          <Button onClick={() => setAction({ kind: 'self' })}>
            <Plus className="h-4 w-4" />
            O‘zimga topshiriq
          </Button>
        }
      />

      {s && (
        <Card className="p-4">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <p className="text-sm font-medium text-slate-700">Bu oy natijam</p>
            <p className="text-xs text-slate-400">
              {s.done}/{s.total} bajarildi · {s.missed} bajarilmadi · {s.late} kechikkan · {s.open} ochiq
            </p>
          </div>
          <ProgressBar className="mt-2" percent={percent} caption={hrFormatPercent(percent)} />
          {s.kpi.length > 0 && (
            <ul className="mt-3 grid gap-2 sm:grid-cols-2">
              {s.kpi.map((kpi, index) => (
                <li key={kpi.taskId ?? index} className="min-w-0 rounded-xl border border-slate-200 px-3 py-2">
                  <p className="truncate text-sm text-slate-700">{kpi.title}</p>
                  <ProgressBar
                    className="mt-1"
                    percent={hrProgressPercent({ kind: 'numeric', targetAmount: kpi.targetAmount, currentAmount: kpi.currentAmount })}
                    caption={hrProgressText({ kind: 'numeric', targetAmount: kpi.targetAmount, currentAmount: kpi.currentAmount, unit: kpi.unit }) ?? undefined}
                  />
                </li>
              ))}
            </ul>
          )}
        </Card>
      )}

      <div className="flex gap-1 overflow-x-auto border-b border-slate-200">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key)}
            className={cn(
              '-mb-px shrink-0 border-b-2 px-3 pb-2.5 pt-1 text-sm font-medium transition-colors',
              tab === t.key ? 'border-brand-600 text-brand-700' : 'border-transparent text-slate-500 hover:text-slate-800',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'board' ? (
        <MyBoard
          load={board}
          today={serverToday}
          search={search}
          onSearch={setSearch}
          busyId={busyId}
          onAccept={(task) => void run(task, acceptMyTask, 'Tasdiqlandi — rahbar ko‘rganingizni biladi')}
          onComplete={(task) => setAction({ kind: 'complete', task })}
          onProgress={(task) => setAction({ kind: 'progress', task })}
        />
      ) : tab === 'day' ? (
        <>
          <Card className="space-y-2 p-3 sm:p-4">
            <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
              <p className="text-base font-semibold text-slate-800">{dayHeadline(day, serverToday)}</p>
              {dayItems.length > 0 && (
                <p className="text-xs text-slate-400">{daySummary(dayItems, day, serverToday)}</p>
              )}
            </div>
            <DateStrip selected={day} today={serverToday} onSelect={setPickedDay} />
            {day !== serverToday && (
              <Button variant="secondary" className="w-full" onClick={() => setPickedDay(null)}>
                Bugunga qaytish
              </Button>
            )}
          </Card>
          {dayList.loading && dayItems.length === 0 ? (
            <div className="space-y-3">
              {Array.from({ length: 3 }, (_, i) => (
                <Skeleton key={i} className="h-36 w-full rounded-2xl" />
              ))}
            </div>
          ) : dayItems.length === 0 ? (
            <Card>
              {dayList.error ? (
                <EmptyState
                  title="Topshiriqlarni yuklab bo‘lmadi"
                  hint={dayList.error}
                  action={
                    <Button variant="secondary" onClick={dayList.reload}>
                      Qayta urinish
                    </Button>
                  }
                />
              ) : (
                <EmptyState title={dayEmptyText(day, serverToday)} />
              )}
            </Card>
          ) : (
            <ul className={cn('space-y-3 transition-opacity', dayList.loading && 'opacity-60')}>
              {dayItems.map((task) => (
                <MyTaskCard
                  key={task.id}
                  task={task}
                  today={serverToday}
                  busy={busyId === task.id}
                  onAccept={() => void run(task, acceptMyTask, 'Tasdiqlandi — rahbar ko‘rganingizni biladi')}
                  onComplete={() => setAction({ kind: 'complete', task })}
                  onProgress={() => setAction({ kind: 'progress', task })}
                  onReopen={() => void run(task, reopenMyTask, 'Topshiriq qayta ochildi')}
                />
              ))}
            </ul>
          )}
        </>
      ) : (
        <>
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input className={cn(inputCls, 'pl-9')} placeholder="Topshiriq sarlavhasi" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>

        {list.loading && items.length === 0 ? (
          <div className="space-y-3">
            {Array.from({ length: 3 }, (_, i) => (
              <Skeleton key={i} className="h-36 w-full rounded-2xl" />
            ))}
          </div>
        ) : items.length === 0 ? (
          <Card>
            {list.error ? (
              <EmptyState
                title="Topshiriqlarni yuklab bo‘lmadi"
                hint={list.error}
                action={
                  <Button variant="secondary" onClick={list.reload}>
                    Qayta urinish
                  </Button>
                }
              />
            ) : (
              <EmptyState
                title="Sizga biriktirilgan topshiriq yo‘q"
                hint={
                  search.trim()
                    ? 'Qidiruvni o‘zgartirib ko‘ring.'
                    : tab === 'open'
                      ? 'Bajarilishi kerak bo‘lgan topshiriq yo‘q. Yangi topshiriq kelsa Telegram’ga xabar keladi.'
                      : 'Bu ro‘yxatda hozircha hech narsa yo‘q.'
                }
              />
            )}
          </Card>
        ) : (
          <ul className={cn('space-y-3 transition-opacity', list.loading && 'opacity-60')}>
            {items.map((task) => (
              <MyTaskCard
                key={task.id}
                task={task}
                today={serverToday}
                busy={busyId === task.id}
                onAccept={() => void run(task, acceptMyTask, 'Tasdiqlandi — rahbar ko‘rganingizni biladi')}
                onComplete={() => setAction({ kind: 'complete', task })}
                onProgress={() => setAction({ kind: 'progress', task })}
                onReopen={() => void run(task, reopenMyTask, 'Topshiriq qayta ochildi')}
              />
            ))}
          </ul>
        )}

        <CursorPager
          summary={pagerSummary}
          loading={list.loading}
          hasPrev={pager.hasPrev}
          hasMore={Boolean(page?.hasMore && page.nextCursor)}
          onPrev={pager.prev}
          onNext={() => pager.next(page?.nextCursor ?? null)}
        />
        </>
      )}

      <p className="text-xs text-slate-400">
        Bu sahifa Telegram botidagi «📱 Panel»da ham ochiladi — u yerdan ham bajarish mumkin.
      </p>

      {action?.kind === 'complete' && <CompleteTaskModal task={action.task} onClose={() => setAction(null)} onDone={done} />}
      {action?.kind === 'progress' && <ProgressModal task={action.task} onClose={() => setAction(null)} onDone={done} />}
      {action?.kind === 'self' && <SelfTaskModal today={serverToday} onClose={() => setAction(null)} onDone={selfCreated} />}
      {toast.node}
    </div>
  )
}

const BOARD_TONE: Record<BoardColumnKey, { title: string; bar: string; bg: string }> = {
  overdue: { title: 'text-red-700', bar: 'bg-red-500', bg: 'bg-red-50/60' },
  today: { title: 'text-emerald-700', bar: 'bg-emerald-500', bg: 'bg-emerald-50/50' },
  tomorrow: { title: 'text-sky-700', bar: 'bg-sky-500', bg: 'bg-sky-50/50' },
  later: { title: 'text-slate-600', bar: 'bg-slate-400', bg: 'bg-slate-50' },
}

/**
 * LC: «Doska» — BELGILANGAN 4 ustun (`myBoard.ts`). Faqat bajarilmaganlar: «Bajardim» bosilgach topshiriq doskadan
 * yo'qoladi (ro'yxat qayta yuklanadi), «Bajarilgan» tabida tarix bo'lib qoladi. Telefonda ustunlar yonma-yon suriladi.
 */
function MyBoard({
  load,
  today,
  search,
  onSearch,
  busyId,
  onAccept,
  onComplete,
  onProgress,
}: {
  load: ReturnType<typeof useLoad<Page<HrMyTask>>>
  today: string
  search: string
  onSearch: (v: string) => void
  busyId: string | null
  onAccept: (task: HrMyTask) => void
  onComplete: (task: HrMyTask) => void
  onProgress: (task: HrMyTask) => void
}) {
  const items = (load.data ?? load.previous)?.items ?? []
  const columns = groupBoard<HrMyTask>(items, today)

  return (
    <>
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <input className={cn(inputCls, 'pl-9')} placeholder="Topshiriq sarlavhasi" value={search} onChange={(e) => onSearch(e.target.value)} />
      </div>

      {load.error && items.length === 0 ? (
        <Card>
          <EmptyState
            title="Topshiriqlarni yuklab bo‘lmadi"
            hint={load.error}
            action={
              <Button variant="secondary" onClick={load.reload}>
                Qayta urinish
              </Button>
            }
          />
        </Card>
      ) : load.loading && items.length === 0 ? (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          {BOARD_COLUMNS.map((c) => (
            <Skeleton key={c.key} className="h-48 w-full rounded-2xl" />
          ))}
        </div>
      ) : (
        <div
          className={cn(
            '-mx-4 flex snap-x gap-3 overflow-x-auto px-4 pb-2 transition-opacity md:mx-0 md:grid md:grid-cols-2 md:overflow-visible md:px-0 xl:grid-cols-4',
            load.loading && 'opacity-60',
          )}
        >
          {BOARD_COLUMNS.map((col) => {
            const tone = BOARD_TONE[col.key]
            const tasks = columns[col.key]
            return (
              <section key={col.key} className={cn('flex w-[85vw] max-w-[360px] shrink-0 snap-start flex-col rounded-2xl p-2 md:w-auto md:max-w-none', tone.bg)}>
                <div className="flex items-center justify-between px-1.5 pb-1.5">
                  <h3 className={cn('text-sm font-semibold', tone.title)}>{col.label}</h3>
                  <span className="rounded-full bg-white px-2 py-0.5 text-xs font-medium text-slate-600 ring-1 ring-slate-200">{tasks.length}</span>
                </div>
                <div className={cn('mb-2 h-0.5 rounded-full', tone.bar)} />
                {tasks.length === 0 ? (
                  <p className="px-2 py-6 text-center text-xs text-slate-400">{col.empty}</p>
                ) : (
                  <ul className="space-y-2">
                    {tasks.map((task) => (
                      <MyTaskCard
                        key={task.id}
                        task={task}
                        today={today}
                        busy={busyId === task.id}
                        onAccept={() => onAccept(task)}
                        onComplete={() => onComplete(task)}
                        onProgress={() => onProgress(task)}
                        onReopen={() => undefined}
                      />
                    ))}
                  </ul>
                )}
              </section>
            )
          })}
        </div>
      )}
      {load.data?.hasMore && (
        <p className="text-xs text-amber-700">
          Doskada birinchi {HR_MAX_PAGE_SIZE} ta ko‘rsatildi{load.data.total ? ` (jami ${load.data.total} ta)` : ''} — qolganini «Bajarish kerak» tabida qidiring.
        </p>
      )}
    </>
  )
}

function MyTaskCard({
  task,
  today,
  busy,
  onAccept,
  onComplete,
  onProgress,
  onReopen,
}: {
  task: HrMyTask
  today: string
  busy: boolean
  onAccept: () => void
  onComplete: () => void
  onProgress: () => void
  onReopen: () => void
}) {
  const overdue = task.isOverdue ?? hrIsOverdue(task)
  const progress = hrProgressText(task)
  const voice = task.attachments.find((a) => a.kind === 'voice')
  const voiceUrl = hrMediaSrc(voice?.url ?? task.voiceUrl)
  const files = task.attachments.filter((a) => a.kind !== 'voice')
  const open = task.status === 'open' || task.status === 'returned'
  // Reopen — only tasks completed today (the backend rejects the rest anyway).
  const canReopen = task.status === 'done' && hrTashkentParts(task.completedAt)?.date === today

  return (
    <li
      className={cn(
        'rounded-2xl border border-slate-200/80 bg-white p-3 shadow-sm sm:p-4',
        // LC: sotuv (lid / sinov darsi) topshirig'i — boshqa rangda (egasi, 2026-11-10).
        task.salesKind && cn('bg-orange-50/40', SALES_ROW_ACCENT),
      )}
    >
      {task.salesKind && (
        <div className="mb-1">
          <SalesBadge kind={task.salesKind} leadId={task.leadId} leadName={task.leadName} />
        </div>
      )}
      <p className="break-words font-medium text-slate-800">{task.title}</p>
      {task.description && <p className="mt-1 whitespace-pre-line break-words text-sm text-slate-500">{task.description}</p>}

      <div className="mt-2 flex flex-wrap items-center gap-1.5">
        <Badge tone={hrTaskStatusTone(task.status)}>{hrTaskStatusLabel(task.status)}</Badge>
        {overdue && open && <Badge tone="danger">{hrOverdueLabel(task.dueAt)}</Badge>}
        {task.needsAcceptance && (
          <Badge tone="warning" dot>
            Tasdiq kutilmoqda
          </Badge>
        )}
        {task.isLate && <Badge tone="warning">Kechikib bajarilgan</Badge>}
        <Badge>{hrTaskSourceLabel(task.source)}</Badge>
        {progress && <Badge tone="info">{progress}</Badge>}
      </div>

      <dl className="mt-2 grid gap-1 text-sm text-slate-400 sm:grid-cols-2">
        <div className="flex gap-2">
          <dt className="shrink-0">Muddat:</dt>
          <dd className="tabular-nums text-slate-700">{hrFormatDateTime(task.dueAt)}</dd>
        </div>
        {task.completedAt && (
          <div className="flex gap-2">
            <dt className="shrink-0">Bajarildi:</dt>
            <dd className="tabular-nums text-slate-700">{hrFormatDateTime(task.completedAt)}</dd>
          </div>
        )}
      </dl>

      {task.kind === 'numeric' && <ProgressBar className="mt-2 max-w-md" percent={hrProgressPercent(task)} showValue caption={progress ?? undefined} />}

      {task.status === 'returned' && task.directorComment && (
        <p className="mt-2 flex gap-2 rounded-xl border border-amber-200 bg-amber-50 p-2.5 text-sm text-amber-900">
          <CornerUpLeft className="mt-0.5 h-4 w-4 shrink-0" />
          <span className="min-w-0 break-words">{task.directorComment}</span>
        </p>
      )}

      {task.employeeComment && <p className="mt-2 break-words text-sm text-slate-500">Izohingiz: {task.employeeComment}</p>}

      {voiceUrl && (
        <div className="mt-2 space-y-1">
          <p className="flex items-center gap-1.5 text-xs text-slate-400">
            <Mic className="h-3.5 w-3.5" />
            Rahbarning ovozli izohi
            {hrVoiceDuration(voice?.durationSeconds) && <span className="tabular-nums">· {hrVoiceDuration(voice?.durationSeconds)}</span>}
          </p>
          <audio className="h-10 w-full max-w-md" controls preload="metadata" src={voiceUrl} aria-label="Rahbarning ovozli izohi" />
        </div>
      )}

      {files.length > 0 && (
        <ul className="mt-2 space-y-1">
          {files.map((file) => (
            <li key={file.id}>
              <a
                href={hrMediaSrc(file.url) ?? undefined}
                target="_blank"
                rel="noreferrer"
                className="inline-flex max-w-full items-center gap-1.5 text-sm text-brand-600 hover:underline"
              >
                <FileText className="h-3.5 w-3.5 shrink-0" />
                <span className="truncate">Biriktirilgan fayl</span>
              </a>
            </li>
          ))}
        </ul>
      )}

      {task.proofs.length > 0 && <p className="mt-2 text-xs text-slate-400">{task.proofs.length} ta isbot biriktirilgan</p>}

      {(open || canReopen) && (
        <div className="mt-3 flex flex-col gap-2 sm:flex-row sm:flex-wrap">
          {open && task.needsAcceptance && (
            <Button variant="secondary" disabled={busy} onClick={onAccept}>
              <BellRing className="h-4 w-4" />
              Tasdiqlash
            </Button>
          )}
          {open && task.kind === 'numeric' && (
            <Button variant="secondary" disabled={busy} onClick={onProgress}>
              <TrendingUp className="h-4 w-4" />
              Natija qo‘shish
            </Button>
          )}
          {open && (
            <Button disabled={busy} onClick={onComplete}>
              <CheckCircle2 className="h-4 w-4" />
              Bajardim
            </Button>
          )}
          {canReopen && (
            <Button variant="ghost" disabled={busy} onClick={onReopen}>
              <RotateCcw className="h-4 w-4" />
              Qayta ochish
            </Button>
          )}
        </div>
      )}

      {task.acceptedAt && open && <p className="mt-2 text-xs text-slate-400">Tasdiqlangan: {hrFormatDateTime(task.acceptedAt)}</p>}
    </li>
  )
}
