---
description: Filiallar — har filial alohida ilova va baza (new-branch.sh, BRANCH_LINKS, BranchSwitcher).
paths:
  - "infra/branch/**"
  - "infra/nginx/**"
  - "docker-compose*.yml"
  - "WunderkindLC.Application/Services/BranchLinks.cs"
  - "WunderkindLC.Server/Controllers/Branch*.cs"
  - "WunderkindLC.Client/src/api/services/branch*.ts"
  - "WunderkindLC.Client/src/components/layout/BranchSwitcher.tsx"
  - "WunderkindLC.Client/src/components/layout/Topbar.tsx"
  - "WunderkindLC.Client/src/pages/admin/branches/**"
  - "WunderkindLC.Tests/BranchLinksTests.cs"
---

# Filiallar — har filial ALOHIDA ilova va baza (2026-10-27)

Egasining talabi: yangi filial — noldan bo'sh baza, o'z xodimlari va login/parollari; tepadagi tanlovdan FAQAT superadmin
boshqa filialga o'tadi. Kod bitta (multi-tenant YO'Q) — har filial shu image'ning alohida nusxasi.

| Qism | Qayerda |
|---|---|
| Filial yaratish (serverda) | `infra/branch/new-branch.sh <slug> <host> "<Nomi>" [asosiy nomi]` → `/root/wlc-branches/<slug>/` (.env, compose) |
| Konteynerlar | `wlc-<slug>-app` · `wlc-<slug>-postgres` · `wlc-<slug>-backup` (kunlik dump, 7 ta), servis nomlari `app_<slug>`/`db_<slug>` |
| Yo'naltirish | `infra/nginx/nginx.conf` → `map $host $app_upstream` + `/root/wlc-branches/nginx/<slug>.map` |
| Almashtirgich | `BRANCH_LINKS` (env, barcha nusxada bir xil) → `GET api/admin/branch-links` (superadmin) → `Topbar` → `BranchSwitcher` |
| Har deploydan keyin | `bash infra/branch/update-branches.sh` — filiallar yangi image bilan qayta yaratiladi |

- ⚠️ **Servis nomi `app`/`postgres` BO'LMASIN**: filial app'i asosiy tarmoqqa (`wunderkindlc_default`, nginx uchun) ulanadi —
  bir xil nom DNS aliasi bo'lib, asosiy app'ning `Host=postgres` ulanishi yoki nginx'ning `app` i filialga ketib qolardi.
  Baza faqat filialning ichki tarmog'ida.
- Har filialning O'Z `JWT_KEY`, `CREDENTIAL_VAULT_KEY`, Postgres paroli (skript serverda yaratadi, chiqarmaydi). Sessiya
  filiallar orasida O'TMAYDI — superadmin har filialda alohida kiradi (seed: asosiy `.env` dagi OWNER_LOGIN/OWNER_PASSWORD).
- Filialda MCP, kamera, turniket O'CHIQ (tokenlar berilmagan). Telegram bot — BITTA, hamma filial uchun (pastda).
- **BITTA BOT, HAMMA FILIAL (2026-11-06, egasining qarori):**
  - Token umumiy, lekin `getUpdates` FAQAT asosiy nusxada: filialda `Telegram__PollingDisabled: "1"` (ikkinchi poller = 409).
    Filial xabarni O'ZI to'g'ridan yuboradi; kelganini asosiy nusxa `TelegramBranchRouter` bilan uzatadi
    (`POST api/internal/telegram/update`, ichki Docker tarmog'i, maxfiy kalit; nginx bu yo'lni tashqariga bermaydi).
  - Kimga: teg (`@yakkatut|hrack:…`) → o'sha filial. Tegsizda: asosiy nusxada ulangan chat → shu yerda; aks holda
    `GET api/internal/telegram/knows` bilan chatni taniydigan filial (1 daq kesh); hech kim tanimasa → asosiy nusxa darvozasi.
    Qoidalar sof — `TelegramBranchRouting`, testlar — `TelegramBranchRoutingTests`. Baza/migratsiya YO'Q, chat↔filial ro'yxati ham YO'Q.
  - ⚠️ Teg kerak joylar: mustaqil keladigan tugmalar (`HrTaskAck`, `MessageAckService`) va `link-token`: bir odam ikki filialda
    bo'lishi mumkin. Yangi shunday tugma qo'shsangiz — `TagOwn`. Suhbat ichidagi tugmalarga shart emas.
  - ⚠️ Ikki filialda ulangan odam oddiy matn/tugma bilan ASOSIY nusxada gaplashadi (u yerdan kod oladi); filial xabarlari va
    ularning tugmalari baribir ishlaydi.
  - Panel: filial xabari/klaviaturasi o'z saytini ochadi (`PanelUrl` ← `App:Host`). Hali ulanmagan odam uchun umumiy tugma
    asosiy manzilni ochadi va login sahifasida filial tanlovi chiqadi: `MiniAppBranchPicker` (FAQAT Mini App ichida; ro'yxat —
    anonim `GET api/public/branch-links`; mantiq — `lib/miniAppBranch.ts`). Tanlov uy origin'ida eslab qolinadi; boshqa filialdan
    tanlash uy orqali o'tadi (`/login?filial=<url>`), manzil faqat server ro'yxatidan (open redirect yo'q); Telegram parametrlari
    hash'da uzatiladi (`launchHash`). ⚠️ `BRANCH_LINKS` da uy BIRINCHI (`new-branch.sh` shunday yozadi).
  - Sozlash — `new-branch.sh` (`.env` ga qo'lda yozilmaydi): `TELEGRAM_BRANCH_KEY` (asosiysi `main`), `TELEGRAM_BRANCH_ROUTES`
    (faqat asosiyda), `TELEGRAM_ROUTER_SECRET`, token. Mavjud filialga ham qayta ishga tushirsa bo'ladi.
  - Hali YO'Q: lid kartalari guruhlari (guruh yangilanishlari faqat asosiy nusxada) — filial lidi guruhga bormaydi.
- Cloudflare: DNS `A <slug>` → server IP (PROXIED) va Origin Rule «nginx 8443» ifodasiga `http.host eq "<host>"` qo'shiladi —
  aks holda trafik 443 dagi xray'ga tushadi.
- `Branch` jadvali («Boshqaruv → Filiallar») — faqat ma'lumotnoma (nom, manzil, xarita), almashtirgichga ta'sir qilmaydi.
- Umumiy (filiallararo) hisobot YO'Q — kerak bo'lsa alohida ish.
