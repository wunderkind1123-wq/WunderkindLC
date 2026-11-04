import { useCallback, useEffect, useRef, useState } from 'react'
import { getIgSecrets, setIgSecret, type IgSecretItem, type IgSecrets } from '@/api/services/instagram'
import { apiErrorMessage, formatDateTime } from '@/lib/utils'
import { MkError, MkLoading } from './mk'

/** Kalit qayerdan o'qilayotgani — yorliq va rang. */
function sourceBadge(s: IgSecretItem['source']): { text: string; color: string; bg: string } {
  if (s === 'env') return { text: ".env faylidan", color: '#0d6b4b', bg: 'var(--success-soft)' }
  if (s === 'crm') return { text: "CRM'da saqlangan", color: '#0d6b4b', bg: 'var(--success-soft)' }
  return { text: 'Sozlanmagan', color: '#b42318', bg: '#fdecea' }
}

/**
 * «Maxfiy kalitlar» — Instagram/Meta kalitlarini CRM'dan kiritish (2026-11-04, `marketing-instagram.md`
 * «KALITLARNI CRM'DAN KIRITISH»).
 *
 * ⚠️ Qiymat HECH QACHON ko'rsatilmaydi va maydon har saqlashdan keyin bo'shaydi — faqat holat ("CRM'da saqlangan").
 * Kiritish — FAQAT superadmin (server ham tekshiradi, 403). Xato forma ichida ko'rsatiladi, kiritilgan matn qoladi
 * (`error-visibility.md`).
 */
export function IgSecretsBlock({ onChanged }: { onChanged: () => void }) {
  const [data, setData] = useState<IgSecrets | null>(null)
  const [loadError, setLoadError] = useState('')
  const [values, setValues] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState('')
  const inFlight = useRef(false)

  const load = useCallback(() => {
    getIgSecrets()
      .then((d) => { setData(d); setLoadError('') })
      .catch((e) => setLoadError(apiErrorMessage(e, "Kalitlar holatini yuklab bo'lmadi")))
  }, [])

  useEffect(() => { load() }, [load])

  const save = async (key: string, value: string) => {
    if (inFlight.current) return
    inFlight.current = true
    setBusy(key)
    setErrors((p) => ({ ...p, [key]: '' }))
    try {
      setData(await setIgSecret(key, value))
      setValues((p) => ({ ...p, [key]: '' }))
      onChanged()
    } catch (e) {
      setErrors((p) => ({ ...p, [key]: apiErrorMessage(e, "Saqlab bo'lmadi") }))
    } finally {
      inFlight.current = false
      setBusy('')
    }
  }

  return (
    <div className="card card-pad" style={{ gridColumn: '1 / -1' }}>
      <div className="section-head">
        <div>
          <div className="section-title">Maxfiy kalitlar</div>
          <div className="page-sub">
            Bazada shifrlangan holda saqlanadi, kiritilgandan keyin qayta ko'rsatilmaydi. Serverdagi .env faylida
            qiymat bo'lsa — o'sha ustun.
          </div>
        </div>
      </div>

      {loadError && <MkError text={loadError} onRetry={load} />}
      {!data && !loadError && <MkLoading />}

      {data && !data.vaultConfigured && (
        <div className="mk-alert" style={{ marginBottom: 12 }}>
          Kalitlarni CRM'dan kiritish o'chiq: serverdagi .env faylida <b>{data.vaultEnvKey}</b> yo'q. Hozircha kalitlarni
          .env ga yozing (instagram/SOZLASH.md, 3-qadam).
        </div>
      )}
      {data && data.vaultConfigured && !data.canEdit && (
        <div className="field-hint" style={{ marginBottom: 12 }}>Kalitlarni faqat tizim egasi (superadmin) kirita oladi.</div>
      )}

      {data && (
        <div style={{ display: 'grid', gap: 14 }}>
          {data.items.map((it) => {
            const badge = sourceBadge(it.source)
            const editable = data.canEdit && data.vaultConfigured && it.source !== 'env'
            const v = values[it.key] ?? ''
            return (
              <div key={it.key} className="field" style={{ marginBottom: 0 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                  <label className="field-label" style={{ marginBottom: 0 }} htmlFor={`secret-${it.key}`}>{it.label}</label>
                  <span style={{ fontSize: 12, fontWeight: 600, padding: '2px 8px', borderRadius: 999, color: badge.color, background: badge.bg }}>
                    {badge.text}
                  </span>
                  <code style={{ fontSize: 11, color: 'var(--muted, #6b7280)' }}>{it.key}</code>
                </div>
                {editable && (
                  <form
                    style={{ display: 'flex', gap: 8, marginTop: 6, flexWrap: 'wrap' }}
                    onSubmit={(e) => { e.preventDefault(); if (v.trim()) void save(it.key, v) }}
                  >
                    <input
                      id={`secret-${it.key}`}
                      className="input"
                      style={{ flex: '1 1 260px' }}
                      type="password"
                      autoComplete="off"
                      spellCheck={false}
                      value={v}
                      onChange={(e) => setValues((p) => ({ ...p, [it.key]: e.target.value }))}
                      placeholder={it.source === 'crm' ? 'Yangi qiymat (almashtirish uchun)' : 'Qiymatni kiriting'}
                    />
                    <button className="btn btn-primary btn-sm" type="submit" disabled={!v.trim() || busy === it.key}>
                      {busy === it.key ? 'Saqlanmoqda…' : 'Saqlash'}
                    </button>
                    {it.storedInCrm && (
                      <button
                        className="btn btn-outline btn-sm"
                        type="button"
                        disabled={busy === it.key}
                        onClick={() => void save(it.key, '')}
                      >
                        O'chirish
                      </button>
                    )}
                  </form>
                )}
                {errors[it.key] && <div style={{ marginTop: 6, color: '#b42318', fontSize: 13 }}>{errors[it.key]}</div>}
                <div className="field-hint">
                  {it.hint}
                  {it.source === 'crm' && it.updatedAt && (
                    <> · Oxirgi o'zgarish: {formatDateTime(it.updatedAt)}{it.updatedBy ? `, ${it.updatedBy}` : ''}</>
                  )}
                  {it.source === 'env' && it.storedInCrm && <> · CRM'dagi qiymat ishlatilmayapti (.env ustun)</>}
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
