# SALES MODULE SPEC — amoCRM klonini mavjud CRM ga alohida bo'lim sifatida qo'shish

> **Kimga:** AI coding agent (Claude Code, Cursor, Codex va h.k.).
> **Nima:** amoCRM ning barcha asosiy bo'limlarini (Dashboard, Voronka/Sdelkalar, Mijoz kartochkasi, imBox, Vazifalar, Ro'yxatlar, Analitika, Avtomatlashtirish, Sozlamalar) mavjud **Markaz CRM** (Wunderkind) ichida **alohida modul** — `sales` — sifatida qurish.
> **Manba:** `BRIGHT FUTURE` amoCRM hisobi (2026-11-05 da UI va API v4 orqali o'rganildi) + `Wunderkind.uz` HubSpot hisobi (Deals board, deal record, pipeline settings) va HubSpot Sales Hub hujjatlari + avvalgi ikki hujjat: "amoCRM voronka va vazifalar tizimi — Markaz CRM uchun texnik topshiriq" (2026-11-04) va "CRM voronkasini yangilash: tahlil va takliflar" (2026-11-05).
> **Versiya:** v2 — amoCRM asosiga HubSpot'ning kuchli tomonlari qo'shildi (§19). Asos (skelet, kanban, kartochka, imBox) — amoCRM; sotuv intizomi, prognoz, ketma-ketliklar, playbook va AI qatlami — HubSpot.
> **Til:** izohlar o'zbekcha, kod/jadval/maydon nomlari inglizcha.

---

## 0. AGENT UCHUN ISH QOIDALARI (avval shuni o'qi)

1. **Avval mavjud kodni o'rgan, keyin yoz.** Quyidagilarni topib, qisqa hisobot yoz (`docs/sales/00_discovery.md`):
   - stack (backend framework, ORM, migratsiya vositasi, frontend framework, UI kit, state manager);
   - auth va foydalanuvchi modeli (`users`/`staff`), rollar va ruxsatlar qanday ishlaydi;
   - mavjud `leads`, `lead_stages`, `students`, `groups`, `trial_lessons`, `payments`, `telegram bot`, `auto messages`, `contact_queue` ("Bog'lanish kerak") modullari;
   - fon job / queue / scheduler mexanizmi; websocket yoki realtime bor-yo'qligi;
   - fayl saqlash (S3/local), bildirishnoma tizimi.
2. **Mavjudini qayta ishlat, dublikat qilma.** Foydalanuvchi, rol, filial, kurs, guruh, o'quvchi, to'lov — mavjud jadvallarga FK bilan bog'lan. Yangi auth yozma.
3. **Izolyatsiya.** Hamma yangi narsa `sales` namespace ichida: backend `modules/sales/*` (yoki stackka mos), jadval prefiksi `sales_`, API prefiksi `/api/sales/*`, frontend route `/sales/*`, ruxsatlar `sales.*`. Modulni feature-flag (`SALES_MODULE_ENABLED`) bilan o'chirib qo'yish mumkin bo'lsin.
4. **Mavjud `leads` moduli bilan munosabat:** eski lid modulini o'chirma. Migratsiya skripti eski lidlarni `sales_deals` ga ko'chiradi (§16). Ko'chirish tasdiqlangandan keyin eski "Lidlar" menyusi yangi modulga redirect qilinadi.
5. **Bosqichma-bosqich ishla** (§20 — v2 reja; §17 dagi qabul mezonlari ham amal qiladi). Har bosqich oxirida: migratsiyalar, seed, unit + integration testlar, `docs/sales/CHANGELOG.md`. Keyingi bosqichga o'tishdan oldin acceptance criteria bajarilganini tekshir.
6. **Har bir yozish amali tarixga tushadi** (`sales_events`). Hech qaysi o'zgarish "jim" bo'lmasin.
7. **Soft delete** hamma asosiy obyektlarda (`deleted_at`). Haqiqiy o'chirish faqat admin "Savat"dan.
8. **Vaqt:** DB da UTC, UI da hisob vaqt zonasi (`Asia/Tashkent`, sozlanadi). Pul: butun son (tiyin emas, so'm) `bigint`, valyuta `UZS`.
9. **Tushunmagan joyda** — taxmin qil, `docs/sales/DECISIONS.md` ga yoz va davom et. To'xtab so'rama, faqat ma'lumot yo'qolishi mumkin bo'lgan joyda so'ra.

---

## 1. MODUL TUZILISHI VA NAVIGATSIYA

amoCRM chap menyusi (biz ham xuddi shunday, mavjud CRM menyusida bitta **"Sotuv"** bandi ochiladi va ichida ikkinchi darajali sidebar bo'ladi):

| # | Bo'lim | amoCRM nomi | Route | Qisqa mazmun |
|---|---|---|---|---|
| 0 | **Bugun** (HubSpot Sales Workspace) | — | `/sales/today` | Menejerning ish maydoni: vazifalar navbati, bugungi uchrashuv/sinovlar, javobsiz lidlar, xavfdagi sdelkalar (§19.3). **Menejer uchun default sahifa** |
| 1 | Ish stoli | Рабочий стол | `/sales/dashboard` | KPI vidjetlar, davr/menejer filtri (rahbar uchun default sahifa) |
| 2 | Sdelkalar | Сделки | `/sales/pipeline/:pipelineId` (kanban), `/sales/deals` (ro'yxat) | Voronka |
| 3 | Chatlar | imBox | `/sales/inbox` | Telegram/Instagram/WhatsApp suhbatlari bitta oynada |
| 4 | Vazifalar | Задачи | `/sales/tasks` (kanban-muddat), `/sales/tasks/list`, `/sales/tasks/calendar` | |
| 5 | Ro'yxatlar | Списки | `/sales/contacts`, `/sales/companies`, `/sales/files` | Kontaktlar, kompaniyalar |
| 6 | Analitika | Аналитика | `/sales/analytics/*` | Sotuv tahlili, svodka, xodimlar, hodisalar, qo'ng'iroqlar, maqsadlar |
| 7 | Sozlamalar | Настройки | `/sales/settings/*` | Voronkalar, maydonlar, teglar, avtomatika, integratsiyalar |
| 8 | Ketma-ketliklar | (HubSpot Sequences) | `/sales/sequences` | Ko'p qadamli follow-up zanjirlari (§19.4) |
| 9 | Uchrashuvlar | (HubSpot Meetings) | `/sales/meetings` | Sinov darsi/konsultatsiyaga onlayn yozilish havolalari (§19.5) |
| 10 | Playbooklar | (HubSpot Playbooks) | `/sales/playbooks` | Qo'ng'iroq skriptlari, savollar kartochkada maydonga yoziladi (§19.6) |
| 11 | Prognoz | (HubSpot Forecast) | `/sales/forecast` | Reja vs prognoz, menejer bo'yicha (§19.2) |

Kartochkalar modal emas, alohida route: `/sales/deals/:id`, `/sales/contacts/:id`, `/sales/companies/:id`. Kanban ustidan ochilganda — o'ngdan chiqadigan to'liq ekranli panel, URL o'zgaradi, "orqaga" kanbanga qaytaradi.

Global elementlar (har sahifada):
- **Global qidiruv** (yuqorida): sdelka nomi, kontakt ismi, telefon (raqamning istalgan qismi, formatdan qat'i nazar), email, teg, ID bo'yicha. Natijalar guruhlangan: Sdelkalar / Kontaktlar / Kompaniyalar / Vazifalar.
- **"Hodisalar" (События) paneli** — o'ngdan chiqadi: bildirishnomalar lentasi (yangi lid, menga vazifa berildi, vazifa muddati, kiruvchi xabar, mas'ul o'zgardi). O'qilmaganlar soni sidebar badge da.
- **Sidebar badge lar:** imBox — javob berilmagan suhbatlar soni; Vazifalar — mening muddati o'tgan vazifalarim soni (999+ gacha).
- Tema: `auto | light | dark`.

---

## 2. MA'LUMOTLAR MODELI

Belgilar: `PK` — primary key, `FK→` — tashqi kalit, `?` — nullable. Hamma jadvalda `created_at`, `updated_at`, `deleted_at?` bor (quyida takrorlanmaydi). `account_id`/`branch_id` — agar mavjud CRM multi-filial bo'lsa, hamma jadvalga qo'sh.

### 2.1 Voronka

```
sales_pipelines
  id PK, name, sort_order int, is_main bool, is_unsorted_enabled bool default true,
  is_archived bool default false

sales_statuses                     -- voronka bosqichlari
  id PK, pipeline_id FK→sales_pipelines, name, color (hex), sort_order int,
  type enum('unsorted','regular','won','lost'),
  description text?               -- ustun tepasidagi xodim uchun yo'riqnoma
  sla_minutes int?                -- bosqichda maksimal turish vaqti (oshsa "qotib qolgan")
  is_editable bool                -- unsorted/won/lost = false (o'chirilmaydi, ko'chirilmaydi)
  UNIQUE(pipeline_id, sort_order)
```
Qoidalar:
- Har voronkada majburiy 3 ta tizim bosqichi: `unsorted` (birinchi, faqat `is_unsorted_enabled` bo'lsa ko'rinadi), `won` ("Muvaffaqiyatli"), `lost` ("Yopildi, amalga oshmadi"). Ular o'chirilmaydi, nomi va rangi o'zgartiriladi.
- `regular` bosqichlar orasida tartib drag&drop bilan o'zgaradi.
- Bosqichni o'chirishda ichidagi sdelkalar qaysi bosqichga ko'chirilishini so'ra (majburiy tanlov).

### 2.2 Sdelka (lid)

```
sales_deals
  id PK,
  name                       -- default: "Sdelka #<id>"
  pipeline_id FK, status_id FK
  price bigint default 0      -- byudjet
  responsible_user_id FK→users
  created_by FK→users?        -- null = tizim/integratsiya
  updated_by FK→users?
  source_id FK→sales_sources?
  loss_reason_id FK→sales_loss_reasons?
  loss_comment text?
  closed_at timestamp?        -- won/lost ga o'tganda
  status_changed_at timestamp -- "bu bosqichda N kun" shu ustundan
  first_response_at timestamp?-- birinchi chiqish qo'ng'irog'i/xabar/teg "Telefon qildim"
  last_activity_at timestamp  -- har qanday hodisada yangilanadi (saralash uchun)
  last_message_at timestamp?  -- oxirgi chat xabari (saralash uchun)
  next_task_due_at timestamp? -- denormalizatsiya: eng yaqin ochiq vazifa muddati (badge uchun)
  open_tasks_count int default 0
  score int?                  -- 0..100 (keyingi bosqich)
  utm jsonb?                  -- utm_source, utm_medium, utm_campaign, utm_content, utm_term, referrer, gclid, fbclid, yclid, ...
  campaign_code varchar?
  student_id FK→students?     -- won bo'lganda yaratilgan/bog'langan o'quvchi (integratsiya)
  external_id varchar?        -- amoCRM dagi id (migratsiya uchun), UNIQUE
  INDEX(pipeline_id, status_id, deleted_at), INDEX(responsible_user_id), INDEX(next_task_due_at), INDEX(last_activity_at)
```

### 2.3 Kontakt va Kompaniya

```
sales_contacts
  id PK, name, first_name?, last_name?, responsible_user_id FK,
  position?, created_by?, external_id?

sales_contact_phones   (id, contact_id FK, phone_e164 varchar, type enum('work','mobile','home','other'), is_primary)
  INDEX(phone_e164)     -- dublikat va telefoniya uchun
sales_contact_emails   (id, contact_id FK, email, type, is_primary)
sales_contact_messengers (id, contact_id FK, channel enum('telegram','instagram','whatsapp','facebook'), external_user_id, username?)
  UNIQUE(channel, external_user_id)

sales_companies
  id PK, name, responsible_user_id FK, phone?, email?, web?, address?, external_id?

-- bog'lanishlar (ko'pga-ko'p)
sales_deal_contacts   (deal_id FK, contact_id FK, is_main bool)  PK(deal_id, contact_id)
sales_deal_companies  (deal_id FK, company_id FK)               -- amoCRM: sdelkada 1 ta kompaniya
sales_company_contacts(company_id FK, contact_id FK)
```
Telefon normalizatsiyasi: saqlashda `+998XXXXXXXXX` (E.164). `94 224 26 12`, `998942242612`, `+998 (94) 224-26-12` → `+998942242612`. 9 xonali kiritilsa `+998` qo'shiladi. Qidiruv oxirgi 9 raqam bo'yicha.

### 2.4 Qo'shimcha maydonlar (custom fields)

amoCRM da sdelkada 58 ta, kontaktda ~6 ta qo'shimcha maydon bor. Ular sozlanadigan bo'lishi shart:

```
sales_field_groups  (id, entity_type enum('deal','contact','company'), name, sort_order)
sales_fields
  id PK, entity_type enum('deal','contact','company'),
  group_id FK?, name, code varchar? UNIQUE per entity (API uchun),
  type enum('text','textarea','numeric','checkbox','select','multiselect','date','datetime',
            'url','phone','email','price','radiobutton','birthday','address','tracking'),
  is_required bool, required_statuses int[]?   -- faqat shu bosqichlarga o'tishda majburiy
  is_api_only bool, is_hidden bool, sort_order int
sales_field_options (id, field_id FK, value, sort_order, color?)
sales_field_values
  id PK, field_id FK, entity_type, entity_id,
  value_text text?, value_number numeric?, value_date timestamp?, value_bool bool?,
  option_ids int[]?
  UNIQUE(field_id, entity_type, entity_id)  INDEX(field_id, value_text), INDEX(field_id, value_number)
```
- `required_statuses`: amoCRM dagi "bosqich uchun majburiy maydon". Sdelka shu bosqichga o'tkazilayotganda maydon bo'sh bo'lsa — modal ochilib to'ldirish so'raladi; to'ldirmasa o'tmaydi.
- `tracking` turi — UTM va reklama identifikatorlari (faqat o'qish, API/forma yozadi).
- Kartochkada guruhlar tab ko'rinishida: "Asosiy" + har bir `sales_field_groups`.

### 2.5 Teglar, manbalar, yo'qotish sabablari

```
sales_tags          (id, name, color?, entity_type enum('deal','contact','company'))  UNIQUE(entity_type, lower(name))
sales_entity_tags   (tag_id FK, entity_type, entity_id)  PK(...)
sales_sources       (id, name, channel enum('instagram','telegram','whatsapp','phone','web_form','walk_in','referral','other'), is_active)
sales_loss_reasons  (id, name, sort_order, is_active)
```

### 2.6 Vazifalar

```
sales_task_types  (id, name, icon, color, is_system, sort_order)   -- seed: "Bog'lanish" (Связаться), "Uchrashuv" (Встреча)
sales_tasks
  id PK, type_id FK, text,
  entity_type enum('deal','contact','company')?, entity_id?   -- null = shaxsiy vazifa
  responsible_user_id FK, created_by FK?   -- null = tizim
  created_by_rule_id FK→sales_automation_rules?
  due_at timestamp, is_all_day bool, duration_minutes int default 0,
  status enum('open','done','cancelled') default 'open',
  result_text text?, completed_at?, completed_by?,
  auto_closed_reason varchar?,
  reminded_at timestamp?, snoozed_until timestamp?
  INDEX(responsible_user_id, status, due_at), INDEX(entity_type, entity_id, status)
```

### 2.7 Lenta (timeline): izohlar, hodisalar, qo'ng'iroqlar, xabarlar

```
sales_notes        -- foydalanuvchi yozgan izoh yoki tizim izohi
  id PK, entity_type, entity_id, author_id?, type enum('common','call_in','call_out','sms_in','sms_out',
  'service','attachment','geo'), text?, params jsonb?, is_pinned bool
  -- call_*: params = {phone, duration_sec, status, recording_url, provider, call_id}

sales_events       -- avtomatik audit/hodisa (HECH QACHON o'chirilmaydi)
  id PK, entity_type, entity_id, actor_id?  (null = tizim/robot), actor_label? ("SalesBot","UTel","Sipuni")
  type varchar  -- deal_created, status_changed, responsible_changed, field_changed, price_changed,
                -- tag_added, tag_removed, contact_linked, contact_unlinked, task_created, task_completed,
                -- task_deadline_changed, note_added, incoming_call, outgoing_call, missed_call,
                -- incoming_message, outgoing_message, deal_merged, deal_restored, deal_deleted, ...
  value_before jsonb?, value_after jsonb?, created_at
  INDEX(entity_type, entity_id, created_at), INDEX(type, created_at), INDEX(actor_id, created_at)

sales_attachments  (id, entity_type, entity_id, note_id?, file_url, file_name, size, mime, uploaded_by)
```

### 2.8 imBox (chatlar)

```
sales_channels       (id, type enum('telegram_bot','instagram','whatsapp','facebook','web_chat'),
                      name, credentials_encrypted, is_active, default_pipeline_id, default_responsible_user_id?)
sales_conversations
  id PK, channel_id FK, contact_id FK?, deal_id FK?, external_chat_id,
  number varchar        -- "A24933" (prefiks + autoincrement), UI da "Suhbat № A24933"
  status enum('open','closed','no_reply_needed'),
  last_message_at, last_incoming_at, last_outgoing_at, unread_count int,
  responsible_user_id FK?, first_reply_seconds int?
sales_messages
  id PK, conversation_id FK, direction enum('in','out'), author_user_id?, is_bot bool,
  text?, attachments jsonb?, external_message_id, delivered_at?, read_at?, created_at
```

### 2.9 Avtomatika, maqsadlar, sozlamalar

```
sales_automation_rules
  id PK, name, is_active, pipeline_id FK, status_id FK?   -- qaysi ustunga biriktirilgan
  trigger enum('status_entered','deal_created','status_idle','responsible_changed',
               'field_changed','task_overdue','incoming_message','incoming_call','missed_call','tag_added')
  conditions jsonb          -- [{field:'source_id',op:'in',value:[..]}, {field:'tag',op:'has',value:'TG/INST → CC'}]
  delay_minutes int default 0, only_working_hours bool
  action enum('create_task','change_status','change_responsible','set_field','add_tag','remove_tag',
              'send_message','notify_user','move_to_pipeline','close_open_tasks','webhook','start_bot')
  action_params jsonb
  sort_order int
sales_automation_runs (id, rule_id, deal_id, scheduled_at, executed_at?, status enum('pending','done','skipped','failed'), error?)

sales_goals  -- v2: §19.2 dagi sales_quotas bilan almashtiriladi
                     (id, user_id? (null=jamoa), period enum('month','quarter'), period_start date,
                      target_amount bigint?, target_count int?)
sales_settings       (key PK, value jsonb)   -- working_hours, timezone, currency, conversation_prefix, ...
sales_saved_filters  (id, user_id?, entity, name, params jsonb, is_shared)
sales_user_presence  (user_id, date, active_seconds int)   -- "Tizimdan foydalanish" vidjeti
sales_dashboard_layouts (user_id? , widgets jsonb)
```

---

## 3. RUXSATLAR (amoCRM "Права доступа" analogi)

Har rol uchun har obyekt (`deal`, `contact`, `company`, `task`) bo'yicha 4 amal × 3 qamrov:

| Amal | Qamrov variantlari |
|---|---|
| `view` | `none` / `own` (responsible = men) / `group` (mening jamoam) / `all` |
| `create` | `none` / `all` |
| `edit` | `none` / `own` / `group` / `all` |
| `delete` | `none` / `own` / `group` / `all` |
| `export` | `none` / `all` |

Qo'shimcha flaglar: `sales.settings.manage`, `sales.analytics.view_all`, `sales.bulk_actions`, `sales.inbox.view_all`, `sales.pipelines.access` (qaysi voronkalarni ko'radi — ro'yxat). Har voronka va har bosqich uchun alohida cheklov bo'lishi mumkin ("faqat shu bosqichdagi sdelkalarni ko'radi").

Default rollar (seed): **Admin** (hammasi `all`), **Rahbar** (hammasi `all`, sozlamalarsiz), **Menejer** (`view/edit: own`, `create: all`, `delete: none`, `export: none`). Mavjud CRM rollariga mapping `sales_role_permissions(role_id, ...)` orqali.

Backend: har so'rovda qamrov filtri query darajasida qo'llanadi (UI da yashirish yetarli emas). Testlar: menejer boshqaning sdelkasini API orqali ham ololmasligi kerak.

---

## 4. DASHBOARD (Ish stoli)

### 4.1 Sarlavha va filtrlar
- Yuqorida hisob nomi (katta, markazda; masalan "BRIGHT FUTURE"), fon rasmi sozlanadi (`sales_settings.dashboard_background`).
- **Davr tugmalari:** `Bugun | Kecha | Hafta | Oy | Davr (sana oralig'i)`. Default — Bugun.
- **Kim:** `Hammasi | Meniki` (+ kengaytirilganda aniq menejer yoki jamoa tanlash). Ruxsati `view: own` bo'lgan xodim uchun majburan "Meniki".
- **Voronka:** URL da `pipeline_id` (barcha yoki bitta voronka).
- **"Sozlash" tugmasi:** vidjetlarni qo'shish/olib tashlash, drag&drop bilan joyini o'zgartirish, o'lchami (1×1, 2×1, 2×2). Layout `sales_dashboard_layouts` da (foydalanuvchi bo'yicha, admin default layout belgilaydi).
- URL formati: `/sales/dashboard?sel=all|my|user:<id>&period=day|yesterday|week|month|custom&from=&to=&pipeline_id=`.
- Har vidjet bosilganda — o'sha filtr bilan tegishli ro'yxatga o'tadi (masalan "Muddati o'tgan vazifalar" → `/sales/tasks?filter=overdue&user=...`).

### 4.2 Vidjetlar (amoCRM dagi aynan shu to'plam) va hisoblash formulalari

`P` = tanlangan davr, `U` = tanlangan xodimlar, `PL` = tanlangan voronka. Har kichik vidjetda: katta raqam + pastda "за сегодня" (davr ichidagi o'zgarish, ±, rangli).

| # | Vidjet | Asosiy raqam | Pastki raqam (delta) | Bosilganda |
|---|---|---|---|---|
| 1 | **Muddati o'tgan vazifalar** | `count(tasks where status=open and due_at < now and responsible in U)` | davr ichida o'zgarish (bugun yangi kechikkan − bugun yopilgan kechikkanlar) | Vazifalar, "muddati o'tgan" |
| 2 | **Bajarilgan vazifalar** | `count(tasks where status=done and completed_at in P)` | — | Vazifalar, "bajarilgan" |
| 3 | **Bajariladigan vazifalar** | `count(tasks where status=open)` (barcha ochiq) | delta | Vazifalar |
| 4 | **Vazifasiz sdelkalar** | `count(deals where open and open_tasks_count=0)` + ularning `sum(price)` so'mda | delta | Sdelkalar, filtr "Vazifasiz" |
| 5 | **Sdelkalar manbalari** | doira (donut) diagramma: davrda yaratilgan sdelkalar `source_id` bo'yicha | — | Sdelkalar, manba filtri |
| 6 | **Menejerlar bo'yicha sdelkalar** | jami ochiq sdelkalar soni; ostida har menejer: ism, "2954 sdelka (0 so'm)", progress chizig'i | davrda yangi | Sdelkalar, mas'ul filtri |
| 7 | **Maqsadlar** (Цели) | tab: `Byudjet bo'yicha | Soni bo'yicha`; doiraviy progress 0–100%, "X / Y" | — | Analitika → Maqsadlar |
| 8 | **Tizimdan foydalanish** | har xodim: avatar, ism, bugun faol vaqt ("19 daq"), progress | — | Analitika → Xodimlar |
| 9 | **Sotuv prognozi** | ishdagi sdelkalar summasi: hozir / 20 kundan keyin / 40 / 60 kundan keyin (§4.3) | — | Sotuv tahlili |
| 10 | **Kiruvchi qo'ng'iroqlar** | `count(notes type=call_in, created in P)`; mini grafik | delta | Analitika → Qo'ng'iroqlar |
| 11 | **Chiquvchi qo'ng'iroqlar** | `count(notes type=call_out)` | delta | — |
| 12 | **Izohlar** | `count(notes type=common, in P)` | delta | Hodisalar ro'yxati |
| 13 | **Muvaffaqiyatli sdelkalar** | `count, sum(price)` where status.type=won and closed_at in P | delta | Sdelkalar, "muvaffaqiyatli" |
| 14 | **Ishlangan suhbatlar** | `count(conversations closed or replied in P)` | — | imBox |
| 15 | **Menejerlar bo'yicha chiquvchi xabarlar** | har menejer: `count(messages direction=out, author=user, in P)` | — | imBox |
| 16 | **Kiruvchi xabarlar** | `count(messages direction=in, in P)` | — | imBox |
| 17 | **O'rtacha javob vaqti** | `avg(first_reply_seconds)` davrda boshlangan suhbatlar bo'yicha, "5 daq 12 s" | — | imBox |
| 18 | **Uzoq javob kutayotgan** | `count(conversations where status=open and last_incoming_at > last_outgoing_at and now - last_incoming_at > 15 min)` | — | imBox, filtr |
| 19 | **Maxsus hisoblagich** (custom) | admin yaratadi: nom + saqlangan filtr (masalan "QO'NG'IROQLAR SONI" — har menejer bo'yicha; "UMUMIY AKTIV LIDLAR") | — | o'sha filtr |

Vidjet #19 — "Custom counter": `sales_dashboard_layouts.widgets[]` ichida `{type:'counter', title, entity:'deals'|'tasks'|'notes'|'events', saved_filter_id|filter_json, group_by:'responsible'|null}`. amoCRM hisobidagi "QO'NG'IROQLAR SONI" vidjeti aynan shunday: har menejer (shu jumladan "Пользователь удален" — o'chirilgan xodimlar ham ko'rinadi) bo'yicha son.

### 4.3 Sotuv prognozi formulasi
Har ochiq sdelka uchun bosqich konversiyasi `p(status) = won_from_status / total_left_status` (oxirgi 90 kun tarixidan, `sales_events.status_changed` asosida). O'rtacha bosqichdan won gacha vaqt `t(status)`. Prognoz `N` kundan keyin = `Σ price × p(status)` faqat `t(status) ≤ N` bo'lganlar uchun. Ma'lumot yetarli bo'lmasa `-/-` ko'rsat (amoCRM ham shunday qiladi).

### 4.4 Texnik
- Endpoint: `GET /api/sales/dashboard?widgets=...&period=...&sel=...&pipeline_id=` → har vidjet uchun `{value, delta, series?, breakdown?}`.
- Og'ir agregatlar uchun 60 soniyalik kesh (kalit = filtrlar + user scope). "Bugun" uchun kesh 30 s.
- Ruxsat qamrovi (own/group/all) barcha hisoblarga qo'llanadi.

---

## 5. VORONKA — SDELKALAR BO'LIMI

### 5.1 Sarlavha paneli
Chapdan o'ngga: **voronka nomi** (bosilsa ro'yxat — voronkalar orasida almashish; "+ voronka qo'shish" adminlarga) · ko'rinish almashtirgich `Kanban | Ro'yxat` · **"Qidiruv va filtr"** maydoni · o'ngda "2938 sdelka: 0 so'm" (filtrlangan jami son va summa) · `⋯` menyu · **"Yangi sdelka"** tugmasi.

`⋯` menyu: `Ommaviy amallar` (tanlash rejimini yoqadi) · `Saralash`: oxirgi xabar bo'yicha / oxirgi hodisa bo'yicha / yaratilgan sana / nom / byudjet (har biri ↑↓) · `Import` · `Eksport (CSV/XLSX)` · `Voronkani sozlash` · `Dublikatlarni topish`.

### 5.2 Kanban
- Har bosqich = ustun. Ustun sarlavhasi: **NOM (katta harf)**, ostida "95 sdelka: 0 so'm", ostida bosqich rangidagi chiziq. Sarlavha yonida `ⓘ` — bosqich izohi (description) popover.
- **"Saralanmagan" (unsorted) ustuni** birinchi: integratsiyadan kelgan (qo'ng'iroq, forma, chat) murojaatlar. Har kartada ikki tugma: **"Qabul qilish"** (1-regular bosqichga o'tadi, mas'ul = bosgan xodim yoki qoidaga ko'ra) va **"Rad etish"** (o'chiriladi/`lost`ga, sabab so'raladi). Ichida turi ko'rinadi: qo'ng'iroq / forma / chat ("Kategoriya" filtri shu).
- Oxirida yig'ilgan **Won** va **Lost** ustunlari (default yashirin; filtr "Muvaffaqiyatli"/"Amalga oshmagan" tanlansa ko'rinadi). Sudrash paytida pastda 3 ta zona chiqadi: **"Muvaffaqiyatli"**, **"Amalga oshmadi"**, **"O'chirish"** (+ "Boshqa voronkaga ko'chirish").
- **Infinite scroll** har ustunda alohida (50 tadan yuklash).
- Realtime: boshqa xodim sdelkani ko'chirsa, karta jonli ko'chadi (websocket; bo'lmasa 30 s polling).

**Kanban kartasi** (aynan amoCRM tartibida):
```
┌──────────────────────────────────────┐
│ Kontakt 947490033          16.10.2026│  ← asosiy kontakt nomi (qora)    | yaratilgan/oxirgi hodisa sanasi
│ Sdelka #43988543                     │  ← sdelka nomi (havola rangida)
│ Robiya                               │  ← mas'ul
│ [Propushenniy zvonok] [TG/INST → CC] │  ← teglar (kulrang chip, max 3 + "+N")
│ Utel  552037171                 19дн●│  ← o'ng past: vazifa indikatori
└──────────────────────────────────────┘
```
Vazifa indikatori (o'ng pastki burchak):
- `●` sariq nuqta — **ochiq vazifa yo'q**;
- `19дн ●` qizil — eng eski ochiq vazifa 19 kun kechikkan;
- yashil `Bugun 15:00` / ko'k `Ertaga` — kelgusi vazifa;
- kursor olib borilganda tooltip: vazifa matni va muddati.
Byudjet > 0 bo'lsa karta ostida "1 500 000 so'm". Karta ustiga sichqoncha bilan — tezkor tugmalar: "Vazifa qo'shish", "Qo'ng'iroq", "Xabar".

**Drag & drop qoidalari:**
1. Ruxsat tekshiruvi (`edit`).
2. Maqsad bosqichda `required_statuses` maydonlari bo'sh bo'lsa → modal, to'ldirmasa bekor.
3. `won`ga tashlansa → byudjetni tasdiqlash modali (ixtiyoriy) + (integratsiya) "O'quvchi yaratish?" taklifi (§14.1).
4. `lost`ga tashlansa → **yo'qotish sababi majburiy** (ro'yxatdan) + izoh.
5. Muvaffaqiyatli o'tishda: `status_id`, `status_changed_at = now`, `sales_events(status_changed, before, after)`, avtomatika triggerlari (`status_entered`), `closed_at` (won/lost bo'lsa).
6. Bosqichga o'tgandan keyin sdelkada ochiq vazifa bo'lmasa — toast: "Keyingi vazifani qo'ying" (tugma bilan).

### 5.3 Ro'yxat ko'rinishi
Jadval: checkbox · Nomi · Asosiy kontakt · Kompaniya · Bosqich (rangli chip) · Byudjet · Mas'ul · Yaratilgan · Oxirgi o'zgarish · Keyingi vazifa · teglar · + istalgan qo'shimcha maydon. Ustunlarni yoqish/o'chirish va tartibini o'zgartirish (foydalanuvchi bo'yicha saqlanadi). Ustun sarlavhasini bosish — saralash. Inline tahrirlash (mas'ul, bosqich, byudjet). Sahifalash 50/100/250.

### 5.4 Qidiruv va filtr paneli (amoCRM dagi aynan shu tuzilma)
Panel 3 ustunli:

**Chap — tayyor filtrlar (presets):**
`Ochiq sdelkalar` (default) · `Faqat mening sdelkalarim` · `Muvaffaqiyatli yakunlangan` · `Amalga oshmagan` · `🟡 Vazifasiz sdelkalar` · `🔴 Vazifasi muddati o'tgan` · + foydalanuvchi saqlagan filtrlar (masalan "Hamma etap"). Har saqlangan filtrni o'chirish/qayta nomlash; `is_shared` bo'lsa hammaga ko'rinadi.

**O'rta — sdelka xossalari:**
Nomi · Sana (`Barcha vaqt` / bugun / kecha / hafta / oy / oraliq + qaysi sana: yaratilgan, yopilgan, o'zgargan, bosqichga o'tgan) · Bosqichlar (`Faol bosqichlar` yoki aniq bosqichlar multi-select, voronkalar bo'yicha guruhlangan) · Saralanmagan kategoriyasi · Sdelka manbasi · Menejerlar · Kim yaratgan · Kim o'zgartirgan · Vazifalar (`Barcha` / vazifasiz / muddati o'tgan / bugun / ertaga / shu hafta) · Byudjet (dan–gacha) · **har bir qo'shimcha maydon** (turiga mos input: select → multi-select, date → oraliq, numeric → dan–gacha, text → "o'z ichiga oladi") · UTM maydonlari.

**O'ng — teglar:** qidiruv + barcha teglar soni bilan ("Prinyat 2621", "Propushenniy zvonok 2151" ...), bosish bilan multi-tanlash (AND/OR almashtirgich).

Pastda: `Qo'llash` · `Tozalash` · `Saqlash` (nom bilan). Faol filtr qidiruv maydonida chip ko'rinishida ko'rinadi. Filtr parametrlari URL query da bo'ladi (havola bilan ulashsa bo'ladi).

Backend: `GET /api/sales/deals?filter[...]` — barcha filtrlar server tarafda; custom field filtrlari `sales_field_values` ga JOIN/EXISTS bilan.

### 5.5 Ommaviy amallar
Tanlash rejimi (kartalarda checkbox; "ustundagi hammasini tanlash"; "filtrga mos hammasini tanlash — 2163 ta"). Pastdan panel: **Mas'ulni o'zgartirish** · **Bosqichni o'zgartirish** (boshqa voronkaga ham) · **Vazifa qo'yish** · **Teg qo'shish / olib tashlash** · **Maydonni tahrirlash** · **Xabar yuborish** (shablon, kanal) · **Birlashtirish** (2–3 ta) · **Eksport** · **O'chirish**. Har amal fon jobida, progress ko'rsatiladi, har sdelkaga alohida `sales_events` yoziladi. Faqat `sales.bulk_actions` ruxsati bilan.

### 5.6 Yangi sdelka yaratish
"Yangi sdelka" → bo'sh kartochka ochiladi (yoki kanban ustun tepasida "Tezkor qo'shish": nom, kontakt ismi, telefon, byudjet). Telefon kiritilganda **dublikat tekshiruvi**: shu telefonli kontakt bor bo'lsa — "Mavjud kontakt: Ali Valiyev (2 ta sdelka). Bog'lansinmi?" taklifi.

---

## 6. MIJOZ BILAN ISHLASH — SDELKA KARTOCHKASI

Ekran 2 qismga bo'linadi: **chap panel (~380px, maydonlar)** va **o'ng qism (lenta + yozish maydoni)**.

### 6.1 Chap panel — sarlavha
```
‹  Sdelka 998940325512                         ⋯
   #46661313   [Ne dozvonilis]   [+ teg]
   Call center
   Propushen Zvonok  (19 kun)
   ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬░░░░░░░   ← bosqichlar progress chizig'i (har segment = bosqich rangi)
   Asosiy | <guruh 1> | <guruh 2> | API | Statistika
```
- Nom — bosib tahrirlanadi.
- `#id`, teglar (bosilsa qo'shish/o'chirish, autocomplete).
- Voronka nomi + **bosqich** (bosilsa dropdown: shu voronka bosqichlari + boshqa voronkalar; o'tish §5.2 qoidalari bilan) + `(19 kun)` = `now − status_changed_at`.
- Progress chizig'i — joriy bosqichgacha to'ldirilgan.
- `⋯` menyu: Chop etish · Dublikatlarni birlashtirish · Nusxa olish · O'chirish · Hodisalar tarixini eksport.
- Tablar: **Asosiy**, har bir maydon guruhi, **API** (id, external_id, yaratilish manbasi, webhook ma'lumotlari), **Statistika** (§6.6).

### 6.2 Chap panel — "Asosiy" tab
- **Mas'ul** (select, xodimlar ro'yxati; o'zgartirilsa ochiq vazifalarni ham ko'chirish so'raladi).
- **Byudjet** (so'm, ming ajratgich bilan).
- Asosiy guruhdagi qo'shimcha maydonlar (bo'sh qiymat `...` ko'rinishida, bosilsa inline tahrir; `Enter` saqlaydi, `Esc` bekor). Har o'zgarish → `sales_events(field_changed)`.
- **Kontaktlar bloki:** asosiy kontakt kartasi — avatar, ism (`⋯` menyu: kontaktni ochish, uzish, asosiy qilish), Ish telefoni (bosilsa qo'ng'iroq — telefoniya click-to-call yoki `tel:`), Email, Lavozim, kontaktning qo'shimcha maydonlari. "+ Kontakt qo'shish" (qidiruv yoki yangi). Bir nechta kontakt — yig'ilgan ro'yxat (dropdown "Kontakt 998940325512 ▾").
- **Kompaniya bloki:** shunga o'xshash.
- **Kontaktning boshqa sdelkalari:** "Barcha muvaffaqiyatli sdelkalar summasi: 0 so'm", ostida voronkalar bo'yicha guruhlangan boshqa sdelkalar (bosqich rangli nuqta, nom, mas'ul, summa). Bu dublikat lidlarni darrov ko'rsatadi.

### 6.3 O'ng qism — lenta (timeline)
Xronologik (eski yuqorida, yangi pastda, ochilganda eng pastga scroll). Kun bo'yicha ajratgichlar ("Sentabr"). Yuqorida filtr: `Hammasi | Izohlar | Qo'ng'iroqlar | Chat | Vazifalar | Tizim hodisalari` + lenta ichida qidiruv. Uzun tizim hodisalari ketma-ketligi yig'iladi: "Mas'ul o'zgarishi: 2 hodisa [Yoyish]".

Lenta element turlari va ko'rinishi:
| Tur | Ko'rinish |
|---|---|
| Kiruvchi/chiquvchi qo'ng'iroq | ikonka 📞, "16.10.2026 16:33 Kiruvchi qo'ng'iroq: 940325512 dan Andijon ga", davomiylik "00:00", tugmalar **[Tinglash]** (inline audio pleyer) **[Yuklab olish]**, natija matni ("Keyinroq qo'ng'iroq qiling: o'tkazib yuborilgan qo'ng'iroq"), o'ngda "Kontakt 998940325512" havola |
| Bosqich o'zgarishi | bir qatorli kulrang: "Missed call Yangi bosqich: [Call center][Propushen Zvonok] ← Kiruvchi qo'ng'iroq dan" (bosqich chiplari rangli) |
| Maydon o'zgarishi | "SalesBot (Lid bo'lib berilgan sana…) maydoniga qiymat o'rnatildi «…»" |
| Izoh (note) | avtor avatari, matn, fayllar, `⋯` (tahrirlash/o'chirish/qadab qo'yish) |
| Chat xabari | pufakcha ko'rinishida, kanal ikonkasi (Telegram/Instagram), o'qildi belgisi |
| Vazifa (ochiq) | **ramkali blok** (muddati o'tgan bo'lsa qizil ramka): "16.10.2026 16:34 **(19 kun)** Sipuni dan Andijon uchun", "🔄 **Bog'lanish** — 998940325512", pastida `[Natija qo'shish ______] [Bajarish]` |
| Vazifa (bajarilgan) | yig'ilgan: ✓ turi, matn, natija, kim bajardi |

Ochiq vazifalar **doim lentaning eng pastida** (yozish maydonidan darrov tepada) turadi.

**Vazifani bajarish oqimi:** natija yoziladi → "Bajarish" → vazifa `done`, `result_text` saqlanadi, lentaga "Vazifa bajarildi: …" → darhol taklif oynasi **"Keyingi vazifa?"** (tur, muddat tez tugmalari: *Bugun · Ertaga · Hafta oxiri · 1 haftadan keyin · Sana/soat*). Natija maydoni bo'sh bo'lsa "Bajarish" faol emas (biznes qoidasi §15).

### 6.4 O'ng qism — yozish maydoni (composer)
Pastda turadi: `[Chat ▾] uchun [hammasi ▾]: matn kiriting…`
- Birinchi dropdown — rejim: **Chat** (mijozga xabar — ulangan kanal orqali) · **Izoh** (ichki, mijoz ko'rmaydi) · **Vazifa** · **SMS** (agar ulangan) · **Email** (agar ulangan).
- Ikkinchi dropdown — Chat rejimida: qabul qiluvchi/kanal ("hammasi" = suhbat ishtirokchilari, yoki aniq Telegram/Instagram suhbati); Vazifa rejimida: **muddat** (tez tugmalar) + **mas'ul** + **tur**.
- Fayl biriktirish, emoji, **shablonlar** (`/` yozilsa shablonlar ro'yxati), `@` bilan xodimni eslatish (izohda — bildirishnoma yuboradi).
- "Ishtirokchilar: 0" — suhbatga ulangan xodimlar soni (o'ngda).
- `Ctrl+Enter` — yuborish.

### 6.5 Dublikatlar va birlashtirish
`⋯ → Birlashtirish`: 2–3 ta sdelka/kontakt tanlanadi → ustunli jadval, har maydon uchun qaysi qiymat qolishini radio bilan tanlash → natija: bitta yozuv, barcha lenta/vazifa/chat/kontakt bog'lanishlari ko'chiriladi, eskilari `merged_into_id` bilan soft-delete, `sales_events(deal_merged)`. ("merged" tegi amoCRM hisobida 372 ta — bu jarayon ko'p ishlatiladi.)

### 6.6 "Statistika" tab
Sdelka umri: yaratilgan sana, har bosqichda o'tkazilgan vaqt (jadval: bosqich — kirdi — chiqdi — davomiylik), qo'ng'iroqlar soni (kiruvchi/chiquvchi/o'tkazib yuborilgan), xabarlar soni, vazifalar (bajarilgan/kechiktirilgan), birinchi javob vaqti.

### 6.7 Kontakt va Kompaniya kartochkalari
Xuddi shu 2 panelli tuzilma. Chapda: ism, mas'ul, telefonlar (bir nechta, turi bilan), email, messenjerlar, lavozim, kompaniya, qo'shimcha maydonlar, **barcha sdelkalari** (voronka bo'yicha, "+ Sdelka qo'shish"). O'ngda: kontaktning o'z lentasi + uning barcha sdelkalari lentasini birlashtirib ko'rsatish almashtirgichi.

---

## 7. VAZIFALAR BO'LIMI

### 7.1 Ko'rinishlar (sarlavhada almashtirgich)
1. **Muddat kanbani (default, amoCRM `/todo/line`)** — 3 ustun: **MUDDATI O'TGAN** (qizil chiziq) · **BUGUNGI** (yashil) · **ERTANGI** (kulrang); ustun tepasida son ("1509 vazifa"). Karta: obyekt nomi ("Kontakt 505044841" — havola), muddat sana+soat (o'tgan bo'lsa qizil) + ", Sipuni dan Robiya uchun" (kim qo'ydi → kimga), pastda tur ikonkasi + "Bog'lanish: 998505044841". Kartani boshqa ustunga sudrash = muddatni o'sha kunga ko'chirish.
2. **Ro'yxat** — guruhlar: Muddati o'tgan → Bugun → Ertaga → Shu hafta → Keyinroq → Sanasiz; ustunlar: muddat, tur, matn, obyekt, mas'ul, kim qo'ygan, natija.
3. **Taqvim** — `KUN | HAFTA | OY` (sarlavhadagi tugmalar). Soat bo'yicha joylashadi, `duration_minutes` balandlikni belgilaydi. Drag bilan vaqtni o'zgartirish.

### 7.2 Filtr
Mas'ul · Tur · Holat (bajarilgan/bajarilmagan/bekor) · Muddat oralig'i · Kim qo'ygan (xodim/tizim/integratsiya) · Obyekt turi (sdelka/kontakt/kompaniya/shaxsiy) · Voronka/bosqich (sdelka vazifalari uchun) · matn bo'yicha qidiruv.

### 7.3 Amallar
- **"Vazifa qo'shish"** modal: tur, matn, obyekt (qidiruv bilan tanlash yoki bo'sh = shaxsiy), mas'ul, muddat (tez tugmalar + soat; "butun kun" checkbox), davomiylik.
- Kartadan bir bosishda "Bajarish" (natija modali bilan) va obyekt kartochkasiga o'tish.
- Ommaviy: bajarish, mas'ulni o'zgartirish, muddatni surish (+1 kun, +1 hafta, sana), bekor qilish.

### 7.4 Eslatmalar
- Muddatdan 15 daqiqa oldin (sozlanadi) — mas'ulga brauzer bildirishnomasi + "Hodisalar" paneli + (ixtiyoriy) xodimning Telegram botiga xabar: obyekt nomi, bosiladigan telefon, matn, tugmalar `✅ Bajarildi` / `⏰ 1 soatga qoldirish` (callback → API).
- 24 soat kechiksa — rahbarga xabar.
- Har kuni 09:00 da xodimga kunlik ro'yxat (muddati o'tgan + bugungi).

---

## 8. imBOX — CHATLAR

Uch ustunli ekran:
1. **Chap — suhbatlar ro'yxati:** tepada "Filtr" va "Jami: 2885". Har qator: avatar (kanal ikonkasi bilan), **suhbat raqami chip** ("A24933", yashil), ism/username, oxirgi xabar qisqa matni (fayl bo'lsa 📄 nomi), sana-vaqt. O'qilmagan — qalin + badge. Tartib: `last_message_at desc`.
   Filtrlar: Holat (ochiq / javob kutmoqda / yopilgan / javob shart emas) · Kanal · Mas'ul · Sana · "Javob berilmagan > N daq" · Teg.
2. **O'rta — suhbat:** sarlavhada ism + `[TEGLASH]` tugmasi; xabarlar pufakcha ko'rinishida, kun ajratgichlari; tizim xabarlari ("Robot: maydonga qiymat o'rnatildi…"); suhbat boshlanishida kanal kartochkasi ("Bright Future" — qaysi bot/sahifa). Suhbat oxirida tugmalar: **[Yopish]** · **[Javob shart emas]** · "Suhbat № A24933". Pastda yozish maydoni (matn, fayl, shablon, emoji).
3. **O'ng — kontekst panel** (`☰` tugma bilan ochiladi): bog'langan kontakt va sdelka qisqa kartochkasi (bosqich, mas'ul, byudjet, asosiy maydonlar), "Sdelka yaratish" / "Mavjud sdelkaga bog'lash", sdelka vazifalari.

Qoidalar:
- Yangi kiruvchi xabar, kontakt topilmasa: yangi kontakt (`messengers` bilan) + kanal sozlamasidagi voronkaning **Saralanmagan** bosqichida yangi sdelka (kategoriya = chat). Kontakt bor, ochiq sdelka bor — xabar o'sha sdelkaga tushadi; ochiq sdelka yo'q — yangi sdelka (sozlanadi).
- Xabar sdelka lentasida ham ko'rinadi (bitta manba — `sales_messages`, ikki joyda render).
- `first_reply_seconds` = birinchi chiquvchi xabar − suhbat ochilgan birinchi kiruvchi xabar (ish vaqti hisobga olinadi — sozlama).
- "Yopish" — status `closed`; yangi kiruvchi xabar kelsa avtomatik `open`.
- Kanal adapterlari interfeysi: `ChannelAdapter { receiveWebhook(payload) → NormalizedMessage; send(conversation, message) → externalId; }`. Birinchi bo'lib **Telegram bot** (mavjud CRM botini qayta ishlat), keyin Instagram (Meta Messaging API), WhatsApp (Cloud API).

---

## 9. RO'YXATLAR (Kontaktlar, Kompaniyalar, Fayllar)

- `/sales/contacts` — sarlavha "BARCHA KONTAKTLAR VA KOMPANIYALAR" (almashtirgich: Hammasi / Kontaktlar / Kompaniyalar), "23919 element", qidiruv va filtr, `⋯` (import, eksport, ommaviy amallar, dublikatlar), "Kontakt qo'shish".
- Jadval: checkbox · Nomi (havola; yonida "+3" — bog'langan sdelkalar soni chip) · Kompaniya · Telefon (bosiladigan) · Email · Mas'ul · Yaratilgan · teglar · qo'shimcha maydonlar. Ustunlar sozlanadi.
- Filtr: nomi, telefon, email, mas'ul, yaratilgan sana, teglar, "sdelkasi bor/yo'q", qo'shimcha maydonlar.
- **Dublikatlarni topish** — telefon (E.164), email, nom bo'yicha guruhlab ko'rsatadi; har guruhda "Birlashtirish".
- **Import:** CSV/XLSX yuklash → ustunlarni maydonlarga moslash (mapping ekrani) → dublikat strategiyasi (o'tkazib yuborish / yangilash / yangi yaratish) → fon jobi → hisobot (qo'shildi/yangilandi/xato qatorlar fayli).
- **Fayllar** — barcha biriktirilgan fayllar (obyekt, kim, qachon, hajm), filtr va qidiruv.

---

## 10. ANALITIKA

Sidebar menyu (amoCRM dagi aynan shu 6 band):

| Hisobot | Mazmun | Filtrlar |
|---|---|---|
| **Sotuv tahlili** (Анализ продаж) | Voronka diagrammasi: har bosqichda sdelkalar soni va summasi, bosqichdan bosqichga konversiya %, o'rtacha bosqichda turish vaqti; won/lost soni va summasi; yo'qotish sabablari bo'yicha taqsimot | davr, voronka, menejer, manba, teg, UTM |
| **Svodka hisobot** (Сводный отчёт) | Jadval: qatorlar = menejerlar (yoki manba/oy), ustunlar = yangi / ishda / muvaffaqiyatli / amalga oshmagan (soni va summasi), konversiya, o'rtacha chek, o'rtacha sotuv sikli (kun) | davr, voronka, guruhlash |
| **Xodimlar hisoboti** (Отчёт по сотрудникам) | Har xodim: yaratilgan sdelkalar, bosqich o'zgartirishlari, qo'ng'iroqlar (kir/chiq/davomiylik), xabarlar, izohlar, vazifalar (qo'yilgan/bajarilgan/muddatida %/kechikkan), birinchi javob o'rtacha vaqti, tizimda faol vaqt | davr, xodim |
| **Hodisalar ro'yxati** (Список событий) | `sales_events` jadvali: Sana · Avtor · Obyekt (havola) · Nomi · Hodisa · Qiymat oldin · Qiymat keyin. "301370 hodisa". Kuchli filtr (avtor, obyekt turi, hodisa turi, sana) va eksport | |
| **Qo'ng'iroqlar** | Qo'ng'iroqlar jurnali: sana, yo'nalish, raqam, kontakt/sdelka, xodim, davomiylik, holat (javob berildi/o'tkazib yuborildi), yozuvni tinglash; xodim va soat bo'yicha agregatlar; o'tkazib yuborilib, qayta qo'ng'iroq qilinmaganlar ro'yxati | davr, xodim, yo'nalish, holat |
| **Maqsadlar** (Цели) | Oy/chorak uchun xodim va jamoa rejasi (summa va/yoki son) — kiritish jadvali + bajarilish % | davr |

Hammasi uchun: grafik + jadval, XLSX eksport, ruxsat qamrovi (`analytics.view_all` bo'lmasa faqat o'zi).

---

## 11. AVTOMATLASHTIRISH (Digital Pipeline + oddiy SalesBot)

### 11.1 Interfeys
`/sales/settings/automation/:pipelineId` — voronka bosqichlari **ustunlar** ko'rinishida (kanban kabi), har ustun ostida shu bosqichga biriktirilgan qoidalar kartalari va "+ Qoida qo'shish". Ustunlar ustida umumiy satr: "Barcha bosqichlar uchun" qoidalari (masalan `deal_created`).

Qoida muharriri: **Qachon** (trigger) → **Shart** (filtr quruvchi, §5.4 dagi maydonlar) → **Kechikish** (darhol / N daq/soat/kun / aniq vaqtda; "faqat ish vaqtida") → **Amal** (parametrlari bilan) → Faol/Nofaol.

### 11.2 Triggerlar va amallar
Triggerlar: bosqichga o'tganda · sdelka yaratilganda · bosqichda N vaqt turganda (`status_idle`) · mas'ul o'zgarganda · maydon o'zgarganda · teg qo'shilganda · vazifa muddati o'tganda · kiruvchi xabar · kiruvchi qo'ng'iroq · o'tkazib yuborilgan qo'ng'iroq.

Amallar: vazifa yaratish (tur, matn shabloni `{{deal.name}} {{contact.phone}}`, muddat offset, mas'ul = sdelka mas'uli / aniq xodim / **navbat bilan (round-robin)**) · bosqichni o'zgartirish · boshqa voronkaga ko'chirish · mas'ulni o'zgartirish · maydon qiymatini o'rnatish (masalan "Lid bo'lib berilgan sana = bugun") · teg qo'shish/olib tashlash · mijozga xabar yuborish (kanal + shablon) · xodimga bildirishnoma · ochiq vazifalarni yopish · webhook (POST JSON) · bot ssenariysini boshlash.

### 11.3 Dvigatel
- Sinxron domen hodisalari: `DealCreated`, `DealStatusChanged`, `DealResponsibleChanged`, `DealFieldChanged`, `TagAdded`, `MessageReceived`, `CallReceived`, `CallMissed`. Handler mos qoidalarni topadi → `sales_automation_runs` ga `scheduled_at` bilan yozadi → queue worker bajaradi.
- `status_idle` va `task_overdue` — har 5 daqiqada scheduler.
- **Idempotentlik:** bir qoida bir sdelka uchun bir trigger hodisasida faqat bir marta. **Sikl himoyasi:** zanjir chuqurligi ≤ 5.
- Bajarilganda sdelka lentasiga "Robot: …" hodisasi (`actor_label='SalesBot'`).
- Kechiktirilgan run bajarilishidan oldin shart qayta tekshiriladi (sdelka boshqa bosqichga o'tib ketgan bo'lsa — `skipped`).

### 11.4 Seed qoidalari (amoCRM hisobidagi amaliyot + avvalgi hujjat tavsiyalari)
| Qachon | Shart | Amal |
|---|---|---|
| O'tkazib yuborilgan qo'ng'iroq | shu sdelkada ochiq "Bog'lanish" vazifasi **yo'q** | "Bog'lanish — {{phone}}" vazifasi, 30 daq; bosqich → "Propushen Zvonok"; teg "Propushenniy zvonok" |
| O'tkazib yuborilgan qo'ng'iroq | ochiq vazifa **bor** | yangi vazifa yaratma, mavjudining muddatini yangila (dublikat himoyasi) |
| Sdelka yaratildi | ish vaqti | mas'ul round-robin; "Birinchi aloqa" vazifasi 15 daq |
| Bosqich "Учрашув белгиланди" | "Uchrashuv sanasi" bor | "Uchrashuv" vazifasi shu sanaga; mijozga eslatma −24 soat va −2 soat |
| Bosqichda 3 kun turdi ("Мижоз ўйлаб кўряпти") | ochiq vazifa yo'q | "Qayta bog'lanish" vazifasi |
| Teg "TG/INST → CC" qo'shildi | — | "Call center" voronkasiga ko'chirish, "Instagramm/telegram" bosqichi |
| Won yoki Lost | — | barcha ochiq vazifalarni `cancelled` (`auto_closed_reason`) |
| Vazifa 24 soat kechikdi | — | rahbarga bildirishnoma |

---

## 12. SOZLAMALAR

`/sales/settings`:
1. **Voronkalar va bosqichlar** — voronka qo'shish/qayta nomlash/arxivlash/tartiblash; bosqichlar: nom, rang (palitra: `#99ccff #ffcc66 #ff8f92 #fff000 #CCFF66 #D5D8DB #c1c1c1` va boshqalar), izoh, SLA, tartib (drag), "Saralanmagan"ni yoqish/o'chirish.
2. **Maydonlar** — obyekt bo'yicha (sdelka/kontakt/kompaniya) maydonlar va guruhlar; tur, variantlar (select uchun, drag bilan tartib), majburiylik (umuman yoki bosqich bo'yicha), API kodi.
3. **Teglar** — ro'yxat, soni, rang, qayta nomlash, birlashtirish, o'chirish.
4. **Manbalar** va **Yo'qotish sabablari**.
5. **Vazifa turlari** (nom, ikonka, rang).
6. **Avtomatika** (§11).
7. **Xabar shablonlari** — nom, matn, o'zgaruvchilar `{{contact.name}}`, kanal.
8. **Kanallar / integratsiyalar** — Telegram bot, Instagram, WhatsApp, telefoniya (UTel/Sipuni), veb-forma (embed kod + API kalit), webhooklar.
9. **Foydalanuvchilar va ruxsatlar** (§3) — mavjud CRM xodimlari ro'yxatidan, rol, jamoa (guruh).
10. **Umumiy** — ish vaqti (hafta kunlari + soatlar), vaqt zonasi, valyuta, suhbat raqami prefiksi, dashboard fon rasmi, savat (o'chirilganlarni tiklash, 30 kun).

---

## 13. API (REST, JSON)

Prefiks `/api/sales`. Javob formati: `{data, meta:{total, page, per_page}}`. Xatolar: `{error:{code, message, fields?}}`. Barcha ro'yxatlar: `?page&per_page&sort&filter[...]`, `?with=contacts,tags,fields,tasks`.

| Metod | Yo'l | Izoh |
|---|---|---|
| GET/POST | `/pipelines` | voronkalar (statuses bilan) |
| PATCH/DELETE | `/pipelines/{id}` | |
| POST/PATCH/DELETE | `/pipelines/{id}/statuses[/{sid}]` | DELETE da `?move_to=` majburiy |
| POST | `/pipelines/{id}/statuses/reorder` | `[ids]` |
| GET/POST | `/deals` | filtrlar §5.4 |
| GET/PATCH/DELETE | `/deals/{id}` | PATCH da `status_id` → §5.2 qoidalari, 422 + `required_fields` qaytaradi |
| POST | `/deals/{id}/won` , `/deals/{id}/lost` | lost: `{loss_reason_id, comment}` majburiy |
| POST | `/deals/{id}/accept` , `/deals/{id}/decline` | saralanmagan uchun |
| POST | `/deals/bulk` | `{ids|filter, action, params}` → `{job_id}` |
| POST | `/deals/merge` | `{ids, resolution:{field: source_id}}` |
| GET | `/deals/{id}/timeline?types=&before=` | lenta (notes+events+messages+calls+tasks birlashtirilgan, kursor bilan) |
| GET | `/deals/{id}/stats` | |
| CRUD | `/contacts`, `/companies` | `/contacts/lookup?phone=` — telefoniya va dublikat uchun |
| POST/DELETE | `/deals/{id}/contacts/{cid}` | bog'lash/uzish |
| CRUD | `/notes` | `{entity_type, entity_id, type, text, params}` |
| GET/POST | `/tasks` ; PATCH `/tasks/{id}` | |
| POST | `/tasks/{id}/complete` | `{result_text, next_task?}` |
| POST | `/tasks/bulk` | |
| GET | `/conversations` ; `/conversations/{id}/messages` | |
| POST | `/conversations/{id}/messages` ; `/conversations/{id}/close` | |
| CRUD | `/fields`, `/field-groups`, `/tags`, `/sources`, `/loss-reasons`, `/task-types`, `/templates`, `/automation-rules`, `/goals`, `/saved-filters` | |
| GET | `/dashboard` ; `/analytics/{report}` | |
| GET | `/events` | hodisalar ro'yxati |
| POST | `/import/{entity}` ; GET `/export/{entity}` | fon job |
| POST | `/webhooks/telephony/{provider}` ; `/webhooks/channel/{type}/{channel_id}` ; `/webhooks/form/{token}` | tashqi kirishlar, imzo tekshiruvi bilan |

Realtime (websocket kanallari): `sales.pipeline.{id}` (deal_moved, deal_created, deal_updated), `sales.deal.{id}` (timeline_item), `sales.user.{id}` (notification, task_reminder, new_message).

---

## 14. INTEGRATSIYALAR

### 14.1 Mavjud Markaz CRM bilan (eng muhim)
- **Won → o'quvchi:** sdelka `won` bo'lganda modal "O'quvchi yaratish": kontaktdan ism/telefon, kurs/guruh tanlash → mavjud `students` moduliga yozuv, `sales_deals.student_id` bog'lanadi. Agar mavjud CRM da "birinchi to'lov" hodisasi bo'lsa — uni ham `won` triggeri sifatida ishlatish imkoniyati (sozlama: `won_on = manual | student_created | first_payment`).
- **Sinov darsi:** mavjud `trial_lessons` sdelkaga bog'lanadi (`deal_id`), sana kartochkada ko'rinadi, natija (keldi/kelmadi) avtomatika triggeri bo'ladi.
- **Telegram bot:** mavjud bot infratuzilmasi imBox kanali sifatida ulanadi.
- **Bog'lanish kerak (contact_queue):** keyinchalik `sales_tasks` ga `entity_type='student'` sifatida birlashtirish mumkin — 1-bosqichda tegma.

### 14.2 Telefoniya (UTel, Sipuni)
Webhook: `call_started | call_answered | call_ended | call_missed` → raqamni normalizatsiya → `/contacts/lookup` → topilmasa kontakt "Kontakt 998XXXXXXXXX" + Saralanmagan (kategoriya: qo'ng'iroq) sdelka → `sales_notes(type=call_in/out, params{duration, recording_url, status})` → `missed_call` triggeri. Click-to-call: kartochkadagi telefon bosilganda provayder API orqali xodim telefoniga chaqiruv. Kiruvchi qo'ng'iroqda xodim ekranida popup: kontakt nomi, ochiq sdelka, "Kartochkani ochish".

### 14.3 Veb-forma va UTM
`POST /api/sales/webhooks/form/{token}`: `{name, phone, email?, fields:{code:value}, utm:{...}, page_url}` → dublikat tekshiruvi → kontakt + sdelka (Saralanmagan, kategoriya: forma) + barcha javoblar qo'shimcha maydonlarga (amoCRM hisobidagi "Siz bilan qachon bog'lanishimiz qulay?" kabi forma savollari aynan shunday saqlangan).

---

## 15. BIZNES QOIDALARI (amoCRM xatolarini takrorlamaslik uchun)

amoCRM hisobini o'rganishdan saboq: **2 563 ta ochiq vazifaning hammasi muddati o'tgan**, ularning deyarli barchasini telefoniya avtomatik yaratgan (matni faqat telefon raqami), 2 944 ta sdelkada umuman vazifa yo'q. Shuning uchun quyidagilar **majburiy**:

1. **Avto-vazifa dublikati yo'q:** bir sdelkada bir turdagi ochiq avto-vazifa bo'lsa, yangisi yaratilmaydi — mavjudining muddati yangilanadi.
2. **Avto-yopish:** won/lost bo'lganda ochiq vazifalar `cancelled`. Mas'ul o'zgarsa ochiq vazifalar ham yangi mas'ulga o'tadi (so'rov bilan).
3. **Natija majburiy:** vazifani yopishda `result_text` ≥ 3 belgi (yoki natija tegi tanlanadi).
4. **Vazifasiz ochiq sdelka — ogohlantirish:** kanbanda sariq nuqta, bosqich o'zgarganda va vazifa yopilganda "keyingi vazifa" taklifi, dashboardda vidjet #4.
5. **Yo'qotish sababi majburiy** va o'sha zahoti (orqaga qarab emas).
6. **Ish vaqti:** avto-vazifa muddati ish vaqtidan tashqariga tushsa — keyingi ish kuni boshiga suriladi.
7. **Telefon validatsiyasi:** saqlashdan oldin E.164; noto'g'ri raqam bilan saqlash taqiqlanadi.
8. **O'chirilgan xodim:** sdelkalari va tarixi saqlanadi, UI da "Foydalanuvchi o'chirilgan" deb ko'rsatiladi, ommaviy amal bilan boshqaga o'tkazish taklif qilinadi.
9. **Hech narsa izsiz o'zgarmaydi:** har yozish → `sales_events`.

---

## 16. amoCRM DAN MA'LUMOTLARNI KO'CHIRISH (og'riqsiz o'tish uchun)

Artisan/CLI buyrug'i: `sales:import-amocrm --subdomain=1234567890m --token=... [--since=] [--dry-run]`. amoCRM API v4 (OAuth uzoq muddatli token, admin beradi). Tartib va mapping:

| # | amoCRM endpoint | → | Bizning jadval | Izoh |
|---|---|---|---|---|
| 1 | `GET /api/v4/users` | → | `users` mapping jadvali `sales_user_map(amo_user_id, user_id)` | email bo'yicha moslash, topilmasa "o'chirilgan xodim" placeholder |
| 2 | `GET /api/v4/leads/pipelines` | → | `sales_pipelines`, `sales_statuses` | 142→won, 143→lost, `type=1`→unsorted; rang va tartib saqlanadi |
| 3 | `GET /api/v4/{leads,contacts,companies}/custom_fields` + `/groups` | → | `sales_fields`, `sales_field_options`, `sales_field_groups` | turi 1:1, `tracking_data`→`tracking` |
| 4 | `GET /api/v4/leads/loss_reasons`, `/leads/tags`, `/contacts/tags`, `/sources` | → | tegishli ma'lumotnomalar | |
| 5 | `GET /api/v4/contacts?with=leads` (250 dan sahifalab) | → | `sales_contacts` + phones/emails | telefon normalizatsiya |
| 6 | `GET /api/v4/companies` | → | `sales_companies` | |
| 7 | `GET /api/v4/leads?with=contacts,loss_reason,source` | → | `sales_deals` + bog'lanishlar + field values + teglar | `external_id = amo id` |
| 8 | `GET /api/v4/tasks` | → | `sales_tasks` | `task_type_id` 1→Bog'lanish, 2→Uchrashuv; **muddati 30 kundan ko'p o'tgan, avtomatik yaratilgan ochiq vazifalar `cancelled` qilib import qilinadi** (`auto_closed_reason='amo_import_stale'`) — shovqinni olib kelmaslik uchun; buni `--keep-stale-tasks` bilan o'chirsa bo'ladi |
| 9 | `GET /api/v4/{leads,contacts}/notes` | → | `sales_notes` | qo'ng'iroq yozuvlari URL sifatida (ixtiyoriy yuklab olish) |
| 10 | `GET /api/v4/events` | → | `sales_events` | oxirgi 12 oy (sozlanadi) |

Talablar: rate-limit (7 so'rov/s) hurmat qilinsin, qayta ishga tushirilsa davom etsin (har bosqich progressi `sales_import_state`da), `--dry-run` hisobot beradi (nechta yozuv, mos kelmagan maydonlar). Import tugagach solishtirish hisoboti: amoCRM dagi son vs bizdagi son har obyekt bo'yicha.

**Joriy hisob ko'lami (taxminiy):** ~2 950 ochiq sdelka (Call center voronkasida 2 938), ~23 900 kontakt, ~301 000 hodisa, 2 885 suhbat, 58 sdelka maydoni.

### 16.1 Seed (amoCRM hisobidagi joriy sozlamalar — import qilinmasa shu bilan boshlang)

**Voronkalar:**
1. **Instagram | Telegram** (asosiy): Saralanmagan → Янги қизиқиш (`#99ccff`) → Квалификация килинди (`#ffcc66`) → Колл-марказга ўтказилди → Онлайн учрашув белгиланди → ✅ Муваффақиятли (тўғридан-тўғри сотув) / ❌ сифатсиз
2. **Call center:** Saralanmagan → Instagramm/telegram → Propushen Zvonok (`#ff8f92`) → Kiruvchi qo'ng'iroq → Ko'tarmadi (`#fff000`) → Ишга олинди → eski mijoz → Алока урнатилди (`#ffcc66`) → Учрашув белгиланди → Мижоз ўйлаб кўряпти → ✅ Муваффақиятли / ❌ сифатсиз
3. **Консультация:** Saralanmagan → Учрашув кутилмоқда → Propushen Zvonok → Мижоз ўйлаб кўряпти → Шартнома юборилди → ✅ Муваффақиятли / ❌ Рад этилди / мос эмас

**Vazifa turlari:** Bog'lanish (Связаться), Uchrashuv (Встреча).
**Manbalar:** Facebook, Instagram, Telegram, Ko'chadan keldi, Tanish-bilish.
**Yo'qotish sabablari:** Ҳозир тайёр эмас, Молиявий имконият йўқ, Шартлар мос эмас, Ёши мос эмас, Муддат мос келмади, Бошқа компанияни танлади, … (to'liq ro'yxat importdan).
**Sdelka maydonlari (asosiylari):** Источник (select), Пол (select), Худуд (select, 20 viloyat/shahar), Йоши (numeric), Ҳозирги таълим даражаси, Қайси дастур (select 20), Тил сертификати борми? (select), IELTS, TOPIK, Universitet, Виза олдин чиққанми? (select), Учрашув санаси (date), Учрашув тури/формати/натижаси (select), Учрашув иловаси (url), Қарор кечикиш сабаби (select), Шартнома юборилган сана (date), Шартнома тури (select), Шартнома ID, Причина отказа (select 11), Callga o'tkazilgan sana, Calldan konsultatsiya berilgan sana, Lid bo'lib berilgan sana (date), forma savollari (text) + 19 ta UTM/tracking maydoni.
**Ko'p ishlatiladigan teglar:** Prinyat, Propushenniy zvonok, Ne dozvonilis, TG/INST → CC, CC → Consultation, merged.

---

## 17. BOSQICHMA-BOSQICH REJA VA QABUL MEZONLARI

> v2: tartib §20 dagi 4 bosqichli rejaga ko'ra; bu yerdagi qabul mezonlari o'z kuchida.

**Bosqich 1 — Asos (Voronka + Kartochka + Vazifalar)**
Jadval: pipelines, statuses, deals, contacts(+phones/emails), companies, fields(+values, groups, options), tags, sources, loss_reasons, task_types, tasks, notes, events, saved_filters. Ruxsatlar. Kanban + ro'yxat + filtr paneli + ommaviy amallar. Sdelka/kontakt kartochkasi (lenta, composer: izoh + vazifa). Vazifalar bo'limi (muddat kanbani + ro'yxat). Sozlamalar: voronkalar, maydonlar, teglar, manbalar, sabablar.
*Qabul:* (a) 3 voronkani UI dan yaratib, sdelkani sudrab o'tkazish ishlaydi va lentada hodisa chiqadi; (b) lost ga sababsiz o'tkazib bo'lmaydi; (c) bosqich uchun majburiy maydon modal chiqaradi; (d) menejer boshqaning sdelkasini API orqali ham ko'rmaydi; (e) 10 000 sdelkali voronkada kanban birinchi yuklanishi < 1.5 s; (f) vazifani natijasiz yopib bo'lmaydi va "keyingi vazifa" taklifi chiqadi.

**Bosqich 2 — Dashboard + Analitika + Avtomatika**
Dashboard (19 vidjet, sozlanadigan layout), 6 ta hisobot, avtomatika dvigateli + UI + seed qoidalari, bildirishnomalar va eslatmalar, maqsadlar, ish vaqti.
*Qabul:* dashboard raqamlari ro'yxat filtrlari bilan bir xil (vidjetni bosganda chiqqan ro'yxat soni = vidjet raqami); avto-vazifa dublikati yaratilmaydi; won/lost da vazifalar yopiladi.

**Bosqich 3 — imBox + Integratsiyalar + Ko'chirish**
Telegram kanal, telefoniya webhook va click-to-call, veb-forma, won → o'quvchi, sinov darsi bog'lanishi, amoCRM import, Instagram/WhatsApp.
*Qabul:* Telegram'dagi yangi xabar 5 s ichida imBox va Saralanmagan ustunida paydo bo'ladi; o'tkazib yuborilgan qo'ng'iroq bitta vazifa yaratadi; import `--dry-run` hisobotida amoCRM bilan son farqi 0.

**Har bosqichda testlar:** ruxsat qamrovi, bosqich o'tish qoidalari, avtomatika idempotentligi, telefon normalizatsiyasi, birlashtirish (hech qanday lenta yozuvi yo'qolmasligi), import mapping.

---

## 18. UI/UX TALABLARI

- Dizayn mavjud CRM UI kitida, lekin tuzilma amoCRM dagidek: qorong'i/yorug' tema, zich kartalar, chapda ikonkali sidebar (badge lar bilan).
- Mobil: kanban gorizontal scroll (bitta ustun ekran kengligida, swipe), kartochka bitta ustun (maydonlar → lenta tab), composer pastga qadalgan.
- Klaviatura: `/` qidiruv, `N` yangi sdelka, kartochkada `Ctrl+Enter` yuborish, `Esc` yopish.
- Bo'sh holatlar (empty state) har ro'yxatda tushuntirish va asosiy tugma bilan.
- Hamma sana `dd.mm.yyyy HH:mm`, nisbiy vaqt tooltipda ("2 soat oldin"). Pul `1 500 000 so'm`.
- Interfeys tili: o'zbek (lotin) default, rus — i18n kalitlari orqali.

---

## 19. HUBSPOT QATLAMI — sotuv intizomi, prognoz va avtomatik follow-up

### 19.0 Tahlil: nega amoCRM yetarli emas va HubSpot'dan nima olinadi

**Sotuv menejeri nigohi bilan.** amoCRM — "ish joyi": lid tushadi, kanbanda suriladi, chat bitta oynada. Lekin u menejerni **boshqarmaydi**: BRIGHT FUTURE hisobida 2 563 vazifa muddati o'tgan, 2 944 sdelka vazifasiz, prognoz vidjeti `-/-`, maqsadlar bo'sh. Ya'ni tizim "nima bo'ldi"ni yozadi, "endi nima qilish kerak"ni aytmaydi. HubSpot'ning kuchi aynan shu ikkinchi qismda: **har kuni ertalab menejer nima qilishini tizim o'zi navbatga qo'yadi** (task queue, sequences), **har bosqichning pul qiymati bor** (probability → weighted amount → forecast), **jarayonni buzib bo'lmaydi** (pipeline rules), **yangi menejer birinchi kundan to'g'ri gapiradi** (playbooks). Ta'lim markazida konversiyani hal qiladigan 4 omil: javob tezligi, sinovga kelish, sinovdan keyingi 48 soat, to'lovgacha bosim — HubSpot vositalari aynan shularni yopadi.

**Dasturchi nigohi bilan.** HubSpot'ni to'liq ko'chirish (marketing hub, CMS, tickets, custom objects) — ortiqcha va qimmat. Olinadigan narsalar mavjud modelga **qatlam** sifatida qo'shiladi: ko'pi 3–5 ta yangi jadval + mavjud avtomatika dvigateli (§11) ustiga quriladi. Yangi "Lead" obyekti, alohida marketing modul, email tracking (bizda mijozlar email ishlatmaydi) — **olinmaydi**.

**Qaror jadvali** (O = olamiz, M = moslab olamiz, Y = yo'q):

| # | HubSpot imkoniyati | Qaror | Nega | Ustuvorlik | Narx (dev) |
|---|---|---|---|---|---|
| H1 | Bosqich ehtimoli (probability) + kanban ustunida **Total / Weighted amount** | O | Rahbar voronka "qancha pul" ekanini ko'radi; prognoz shundan | P1 | K |
| H2 | **Pipeline rules**: bosqich sakrashni cheklash, orqaga qaytarishni cheklash, bosqichni faqat ma'lum rol o'zgartiradi, won faqat to'lov bilan | O | Ma'lumot tozaligi; "aylantirildi, lekin to'lamadi" muammosi yo'qoladi | P1 | K |
| H3 | **Conditional stage properties** (bosqichga o'tishda majburiy maydon) | allaqachon §2.4 `required_statuses` | — | — | — |
| H4 | **Task queues** — vazifalarni ketma-ket "Start" rejimida bajarish | O | Menejer unumdorligi 2–3 baravar; ertalabki 30 ta qo'ng'iroq to'xtovsiz | P1 | O |
| H5 | **Sales Workspace** ("Bugun" ekrani) | O | Menejer CRM ni ochganda nima qilishini darrov ko'radi | P1 | O |
| H6 | **Sequences** (ko'p qadamli follow-up, javob kelganda avto-to'xtaydi) | M — email o'rniga Telegram/SMS + qo'ng'iroq vazifasi | Sinovdan keyingi va "o'ylab ko'ryapti" bosqichidagi lidlar unutilmaydi | P1 | O |
| H7 | **Meetings scheduler** (onlayn yozilish havolasi) | M — sinov darsi va konsultatsiya uchun, bo'sh slotlar o'qituvchi/xona jadvalidan | Ota-ona o'zi vaqt tanlaydi; menejer yozishmalarga vaqt sarflamaydi; sdelka avtomatik "Sinovga yozildi" | P2 | O |
| H8 | **Playbooks** (skript + savollar → maydonlarga yoziladi) | O | Yangi menejerni o'qitish, standart malaka (kvalifikatsiya) savollari | P2 | K–O |
| H9 | **Forecast** (kvota, kategoriyalar Commit / Best case / Pipeline, menejer o'z prognozini kiritadi) | O | Oy oxirini oldindan bilish, rahbar haftalik yig'ilishi shu ekranda | P2 | O |
| H10 | **Association labels** (kontakt sdelkada qanday rolda: Ota-ona / O'quvchi / To'lovchi) | O | Ta'lim markazida qaror qiluvchi (ota-ona) va mijoz (bola) har xil | P1 | K |
| H11 | **Collaborators** (sdelkada yordamchi xodimlar: o'qituvchi, kassir) | O | Sinov darsidagi o'qituvchi sdelkani ko'radi va natija yozadi | P2 | K |
| H12 | **Lead scoring** (Fit + Engagement, vaqt bilan so'nish) | M — qoidaga asoslangan | Menejer "issiq" lidlardan boshlaydi | P2 | O |
| H13 | **Deal health / xavf belgilari** (tegilmagan, muddat o'tgan, bosqichda qotgan) | O | "Xavfdagi sdelkalar" ro'yxati rahbar uchun | P2 | K |
| H14 | **Record customization** — kartochka chap/o'ng panel va kanban kartasidagi maydonlarni rol bo'yicha sozlash | O | Call-markaz va konsultantga har xil kartochka kerak | P2 | O |
| H15 | **Views** — saqlangan ko'rinishlar tab sifatida ("Hammasi", "Meniki", + o'zimizniki) | O (amo filtrlar ustiga UI) | Kundalik ish 1 bosishda | P1 | K |
| H16 | **Products / Line items / Quotes + to'lov havolasi** | M — mahsulot = kurs/paket; taklif → Click/Payme havola | Sdelka summasi qo'lda emas, kurs narxidan; chegirma nazorati | P2 | O |
| H17 | **Approvals** (chegirma > X% bo'lsa rahbar tasdig'i) | O | Menejerlar "chegirma bilan sotish"ni suiste'mol qilmaydi | P3 | K |
| H18 | **Workflows: shoxlanish (if/else), maqsad (goal) va qayta yozilish** | M — §11 qoidalari "zanjir"ga kengaytiriladi | Murakkab ssenariy (sinovga keldi/kelmadi) bitta joyda | P2 | O–Y |
| H19 | **Activity Feed** (real vaqtda: xabar o'qildi, havola bosildi, to'lov sahifasi ochildi) | M | "Hozir qo'ng'iroq qilish vaqti" signali | P3 | O |
| H20 | **Calculated / rollup properties** (masalan kontaktda "jami to'langan", sdelkada "sinovgacha kun") | O | Hisobotlar va filtrlar boyiydi | P2 | O |
| H21 | **Catch-up / AI record summary + "Savol berish"** | M — anonimlashtirilgan matn bilan (§19.10) | Kartochkani 5 soniyada tushunish | P3 | O |
| H22 | **Conversation intelligence** (qo'ng'iroq transkripti, skriptga moslik bahosi) | M | Menejerlar sifatini nazorat qilish | P3 | Y |
| H23 | **Coaching playlists, leaderboard** | M — leaderboard olinadi | Musobaqa ruhi, oylik bonus shaffofligi | P2 | K |
| H24 | Alohida **Lead** obyekti va Prospecting workspace | Y | Bizda "Saralanmagan" bosqichi + kvalifikatsiya bosqichi yetarli; ikkinchi obyekt ma'lumotni ikkiga bo'ladi | — | — |
| H25 | Email tracking, marketing email, CMS, tickets | Y | Auditoriya Telegram/Instagram'da; bu modul doirasidan tashqari | — | — |

### 19.1 Bosqich ehtimoli va weighted amount (H1)
- `sales_statuses` ga: `probability numeric(5,2)` (0–100; won = 100, lost = 0, unsorted = 0), `forecast_category enum('pipeline','best_case','commit','closed_won','omitted')`.
- `sales_deals` ga: `probability_override numeric?` (menejer o'zi o'zgartirishi mumkin — `sales.deals.override_probability` ruxsati), `expected_close_date date?` (HubSpot "Close date"; default — yaratilgandan +14 kun, sozlanadi), `forecast_category_override?`.
- Hisob: `weighted_amount = price × coalesce(probability_override, status.probability) / 100`.
- Kanban ustuni pastida (HubSpot kabi): `1 500 000 so'm | Jami` va `300 000 so'm (20%) | Weighted`. Sarlavhadagi umumiy son yonida ham weighted jami.
- Dashboard "Sotuv prognozi" vidjeti (§4.3) tarixiy ehtimol bo'lmasa shu sozlangan ehtimoldan hisoblaydi — `-/-` chiqmaydi.
- Ta'lim markazi uchun boshlang'ich ehtimollar: Yangi 10%, Aloqa o'rnatildi 20%, Sinovga yozildi 40%, Sinovga keldi 60%, Taklif berildi 75%, To'lov kutilmoqda 90%, To'ladi 100%. 3 oydan keyin tizim haqiqiy konversiyani ko'rsatadi va "ehtimolni yangilash" taklif qiladi (Sozlamalar → Voronka → "Tarixiy konversiya: 34% (sozlangan 40%)").

### 19.2 Prognoz (Forecast) va kvota (H9)
- Jadval: `sales_quotas (id, user_id?, team_id?, pipeline_id?, period_month date, amount bigint, count int?)` — §2.9 `sales_goals` shu bilan almashtiriladi (bitta jadval).
- `/sales/forecast` ekrani: davr (oy/chorak), voronka, jamoa. Jadval qatorlari = menejerlar; ustunlar: **Kvota · Yopildi (won) · Commit · Best case · Pipeline · Weighted · Menejer prognozi · Kvotadan %**. Har katak bosilsa — o'sha sdelkalar ro'yxati.
- **Menejer prognozi** — menejer har dushanba o'z raqamini kiritadi (`sales_forecast_submissions(user_id, period, amount, comment, submitted_at)`); tarix saqlanadi → "prognoz aniqligi" ko'rsatkichi (oy oxirida haqiqiy/prognoz).
- Pastda: oy bo'yicha grafik (kvota chizig'i, yopilgan to'plangan, prognoz).
- Dashboard'ga vidjet #20 "Kvota bajarilishi" (doira, har menejer progress chizig'i) — amoCRM'dagi bo'sh "Maqsadlar" vidjeti o'rniga.

### 19.3 "Bugun" ekrani — Sales Workspace (H5) va Task queue (H4)
Menejer kirganda ochiladigan sahifa. Bloklar (tartib bilan):
1. **Sarlavha kartochkalari:** Bugungi vazifalar `12` · Muddati o'tgan `3` (qizil) · Javob kutayotgan chatlar `5` · Bugungi sinov/uchrashuvlar `4` · Oy kvotasi `62%`.
2. **"Navbatni boshlash" tugmasi** (task queue). Navbatlar: *Muddati o'tgan*, *Bugungi qo'ng'iroqlar*, *Yangi lidlar (SLA)*, *Ketma-ketlik qadamlari*, + foydalanuvchi yaratgan navbatlar (`sales_task_queues(id, name, owner_id, is_shared, filter jsonb)`; vazifa navbatga filtr orqali yoki qo'lda `queue_id` bilan tushadi).
   - Navbat rejimi: ekranda **bitta vazifa** — chapda sdelka kartochkasi (to'liq), yuqorida panel: `Vazifa 3/27 · ← Oldingi · O'tkazib yuborish · Bajarildi va keyingi →`. Qo'ng'iroq tugmasi (click-to-call), playbook paneli (§19.6), natija maydoni.
   - "Bajarildi va keyingi" = §6.3 dagi yopish oqimi (natija majburiy + keyingi vazifa taklifi) + avtomatik keyingi vazifa yuklanadi. Hech qanday sahifa almashinuvi yo'q.
   - Navbat oxirida xulosa: "27 ta vazifa: 22 bajarildi, 5 o'tkazildi, 41 daqiqa".
3. **Bugungi jadval** (soat bo'yicha): uchrashuvlar, sinov darslari, vaqtli vazifalar.
4. **Javob kutayotgan lidlar:** SLA taymeri bilan (`first_response_at` bo'sh, yaratilganiga X daqiqa) — qizil 15 daq dan oshganda.
5. **Xavfdagi sdelkalar** (§19.9).
6. **Oxirgi faollik signallari** (§19.11).

API: `GET /api/sales/today`, `GET/POST /api/sales/task-queues`, `GET /api/sales/task-queues/{id}/next?after_task_id=`.

### 19.4 Ketma-ketliklar — Sequences (H6)
**Maqsad:** sdelka bosqichida "osilib qolmasin" — oldindan yozilgan qadamlar zanjiri avtomatik yuradi, mijoz javob bersa yoki keyingi bosqichga o'tsa to'xtaydi.

Ma'lumot modeli:
```
sales_sequences       (id, name, owner_id, is_shared, pipeline_id?, settings jsonb)
  -- settings: {working_days_only, send_window:{from:'09:00',to:'20:00'}, unenroll_on:['reply','stage_change','meeting_booked','won','lost'], timezone}
sales_sequence_steps  (id, sequence_id, step_no, delay_minutes, type enum('message','call_task','task','manual_message'),
                       channel enum('telegram','sms','whatsapp','instagram')?, template_id?, task_text?, task_type_id?)
sales_sequence_enrollments (id, sequence_id, deal_id, contact_id, enrolled_by, status enum('active','paused','finished','unenrolled'),
                       current_step int, next_run_at, unenroll_reason?, started_at, finished_at?)
  UNIQUE(deal_id) WHERE status='active'   -- bir sdelka bir vaqtda faqat bitta ketma-ketlikda
sales_sequence_step_runs (id, enrollment_id, step_id, scheduled_at, executed_at?, status, message_id?, task_id?, error?)
```
Qoidalar:
- Qadam turlari: **avto-xabar** (shablon, kanal orqali darhol yuboriladi), **qo'lda xabar** (menejerga "tekshirib yuboring" vazifasi, matn tayyor), **qo'ng'iroq vazifasi**, **oddiy vazifa**. Vazifa qadamlari task queue "Ketma-ketlik qadamlari" navbatiga tushadi; keyingi qadam vazifa bajarilgandan keyin hisoblanadi.
- **Avto-to'xtash:** kiruvchi xabar (`MessageReceived`), bosqich o'zgarishi, uchrashuv yozilishi, won/lost → `unenrolled` + lentada "Ketma-ketlik to'xtadi: mijoz javob berdi".
- Yozish usullari: kartochkadan "Ketma-ketlikka qo'shish" (oldindan ko'rish bilan — har qadamning sanasi), ommaviy amal, avtomatika amali `enroll_sequence` (§11.2 ga qo'shiladi).
- Hisobot: har ketma-ketlik bo'yicha yozilganlar, javob berganlar %, uchrashuvga yozilganlar %, won %; qaysi qadamda eng ko'p javob.
- **Seed ketma-ketliklar (Wunderkind uchun):**
  1. *"Sinovga kelmadi"* — 0: Telegram "Bugun sizni kutgandik, boshqa kunga yozamizmi? [Ha, yozing]" · +1 kun: qo'ng'iroq vazifasi · +3 kun: Telegram "Bu hafta bo'sh joylar: …" · +7 kun: qo'ng'iroq, natija bo'lmasa lost "Javob bermadi".
  2. *"Sinovdan keyin"* — +2 soat: qo'ng'iroq vazifasi (playbook "Sinovdan keyingi suhbat") · +1 kun: Telegram guruh, jadval, narx · +2 kun: "Chegirma ertaga tugaydi" · +4 kun: qo'ng'iroq.
  3. *"O'ylab ko'ryapti"* — +1 kun xabar (o'quvchi natijasi/fikr) · +3 kun qo'ng'iroq · +7 kun xabar · +14 kun qo'ng'iroq.
  4. *"Javob bermadi (yangi lid)"* — 0 qo'ng'iroq · +2 soat qo'ng'iroq · +4 soat Telegram/SMS · +1 kun qo'ng'iroq · +2 kun SMS.

### 19.5 Onlayn yozilish havolalari — Meetings (H7)
- `sales_booking_pages (id, slug, title, type enum('trial_lesson','consultation','call'), owner_id?, course_id?, branch_id?, duration_min, buffer_min, min_notice_hours, max_days_ahead, questions jsonb, pipeline_id, target_status_id, is_active)`.
- Bo'sh slotlar: **mavjud CRM jadvalidan** (o'qituvchi darslari, xonalar band bo'lishi, sinov guruhlari) + menejer ish vaqti. Agar sinov darsi mavjud guruh darsiga yozish bo'lsa — guruhdagi bo'sh o'rinlar soni.
- Ochiq sahifa `/{account}/book/{slug}` (mobilga moslashgan, o'zbek/rus): kun → soat → ism, telefon (+998 validatsiya), farzand yoshi/sinfi, savollar → tasdiq. Telegram bot ichida ham xuddi shu oqim (inline tugmalar).
- Yozilganda: kontakt topiladi/yaratiladi → sdelka topiladi/yaratiladi → `trial_lessons` yozuvi → sdelka `target_status_id` ga (masalan "Sinovga yozildi") → menejer va o'qituvchiga bildirishnoma → mijozga tasdiq + eslatmalar (−24 soat, −2 soat, "✅ Kelamiz / 🔁 Ko'chirish" tugmalari) → faol ketma-ketlik to'xtaydi (`meeting_booked`).
- Menejer chatda bitta tugma bilan havolani yuboradi (shablon `{{booking_link}}`).
- Ko'chirish/bekor qilish havolasi xabarda; bekor qilinsa sdelka "Aloqa o'rnatildi"ga qaytadi va vazifa yaratiladi.

### 19.6 Playbooklar (H8)
- `sales_playbooks (id, name, pipeline_id?, status_ids int[]?, content jsonb, is_active)`; content — bloklar: matn (skript), maslahat, **savol kartochkasi** `{question, field_id, input: select|text|number|date}`.
- Kartochkaning o'ng panelida "Playbooklar" tab: joriy bosqichga mos playbook avtomatik taklif qilinadi. Savolga javob berilsa — darhol sdelka/kontakt maydoniga yoziladi (masalan "Farzand necha yoshda?" → `Йоши`, "Qaysi vaqt qulay?" → maydon).
- Ishlatish statistikasi `sales_playbook_usage (playbook_id, user_id, deal_id, completed_questions, created_at)`; hisobot: playbook ishlatilgan sdelkalar konversiyasi vs ishlatilmaganlar — rahbar uchun dalil.
- Seed: "Birinchi qo'ng'iroq (kvalifikatsiya)", "Sinovdan keyingi suhbat", "E'tirozlar: qimmat / vaqt yo'q / uzoq / o'ylab ko'raman", "To'lovga undash".

### 19.7 Pipeline qoidalari (H2)
`sales_pipelines.rules jsonb`:
```
{ "restrict_skip": true,              // faqat keyingi bosqichga (yoki "allowed_jumps": {status_id:[ids]})
  "restrict_backward": true,          // orqaga faqat ruxsatli rol
  "stage_edit_roles": [role_ids],     // bosqichni kim o'zgartira oladi (bo'sh = hamma)
  "won_requires": "first_payment" | "student_created" | "manual",
  "won_roles": [role_ids],            // manual bo'lsa won ni kim qo'ya oladi
  "lost_requires_reason": true,
  "reopen_roles": [role_ids] }        // yopilgan sdelkani kim qayta ochadi
```
- Backend `DealStatusPolicy::canMove(deal, from, to, user)` — UI va API bir xil siyosatdan foydalanadi; rad etilganda 422 `{code:'stage_skip_forbidden', allowed:[...]}`. Kanbanda ruxsat yo'q ustunlar sudrash paytida xira ko'rinadi.
- **Tavsiya (Wunderkind):** `won_requires = first_payment` — "Aylantirildi, lekin to'lamadi" holati yo'qoladi; ulardan "To'lov kutilmoqda" bosqichi + ketma-ketlik ishlaydi.

### 19.8 Association labels va Collaborators (H10, H11)
- `sales_deal_contacts` ga `role enum('parent','student','payer','decision_maker','other')` va `sales_contact_relations (contact_id, related_contact_id, label)` (masalan "ota — o'g'li").
- Kartochkada kontaktlar bloki rol bo'yicha: **Ota-ona (qaror qiluvchi)** · **O'quvchi**. Xabarlar default ota-onaga ketadi. Bitta ota-onaning bir nechta farzandi = bir nechta sdelka, bitta kontakt (oila) — dublikat muammosi (aka-ukalar) yechiladi.
- `sales_deal_collaborators (deal_id, user_id, role enum('teacher','assistant','cashier','other'))`: hamkorlar sdelkani `view` qamrovidan qat'i nazar ko'radi, izoh yozadi, sinov natijasini kiritadi; "Men hamkor bo'lgan sdelkalar" tayyor ko'rinishi.

### 19.9 Lead scoring va sdelka salomatligi (H12, H13)
- `sales_score_rules (id, kind enum('fit','engagement'), condition jsonb, points int, decay_days int?)`. Misollar — Fit: manba = Tavsiya +25, filial yaqin hudud +10, yoshi kurs yoshiga mos +15, telefon yo'q −30. Engagement: chatga javob berdi +10 (14 kunda so'nadi), sinovga yozildi +20, to'lov sahifasini ochdi +15, 7 kun faolliksiz −10.
- `sales_deals.score_fit`, `score_engagement`, `score` (=0.5×fit+0.5×eng, 0–100), har hodisada va har tunda qayta hisob. Kanban kartasida rangli belgi (🔥 ≥70, 🙂 40–69, ❄ <40); saralash "ball bo'yicha".
- **Salomatlik** `health enum('ok','attention','at_risk')` + `health_reasons text[]`: vazifasiz; vazifa > 24 soat kechikkan; bosqich SLA'sidan oshgan; 7 kun aloqasiz; `expected_close_date` o'tib ketgan; sinov sanasi o'tgan, natija yo'q. Kartochka sarlavhasida badge va sabablar ro'yxati; "Bugun" va dashboard'da "Xavfdagi sdelkalar".

### 19.10 Kartochka: Catch-up / AI xulosa va kartochkani sozlash (H14, H21)
- Kartochkaning o'rta qismi 2 tab (HubSpot kabi): **Xulosa (Catch-up)** — AI xulosa, asosiy maydonlar, salomatlik, keyingi vazifa, faol ketma-ketlik, yaqin uchrashuv; **Faoliyat** — §6.3 lenta. O'ng ustun (yangi): bog'langan obyektlar — kontaktlar (rol bilan), kompaniya, o'quvchi profili (mavjud CRM), sinov darslari, to'lovlar, fayllar, playbooklar.
- **AI xulosa:** "Yaratish" tugmasi → lenta oxirgi 50 yozuvidan (anonimlashtirilgan: ism → `[ISM]`, telefon → `[TEL]`) LLM chaqiruvi → 4 qism: *Mijoz nima istaydi · Nima va'da qilingan · Javobsiz savollar · Keyingi qadam taklifi*; manbalar soni va 👍/👎 baho. "Savol berish" — shu sdelka konteksti bo'yicha erkin savol. Natija `sales_ai_summaries (deal_id, content, model, created_at, feedback)` keshida; yangi hodisa kelsa "eskirgan" belgisi.
- **Kartochkani sozlash:** `sales_record_layouts (id, entity_type, role_id?, pipeline_id?, left_fields int[], card_fields int[], tabs jsonb)` — admin rol/voronka bo'yicha chap panel va kanban kartasidagi maydonlarni tanlaydi (call-markaz: telefon, manba, oxirgi qo'ng'iroq; konsultant: dastur, sinov sanasi, ball).

### 19.11 Faollik signallari — Activity Feed (H19)
Kuzatiladigan hodisalar: mijoz Telegram xabarini o'qidi; booking havolasini ochdi; to'lov havolasini ochdi, lekin to'lamadi; 7+ kun jim turgan mijoz yozdi. `sales_signals (id, deal_id, contact_id, type, payload, created_at, seen_by[])`. "Bugun" ekranida va o'ngdagi "Hodisalar" panelida: "🔔 Aziza opa to'lov sahifasini 3 daqiqa oldin ochdi — hozir qo'ng'iroq qiling". Qisqa havolalar `/{account}/l/{code}` orqali bosish qayd qilinadi.

### 19.12 Mahsulotlar, taklif va to'lov havolasi (H16, H17)
- Mavjud CRM'dagi `courses`/narxlar **mahsulot katalogi** sifatida ishlatiladi (yangi katalog yaratilmaydi). `sales_deal_items (id, deal_id, course_id?, name, qty, unit_price, discount_pct, total)`; `sales_deals.price` = Σ total (qo'lda kiritish o'chiriladi, agar items bo'lsa).
- **Taklif:** kartochkadan "Taklif yaratish" → kurs, guruh, jadval, narx, chegirma, amal qilish muddati → chiroyli sahifa `/{account}/q/{code}` + PDF → Telegram'da yuboriladi; ichida **Click/Payme to'lov tugmasi**. Ochilishi signal (§19.11), to'lov webhook → won (agar `won_requires=first_payment`) → o'quvchi va guruhga yozish taklifi.
- **Tasdiqlash:** chegirma `> sales_settings.max_discount_pct` bo'lsa taklif "Tasdiq kutilmoqda" — rahbarga bildirishnoma (CRM + Telegram, tugmalar ✅/❌); tasdiqsiz yuborilmaydi. `sales_approvals (id, entity_type, entity_id, requested_by, approver_id, status, comment)`.

### 19.13 Ko'rinishlar (Views), hisoblanadigan maydonlar, leaderboard (H15, H20, H23)
- Sdelkalar/kontaktlar/vazifalar sahifasi tepasida **tab-ko'rinishlar**: `Hammasi | Meniki | + qo'shish` (har tab = saqlangan filtr + ustunlar + saralash + kanban/ro'yxat). `sales_saved_filters` ga `columns`, `sort`, `view_type`, `is_tab`, `position` qo'shiladi. Maksimum 15 tab.
- **Hisoblanadigan maydonlar:** `sales_fields.type` ga `calculated` va `rollup` qo'shiladi; `formula jsonb` (masalan `days_between(now, trial_date)`, `sum(payments.amount where contact=...)`, `count(deals where contact=... and status=won)`). Har tunda + bog'liq hodisada qayta hisob; filtr va hisobotlarda oddiy maydondek ishlaydi.
- **Leaderboard** vidjeti (dashboard #21): menejerlar — to'lovlar summasi, won soni, sinovga kelish %, muddatida bajarilgan vazifa %; oy/hafta; 🥇🥈🥉.

### 19.14 Avtomatika v2 — zanjirli workflow (H18)
§11 dagi "trigger → shart → amal" qoidalari saqlanadi (oddiy holatlar uchun). Qo'shimcha **Workflow** turi:
- `sales_workflows (id, name, is_active, trigger jsonb, reenroll bool, goal jsonb?)`, `sales_workflow_nodes (id, workflow_id, type enum('action','delay','branch','goal_check'), config jsonb, next_node_id?, branches jsonb?)`, `sales_workflow_enrollments (id, workflow_id, deal_id, current_node_id, status, next_run_at)`.
- Vizual muharrir: vertikal oqim, `+` bilan qadam qo'shish; **Branch** — shart bo'yicha ikki/ko'p yo'l ("Sinov natijasi = keldi" → A, "kelmadi" → B, "kiritilmadi 2 soatda" → C). **Goal** — maqsad shartiga yetganda (masalan "to'lov bor") sdelka workflow'dan chiqadi.
- Dvigatel §11.3 bilan bir xil queue/scheduler; idempotentlik va sikl himoyasi saqlanadi. Har enrollment tarixi lentada.
- Seed workflow: **"Sinov darsi"** — trigger: `trial_lesson_created` → eslatmalar −24/−2 soat → sinovdan +2 soat branch: keldi → bosqich "Sinovga keldi" + "Sinovdan keyin" ketma-ketligi; kelmadi → "Sinovga kelmadi" ketma-ketligi + bosqich "Aloqa o'rnatildi"; natija yo'q → o'qituvchiga vazifa + 2 soatdan keyin rahbarga signal.

### 19.15 Qo'shimcha API (v2)
| Metod | Yo'l |
|---|---|
| GET | `/today` |
| CRUD | `/task-queues`, GET `/task-queues/{id}/next` |
| CRUD | `/sequences`, `/sequences/{id}/steps`; POST `/sequences/{id}/enroll` `{deal_ids, start_step?}`; POST `/enrollments/{id}/pause|resume|unenroll` |
| CRUD | `/booking-pages`; GET `/public/book/{slug}/slots?from=&to=`; POST `/public/book/{slug}` (rate-limit, captcha-siz, telefon tasdig'i SMS/Telegram orqali ixtiyoriy) |
| CRUD | `/playbooks`; POST `/playbooks/{id}/answers` `{deal_id, answers:{field_id:value}}` |
| GET/PUT | `/forecast?period=&pipeline_id=&team_id=`; POST `/forecast/submissions`; CRUD `/quotas` |
| CRUD | `/score-rules`; POST `/deals/{id}/rescore` |
| POST | `/deals/{id}/ai-summary`; POST `/deals/{id}/ai-ask` `{question}` |
| CRUD | `/deals/{id}/items`, `/quotes`, POST `/quotes/{id}/send`; `/approvals/{id}/approve|reject` |
| CRUD | `/workflows`, `/workflows/{id}/nodes` |
| CRUD | `/record-layouts` |

Yangi domen hodisalari: `SequenceEnrolled`, `SequenceUnenrolled`, `BookingCreated`, `BookingCancelled`, `QuoteViewed`, `PaymentLinkOpened`, `PaymentReceived`, `ScoreChanged`, `HealthChanged`.

### 19.16 Yangi KPI'lar (rahbar dashboard va analitikaga)
- **Speed to lead:** yangi lidga birinchi javobning median vaqti va "15 daqiqada" ulushi (menejer bo'yicha).
- **Sinovga kelish %** = keldi / yozildi; booking havolasi orqali yozilganlar vs menejer yozganlar.
- **Lid → to'lov %** va o'rtacha sotuv sikli (kun).
- **Ketma-ketlik samaradorligi:** javob %, uchrashuv %, won %.
- **Prognoz aniqligi** = haqiqiy / menejer prognozi.
- **Pipeline coverage** = (weighted ochiq pipeline) / (qolgan kvota); < 3× bo'lsa ogohlantirish.
- **Vazifa intizomi:** muddatida bajarilgan %, vazifasiz ochiq sdelkalar soni (maqsad 0).

---

## 20. YANGILANGAN REJA (v2) — §17 o'rniga

| Bosqich | Mazmun | Davomiylik (taxmin) |
|---|---|---|
| **1. Asos** | §17 Bosqich 1 + **H1** (ehtimol/weighted), **H2** (pipeline rules, `won_requires`), **H10** (kontakt rollari), **H15** (tab-ko'rinishlar) | 4–5 hafta |
| **2. Menejer intizomi** | **"Bugun" ekrani + task queue (H4, H5)**, dashboard, avtomatika v1, **ketma-ketliklar (H6)**, **salomatlik (H13)**, leaderboard, bildirishnomalar | 4 hafta |
| **3. Kanallar va konversiya** | imBox (Telegram → Instagram), telefoniya, **onlayn yozilish (H7)**, **playbooklar (H8)**, **mahsulot/taklif/to'lov havolasi (H16)**, amoCRM import | 4–5 hafta |
| **4. Boshqaruv va aql** | **Prognoz + kvota (H9)**, scoring (H12), workflow v2 (H18), hisoblanadigan maydonlar (H20), kartochkani sozlash (H14), signallar (H19), tasdiqlash (H17), AI xulosa (H21) | 4–6 hafta |

**Qo'shimcha qabul mezonlari:**
- Bosqich 1: `restrict_skip` yoqilganda "Yangi"dan "To'ladi"ga sudrab bo'lmaydi (UI va API); won faqat to'lov hodisasi bilan.
- Bosqich 2: menejer 20 ta vazifani navbat rejimida sahifa qayta yuklanmasdan yopadi; mijoz Telegram'da javob yozsa faol ketma-ketlik 1 daqiqa ichida to'xtaydi va keyingi xabar ketmaydi.
- Bosqich 3: ota-ona booking havolasi orqali yozilganda 10 s ichida sdelka "Sinovga yozildi"da, `trial_lessons` yozuvi va o'qituvchiga bildirishnoma bor; band slot ikkinchi marta tanlanmaydi (tranzaksiya + unikal cheklov).
- Bosqich 4: forecast jadvalidagi har katakning summasi u ochadigan ro'yxat summasiga teng; AI'ga yuborilgan so'rovda telefon va ism yo'qligi testda tekshiriladi.

**Agentga eslatma:** HubSpot imkoniyatlari mavjud amoCRM-asosli model **ustiga** quriladi. Yangi parallel obyekt (Lead), alohida email-marketing yoki ikkinchi avtomatika dvigateli yaratma — §11 dvigatelini kengaytir.

---

*Hujjat oxiri. Agent: §0 dagi discovery hisobotidan boshlang, keyin §20 dagi Bosqich 1.*

