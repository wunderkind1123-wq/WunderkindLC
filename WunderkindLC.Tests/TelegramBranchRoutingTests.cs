using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>One bot for all branches: which instance handles an update (.claude/rules/branches.md).</summary>
public class TelegramBranchRoutingTests
{
    private const string Routes = "yakkatut|http://app_yakkatut:8080;furqat|http://app_furqat:8080";

    private static JsonElement Json(string s)
    {
        using var d = JsonDocument.Parse(s);
        return d.RootElement.Clone();
    }

    private static string PrivateChat(long chat) => "\"chat\":{\"id\":" + chat + ",\"type\":\"private\"}";

    private static JsonElement Text(long chat = 5) =>
        Json("{\"message\":{" + PrivateChat(chat) + ",\"text\":\"salom\"}}");

    private static JsonElement Callback(string data, long chat = 5) =>
        Json("{\"callback_query\":{\"id\":\"cb1\",\"from\":{\"id\":" + chat + "},\"data\":\"" + data + "\",\"message\":{" + PrivateChat(chat) + "}}}");

    private static JsonElement AppData(string data, long chat = 5) =>
        Json("{\"message\":{" + PrivateChat(chat) + ",\"web_app_data\":{\"data\":\"" + data + "\"}}}");

    private sealed class FakeBranches(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler, IHttpClientFactory
    {
        public List<(string Url, string? Body, string? Secret)> Calls { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            req.Headers.TryGetValues(TelegramBranchRouting.SecretHeader, out var secret);
            Calls.Add((req.RequestUri!.ToString(), body, secret?.FirstOrDefault()));
            return answer(req);
        }
    }

    private static HttpResponseMessage Ok(string json = "{}") =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static FakeBranches YakkatutOwnsChat() => new(req =>
        req.RequestUri!.AbsolutePath.EndsWith("/knows")
            ? Ok(req.RequestUri.Host == "app_yakkatut" ? """{"linked":true}""" : """{"linked":false}""")
            : Ok());

    private static TelegramBranchRouter Router(FakeBranches http, string? routes = Routes, string? key = "main") =>
        new(http, NullLogger<TelegramBranchRouter>.Instance, new TelegramBranchRouter.Settings(key, routes, "s3cret"));

    private static Task<bool> NotHere(long _, CancellationToken __) => Task.FromResult(false);
    private static Task<bool> LinkedHere(long _, CancellationToken __) => Task.FromResult(true);

    [Fact]
    public async Task Filialda_ulangan_chat_oz_filialiga_boradi_va_kalit_yuboriladi()
    {
        var http = YakkatutOwnsChat();
        var d = await Router(http).RouteAsync(Text(), NotHere, default);

        Assert.True(d.Handled);
        var post = Assert.Single(http.Calls, c => c.Body is not null);
        Assert.Equal("http://app_yakkatut:8080/api/internal/telegram/update", post.Url);
        Assert.Equal("s3cret", post.Secret);
        Assert.Contains("salom", post.Body);
    }

    [Fact]
    public async Task Asosiy_nusxada_ulangan_chat_shu_yerda_qoladi_va_filial_soralmaydi()
    {
        var http = YakkatutOwnsChat();
        var d = await Router(http).RouteAsync(Text(), LinkedHere, default);

        Assert.False(d.Handled);
        Assert.Null(d.Failure);
        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task Hech_kim_tanimagan_chat_asosiy_nusxadagi_darvozaga_tushadi()
    {
        var http = new FakeBranches(_ => Ok("""{"linked":false}"""));
        var d = await Router(http).RouteAsync(Text(), NotHere, default);

        Assert.False(d.Handled);
        Assert.Null(d.Failure);
        Assert.DoesNotContain(http.Calls, c => c.Body is not null);
    }

    [Fact]
    public async Task Tugma_tegi_chat_asosiyda_ulangan_bolsa_ham_oz_filialiga_boradi_va_teg_olib_tashlanadi()
    {
        var http = YakkatutOwnsChat();
        var d = await Router(http).RouteAsync(Callback("@yakkatut|hrack:task1"), LinkedHere, default);

        Assert.True(d.Handled);
        var post = Assert.Single(http.Calls);
        Assert.Equal("http://app_yakkatut:8080/api/internal/telegram/update", post.Url);
        Assert.Contains("\"data\":\"hrack:task1\"", post.Body);
        Assert.DoesNotContain("@yakkatut", post.Body);
    }

    [Fact]
    public async Task Ulanish_sorovi_tegi_notanish_chatni_ham_filialga_olib_boradi()
    {
        var http = YakkatutOwnsChat();
        var d = await Router(http).RouteAsync(AppData("@furqat|wlclink:abc"), NotHere, default);

        Assert.True(d.Handled);
        var post = Assert.Single(http.Calls);
        Assert.StartsWith("http://app_furqat:8080/", post.Url);
        Assert.Contains("\"data\":\"wlclink:abc\"", post.Body);
    }

    [Theory]
    [InlineData("@main|hrack:task1")]      // home's own button
    [InlineData("@olibketilgan|hrack:task1")] // a branch that no longer exists
    public async Task Oz_tegi_yoki_notanish_teg_shu_yerda_teglarsiz_ishlanadi(string data)
    {
        var http = YakkatutOwnsChat();
        var d = await Router(http).RouteAsync(Callback(data), LinkedHere, default);

        Assert.False(d.Handled);
        Assert.Empty(http.Calls);
        Assert.Equal("hrack:task1", d.Local.GetProperty("callback_query").GetProperty("data").GetString());
    }

    [Fact]
    public async Task Guruh_xabarlari_doim_asosiy_nusxada()
    {
        var http = YakkatutOwnsChat();
        var group = Json("""{"message":{"chat":{"id":-100,"type":"supergroup"},"text":"x"}}""");
        var member = Json("""{"my_chat_member":{"chat":{"id":-100,"type":"group"}}}""");

        Assert.False((await Router(http).RouteAsync(group, NotHere, default)).Handled);
        Assert.False((await Router(http).RouteAsync(member, NotHere, default)).Handled);
        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task Filial_ishlamasa_foydalanuvchiga_aytish_uchun_xato_qaytadi()
    {
        var http = new FakeBranches(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var d = await Router(http).RouteAsync(Callback("@yakkatut|hrack:task1", chat: 7), NotHere, default);

        Assert.False(d.Handled);
        Assert.Equal(new TelegramBranchRouter.Failed(7, "cb1"), d.Failure);
    }

    [Fact]
    public async Task Filial_tekshiruvi_yiqilsa_chat_darvozaga_tushadi_router_yiqilmaydi()
    {
        var http = new FakeBranches(_ => throw new HttpRequestException("down"));
        var d = await Router(http).RouteAsync(Text(), NotHere, default);

        Assert.False(d.Handled);
        Assert.Null(d.Failure);
    }

    [Fact]
    public async Task Topilgan_egasi_eslab_qolinadi()
    {
        var http = YakkatutOwnsChat();
        var router = Router(http);
        await router.RouteAsync(Text(), NotHere, default);
        var before = http.Calls.Count(c => c.Body is null);
        await router.RouteAsync(Text(), NotHere, default);

        Assert.Equal(before, http.Calls.Count(c => c.Body is null));
        Assert.Equal(2, http.Calls.Count(c => c.Body is not null));
    }

    [Fact]
    public void Sozlanmagan_nusxada_yonaltirish_ochiq()
    {
        var http = YakkatutOwnsChat();
        Assert.False(Router(http, routes: null, key: "").Enabled);
        Assert.True(Router(http, routes: null, key: "main").Enabled);
    }

    [Theory]
    [InlineData("yakkatut|http://app_yakkatut:8080/", 1)]
    [InlineData(Routes, 2)]
    [InlineData("YOMON KALIT|http://a:1", 0)]
    [InlineData("a|http://a:1", 0)]
    [InlineData("yakkatut|javascript:alert(1)", 0)]
    [InlineData("yakkatut", 0)]
    [InlineData(null, 0)]
    public void Yonalish_royxati_tekshiriladi(string? raw, int count) =>
        Assert.Equal(count, TelegramBranchRouting.ParseRoutes(raw).Count);

    [Fact]
    public void Teg_qoshish_va_olish_bir_birini_toldiradi()
    {
        var tagged = TelegramBranchRouting.Tag("msgack:abc", "yakkatut");
        Assert.Equal("@yakkatut|msgack:abc", tagged);
        Assert.Equal(("yakkatut", "msgack:abc"), TelegramBranchRouting.Untag(tagged));
    }

    [Theory]
    [InlineData("hrack:abc")]
    [InlineData("@|x")]
    [InlineData("@KATTA|x")]
    [InlineData(null)]
    public void Tegsiz_yoki_buzuq_qiymat_oz_holicha_qoladi(string? data) =>
        Assert.Equal((null, data), TelegramBranchRouting.Untag(data));

    [Fact]
    public void Teg_callback_chegarasidan_oshirmaydi_eng_uzun_filial_bilan_ham_topshiriq_tugmasi_sigadi()
    {
        var task = "hrack:" + Guid.NewGuid();
        var slug = new string('a', 20);
        Assert.Equal($"@{slug}|{task}", TelegramBranchRouting.Tag(task, slug));

        var tooLong = new string('x', 60);
        Assert.Equal(tooLong, TelegramBranchRouting.Tag(tooLong, slug));
    }

    [Theory]
    [InlineData(false, "yakkatut.uz", "https://lc.uz/")]
    [InlineData(true, "", "https://lc.uz/")]
    [InlineData(true, "yakkatut.uz", "https://yakkatut.uz/")]
    [InlineData(true, "https://yakkatut.uz/", "https://yakkatut.uz/")]
    public void Panel_manzili(bool isBranch, string host, string expected) =>
        Assert.Equal(expected, TelegramBranchRouting.PanelUrl("https://lc.uz/", isBranch, host));

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("", "", false)]
    [InlineData(null, "abc", false)]
    [InlineData("abc", "", false)]
    public void Maxfiy_kalit(string? given, string? expected, bool ok) =>
        Assert.Equal(ok, TelegramBranchRouting.SecretMatches(given, expected));

    private static string Repo(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }

    [Fact]
    public void Ichki_endpointlar_maxfiy_kalit_ortida_va_faqat_filialda()
    {
        var src = Repo("WunderkindLC.Server", "Controllers", "InternalTelegramController.cs");
        Assert.Contains("AppSecrets.TelegramPollingDisabled", src);
        Assert.Contains("TelegramBranchRouting.SecretMatches", src);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(src, @"\[Http(Get|Post)\(").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(src, @"if \(!Open\(\)\) return NotFound\(\);").Count);
    }

    [Fact]
    public void Nginx_ichki_yolni_tashqariga_bermaydi() =>
        Assert.Contains("location /api/internal/", Repo("infra", "nginx", "nginx.conf"));

    [Fact]
    public void Filial_tokenni_oladi_lekin_polling_qilmaydi_ikkinchi_getUpdates_409_beradi()
    {
        var script = Repo("infra", "branch", "new-branch.sh");
        Assert.Contains("Telegram__PollingDisabled: \"1\"", script);
        Assert.Contains("Telegram__BotToken:", script);
        Assert.Contains("TELEGRAM_BRANCH_ROUTES", script);
    }
}
