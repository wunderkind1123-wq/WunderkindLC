namespace WunderkindLC.Application.Services.FinanceReports2;

/// <summary>P&amp;L katakchasi → tafsilot (drill-down). Raqamlar <see cref="PnlEngine.Compute"/> dagi AYNAN o'sha filtrlardan.</summary>
public static partial class PnlEngine
{
    public sealed record DrillRow(string Title, string? Subtitle, string? Date, decimal Amount, decimal? Amount2, string? StudentId);
    public sealed record DrillDto(
        string Metric, string Title, string Description, string AmountLabel, string? Amount2Label, decimal Planned, decimal Current,
        int Total, decimal Sum, int Page, int PageSize, List<DrillRow> Items);

    public static DrillDto Drilldown(Data d, string month, string asOf, string metric, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var p = Compute(d, month, asOf);
        var t = p.Total;
        var m01 = month + "-01";
        bool InWin(string date) => date.Length >= 10 && date.StartsWith(month, StringComparison.Ordinal) && string.CompareOrdinal(date, asOf) <= 0;
        var win = d.Txs.Where(x => InWin(x.Date)).ToList();
        var charges = d.Charges.Where(c => c.Month == month && (c.Date.Length < 10 || string.CompareOrdinal(c.Date, asOf) <= 0)).ToList();
        string Name(string? sid) => sid is not null && d.Students.TryGetValue(sid, out var s) ? s.Name : "—";
        string GroupName(string? gid) => gid is not null && d.Groups.TryGetValue(gid, out var g) ? g.Name : "Guruhsiz";

        List<DrillRow> rows;
        string title, desc, amountLabel = "Summa, so'm";
        string? amount2Label = null;
        decimal planned = 0, current = 0;

        List<DrillRow> TxRows(IEnumerable<Tx> txs) => txs.OrderByDescending(x => x.Date)
            .Select(x => new DrillRow(
                x.StudentId is not null ? Name(x.StudentId) : x.Entry.Name,
                x.StudentId is not null ? x.Entry.Name + (string.IsNullOrEmpty(x.Month) ? "" : $" · {x.Month} uchun") : null,
                x.Date, x.Direction == "income" ? x.Amount : x.Amount, null, x.StudentId)).ToList();

        HashSet<string> ActiveSet(string date) => d.Memberships.GroupBy(m => m.StudentId)
            .Where(g => ActiveAt(d, g.Key, g, date)).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

        switch (metric)
        {
            case "students" or "joined" or "left":
            {
                var start = ActiveSet(m01);
                var cur = p.IsForecast ? start : ActiveSet(asOf);
                var ids = metric switch
                {
                    "joined" => cur.Where(x => !start.Contains(x)),
                    "left" => start.Where(x => !cur.Contains(x)),
                    _ => cur.AsEnumerable(),
                };
                var owed = charges.GroupBy(c => c.StudentId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount - c.Discount));
                rows = ids.Select(id => new DrillRow(Name(id),
                        string.Join(", ", d.Memberships.Where(m => m.StudentId == id && CourseAnalytics.WasActiveAt(m.Row, metric == "left" ? m01 : asOf))
                            .Select(m => GroupName(m.GroupId)).Distinct()),
                        null, owed.GetValueOrDefault(id), null, id))
                    .OrderBy(r => r.Title, StringComparer.Ordinal).ToList();
                title = metric switch { "joined" => "Yangi qabul qilinganlar", "left" => "Ketgan o'quvchilar", _ => "O'quvchilar soni" };
                desc = metric switch
                {
                    "joined" => "Oy boshida faol bo'lmagan, holat sanasigacha faollashganlar.",
                    "left" => "Oy boshida faol bo'lib, holat sanasigacha faolligi tugaganlar (muzlatilgan, chiqarilgan, arxivlangan).",
                    _ => "Holat sanasida kamida bitta guruhda FAOL (to'lovli) a'zoligi bor o'quvchilar — har o'quvchi bir marta.",
                };
                amountLabel = "Shu oy hisobi (sof), so'm";
                planned = metric == "students" ? t.Students.Planned : 0;
                current = metric switch { "joined" => t.Students.Joined, "left" => t.Students.Left, _ => t.Students.Current };
                break;
            }
            case "gross" or "net" or "discount":
            {
                rows = charges.Where(c => metric != "discount" || c.Discount > 0)
                    .Select(c => new DrillRow(Name(c.StudentId), GroupName(c.GroupId), c.Date,
                        metric switch { "gross" => c.Amount, "discount" => c.Discount, _ => c.Amount - c.Discount },
                        metric == "net" ? c.Discount : null, c.StudentId))
                    .OrderByDescending(r => r.Amount).ToList();
                if (metric == "discount")
                    rows.AddRange(TxRows(win.Where(x => x.Entry.IsStudentDiscount)));
                title = metric switch { "gross" => "Yalpi kutilayotgan tushum", "discount" => "Chegirma", _ => "Sof kutuv (NTR)" };
                desc = metric switch
                {
                    "gross" => "Shu oyning oylik hisoblari — chegirmadan OLDINGI summa (har guruh alohida qator).",
                    "discount" => "Oylik hisoblardagi chegirmalar (registr va kelmagan darslar) hamda «chegirma» turidagi amallar.",
                    _ => "Oylik hisob − chegirma. Qaytarishlar (vozvrat) jamida alohida ayiriladi.",
                };
                if (metric == "net") amount2Label = "Chegirma, so'm";
                (planned, current) = metric switch
                {
                    "gross" => (t.Revenue.Gross.Planned, t.Revenue.Gross.Current),
                    "discount" => (t.Revenue.Discount.Planned, t.Revenue.Discount.Current),
                    _ => (t.Revenue.Net.Planned, t.Revenue.Net.Current),
                };
                break;
            }
            case "refunds":
                rows = TxRows(win.Where(x => x.Direction == "expense" && x.Entry.Treatment == FinanceCategoryRules.RevenueContra));
                title = "Qaytarishlar"; desc = "O'quvchilarga qaytarilgan pul (vozvrat) — sof kutuvdan ayiriladi.";
                current = t.Revenue.Refunds.Current;
                break;
            case "otherRevenue":
                rows = TxRows(win.Where(x => (x.Direction == "income" && x.Entry.Treatment == FinanceCategoryRules.OtherRevenue)
                                             || (x.Direction == "expense" && x.Entry.Treatment == FinanceCategoryRules.OtherRevenueContra)))
                    .Select(r => r).ToList();
                title = "Boshqa daromad"; desc = "Kitob, mock va boshqa kirimlar (kitob qaytarishda berilgan pul ayirilgan).";
                current = t.Revenue.OtherRevenue.Current;
                break;
            case "cashCollected" or "priorDebt" or "advances":
            {
                var tuition = win.Where(x => x.Direction == "income" && x.Entry.Treatment == FinanceCategoryRules.TuitionRevenue);
                tuition = metric switch
                {
                    "priorDebt" => tuition.Where(x => !string.IsNullOrEmpty(x.Month) && string.CompareOrdinal(x.Month, month) < 0),
                    "advances" => tuition.Where(x => !string.IsNullOrEmpty(x.Month) && string.CompareOrdinal(x.Month, month) > 0),
                    _ => tuition,
                };
                rows = TxRows(tuition);
                title = metric switch { "priorDebt" => "Eski qarzdan yig'ildi", "advances" => "Oldindan to'lovlar", _ => "Kassaga tushgan pul" };
                desc = metric switch
                {
                    "priorDebt" => "Shu oyda kelgan, lekin O'TGAN oylar hisobiga yozilgan o'quvchi to'lovlari.",
                    "advances" => "Shu oyda kelgan, KEYINGI oylar hisobiga yozilgan to'lovlar (avans).",
                    _ => "Oy 1-kunidan holat sanasigacha kassaga tushgan o'quvchi to'lovlari — qaysi oy uchun ekanidan qat'i nazar.",
                };
                current = metric switch { "priorDebt" => t.Cash.ForPriorMonths, "advances" => t.Cash.ForFutureMonths, _ => t.Cash.Collected };
                break;
            }
            case "collectedForPeriod" or "outstanding":
            {
                var owedBy = charges.GroupBy(c => c.StudentId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount - c.Discount));
                var paidBy = d.Txs.Where(x => x.Month == month && x.StudentId is not null
                                              && ((x.Direction == "income" && x.Entry.Treatment == FinanceCategoryRules.TuitionRevenue)
                                                  || (x.Direction == "expense" && x.Entry.Treatment == FinanceCategoryRules.RevenueContra)))
                    .GroupBy(x => x.StudentId!).ToDictionary(g => g.Key, g => g.Sum(x => x.Direction == "income" ? x.Amount : -x.Amount));
                rows = owedBy.Where(o => o.Value > 0)
                    .Select(o => (o, paid: Math.Max(0, paidBy.GetValueOrDefault(o.Key))))
                    .Where(x => metric == "collectedForPeriod" ? x.paid > 0 : x.paid < x.o.Value)
                    .Select(x => new DrillRow(Name(x.o.Key), $"Hisob: {x.o.Value:N0}".Replace(',', ' '), null,
                        metric == "collectedForPeriod" ? Math.Min(x.paid, x.o.Value) : x.o.Value - x.paid, x.o.Value, x.o.Key))
                    .OrderByDescending(r => r.Amount).ToList();
                title = metric == "outstanding" ? "Shu oy uchun qoldiq (to'lanmagan)" : "Shu oy uchun kelgan to'lovlar";
                desc = "To'lov oy tegi bo'yicha — HOZIRGI holat (tanlangan sanaga emas).";
                amount2Label = "Oy hisobi, so'm";
                current = metric == "outstanding" ? t.PeriodCollection.OutstandingForPeriod : t.PeriodCollection.CollectedForPeriod;
                break;
            }
            default:
            {
                if (metric.StartsWith("discountType:", StringComparison.Ordinal))
                {
                    var key = metric["discountType:".Length..];
                    var dt = t.Revenue.Discount.ByType.FirstOrDefault(x => x.Key == key);
                    title = dt?.Label ?? "Chegirma turi";
                    desc = "Shu turdagi chegirma olgan o'quvchilar.";
                    if (key.StartsWith("type:", StringComparison.Ordinal) || key.StartsWith("cat:", StringComparison.Ordinal))
                        rows = TxRows(win.Where(x => x.Key == key));
                    else
                    {
                        rows = [];
                        foreach (var c in charges.Where(c => c.Discount > 0))
                        {
                            var lesson = c.GroupId is not null && d.LessonDiscounts.TryGetValue((c.StudentId, c.GroupId, month), out var ld) ? Math.Min(ld, c.Discount) : 0m;
                            decimal amt;
                            if (key == "lesson") amt = lesson;
                            else
                            {
                                var w = DiscountRules.Resolve(d.Discounts.For(c.StudentId), month, c.GroupId);
                                var label = w is null ? "Chegirma (tarixiy)" : string.IsNullOrWhiteSpace(w.Reason) ? "Chegirma (sababi ko'rsatilmagan)" : w.Reason.Trim();
                                amt = "reason:" + label == key ? c.Discount - lesson : 0;
                            }
                            if (amt > 0) rows.Add(new DrillRow(Name(c.StudentId), GroupName(c.GroupId), c.Date, amt, null, c.StudentId));
                        }
                        rows = rows.OrderByDescending(r => r.Amount).ToList();
                    }
                    planned = dt?.Planned ?? 0;
                    current = dt?.Amount ?? 0;
                }
                else if (metric.StartsWith("expenseLine:", StringComparison.Ordinal) || metric.StartsWith("expenseGroup:", StringComparison.Ordinal))
                {
                    var isGroup = metric.StartsWith("expenseGroup:", StringComparison.Ordinal);
                    var id = metric[(metric.IndexOf(':') + 1)..];
                    var lines = t.Expenses.Lines.Where(l => isGroup ? l.Group == id : l.Id == id).ToList();
                    rows = [];
                    foreach (var l in lines)
                    {
                        if (l.IsPayroll && d.Payroll.TryGetValue(month, out var pr))
                        {
                            var src = l.Id == "payroll:teachers" ? pr.Teachers : l.Id == "payroll:staff" ? pr.Staff : [];
                            rows.AddRange(src.Where(r => r.Accrued != 0 || r.Paid != 0)
                                .Select(r => new DrillRow(r.Name, r.Role, null, r.Accrued, r.Paid, null)));
                            amount2Label = "To'langan (shu oy uchun), so'm";
                        }
                        foreach (var tr in l.Templates)
                            rows.Add(new DrillRow(tr.Name, tr.IsCorrected ? "Shablon · tuzatilgan" : "Shablon (reja)", null, tr.Current, null, null));
                        if (!l.IsPayroll)
                            rows.AddRange(TxRows(win.Where(x => x.Direction == "expense" && x.Key == l.Id)));
                    }
                    title = lines.Count == 1 ? lines[0].Name : id;
                    desc = isGroup ? "Guruhdagi xarajat qatorlari: shablonlar va haqiqiy chiqimlar."
                        : "Shablon (oy 1-kunida hisoblanadi) va shu turdagi haqiqiy chiqimlar (to'langan qismi).";
                    planned = lines.Sum(l => l.Planned);
                    current = lines.Sum(l => l.Current);
                }
                else
                {
                    rows = [];
                    title = "Ko'rsatkich";
                    desc = "Bu ko'rsatkich uchun tafsilot yo'q.";
                }
                break;
            }
        }

        var slice = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new DrillDto(metric, title, desc, amountLabel, amount2Label, planned, current, rows.Count, rows.Sum(r => r.Amount), page, pageSize, slice);
    }
}
