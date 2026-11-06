import type { BranchLink } from '@/api/services/branchLinks'

// One Telegram bot for every branch (owner, 2026-11-06). Its «📱 Panel» always opens the FIRST branch in BRANCH_LINKS
// (the "home"); each branch has its own database, so a teacher must reach their own branch's login. The choice is
// remembered on the home origin only. Rules: .claude/rules/branches.md.

const STORAGE_KEY = 'wlc:miniAppBranch'
export const BRANCH_PARAM = 'filial'

export function isTelegramMiniApp(webApp = window.Telegram?.WebApp): boolean {
  return !!webApp?.platform && webApp.platform !== 'unknown'
}

/**
 * The Telegram SDK reads its launch params from the URL hash of the page it loads on. Another origin has its own
 * sessionStorage, so without this hash the branch would not know it runs inside Telegram.
 */
export function launchHash(webApp = window.Telegram?.WebApp): string {
  if (!webApp) return ''
  const parts: string[] = []
  if (webApp.initData) parts.push(`tgWebAppData=${encodeURIComponent(webApp.initData)}`)
  if (webApp.version) parts.push(`tgWebAppVersion=${encodeURIComponent(webApp.version)}`)
  if (webApp.platform) parts.push(`tgWebAppPlatform=${encodeURIComponent(webApp.platform)}`)
  if (webApp.themeParams) parts.push(`tgWebAppThemeParams=${encodeURIComponent(JSON.stringify(webApp.themeParams))}`)
  return parts.length ? `#${parts.join('&')}` : ''
}

export type BranchStep =
  | { kind: 'stay' }
  /** Home origin: remember (or, for home itself, forget) the branch; `go` = open that branch's login. */
  | { kind: 'remember'; url: string | null; go: string | null }
  /** Other branch: only home holds the memory, so the choice travels through it. */
  | { kind: 'viaHome'; go: string }

const find = (links: BranchLink[], url: string | null | undefined) => links.find((l) => l.url === url)

/** What to do when the login page opens. Addresses come ONLY from the server list — no open redirect through the URL. */
export function stepOnOpen(links: BranchLink[], search: string, stored: string | null): BranchStep {
  if (links.length < 2 || !links[0].current) return { kind: 'stay' }
  const home = links[0]
  const asked = find(links, new URLSearchParams(search).get(BRANCH_PARAM))
  if (asked) return asked.url === home.url ? { kind: 'remember', url: null, go: null } : { kind: 'remember', url: asked.url, go: asked.url }
  const saved = find(links, stored)
  return saved && saved.url !== home.url ? { kind: 'remember', url: saved.url, go: saved.url } : { kind: 'stay' }
}

/** What to do when a branch is tapped in the picker. */
export function stepOnPick(links: BranchLink[], target: BranchLink): BranchStep {
  const home = links[0]
  if (!home || target.current) return { kind: 'stay' }
  if (home.current) return { kind: 'remember', url: target.url, go: target.url }
  return { kind: 'viaHome', go: `${home.url}/login?${BRANCH_PARAM}=${encodeURIComponent(target.url)}` }
}

export function readStoredBranch(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY)
  } catch {
    return null // storage blocked — the picker simply asks again
  }
}

export function writeStoredBranch(url: string | null): void {
  try {
    if (url) localStorage.setItem(STORAGE_KEY, url)
    else localStorage.removeItem(STORAGE_KEY)
  } catch {
    // storage blocked — the picker simply asks again
  }
}

/** Runs a step; returns the address to open, or null to stay on this page. */
export function applyStep(step: BranchStep): string | null {
  if (step.kind === 'stay') return null
  if (step.kind === 'viaHome') return step.go + launchHash()
  writeStoredBranch(step.url)
  if (!step.go) return null
  return `${step.go}/login${launchHash()}`
}
