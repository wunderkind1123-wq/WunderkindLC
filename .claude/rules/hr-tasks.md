---
description: Topshiriqlar (HR) — LMS'dan 1:1 port, scope, hayot sikli, Telegram «Qabul qildim», sinov darsi topshiriqlari.
paths:
  - "WunderkindLC.Application/HrTasks/**"
  - "WunderkindLC.Application/Abstractions/IAppDbContext.HrTasks.cs"
  - "WunderkindLC.Domain/Hr*.cs"
  - "WunderkindLC.Infrastructure/Data/AppDbContext.HrTasks.cs"
  - "WunderkindLC.Server/Controllers/Hr*.cs"
  - "WunderkindLC.Server/Controllers/StaffController.cs"
  - "WunderkindLC.Server/Controllers/TeachersController.cs"
  - "WunderkindLC.Client/src/api/services/hrTasks.ts"
  - "WunderkindLC.Client/src/types/hrTasks.ts"
  - "WunderkindLC.Client/src/pages/admin/hr-tasks/**"
  - "WunderkindLC.Client/src/pages/teacher/**/*opshiriq*"
  - "WunderkindLC.Tests/HrTasks/**"
---

# Topshiriqlar (HR) qoidalari

Boshliq o'z xodimlariga topshiriq beradi, xodim o'z panelida («Mening topshiriqlarim») bajaradi.
**WunderkindLMS'dagi «HR topshiriqlari» modulining 1:1 ko'chirmasi** (u esa eski WunderkindHR'ning
ko'chirmasi). SPEC: `docs/SPEC-hr-tasks.md`. LMS manbasi: `WunderkindLMS/docs/modules/hr-tasks.md`.
Migratsiya: `HrTasks` (11 ta `hr_*` jadval; Kanban jadvallarini ham shu migratsiya tushiradi).

> ⚠️ **Port, qayta loyihalash EMAS.** Qoida, matn, xato kodi, limit va JSON kontrakti
> (`WunderkindLC.Client/src/types/hrTasks.ts`) LMS'dagidek. LC'ga moslash joylari kodda `LC:` izohi
> bilan belgilangan va pastdagi §6 jadvalida. Yangi narsa qo'shishdan oldin LMS'da qanday ekanini qarang.

## 1. Kod qayerda

| Qism | Fayl |
|---|---|
| Domen (holatlar mashinasi, takroriylik, eslatma qoidalari, limitlar, xato kodlari) | `WunderkindLC.Domain/HrTasks.cs` |
| EF modeli (`hr_*`, snake_case, CHECK, unikal kalitlar) | `Infrastructure/Data/AppDbContext.HrTasks.cs` |
| Servis | `Application/HrTasks/HrTasksService*.cs` (`.People` · `.Tasks` · `.Jobs` · `.Sync`) |
| Telegram/fayl portlari | `HrTasksPorts.cs` (`IHrTelegramSender`, `IHrFileStore`) |
| Telegram matnlari (eski `texts.py`/`digest.py`, AYNAN) | `HrTasksTexts.cs` |
| Rejalashtiruvchi (har 5 daq) | `HrTasksScheduler.cs` (IMPORT_MODE'ga BOG'LANMAGAN — u faqat oylik hisob va ota-ona eslatmalarini to'xtatadi) |
| Panel API | `Server/Controllers/HrTasksController.cs` — `api/admin/hr/...` |
| Xodim API («Mening topshiriqlarim») | `Server/Controllers/HrMeController.cs` — `api/hr/me/...` |
| Testlar | `WunderkindLC.Tests/HrTasks/*` |

## 2. Kim nimani ko'radi — SCOPE

| Kim | Panel (`api/admin/hr`) | Doira |
|---|---|---|
| `admin`, `superadmin` | ha | butun tashkilot (direktor) |
| xodim + `hrTasks` kaliti + HR'da `hr_role = director` | ha | butun tashkilot |
| xodim + `hrTasks` kaliti (`manager`/`employee`) | ha | O'ZI + butun bo'ysunuvchilar daraxti (`hr_employee_managers`, rekursiv, halqaga chidamli) |
| xodim + kalit, lekin HR qatori YO'Q | ha | HECH KIM (fail-closed) |
| HR qatori bor har qanday akkaunt (o'qituvchi ham) | yo'q — faqat `api/hr/me` | o'zi |

- ⚠️ **Doiradan tashqari yozuv — 404, 403 EMAS** (mavjudligi oshkor bo'lmasin).
- Kalit (`hrTasks`) faqat PANELNI ochadi; QAYSI qatorlar — servis hal qiladi (`HrTasksService.PanelAsync`).
- Lavozimlar yozish va Sozlamalar — faqat direktor (403 `forbidden`).
- Menejer yaratgan xodim avtomatik uning ostiga tushadi; menejer hech kimga `director`/`manager` bera olmaydi
  (403 `hr_panel_access_forbidden`).
- Server darvozasi: `[AdminPerm("hrTasks", ReadRequiresPerm = true)]` — LMS'dagi `GatedRead`. Kalitning har qanday
  tokeni (`hrTasks:view` ham) o'qishni ochadi (GET, eksport ham). ⚠️ **HAR YOZISH — FAQAT YALANG `hrTasks`** (yoki
  admin/superadmin) — `HrTasksController.EnsureCanWrite` (LMS bilan bir xil, 2026-10-21 revyu): `[AdminPerm]` POST'ni
  `create` ga tenglagani uchun «Ko'rish + Qo'shish» roli xodimni bo'shatish, topshiriqni bekor qilish/qaytarish va
  shablonni o'chirib-yoqishni ham ochib qo'yardi. Rad — 403 `forbidden` matn bilan.
- Yangi AKKAUNT yaratish (`POST employees` ism + telefon bilan) — qo'shimcha `HasFullAccess(User, "staff")`
  (`staff-roles.md` §2), aks holda 403 `hr_panel_access_forbidden`. LC UI bu yo'lni chaqirmaydi (odamlar
  Xodimlar'dan); mavjud foydalanuvchini tanlash (`userId`) qo'shimcha ruxsat talab qilmaydi.
- `api/hr/me` — `[Authorize]` (rol muhim emas), xodim id'si TOKENDAN; faol HR qatori bo'lmasa 403
  `hr_not_employee`; begona topshiriq — 403 `task_not_owned`. Bu yerda xato kodlari `hr_` prefiksisiz.
- Har javob `no-store` (`NoStoreAttribute`) — umumiy kompyuterda keyingi odam oldingining topshiriqlarini ko'rmasin.

## 3. Hayot sikli (eski §5 — o'zgartirmang)

`open → done / returned / cancelled` (`missed` faqat eski `close` siyosatli qatorlarda). Bajarish uchun ISBOT —
fayl YOKI bo'sh bo'lmagan izoh (`hr_proof_required`). KPI (raqamli) topshiriqda qadamlar (`hr_task_progress`,
manfiy — tuzatish), maqsadga yetganda O'ZI `done`. Qayta ochish — faqat o'sha mahalliy kun va kechki yakun
yuborilishidan OLDIN. Muddati o'tgan topshiriq ochiq qoladi (`keep_open`), kech bajarilgani `is_late`.
Isbot: 5 ta × 10 MB, html/svg/js/xml rad etiladi. Ovozli izoh ≤ 300 s.

Takroriy shablonlar: kunlik/haftalik/oylik (oyning 31-i qisqa oyda oxirgi kunga), o'tgan muddat yaratilmaydi,
lavozim nishoni FAOL xodimlarga yoyiladi, nofaol xodimga hech narsa. Idempotentlik — unikal kalit
`(template_id, employee_id, period_start, due_at)`. Shablon o'chsa topshiriqlar qoladi (`template_id = null`).

## 4. Xodimlar ro'yxati — Boshqaruv → Xodimlar'dan

Har LC xodimi HR xodimi (`SyncLmsPeopleAsync`, rejalashtiruvchining har siklida): arxivlanmagan o'qituvchi kartasi
(akkaunti bor, lavozim «O'qituvchi») va har `staff`/`admin` akkaunt; superadmin va platforma egasi — yo'q. Faqat
QO'SHADI: hech kimni nofaol qilmaydi, rahbar/rolga tegmaydi; ism, lavozim (`AppUser.Position`) va yo'q telefonni
moslaydi. Yangi qatorlar «Biriktirilmagan» — direktor daraxtga joylaydi. `branch = center`.

- Odam qo'shish/tahrirlash — **Boshqaruv → Xodimlar**da (HR panelida «+ Xodim» yo'q; API qoldi).
- HR'da ishdan bo'shatish = HR qatori nofaol + ochiq topshiriqlar bekor; AKKAUNTGA TEGMAYDI.
- ⚠️ **Akkauntni O'CHIRISH — HECH QACHON BLOKLANMAYDI** (LMS'dan FARQ, 2026-10-21 revyu): LC'da akkauntni o'chirish =
  ishdan bo'shatish (`staff-roles.md` §6, boshqa yo'l yo'q), LMS'dagi 409 esa birorta topshirig'i bo'lgan xodimni
  abadiy o'chirib bo'lmas qilardi. `StaffController.Delete` va `TeachersController.Delete` →
  `HrTasksService.ReleaseEmployeeAsync`: tarixsiz HR qatori olib tashlanadi; tarixi bor qator **SOBIQ XODIM** bo'lib
  qoladi — nofaol, ochiq topshiriqlari bekor, `user_id = "deleted:<eski id>"` (unikal; sinxron uni tiriltirmaydi).
  Shuning uchun `hr_employees.user_id` da **Users'ga FK YO'Q** (LMS'da RESTRICT) — butunlikni shu chaqiruv ushlaydi.
  Audit qatorida «Topshiriqlar tarixi saqlandi (sobiq xodim), N ta ochiq topshiriq bekor qilindi».
  Yangi joyda `Users.Remove` yozsangiz — shu chaqiruvni ham qo'shing.
- ⚠️ **O'qituvchi ARXIVLANSA** (= ishdan ketdi, `billing.md`) HR qatori nofaol + ochiq topshiriqlari bekor
  (`HrTasksService.SetAccountActiveAsync(…, false)`); arxivdan QAYTARILSA — yana faol (bekor qilinganlar
  tiklanmaydi). Arxivdagi o'qituvchi kartasi Telegram chatlari ro'yxatiga ham kirmaydi.

## 5. Telegram

Yangi topshiriq (panel) va qaytarilgan topshiriq — xodimga DARHOL; ertalabki ro'yxat (07:30), muddat eslatmalari
(120/60/30 daq, sozlamada), kechki yakun (19:00, Du–Sh) — direktorga butun tashkilot, menejerga o'z daraxti; bo'sh
yakun yuborilmaydi, 4096 belgida xodim chegarasida bo'linadi. Ommaviy berishda xodimga BITTA xabar (+ ovozli izoh).
Bot sozlanmagan bo'lsa hech narsa yuborilmaydi; Telegram xatosi so'rovni buzmaydi. Xodim operativ xabari —
outbox'siz, to'g'ridan-to'g'ri (`messaging.md` §0.5 ruxsat bergan toifa).

**LC: TO'LIQ XABAR + «✅ Qabul qildim» (egasining talabi, 2026-10-27)** — `HrTasksService.Notify.cs`, `HrTaskAck`:
- Yangi topshiriq botga HAMMA ma'lumoti bilan keladi (`HrTasksTexts.AssignedFull`): sarlavha, tavsif, muddat,
  «👤 Berdi: <kim>» (tizim yaratgani — «Tizim…»), biriktirmalar soni; keyin har biriktirma alohida xabar: ovoz —
  `sendVoice`, rasm (jpeg/png/webp/gif) — `sendPhoto`, qolgani — `sendDocument`.
- Xabar ostida **«✅ Qabul qildim»** (callback `hrack:{taskId}`) + «📋 Topshiriqlarim». Bosish = paneldagi «Tasdiqlash»
  (`HrTask.Accept` → `AcceptedAt`), tugma «✅ Qabul qilindi · Topshiriqlarim» ga almashadi. ⚠️ Faqat topshiriq EGASINING
  chati (staff — `UserId`, o'qituvchi — kartasi) qabul qiladi; begona/noma'lum id — BIR XIL «topilmadi» toast'i (id
  mavjudligi oshkor bo'lmasin, `MessageAckService` naqshi). Bekor qilingan topshiriq qabul qilinmaydi. Ikkinchi bosish
  birinchi vaqtni saqlaydi. `TelegramBotService` `hrack:` ni umumiy `answerCallbackQuery` va bot darvozasidan OLDIN ushlaydi.
- Ommaviy berishda xodimga ≤ 3 ta (`MaxFullTaskMessages`) topshiriq — har biri TO'LIQ xabar; ko'proq — LMS'dagi
  BITTA yig'ma xabar (tugmasiz, panelda qabul qilinadi).

## 6. LC moslashlari (LMS'dan FARQI)

| Mavzu | LC'da |
|---|---|
| Rollar | kassir roli yo'q (kassir = `staff`) |
| Telegram chatlari | `TelegramRegistrations`: staff/admin — `UserId`, o'qituvchi — o'qituvchi KARTASI `TeacherId`; `BlockedAt` bo'lgani o'tkaziladi (`HrTasksService.LinksAsync`) |
| Xabar tugmasi | **LC qo'shimchasi:** har xabarda «📋 Topshiriqlarim» `web_app` tugmasi — Mini App'da xodimga `admin/hr/mening-topshiriqlarim`, o'qituvchiga `teacher/topshiriqlar`, yakunda `admin/hr/topshiriqlar` (`HrTasksService.MyTasksUrl`) |
| «Taklif» | LC'da admin beradigan bir martalik kod YO'Q: chat Panel ichida login qilganda O'ZI ulanadi. `invite` javobida `inviteCode = ""` va bot havolasi; ulanganga 409 |
| Ruxsat tokeni | LC rol matritsasi `hrTasks:view`/`:create` kabi qisman tokenlar yozadi: har qanday token o'qiydi (`PermissionRules.HasSection`), YOZISH va «boshqaradi» (`canManage`) — faqat yalang kalit (`HasFullSection`, `EnsureCanWrite`) |
| Akkaunt o'chirish | LMS — 409; LC — sobiq xodim (yuqorida §4), `user_id` FK'siz |
| Fayl turlari | `/uploads` `ServeUnknownFileTypes = false` — shuning uchun isbot/biriktirma FAQAT u bera oladigan kengaytma bilan qabul qilinadi (`HrFileTypes`: freymvork xaritasi + `.heic/.heif/.opus/.7z`; `Program.cs` `/uploads` ham AYNAN shu xarita). Aks holda fayl "isbot" bo'lib qabul qilinar, rahbar ochganda 404 |
| Ustun nomlari | LC'da global snake_case YO'Q — HR jadvallari uchun nomlar `AppDbContext.HrTasks.cs` da qo'lda (LMS bilan bir xil: `pk_`/`fk_`/`ix_`) |
| SQLite testlari | PG-only qismlar (smallint[] standartlari, massiv CHECK'lari, jsonb) SQLite'da qo'yilmaydi, `DateTimeOffset` — ticks konverteri (faqat testlar) |
| Audit | LMS yozmaydi; LC'da faqat HR paneli YANGI AKKAUNT yaratganda bitta `Staff` qatori («… (Topshiriqlar → Xodimlar) — ruxsatsiz») |
| Zaxira | JSON zaxirada `hr*` biznes jadvallari bor; `hrTaskReminders`, `hrJobRuns` — `ExcludedTables` (texnik) |
| Xodim faolligi | `StaffActivity` «yopilgan topshiriqlar» = xodimning O'ZI bajargan HR topshiriqlari |
| Bot xabari | LMS — sarlavha + muddat; LC — to'liq topshiriq, biriktirmalar va «✅ Qabul qildim» (§5) |
| Sinov darsi topshiriqlari | LC qo'shimchasi (§8) — `hr_trial_tasks` |
| Lid (sotuv) topshiriqlari | LC qo'shimchasi (§9) — `hr_lead_tasks`, ro'yxatlarda «Sotuv» belgisi |
| KPI / so'rovnomalar | LMS'ning HR KPI'si va so'rovnomalari ko'chirilmagan. LC KPI cheklistining `tasks.overdue_zero` / `tasks.due_done` kalitlari HR topshiriqlaridan hisoblanadi (`ChecklistAutoCheck`, `kpi.md` §11) |

## 7. Yangi narsa qo'shsangiz

- Qoida/matn/limitni o'zgartirishdan oldin LMS'dagisini qarang — ikkala tizim bir xil ishlashi kerak.
- Yangi endpoint — `HrTasksController`/`HrMeController` ichida, `Run(...)` orqali (scope + xato formati shu yerda).
  Doiradan tashqari — 404.
- Vaqt — `DateTimeOffset` (UTC, offset 0), "bugun" — sozlamadagi mintaqa (`HrValues.LocalDate`), `DateTime.Now` EMAS.
- Yangi `hr_*` jadval — `AppDbContext.HrTasks.cs` dagi `Entities` ro'yxatiga (snake nomlar), `IAppDbContext.HrTasks.cs`
  ga va `BackupService` ga (yoki `ExcludedTables` ga sababi bilan).
- Telegram matni — `HrTasksTexts` da, foydalanuvchi matni `Escape()` dan o'tadi (HTML parse_mode).
- Yangi xodim manbasi (yangi rol) — `SyncLmsPeopleAsync` va `EmployeeUserRoles`.
- Yangi topshiriq yaratish yo'li — xabarni `NotifyAssignedAsync` orqali yuboring (to'liq matn + «Qabul qildim»).

## 8. SINOV DARSI TOPSHIRIQLARI (LC qo'shimchasi, 2026-10-27)

Egasining talabi: sinov darsiga yozilgan lidlar bilan ishlash avtomatik topshiriq bo'lib administratorga borsin.
Kod: `HrTrialRules` (sof, `HrTrialTasksTests`), `HrTasksService.Trials.cs`, jadval `hr_trial_tasks`
(migratsiya `AddHrTrialTasks`). Rejalashtiruvchining HAR siklida (5 daq) yuguradi — `RunTrialTasksAsync`.

| Ro'yxat | Qachon | Kimlar |
|---|---|---|
| **«Ertaga (dd.MM) sinov darsiga keladiganlar — N ta»** | sinovdan BIR KUN OLDIN, 09:00 dan | ertangi, natijasi belgilanmagan sinovlar |
| **«Kecha (dd.MM) sinov darsi bo'lganlar bilan bog'laning — N ta»** | sinovdan KEYINGI KUNI, 09:00 dan | kechagi sinovlar, natijasi bilan (keldi/kelmadi/belgilanmagan) |

- **Kimga:** lid MAS'ULI (`Lead.AssigneeUserId`) → bo'lmasa (yoki u HR xodimi emas) sinovga YOZGAN xodim (oxirgi
  `LeadEvent` `trial` ning `ActorUserId`) → u ham bo'lmasa HR direktor(lar)i. Faqat FAOL HR xodimi.
- **BITTA ro'yxat** — bir (xodim, tur, kun) uchun bitta topshiriq (unikal `uq_hr_trial_task`); 09:00 dan keyin yozilgan
  sinov o'sha ro'yxatga QO'SHILADI (sarlavha/tavsif yangilanadi, xodimga «➕ Ro'yxatga yangi o'quvchi qo'shildi»;
  bajarilgan ro'yxat qayta ochiladi, bekor qilingani — tegilmaydi).
- **Har o'quvchi:** ism, telefon (+ ota/ona), vaqt, guruh (ustoz), kurs, daraja (lidda — ustun, bo'lmasa daraja testi),
  mas'ul; kechagisida — natija. Maktab sinfi YO'Q (lidda maydon yo'q — egasining qarori: sinfsiz).
- Topshiriq: `Source = director`, `CreatedBy = null` (botda «Berdi: Tizim (sinov darsi)»), `ProofRequired = false`,
  muddat — o'sha kuni 20:00 (o'tib ketgan bo'lsa hozirdan +2 soat). Botga to'liq xabar + «✅ Qabul qildim».
- **O'ZI BAJARILADI** (`AutoDoneComment` izohi bilan) — ro'yxatdagi HAR sinov hal bo'lganda: lid o'chirilgan,
  aylantirilgan, shartnomasi yopilgan yoki RAD ETILGAN (`LostAt`); «ertangi» — natijasi belgilangan yoki boshqa kunga
  ko'chirilgan; «kechagi» — lid KEYINGI sanaga qayta sinovga yozilgan (yangi sinov o'z sikliga — yana ikkala ro'yxat).
  Qo'lda «Bajardim» ham mumkin (isbotsiz). Yopish oynasi — oxirgi 45 kun.
- ⚠️ Kechagi ro'yxat FAQAT ochiq lidlar bilan yaratiladi; 09:00 gacha hech narsa yaratilmaydi (lekin yopish yuguradi);
  server butun kun o'chiq bo'lsa o'sha kun o'tkazib yuboriladi (orqaga yaratilmaydi).

## 9. LID (SOTUV) TOPSHIRIQLARI (LC qo'shimchasi, 2026-11-10)

Egasi: «lid vazifalari alohida bo'lim bo'lmasin — Topshiriqlar bilan BITTA, sotuvnikilari boshqa rangda ajralib tursin».
Lid sahifasidan qo'yilgan «keyingi qadam» — oddiy `HrTask` (`Source = director`, `CreatedBy` = qo'ygan xodim,
`ProofRequired = true` — natija majburiy) + `hr_lead_tasks` bog'lanishi (migratsiya `AddHrLeadTasks`; unikal `task_id`,
lidga FK YO'Q). Kod: `HrTasksService.Leads.cs`, qoidalar `LeadTasks` (Application/Services), testlar `HrLeadTasksTests`.
To'liq lid tomoni — `.claude/rules/crm-leads.md` «LID TOPSHIRIQLARI».

- Yaratish/tahrir/bajarish/bekor qilish — `api/admin/lead-tasks` (`leads.list`, HR panel DOIRASI qo'llanmaydi — tizim yo'li,
  sinov topshiriqlari kabi). Telegram — `NotifyAssignedAsync` (to'liq xabar + «✅ Qabul qildim»).
- Mas'ul — faqat FAOL HR xodimi, panel akkaunti (staff/admin); o'qituvchilar ro'yxatda yo'q.
- **Ajratish:** `HrTaskDto` va `HrAppTaskDto` da `SalesKind` (`lead` | `trial` | null), `LeadId`, `LeadName`
  (`SalesMarksAsync` — `hr_lead_tasks` va `hr_trial_tasks` dan). Klient `hr-tasks/SalesBadge.tsx`: to'q sariq chip + chap
  chiziq — panel ro'yxati, «Mening topshiriqlarim», tafsilot (lid sahifasiga havola), bosh panel.
- HR tomonida yopilgan lid topshirig'i lid tarixiga ham yoziladi: xodim «Bajardim» (`CompleteAsync`) va panel «Bekor qilish»
  (`CancelTaskAsync`) → `RecordLeadTaskChangeAsync` (shu saqlashning o'zida).
- Lid yo'qotilsa/aylantirilsa/o'chirilsa — ochiq lid topshiriqlari BEKOR (`LeadTasks.CancelOpenForLeadAsync`, `LeadRemoval`).
- ⚠️ **«Kelgusi 30 kun»** (LC, `HrTasksPage`): LMS davr tugmalari faqat BUGUNDAN ORQAGA sanaydi — ertangi topshiriq (lid
  topshiriqlari odatda shunday) hech bir tabda chiqmasdi (egasi: «qo'ydim, ko'rinmayapti»). Tugma `dateFrom = bugun, dateTo = +30`.
- ⚠️ **«Bajarilmagan ishlar» havolasi** (`paths.hrtOpenWorkLink`): `−30 … +30 kun` + holat `open`/`returned` — KECHIKKANLAR ham
  ko'rinadi (faqat «bugundan oldinga» oyna ularni yashirardi). Ishlatiladi: lid sahifasidagi «Topshiriqlarda ko'rish» va
  **rahbar «Mening topshiriqlarim»** — superadmin/admin HR'da xodim bo'lmasa (§4) «ro'yxatda yo'qsiz» o'rniga shu
  ro'yxatga yo'naltiriladi (`HrMyTasksPage`, «kim nima ish qilayotgani»).
- HR tomonidagi har o'zgarish lid tarixida: bajarildi / bekor / **qayta ochildi** / **qaytarildi** (`RecordLeadTaskChangeAsync`);
  xodim nofaol bo'lsa (bo'shatish, akkaunt o'chirish/arxiv) ochiq topshiriqlari bekor — YAGONA joy
  `CancelEmployeeOpenTasksAsync`, lid topshirig'ida lid tarixiga «mas'ul xodim faol emas» yoziladi.
- Lid sahifasidan muddat o'zgartirilsa yuborilgan eslatma belgilari o'chadi (`ForgetRemindersAsync`, panel tahriri kabi) —
  «30 daqiqa qoldi» / «muddati keldi» yangi muddat uchun qayta ketadi.

## 10. «MUDDAT KELDI» XABARI (LC qo'shimchasi, 2026-11-10)

Egasi: «topshiriq deadline kelsa botdan xabar borsin». LMS eslatmalari (§5) faqat muddatdan OLDIN (120/60/30 daq).
`HrTasksService.DueNow.cs` → `SendDueNowAsync` (rejalashtiruvchi har 5 daq, «Eslatmalar» yoqilgan bo'lsa, oldindan
eslatmalardan alohida try ichida), matn `HrTasksTexts.DueNow`, testlar `HrDueNowTests`.

- Muddati so'nggi **30 daqiqada** tugagan (`DueNowWindowMinutes` — server o'chib qolsa eski kechikkanlar to'lqin bo'lib
  ketmasin), hali ochiq topshiriq → ijrochiga BITTA xabar (bir nechtasi bo'lsa — ro'yxat).
- Bir topshiriqqa BIR marta: `hr_task_reminders` ga `offset_minutes = 0` qatori (oldindan eslatmalar ≥ 5 daq — to'qnashmaydi).
- «Kun oxiri» (23:59, vaqtsiz / «Butun kun») topshiriqlar — faqat `RemindEndOfDay` yoqilgan bo'lsa (yarim tunda xabar yo'q).
- Telegram ulanmagan xodimga hech narsa (oldindan eslatmalar kabi).

**Eslatma sukuti — faqat 30 daqiqa (egasi, 2026-11-10):** «ikki soat oldin emas, faqat yarim soat oldin, keyin vaqti
kelganda». `HrSettings.DefaultReminderOffsets = [30]` (LMS — `[120, 60, 30]`), baza sukuti `'{30}'`, migratsiya
`AddHrAfternoonList` mavjud sozlama qatorini ham `{30}` ga o'tkazdi. «Sozlamalar»da yana 1–2 soat qo'shish mumkin
(tanlov tugmalari qoldi).

## 11. KUNDUZGI «HALI BAJARILMAGAN» RO'YXAT (LC qo'shimchasi, 2026-11-10)

Egasi: «har kuni 16:00 da bajarilmagan topshiriqlar hammasi ro'yxat bo'lib borsin». `HrSettings.AfternoonListEnabled`
(sukut yoqiq) + `AfternoonTime` (sukut 16:00) — migratsiya `AddHrAfternoonList`, «Sozlamalar»da o'zgartiriladi.
`SendAfternoonListsAsync` — ertalabki ro'yxat (`SendMorningListsAsync`) bilan BIR XIL tanlov (ochiq/qaytarilgan,
`period_start <= bugun`, faol xodim) va ko'rinish (`HrTasksTexts.Afternoon` — «🕓 Hali bajarilmagan topshiriqlar — N ta»,
kechikkanlar alohida qizil). Rejalashtiruvchida `HrJobs.Afternoon` — kuniga BIR marta (`hr_job_runs`), server o'chib
qolsa o'sha kuni kechroq, lekin ko'pi bilan 3 soat (`HrTasksScheduler.AfternoonLateWindow`) — kechqurun/tunda
«hali bajarilmagan» xabari yuborilmaydi. Bo'sh ro'yxat yuborilmaydi. `PUT settings` da maydonlar ixtiyoriy (eski klient yubormasa
joriy qiymat qoladi). Testlar: `HrAfternoonListTests`.

## 12. «MENING TOPSHIRIQLARIM» — DOSKA (kanban, LC qo'shimchasi, 2026-11-10)

Egasi: «topshiriqlarim kanban shaklida — bajarilmagan, bugun, kechikkan ustunlari; bajarilgani o'chib ketsin, «bajarildi»
bo'lib qolib ketmasin». `HrMyTasksPage` — yangi **«Doska»** tabi, admin panelda STANDART (`defaultTab = 'board'`;
o'qituvchi portali avvalgidek «Kun bo'yicha»). Sof qoida — `myBoard.ts` (`myBoard.test.ts`).

- BELGILANGAN ustunlar (egasining tanlovi — xodim qo'shmaydi/o'chirmaydi): **Kechikkan · Bugun · Ertaga · Keyinroq**
  (Toshkent sanasi, «bugun» — serverniki; kechikkan — serverning `isOverdue`; muddatsiz — «Keyinroq»).
- Faqat `open`/`returned` (bitta so'rov, ≤ `HR_MAX_PAGE_SIZE`; ko'prog'i bo'lsa ogohlantirish). «Bajardim» → doskadan
  YO'QOLADI, lekin bazadan O'CHMAYDI (egasining tanlovi): «Bajarilgan» tabi, KPI, hisobot va lid tarixi unga tayanadi.
- Kartalar — o'sha `MyTaskCard` (tasdiqlash, bajarish, KPI qadam, sotuv belgisi). Sudrab muddat o'zgartirish YO'Q:
  muddatni xodim emas, topshiriq bergan o'zgartiradi (LMS qoidasi).

## 13. «QABUL QILDIM / TASDIQLASH» OLIB TASHLANDI (2026-11-10, egasi)

«Topshiriq qabul qildim, tasdiqladim degan narsa kerak emas — topshiriq kelganda darhol bajariladigan bo'lsin».
`HrTask.NeedsAcceptance` → har doim `false` (JSON maydoni qoladi); bot xabari `SendHtmlAsync` (faqat «📋 Topshiriqlarim»,
«✅ Qabul qildim» tugmasi va «tasdiqlang» qatori YO'Q); jamlanmadagi `UnacceptedCount` = 0; UI'da «Tasdiqlash» tugmasi,
«Tasdiqlanmagan» kartasi, «Tasdiq» filtri, tafsilotdagi tasdiq qatori, lid sahifasidagi «qabul qildi» — yo'q.
Eski xabarlardagi `hrack:` tugmasi zararsiz ishlayveradi (`HrTaskAck`), eski `AcceptedAt` qiymatlari tegilmaydi.
§5 dagi «✅ Qabul qildim» tavsifi — TARIX.

## 14. AVTOMATIK TOPSHIRIQLAR OLUVCHISI — sinov darsi va «Service» (LC, 2026-11-10)

Egasi: «sinov darsiga kelmaganlar faqat Dilfuza Rahimovaga topshiriq bo'lib yozilsin; servis uchun faqat Munojatga».
Ikkalasi ham «Topshiriqlar → Sozlamalar» → «Avtomatik topshiriqlar kimga» (`HrSettings.TrialTaskEmployeeId`,
`ServiceTaskEmployeeId`, migratsiya `AddHrServiceTasks`; faqat FAOL HR xodimi, `""` — olib tashlash, `null` — tegilmaydi).

- **Sinov darsi (§8):** tanlangan bo'lsa — IKKALA ro'yxat (ertangi va kechagi) FAQAT shu xodimga; lid mas'uli/yozgan/direktor
  zanjiri ishlatilmaydi. Tanlangan xodim nofaol bo'lib qolsa — eski zanjirga qaytadi.
- **«Service» kunlik topshirig'i** (`HrTasksService.Service.cs`, jadval `hr_service_tasks`, unikal xodim + kun): har kuni 09:00 dan
  keyin (rejalashtiruvchi har 5 daq) «Bog'lanish kerak» navbatidagi BUGUNGI ish — `new` + qayta qo'ng'iroq sanasi bugun/o'tgan
  (`callback`) — BITTA ro'yxat (ism · telefon (ota-ona, bo'lmasa o'quvchi) · sabab · kechikkan sana; ko'pi bilan 60 qator).
  «Berdi: Tizim (Service)», `ProofRequired = false`, muddat 20:00. Navbatda ish bo'lmasa — yaratilmaydi. Ertasi kuni yangi
  ro'yxat yaratilganda kechagi OCHIQ ro'yxat bekor qilinadi («yangi ro'yxat bilan almashtirildi»). Ro'yxatdagi HAMMA talab
  yopilsa (done/failed/o'chirilgan) — topshiriq O'ZI bajariladi (oyna 14 kun). Kun ichida yangi tushgan talab — ertangi ro'yxatda.
- Testlar: `HrServiceAndFixedTrialTests`.
