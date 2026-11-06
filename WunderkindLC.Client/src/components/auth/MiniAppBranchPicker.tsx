import { useEffect, useState } from 'react'
import { getPublicBranchLinks, type BranchLink } from '@/api/services/branchLinks'
import { applyStep, isTelegramMiniApp, readStoredBranch, stepOnOpen, stepOnPick, type BranchStep } from '@/lib/miniAppBranch'
import { cn } from '@/lib/utils'

/**
 * Branch choice on the login page, ONLY inside the bot's Mini App: the bot always opens the first branch, while logins
 * live in each branch's own database. A normal browser already sits on its branch's own address and sees nothing here.
 */
export function MiniAppBranchPicker() {
  const [links, setLinks] = useState<BranchLink[]>([])

  useEffect(() => {
    if (!isTelegramMiniApp()) return
    let alive = true
    getPublicBranchLinks()
      .then((list) => {
        if (!alive) return
        setLinks(list)
        run(stepOnOpen(list, window.location.search, readStoredBranch()))
      })
      // Background read: without the list this is the plain login form (error-visibility.md §4).
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [])

  if (links.length < 2 || !links.some((l) => l.current)) return null

  return (
    <div className="mb-5">
      <p className="mb-1.5 text-sm font-medium text-slate-600">Filial</p>
      <div className="flex flex-wrap gap-1 rounded-xl bg-slate-100 p-1 text-sm font-medium">
        {links.map((l) => (
          <button
            key={l.url}
            type="button"
            aria-pressed={l.current}
            onClick={() => run(stepOnPick(links, l))}
            className={cn(
              'min-w-0 flex-1 rounded-lg px-2 py-1.5 transition-colors',
              l.current ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            {l.name}
          </button>
        ))}
      </div>
    </div>
  )
}

function run(step: BranchStep) {
  const address = applyStep(step)
  if (address) window.location.replace(address)
}
