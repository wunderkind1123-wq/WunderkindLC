import { useEffect, useRef, useState } from 'react'
import { CheckCircle2, KeyRound, Sparkles, XCircle } from 'lucide-react'
import { getGeminiSettings, setGeminiKey, type GeminiConfig } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, formatDateTime } from '@/lib/utils'

/**
 * AI Tahlil (Google Gemini) sozlamasi.
 * API kaliti (2026-11-13 dan) shu yerdan kiritiladi — FAQAT superadmin; serverda bazaga SHIFRLANGAN holda yoziladi
 * (`StoredSecrets`), qiymat qayta ko'rsatilmaydi. Serverdagi `.env` da `GEMINI_API_KEY` bo'lsa — u ustun.
 * Model env'dan (GEMINI_MODEL, default gemini-3.1-flash-lite).
 */
export function GeminiSettings() {
  const [cfg, setCfg] = useState<GeminiConfig | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [value, setValue] = useState('')
  const [error, setError] = useState('')
  const [saved, setSaved] = useState('')
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false)

  useEffect(() => {
    getGeminiSettings()
      .then(setCfg)
      .catch((e) => setLoadError(apiErrorMessage(e, "Sozlamani yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  const save = async (v: string) => {
    if (inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setError('')
    setSaved('')
    try {
      setCfg(await setGeminiKey(v))
      setValue('')
      setSaved(v ? 'Kalit saqlandi — AI darhol ishlaydi.' : "Kalit o'chirildi.")
    } catch (e) {
      setError(apiErrorMessage(e, "Kalitni saqlab bo'lmadi"))
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  const configured = !!cfg?.configured
  const source = cfg?.source ?? ''
  const editable = !!cfg?.canEdit && !!cfg?.vaultConfigured && source !== 'env'

  return (
    <Card
      title={
        <span className="flex flex-wrap items-center gap-2">
          <Sparkles className="h-4 w-4 text-brand-600" /> AI Tahlil (Gemini)
          {configured ? (
            <Badge tone="green">
              <CheckCircle2 className="h-3.5 w-3.5" /> Sozlangan
            </Badge>
          ) : (
            <Badge tone="default">
              <XCircle className="h-3.5 w-3.5" /> Sozlanmagan
            </Badge>
          )}
        </span>
      }
    >
      <p className="mb-4 text-sm text-slate-400">
        Gemini kaliti Instagram chatbot, o'quvchi/guruh/o'qituvchi AI tahlili, kunlik markaz tahlili va AI matn
        yozish uchun kerak. Kalit berilmasa bu funksiyalar ishlamaydi.
      </p>
      {loadError && <p className="mb-3 text-sm text-red-600">{loadError}</p>}

      <div className="max-w-xl space-y-4">
        <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
          <div className="flex flex-wrap items-center gap-2">
            <KeyRound className="h-4 w-4 text-slate-400" />
            <span className="text-sm font-medium text-slate-700">Gemini API kaliti</span>
            {source === 'env' && <Badge tone="green">.env faylidan</Badge>}
            {source === 'crm' && <Badge tone="green">CRM'da saqlangan</Badge>}
            {source === '' && <Badge tone="default">Kiritilmagan</Badge>}
          </div>

          {editable && (
            <form
              className="mt-3 flex flex-wrap gap-2"
              onSubmit={(e) => {
                e.preventDefault()
                if (value.trim()) void save(value.trim())
              }}
            >
              <Input
                type="password"
                autoComplete="off"
                spellCheck={false}
                value={value}
                onChange={(e) => setValue(e.target.value)}
                placeholder={source === 'crm' ? 'Yangi kalit (almashtirish uchun)' : 'AIzaSy...'}
                className="min-w-[240px] flex-1 font-mono text-xs"
              />
              <Button type="submit" disabled={!value.trim() || busy}>
                {busy ? 'Saqlanmoqda…' : 'Saqlash'}
              </Button>
              {cfg?.storedInCrm && (
                <Button type="button" variant="secondary" disabled={busy} onClick={() => void save('')}>
                  O'chirish
                </Button>
              )}
            </form>
          )}
          {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
          {saved && <p className="mt-2 text-sm text-green-700">{saved}</p>}

          <p className="mt-2 text-xs text-slate-500">
            Kalit <b>Google AI Studio → Get API key</b> (aistudio.google.com/apikey) dan olinadi. Bazada shifrlangan
            holda saqlanadi va kiritilgandan keyin qayta ko'rsatilmaydi.
            {source === 'crm' && cfg?.updatedAt && (
              <> Oxirgi o'zgarish: {formatDateTime(cfg.updatedAt)}{cfg.updatedBy ? `, ${cfg.updatedBy}` : ''}.</>
            )}
          </p>
          {source === 'env' && (
            <p className="mt-1 text-xs text-slate-500">
              Kalit serverdagi <code>.env</code> faylida (<code>GEMINI_API_KEY</code>) — u ustun. Almashtirish uchun
              o'sha yerdagini o'zgartiring yoki olib tashlang.
            </p>
          )}
          {cfg && !cfg.canEdit && source !== 'env' && (
            <p className="mt-1 text-xs text-slate-500">Kalitni faqat tizim egasi (superadmin) kirita oladi.</p>
          )}
          {cfg && cfg.canEdit && !cfg.vaultConfigured && (
            <p className="mt-1 text-xs text-amber-700">
              Kalitlarni shifrlash o'chiq: serverdagi .env faylida <code>CREDENTIAL_VAULT_KEY</code> yo'q. Hozircha
              kalitni .env ga yozing.
            </p>
          )}
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium text-slate-700">Model</label>
          <p className="mb-2 text-xs text-slate-400">
            Server <code className="rounded bg-slate-100 px-1">GEMINI_MODEL</code> env o'zgaruvchisidan olinadi.
          </p>
          <Input value={cfg?.model ?? ''} readOnly disabled className="max-w-[280px] font-mono text-xs" />
        </div>
      </div>
    </Card>
  )
}
