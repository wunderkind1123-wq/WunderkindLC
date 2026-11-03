using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// BOSH SAHIFA kartochkalari (<see cref="DashboardSummary"/>) — "Dars jadvali" sahifasi tepasidagi
/// 12 ta raqam. Asosiy xavf: "ketdi" ni noto'g'ri sanash (guruh almashtirish yoki sertifikat bilan
/// tugatish churn bo'lib ko'rinmasin) va o'quvchi holatini BITTA a'zolikdan olish.
/// </summary>
public class DashboardSummaryTests
{
    private const string Month = "2026-09";
    private const string Today = "2026-09-18";

    private static StudentGroup M(
        string student, string group, string status, string joined,
        string activated = "", string? left = null, string frozen = "",
        bool isActive = true, bool yearFreeze = false, List<string>? past = null) => new()
        {
            StudentId = student, GroupId = group, Status = status, JoinedAt = joined,
            ActivatedAt = activated, LeftAt = left, FrozenAt = frozen,
            IsActive = isActive && left is null, YearFreeze = yearFreeze, PastPeriods = past ?? [],
        };

    private static readonly Dictionary<string, string> Courses = new()
    {
        ["gA"] = "eng", ["gB"] = "eng", ["gM"] = "math", ["gNoCourse"] = "",
    };

    // ============================ Lidlar ============================

    [Fact]
    public void Buyurtmalar_faqat_aylantirilmagan_lidlar()
    {
        var leads = new[]
        {
            new DashboardSummary.LeadRow("l1", false),
            new DashboardSummary.LeadRow("l2", true),
            new DashboardSummary.LeadRow("l3", false),
        };
        Assert.Equal(2, DashboardSummary.OpenLeads(leads, [], "2026-10-09"));
    }

    [Fact]
    public void Buyurtmalar_BIRINCHI_DARSGA_yozilgan_lidni_sanamaydi__ikkala_bolimda_qolmaydi()
    {
        // Mijoz shikoyati (2026-10-09): lid «Birinchi dars»ga o'tkazilganda «Buyurtmalar»dan o'chmasdi.
        var today = "2026-10-09";
        var leads = new[]
        {
            new DashboardSummary.LeadRow("buyurtma", false),
            new DashboardSummary.LeadRow("sinovda", false),
            new DashboardSummary.LeadRow("sinov_otib_ketgan", false),
            new DashboardSummary.LeadRow("sinovga_kelgan", false),
        };
        var trials = new[]
        {
            new DashboardSummary.TrialRow("sinovda", "pending", "2026-10-12T15:00"),
            // Sanasi o'tgan, belgilanmagan sinov — «Birinchi dars» ro'yxatida QIZIL bo'lib turadi.
            new DashboardSummary.TrialRow("sinov_otib_ketgan", "pending", "2026-10-01T15:00"),
            // Natijasi belgilangan — «Birinchi dars»dan chiqqan, buyurtmalarga qaytadi.
            new DashboardSummary.TrialRow("sinovga_kelgan", "came", "2026-10-05T15:00"),
        };

        Assert.Equal(2, DashboardSummary.OpenLeads(leads, trials, today));
        // Ikki ro'yxat KESISHMAYDI va birgalikda barcha ochiq lidlarni qamraydi.
        var first = LeadFirstLesson.Select(leads,
            trials.Select(t => new LeadFirstLesson.TrialRow("", t.LeadId, "", t.Result, t.ScheduledAt)), today);
        Assert.Equal(leads.Length, DashboardSummary.OpenLeads(leads, trials, today) + first.Count);
    }

    [Fact]
    public void Birinchi_darsga_keladiganlar_faqat_bugun_yoki_keyingi_belgilanmagan_sinov()
    {
        var leads = new[]
        {
            new DashboardSummary.LeadRow("today", false),
            new DashboardSummary.LeadRow("future", false),
            new DashboardSummary.LeadRow("past", false),
            new DashboardSummary.LeadRow("came", false),
            new DashboardSummary.LeadRow("converted", true),
        };
        var trials = new[]
        {
            new DashboardSummary.TrialRow("today", "pending", "2026-09-18T09:00"),
            new DashboardSummary.TrialRow("future", "pending", "2026-09-25T15:00"),
            // Bir lidda ikkita sinov — BITTA sanaladi.
            new DashboardSummary.TrialRow("future", "pending", "2026-09-26T15:00"),
            // O'tib ketgan, belgilanmagan — "keladigan" emas.
            new DashboardSummary.TrialRow("past", "pending", "2026-09-10T09:00"),
            // Natijasi belgilangan.
            new DashboardSummary.TrialRow("came", "came", "2026-09-20T09:00"),
            // Aylantirilgan lid ochiq buyurtma emas.
            new DashboardSummary.TrialRow("converted", "pending", "2026-09-20T09:00"),
            // O'chirilgan lidning yetim sinovi.
            new DashboardSummary.TrialRow("deleted", "pending", "2026-09-20T09:00"),
        };
        Assert.Equal(2, DashboardSummary.FirstLessonLeads(leads, trials, Today));
    }

    // ============================ Ketganlar ============================

    [Fact]
    public void Sinovda_turib_shu_oyda_chiqarilgan_yangi_oquvchidan_ketgan()
    {
        var ms = new[] { M("s1", "gA", "trial", "2026-09-01", left: "2026-09-10") };
        Assert.Equal((1, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Aktivlashgan_holda_shu_oyda_chiqarilgan_aktivdan_ketgan()
    {
        var ms = new[] { M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10") };
        Assert.Equal((0, 1), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Guruh_almashtirish_bir_kurs_ichida_KETISH_EMAS()
    {
        var ms = new[]
        {
            M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-05"),
            M("s1", "gB", "active", "2026-09-06", activated: "2026-09-06"),
        };
        Assert.Equal((0, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Sertifikat_bilan_tugatish_churn_EMAS()
    {
        var ms = new[] { M("s1", "gA", "completed", "2026-05-01", activated: "2026-05-03", left: "2026-09-10") };
        Assert.Equal((0, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Otgan_oyda_ketgan_bu_oyga_TUSHMAYDI()
    {
        var ms = new[] { M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-08-30") };
        Assert.Equal((0, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Sinovga_qaytarilgan_lekin_ilgari_aktiv_bolgan_aktivdan_ketgan_hisoblanadi()
    {
        // ReturnToTrial ActivatedAt ni tozalaydi, lekin yopilgan davr PastPeriods da qoladi.
        var ms = new[]
        {
            M("s1", "gA", "trial", "2026-05-01", left: "2026-09-12", past: ["2026-05-03|2026-08-01"]),
        };
        Assert.Equal((0, 1), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Ikki_kursdan_ketgan_oquvchi_BIR_marta_sanaladi()
    {
        var ms = new[]
        {
            M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
            M("s1", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-11"),
        };
        Assert.Equal((0, 1), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Kurssiz_guruhdan_ketish_ham_korinadi()
    {
        var ms = new[] { M("s1", "gNoCourse", "trial", "2026-09-01", left: "2026-09-10") };
        Assert.Equal((1, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    // ============================ Kartochka RO'YXATLARI ============================

    [Fact]
    public void Ketganlar_royxati_sanoq_bilan_TENG_va_chiqib_ketgan_guruh_bilan()
    {
        var ms = new[]
        {
            M("new1", "gA", "trial", "2026-09-01", left: "2026-09-10"),
            M("act1", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-12"),
            // guruh almashtirish — ro'yxatga ham TUSHMAYDI (sanoqdagidek)
            M("move", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-05"),
            M("move", "gB", "active", "2026-09-06", activated: "2026-09-06"),
            // sertifikat bilan tugatgan — churn emas
            M("done", "gA", "completed", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
        };
        var (fromNew, fromActive) = DashboardSummary.LeftStudentsInMonth(ms, Courses, Month);

        Assert.Equal(DashboardSummary.LeftInMonth(ms, Courses, Month), (fromNew.Count, fromActive.Count));
        Assert.Equal(new DashboardSummary.MovementRow("2026-09-10", "gA"), Assert.Single(fromNew).Value);
        Assert.Equal("new1", Assert.Single(fromNew).Key);
        Assert.Equal(new DashboardSummary.MovementRow("2026-09-12", "gM"), fromActive["act1"]);
        Assert.Single(fromActive);
    }

    [Fact]
    public void Ikki_kursdan_ketgan_oquvchi_royxatda_BIR_qator_eng_kechki_ketish_bilan()
    {
        var ms = new[]
        {
            M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-11"),
            M("s1", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
        };
        var (_, fromActive) = DashboardSummary.LeftStudentsInMonth(ms, Courses, Month);
        Assert.Equal(new DashboardSummary.MovementRow("2026-09-11", "gA"), Assert.Single(fromActive).Value);
    }

    [Fact]
    public void Birinchi_tolov_royxati_sanoq_bilan_TENG_va_birinchi_sana_bilan()
    {
        var pays = new[]
        {
            new DashboardSummary.PaymentRow("new", "2026-09-20"),
            new DashboardSummary.PaymentRow("new", "2026-09-03"),
            new DashboardSummary.PaymentRow("old", "2026-08-15"),
            new DashboardSummary.PaymentRow("old", "2026-09-15"),
        };
        var list = DashboardSummary.FirstPaymentStudentsInMonth(pays, Month);
        Assert.Equal(DashboardSummary.FirstPaymentsInMonth(pays, Month), list.Count);
        Assert.Equal(new DashboardSummary.MovementRow("2026-09-03", ""), list["new"]);
    }

    // ============================ Holatlar ============================

    [Fact]
    public void Muzlatilgan_faqat_BOSHQA_faol_azoligi_yoqlar()
    {
        var ms = new[]
        {
            // s1: eski guruhida muzlatilgan, yangisida o'qiyapti — AKTIV, muzlatilgan emas.
            M("s1", "gA", "frozen", "2026-01-01", activated: "2026-01-02", frozen: "2026-06-01"),
            M("s1", "gM", "active", "2026-06-02", activated: "2026-06-02"),
            // s2: faqat muzlatilgan.
            M("s2", "gA", "frozen", "2026-01-01", activated: "2026-01-02", frozen: "2026-06-01"),
            // s3: «aktiv muzlatish» — baribir muzlatilgan.
            M("s3", "gA", "frozen", "2026-01-01", activated: "2026-01-02", frozen: "2026-08-25", yearFreeze: true),
        };
        Assert.Equal(2, DashboardSummary.FrozenStudents(ms));
    }

    [Fact]
    public void Birinchi_tolov_shu_oyda_bolganlar()
    {
        var pays = new[]
        {
            new DashboardSummary.PaymentRow("new", "2026-09-03"),
            new DashboardSummary.PaymentRow("new", "2026-09-20"),
            new DashboardSummary.PaymentRow("old", "2026-08-15"),
            new DashboardSummary.PaymentRow("old", "2026-09-15"),
            new DashboardSummary.PaymentRow("broken", ""),
        };
        Assert.Equal(1, DashboardSummary.FirstPaymentsInMonth(pays, Month));
    }

    [Fact]
    public void Compute_arxivdagilar_joriy_sanoqlarga_kirmaydi_va_Arxivlar_da_sanaladi()
    {
        var students = new[]
        {
            new DashboardSummary.StudentRow("a", false, -100),
            new DashboardSummary.StudentRow("t", false, 0),
            new DashboardSummary.StudentRow("x", true, -500),
        };
        var ms = new[]
        {
            M("a", "gA", "active", "2026-05-01", activated: "2026-05-01"),
            M("a", "gM", "trial", "2026-09-01"),
            M("t", "gA", "trial", "2026-09-01"),
            M("x", "gA", "active", "2026-05-01", activated: "2026-05-01"),
        };
        var groups = new[]
        {
            new DashboardSummary.GroupRow("gA", "eng", false, "active"),
            new DashboardSummary.GroupRow("gM", "math", false, "full"),
            new DashboardSummary.GroupRow("gOld", "eng", true, "archived"),
        };

        var dto = DashboardSummary.Compute(
            leads: [], trials: [], deletedLeadAts: ["2026-09-02T10:00:00", "2026-08-30T10:00:00"],
            students, ms, groups, payments: [], Month, Today);

        Assert.Equal(1, dto.ActiveStudents);   // "x" arxivda
        Assert.Equal(2, dto.NewStudents);      // "a" (boshqa kursda sinov) + "t"
        Assert.Equal(1, dto.Debtors);          // arxivdagi qarzdor sanalmaydi
        Assert.Equal(1, dto.Archived);
        Assert.Equal(2, dto.Groups);           // arxivlangan guruh sanalmaydi
        Assert.Equal(1, dto.OrdersLeft);       // faqat shu oyda o'chirilgan lid
        Assert.Equal(Month, dto.Month);
    }

    // ============================ Baza bilan (EF tarjimasi) ============================

    [Fact]
    public async Task BuildAsync_bazadan_toliq_hisoblaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var eng = new Subject { Name = "Ingliz tili" };
        ctx.Subjects.Add(eng);
        var gA = new Group { Name = "A", CourseId = eng.Id };
        var gB = new Group { Name = "B", CourseId = eng.Id, IsArchived = true, Status = "archived" };
        ctx.Classes.AddRange(gA, gB);

        var active = new Student { FullName = "Aktiv", Balance = -50_000 };
        var leaver = new Student { FullName = "Ketgan" };
        var archived = new Student { FullName = "Arxiv", IsArchived = true };
        ctx.Students.AddRange(active, leaver, archived);

        ctx.StudentGroups.AddRange(
            M(active.Id, gA.Id, "active", "2026-05-01", activated: "2026-05-01"),
            M(leaver.Id, gB.Id, "active", "2026-03-01", activated: "2026-03-01", left: "2026-09-04"));

        var open = new Lead { FullName = "Ochiq" };
        var converted = new Lead { FullName = "Aylangan", ConvertedStudentId = active.Id };
        // Sinovi YO'Q ochiq lid — yagona «Buyurtma»: "Ochiq" sinovga yozilgan, ya'ni u «Birinchi dars»da (2026-10-09).
        var order = new Lead { FullName = "Buyurtma" };
        ctx.Leads.AddRange(open, converted, order);
        ctx.TrialLessons.Add(new TrialLesson
        {
            LeadId = open.Id, GroupId = gA.Id, ScheduledAt = "2026-09-19T10:00", Result = "pending",
        });
        ctx.ArchivedRecords.AddRange(
            new ArchivedRecord { Type = "lead", EntityId = "x1", DeletedAt = "2026-09-01T12:00:00" },
            new ArchivedRecord { Type = "student", EntityId = "x2", DeletedAt = "2026-09-01T12:00:00" });
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { StudentId = active.Id, Direction = "income", Category = "tuition", Amount = 100, Date = "2026-09-02" },
            // Vozvrat va boshqa kategoriya "birinchi to'lov" emas.
            new FinanceTransaction { StudentId = leaver.Id, Direction = "expense", Category = "refund", Amount = 10, Date = "2026-09-02" },
            new FinanceTransaction { StudentId = leaver.Id, Direction = "income", Category = "other", Amount = 10, Date = "2026-09-02" });
        await ctx.SaveChangesAsync();

        var dto = await DashboardSummary.BuildAsync(ctx, new DateOnly(2026, 9, 18));

        Assert.Equal(1, dto.Orders);
        Assert.Equal(1, dto.FirstLesson);
        Assert.Equal(0, dto.NewStudents);
        Assert.Equal(1, dto.ActiveStudents);
        Assert.Equal(1, dto.OrdersLeft);
        Assert.Equal(0, dto.NewLeft);
        Assert.Equal(1, dto.ActiveLeft);       // arxivlangan guruhdan ketgan tarix ham ko'rinadi
        Assert.Equal(1, dto.Debtors);
        Assert.Equal(1, dto.Groups);
        Assert.Equal(1, dto.FirstPayments);
        Assert.Equal(0, dto.Frozen);
        Assert.Equal(1, dto.Archived);
    }

    // ============================ Qarzdorlar toifasi (2026-11-03) ============================

    [Theory]
    [InlineData(false, "active", DashboardSummary.DebtActive)]
    [InlineData(false, "trial", DashboardSummary.DebtTrial)]
    [InlineData(false, "frozen", DashboardSummary.DebtFrozen)]
    [InlineData(false, "yearFrozen", DashboardSummary.DebtFrozen)]
    [InlineData(false, "", DashboardSummary.DebtLeft)]
    [InlineData(true, "active", DashboardSummary.DebtArchived)]   // arxiv hamma narsadan ustun (eski tirik a'zolik)
    [InlineData(true, "", DashboardSummary.DebtArchived)]
    public void DebtorKind_toifa(bool archived, string state, string expected) =>
        Assert.Equal(expected, DashboardSummary.DebtorKind(archived, state));

    [Fact]
    public void Qarzdorlar_kartochkasi_FAQAT_aktiv_oquvchilarni_sanaydi()
    {
        var students = new[]
        {
            new DashboardSummary.StudentRow("act", false, -100),     // aktiv qarzdor
            new DashboardSummary.StudentRow("mix", false, -100),     // bir guruhda aktiv, boshqasidan ketgan — AKTIV
            new DashboardSummary.StudentRow("tri", false, -100),     // sinovdagi
            new DashboardSummary.StudentRow("frz", false, -100),     // muzlatilgan
            new DashboardSummary.StudentRow("lft", false, -100),     // guruhdan chiqqan, arxivlanmagan
            new DashboardSummary.StudentRow("arc", true, -100),      // arxivdagi
            new DashboardSummary.StudentRow("pos", false, 50),       // qarzsiz aktiv
        };
        var ms = new[]
        {
            M("act", "gA", "active", "2026-05-01", activated: "2026-05-01"),
            M("mix", "gA", "active", "2026-05-01", activated: "2026-05-01"),
            M("mix", "gB", "active", "2026-03-01", activated: "2026-03-01", left: "2026-08-01"),
            M("tri", "gA", "trial", "2026-09-01"),
            M("frz", "gA", "frozen", "2026-05-01", activated: "2026-05-01", frozen: "2026-08-01"),
            M("lft", "gA", "active", "2026-05-01", activated: "2026-05-01", left: "2026-09-04"),
            M("arc", "gA", "active", "2026-05-01", activated: "2026-05-01"),
            M("pos", "gA", "active", "2026-05-01", activated: "2026-05-01"),
        };
        var groups = new[]
        {
            new DashboardSummary.GroupRow("gA", "eng", false, "active"),
            new DashboardSummary.GroupRow("gB", "eng", false, "active"),
        };

        var dto = DashboardSummary.Compute([], [], [], students, ms, groups, [], Month, Today);
        Assert.Equal(2, dto.Debtors);

        var kinds = DashboardSummary.DebtorKinds(students, ms);
        Assert.Equal(6, kinds.Count); // qarzsiz o'quvchi toifaga tushmaydi
        Assert.Equal(DashboardSummary.DebtActive, kinds["mix"]);
        Assert.Equal(DashboardSummary.DebtTrial, kinds["tri"]);
        Assert.Equal(DashboardSummary.DebtFrozen, kinds["frz"]);
        Assert.Equal(DashboardSummary.DebtLeft, kinds["lft"]);
        Assert.Equal(DashboardSummary.DebtArchived, kinds["arc"]);
    }

    [Fact]
    public async Task OtherDebtorsAsync_toifalar_summalar_va_royxat()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Name = "A" };
        ctx.Classes.Add(g);
        var active = new Student { FullName = "Aktiv", Balance = -100_000 };
        var leaver = new Student { FullName = "Ketgan", Balance = -300_000 };
        var archived = new Student { FullName = "Arxiv", Balance = -50_000, IsArchived = true, ArchivedAt = "2026-10-01" };
        var trial = new Student { FullName = "Sinov", Balance = -20_000 };
        var paid = new Student { FullName = "Toza", Balance = 0 };
        ctx.Students.AddRange(active, leaver, archived, trial, paid);
        ctx.StudentGroups.AddRange(
            M(active.Id, g.Id, "active", "2026-05-01", activated: "2026-05-01"),
            M(leaver.Id, g.Id, "active", "2026-05-01", activated: "2026-05-01", left: "2026-10-10"),
            M(trial.Id, g.Id, "trial", "2026-10-20"),
            M(paid.Id, g.Id, "active", "2026-05-01", activated: "2026-05-01"));
        await ctx.SaveChangesAsync();

        var r = await DashboardSummary.OtherDebtorsAsync(ctx);

        Assert.Equal((1, 100_000m), (r.ActiveCount, r.ActiveTotal));
        Assert.Equal(["left", "archived", "trial", "frozen"], r.Groups.Select(x => x.Kind));
        Assert.Equal((1, 300_000m), (r.Groups[0].Count, r.Groups[0].Total));
        Assert.Equal((1, 50_000m), (r.Groups[1].Count, r.Groups[1].Total));
        Assert.Equal((1, 20_000m), (r.Groups[2].Count, r.Groups[2].Total));
        Assert.Equal((0, 0m), (r.Groups[3].Count, r.Groups[3].Total));
        // Aktiv qarzdor ro'yxatda YO'Q, eng katta qarz tepada, summa musbat.
        Assert.Equal(["Ketgan", "Arxiv", "Sinov"], r.Items.Select(i => i.FullName));
        Assert.Equal(300_000m, r.Items[0].Debt);
        Assert.Equal("2026-10-01", r.Items[1].ArchivedAt);
        Assert.False(r.Truncated);
    }
}
