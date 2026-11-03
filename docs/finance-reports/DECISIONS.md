# Pul oqimi 2.0 · P&L 2.0 — qarorlar

Manba: `WunderkindLC_PnL_va_PulOqimi_SPEC.md` (EduSchool asosida yozilgan topshiriq). Bu yerda topshiriqdan FARQ qilgan
yoki uni bizning CRM'ga moslagan har qaror sababi bilan yozilgan. Kod: `Application/Services/FinanceReports2/`,
`Server/Controllers/FinanceReports2Controller.cs`, `Client/src/pages/admin/finance/v2/`.

## Umumiy

| # | Qaror | Sabab |
|---|---|---|
| D1 | Eski «P&L» va «Pul oqimi» sahifalari QOLADI, 2.0 ularning OSTIDA (Moliya → Hisobotlar) | Egasining qarori (2026-11-03) |
| D2 | Ikki bosqich: 1) toifalar + Pul oqimi + P&L jadvali + Yillik + Rejali xarajat; 2) oy boshi snapshot, Kutuv dinamikasi, O'zgarishlar tarixi | Egasining qarori |
| D3 | Filial filtri YO'Q | Har filial alohida ilova va baza (`branches.md`) — bitta bazada filial yo'q |
| D4 | Vaqt zonasi o'girilmaydi | `FinanceTransaction.Date` va `MonthlyCharge.Date` allaqachon markaz kunida "yyyy-MM-dd" saqlanadi |
| D5 | Ruxsat: sahifa kalitlari `finance.cashflow`, `finance.pnl` + `finance.main` (BIRORTASI yetadi); sozlama yozish — `finance.main` edit/create; o'qish ham darvozalangan (RRP) | `permissions.md` §10 (o'z marshruti bor sahifa — o'z kaliti); topshiriq: «mavjud moliya ruxsatli rollarga avtomatik» |
| D6 | Excel — mavjud `DocumentFormat.OpenXml` (`StyledExcel`), ClosedXML qo'shilmadi | Loyihada o'rnini bosuvchi bor (topshiriq §0) |
| D7 | Enum o'rniga satr kodlar (`operating`, `tuitionRevenue` …) | Loyiha uslubi (Status, Direction ham satr) |

## Hisobot toifalari (F1)

| # | Qaror | Sabab |
|---|---|---|
| D8 | Kalit: `type:{TypeId}` yoki `cat:{direction}:{category}` | `other`/`book` kodi kirimda ham, chiqimda ham bor |
| D9 | «Pul harakati emas» (vaucher, chegirma, jarima) — pul oqimida **Operatsion**, Excluded EMAS | Bu CRM'da har amal kassa qoldig'ini o'zgartiradi; Excluded qilinsa pul oqimi kassa qoldig'idan farq qilardi. Admin xohlasa o'zgartiradi |
| D10 | Tur bo'limi `vaucher`/`jarima` va toifa `penalty` → nonCash (nomdan oldin) | Bizda tur bo'limi bor — nomdan ishonchliroq |
| D11 | Kitob qaytarish (chiqim `book`) → yangi o'rin `otherRevenueContra` («Boshqa daromad» dan ayiriladi) | Xarajat emas, kitob sotuvini qaytarish |
| D12 | Moliyalashtirish so'zlari nomdagi «qaytarish» dan USTUN | «Kredit qaytarish» — vozvrat emas (test bilan qulflangan) |
| D13 | Jihoz/ta'mir (`supplies`, `repair`) → Investitsiya, P&L da xarajat | Topshiriq §3.2 #8 (amortizatsiya yo'q) |
| D14 | Toifalar sozlamasi va shablonlar «Sozlamalar → Moliya» da EMAS, **Moliya → Ma'lumotlar** da | Eski «Rejalashtirilgan xarajatlar» va «Tranzaksiya turi» shu yerda; `settings.md` §5 — bir band ikki joyda turmasin |

## Pul oqimi (F2)

| # | Qaror | Sabab |
|---|---|---|
| D15 | Yil boshidagi qoldiq — `CashboxService.YearCarry` (kassa boshlang'ich qoldiqlari bilan), faqat Excluded bo'lmagan qatorlar | Kassa qoldig'i bilan bir xil qoida |
| D16 | Kassa bilan solishtirish faqat joriy yil uchun; farq bo'lsa sariq banner sababi bilan | Topshiriq §4.2 |
| D17 | Oylik rejim — yillik jadvalning bitta ustuni (`ToMonth`) | Raqamlar ikki xil chiqmasin |
| D18 | Bitta SQL GROUP BY emas — yengil proyeksiya xotirada guruhlanadi | SQLite testlari decimal yig'indini tarjima qilmaydi (loyihadagi `CashboxService` naqshi); hajm kichik (768 amal) |

## P&L (F4)

| # | Qaror | Sabab |
|---|---|---|
| D20 | Kutuv — **A rejim** (`MonthlyCharge`: `Amount` = chegirmadan oldin, `Discount` alohida) | Bizda haqiqiy oylik hisoblar bor — tarif bo'yicha taxmin kerak emas |
| D21 | Sof kutuv = yalpi − chegirma − qaytarishlar; Foyda = sof kutuv + boshqa daromad − xarajat | Topshiriq §5.0 va §5.10 bir-biriga zid (qaytarish ikki marta ayirilardi) — §5.0 olindi |
| D22 | Chegirma turlari: registr sababi (`DiscountRules.Resolve`), «Kelmagan darslar chegirmasi» (`LessonDiscount`), «chegirma» turidagi kassa amallari | Bizdagi uch manba |
| D23 | Faol o'quvchi — kamida bitta a'zolik o'sha kuni FAOL (`CourseAnalytics.WasActiveAt`, yopilgan davrlar bilan), arxiv sanasidan keyin emas; sinovdagi sanalmaydi | `course-analytics.md` §2: `Status` emas, SANALAR |
| D24 | «Reja» (1-bosqich) — oy 1-kunidagi faol a'zoliklar × `PricingBook.MonthlyFee` − `DiscountBook` (oylik hisob bilan AYNAN bir xil narx/chegirma qoidasi), `PlanIsEstimated = true` | Snapshot 2-bosqichda |
| D25 | Shu oy uchun yig'ilish — to'lovning **oy tegi** (`FinanceTransaction.Month`) bo'yicha, FIFO emas | Bizda to'lov oylarga taqsimlanadi (`PaymentMonthSplit`) — haqiqiy allocation |
| D26 | Kelajak oy — «Joriy» = reja (prognoz) | Topshiriq §5.1 |

## Xarajatlar va ko'prik

| # | Qaror | Sabab |
|---|---|---|
| D30 | Ish haqi — **maosh reestri** (`PayrollRegister`: o'qituvchi `SalaryLedger`, xodim KPI/qat'iy oylik); maosh va avans chiqimlari xarajatga ALOHIDA qo'shilmaydi | Ikki marta sanalmasin (topshiriq §6.4) |
| D31 | Ish haqi shablon bilan kiritilmaydi (400) | Reestr bilan ikki marta sanalardi |
| D32 | Shablon — yangi `PlannedExpenseTemplate`; eski `PlannedExpense` (bir martalik reja) TEGILMAGAN | Boshqa ma'no, eski sahifa ishlab turibdi |
| D33 | Ko'prikdagi xarajat farqi QISILMAYDI (hisoblangan − to'langan), KPI dagi «to'lanmagan» esa ≥ 0 | Avans/oldindan to'lov ko'prikdan yo'qolmasin (test ushlagan) |
| D34 | Ko'prik: foyda + xarajat farqi − yig'ilmagan (shu oy hisobi − shu oy uchun kelgan) + eski qarzdan + oldindan to'lovlar + P&L dan tashqari pul harakati = pul o'zgarishi; haqiqiy pul o'zgarishi bilan farq 0 | Oxirgi so'mgacha yopiladi; farq faqat pul oqimidan chiqarilgan tur bo'lsa |
| D35 | Shablon oylik yozuvi — fon xizmati (har 6 soat), yozuv yo'q oyda P&L shablon summasini oladi | Keyin shablon summasi o'zgarsa o'tgan oylar qayta yozilmasin |

## Yillik (F6 qismi)

| # | Qaror | Sabab |
|---|---|---|
| D40 | Har oy — `PnlEngine.Compute` (oy oxiri / bugun holatiga) | EduSchool xatosi: ikki tab ikki xil raqam (test bilan qulflangan) |
| D41 | YILLIK — fakt oylar yig'indisi; marja = yillik foyda / (yillik sof kutuv + boshqa daromad) | Topshiriq §7.3 va EduSchool xatosi (−524%) |
| D42 | Prognoz: sof kutuv — oxirgi fakt oy, xarajat — oxirgi ≤3 fakt oy o'rtachasi | Topshiriq §7.3 |
