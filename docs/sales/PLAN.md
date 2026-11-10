# Sotuv voronkasi — mavjud «Lidlar»ni kuchaytirish rejasi

Manba: `docs/sales/SALES_MODULE_SPEC.md` (amoCRM + HubSpot asosidagi spetsifikatsiya, v2).
Qaror (egasi, 2026-11-10): **alohida `sales` moduli QURILMAYDI** — mavjud lid moduli (`Lead`,
`LeadStage`, `LeadEvent`, `TrialLesson`, `LeadConversion`, Telegram kartasi, formalar, KPI)
kuchaytiriladi. Spetsifikatsiyadan faqat CRM'da YO'Q imkoniyatlar olinadi.

## 0. Asosiy tamoyil — HECH NARSA BUZILMAYDI

1. **Faqat qo'shish.** Mavjud jadval, endpoint, ekran va hisobotlar o'z ma'nosida ishlayveradi.
   Yangi ustunlar nullable / standart qiymatli; yangi jadvallar alohida.
2. **Backfill yo'q** (kerak bo'lmasa). Eski lidlar yangi imkoniyatsiz ham to'g'ri ko'rinadi
   (masalan vazifasi yo'q lid — sariq «Vazifa yo'q» belgisi, xato emas).
3. **Analitika tegilmaydi.** Voronka (`LeadAnalytics`) faqat `stage`/`created`/`convert` hodisalarini
   o'qiydi — yangi hodisa turlari (`task`, `task_done` …) unga ta'sir qilmaydi.
4. **Ruxsat** — mavjud `leads.list` (va uning amallari). Yangi kalit faqat zarur bo'lsa.
5. Har bosqich: migratsiya + testlar + `.claude/rules/crm-leads.md` yangilanadi; avval localhost.

## 1. Spetsifikatsiya ↔ CRM — nima bor, nima yo'q

| Spetsifikatsiyada | CRM'da | Reja |
|---|---|---|
| Sdelka | `Lead` | bor — kengaytiriladi |
| Voronka bosqichlari | `LeadStage` (bitta voronka) | 2-qism: bir nechta voronka |
| Won / Lost | aylantirish (`ConvertedStudentId`) / `LostAt` + sabab | bor — o'zgarmaydi |
| Hodisalar lentasi | `LeadEvent` | bor — yangi turlar qo'shiladi |
| Manba, yo'qotish sababi | `LeadSource`, `ActionReason(lead_lost)` | bor |
| Mas'ul | `AssigneeUserId` | bor — qo'lda o'zgartirish qo'shiladi |
| Telefon dublikati | `PhoneKey` + 409 | bor |
| Sinov darsi, won → o'quvchi | `TrialLesson`, `LeadConversion` | bor |
| Veb-forma, UTM, Instagram, Telegram | `LeadForm`, Instagram moduli, bot | bor |
| **Vazifalar (keyingi qadam)** | yo'q | **1-qism** |
| Ko'p voronka, bosqich izohi/SLA/ehtimol | yo'q | 2-qism |
| Byudjet (summa) va weighted summa | yo'q | 2-qism |
| «Bugun» ekrani, vazifa navbati | yo'q | 3-qism |
| Dashboard vidjetlari (vazifa intizomi) | qisman (CRM statistikasi) | 3-qism |
| Avtomatika (bosqich → vazifa, qotib qolish) | yo'q | 4-qism |
| Ketma-ketliklar (Telegram follow-up) | yo'q | 5-qism |
| Playbook (skript + savollar) | yo'q | 5-qism |
| Prognoz, kvota, scoring | yo'q | 6-qism |
| Kompaniyalar, amoCRM importi, WhatsApp, Email, AI xulosa | — | **olinmaydi** (B2C, kerak emas) |
| Telefoniya (UTel/Sipuni) | yo'q | markazda telefoniya bo'lsa — alohida |

## 2. Qismlar

### 1-qism — Lid TOPSHIRIQLARI («keyingi qadam») — «Topshiriqlar» bilan BITTA
Egasi (2026-11-10): alohida «Vazifalar» bo'limi bo'lmasin — lid vazifasi «Topshiriqlar» (HR) bo'limidagi oddiy
topshiriq bo'lsin, sotuvnikilari esa boshqa rangda ajralib tursin.
- Lid sahifasidan qo'yilgan topshiriq = `HrTask` + `hr_lead_tasks` bog'lanishi (sinov darsi topshiriqlari naqshi).
  Xodimning «Mening topshiriqlarim»ida ko'rinadi, Telegram'ga «✅ Qabul qildim» bilan boradi, KPI/eslatmalar — o'sha.
- Qoidalar (spec §15): **natija majburiy**; bajarilgach **«Keyingi topshiriq»** taklifi (bitta tranzaksiyada);
  faqat OCHIQ lidga; lid yo'qotilsa/aylantirilsa/o'chirilsa ochiq topshiriqlar **avtomatik bekor**.
- Har amal lid tarixiga (`task`, `task_done`, `task_cancel`) — HR tomonida yopilsa ham.
- Kanban kartasida indikator: 🟡 topshiriq yo'q · 🔴 N kun kechikdi · 🟢 bugun HH:mm · 🔵 ertaga/sana.
- «Topshiriqlar» ro'yxatlarida to'q sariq **«Sotuv · lid» / «Sotuv · sinov darsi»** belgisi + chap chiziq, lidga havola.
- Keyinroq: lidlar ro'yxatida «Topshiriqsiz lidlar» / «Topshirig'i kechikkan lidlar» filtri.

### 2-qism — Voronka kuchaytirish
- `LeadPipeline` (bir nechta voronka; mavjud bosqichlar «Asosiy» voronkaga — migratsiyada).
- Bosqichda: izoh (yo'riqnoma), SLA (kun), ehtimol (%).
- Lidda: byudjet (summa), kutilgan yopilish sanasi; ustun sarlavhasida soni + summa + weighted.
- Mas'ulni qo'lda o'zgartirish (auditi bilan).
- Bosqich qoidalari (ixtiyoriy): bosqich sakrashni cheklash.

### 3-qism — Menejer intizomi
- **«Bugun»** sahifasi: bugungi/kechikkan vazifalar, javobsiz yangi lidlar (SLA), qotib qolgan lidlar.
- Vazifa navbati: bitta-bitta «Bajarildi va keyingi».
- Vidjetlar: kechikkan vazifalar, vazifasiz lidlar, menejerlar reytingi.
- Telegram: xodimga kunlik ro'yxat (asosiy bot, mavjud ulanish).

### 4-qism — Avtomatika v1
- Qoida = bosqich + trigger (kirdi / N kun turdi) + amal (vazifa yaratish, mas'ul, xabar).
- Idempotent, dublikat vazifa yaratmaydi (spec §15.1).

### 5-qism — Ketma-ketliklar va playbook
- Telegram orqali ko'p qadamli follow-up («Sinovga kelmadi», «O'ylab ko'ryapti»), javob kelsa to'xtaydi.
- Playbook: bosqichga mos qo'ng'iroq skripti + savollar lid maydonlariga yoziladi.

### 6-qism — Boshqaruv
- Oylik kvota va prognoz (menejer bo'yicha), lid ball (scoring), xavfdagi lidlar.

## 3. Holat

| Qism | Holat |
|---|---|
| 1 — Lid topshiriqlari («Topshiriqlar» bilan bitta) | tayyor (localhost) — `crm-leads.md` «LID TOPSHIRIQLARI» |
| 2–6 | rejada |
