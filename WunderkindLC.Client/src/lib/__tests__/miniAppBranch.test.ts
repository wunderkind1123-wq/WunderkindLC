// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { applyStep, isTelegramMiniApp, launchHash, readStoredBranch, stepOnOpen, stepOnPick, writeStoredBranch } from '../miniAppBranch'

const HOME = 'https://lc.example.uz'
const YAK = 'https://yakkatut.example.uz'
const FUR = 'https://furqat.example.uz'
const list = (current: string) =>
  [HOME, YAK, FUR].map((url) => ({ name: url, url, current: url === current }))

const webApp = (w: unknown) => w as NonNullable<Window['Telegram']>['WebApp']

afterEach(() => {
  localStorage.clear()
  ;(window as unknown as { Telegram?: unknown }).Telegram = undefined
})

describe('isTelegramMiniApp', () => {
  it('a normal browser (SDK loaded, platform unknown) is not a Mini App', () => {
    expect(isTelegramMiniApp(undefined)).toBe(false)
    expect(isTelegramMiniApp(webApp({ platform: 'unknown' }))).toBe(false)
    expect(isTelegramMiniApp(webApp({ platform: 'ios' }))).toBe(true)
  })
})

describe('launchHash — the branch origin must still know it runs inside Telegram', () => {
  it('forwards platform and version; keyboard launch has no initData', () => {
    const hash = launchHash(webApp({ initData: '', platform: 'android', version: '8.0' }))
    expect(hash).toBe('#tgWebAppVersion=8.0&tgWebAppPlatform=android')
  })

  it('encodes signed init data', () => {
    expect(launchHash(webApp({ initData: 'a=1&b=2', platform: 'ios' }))).toBe('#tgWebAppData=a%3D1%26b%3D2&tgWebAppPlatform=ios')
  })
})

describe('stepOnOpen', () => {
  it('stays when nothing is remembered', () => {
    expect(stepOnOpen(list(HOME), '', null)).toEqual({ kind: 'stay' })
  })

  it('home sends a remembered teacher straight to their branch', () => {
    expect(stepOnOpen(list(HOME), '', YAK)).toEqual({ kind: 'remember', url: YAK, go: YAK })
  })

  it('other branches never redirect by themselves (no loop)', () => {
    expect(stepOnOpen(list(YAK), '', FUR)).toEqual({ kind: 'stay' })
    expect(stepOnOpen(list(YAK), `?filial=${encodeURIComponent(FUR)}`, null)).toEqual({ kind: 'stay' })
  })

  it('choosing home clears the memory and stays', () => {
    expect(stepOnOpen(list(HOME), `?filial=${encodeURIComponent(HOME)}`, YAK)).toEqual({ kind: 'remember', url: null, go: null })
  })

  it('address outside the server list is ignored (no open redirect)', () => {
    const evil = 'https://evil.example'
    expect(stepOnOpen(list(HOME), `?filial=${encodeURIComponent(evil)}`, null)).toEqual({ kind: 'stay' })
    expect(stepOnOpen(list(HOME), '', evil)).toEqual({ kind: 'stay' })
  })

  it('a single branch has nothing to choose', () => {
    expect(stepOnOpen([{ name: 'A', url: HOME, current: true }], '', YAK)).toEqual({ kind: 'stay' })
  })
})

describe('stepOnPick', () => {
  it('tapping the open branch does nothing', () => {
    const links = list(HOME)
    expect(stepOnPick(links, links[0])).toEqual({ kind: 'stay' })
  })

  it('from home: remember and go', () => {
    const links = list(HOME)
    expect(stepOnPick(links, links[1])).toEqual({ kind: 'remember', url: YAK, go: YAK })
  })

  it('from another branch: the choice travels through home, which holds the memory', () => {
    const links = list(YAK)
    expect(stepOnPick(links, links[0])).toEqual({ kind: 'viaHome', go: `${HOME}/login?filial=${encodeURIComponent(HOME)}` })
    expect(stepOnPick(links, links[2])).toEqual({ kind: 'viaHome', go: `${HOME}/login?filial=${encodeURIComponent(FUR)}` })
  })
})

describe('applyStep', () => {
  it('stores the branch and opens its login with the launch params', () => {
    ;(window as unknown as { Telegram?: unknown }).Telegram = { WebApp: { platform: 'ios' } }
    expect(applyStep({ kind: 'remember', url: YAK, go: YAK })).toBe(`${YAK}/login#tgWebAppPlatform=ios`)
    expect(readStoredBranch()).toBe(YAK)
  })

  it('choosing home forgets and stays on the page', () => {
    writeStoredBranch(YAK)
    expect(applyStep({ kind: 'remember', url: null, go: null })).toBeNull()
    expect(readStoredBranch()).toBeNull()
  })

  it('stay opens nothing', () => {
    expect(applyStep({ kind: 'stay' })).toBeNull()
  })
})
