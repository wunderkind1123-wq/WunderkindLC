using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// Home instance: decides which branch owns an update and forwards it (<c>api/internal/telegram/update</c>). A tagged
/// payload names its branch. Otherwise a chat linked on home stays on home (a person in several branches talks to home
/// in plain messages); any other chat goes to the branch that knows it, and an unknown chat gets home's normal gate.
/// </summary>
public sealed class TelegramBranchRouter(
    IHttpClientFactory httpFactory, ILogger<TelegramBranchRouter> logger, TelegramBranchRouter.Settings? settings = null)
{
    public const string HttpClientName = "telegram-branch-router";
    public const string BranchDownText = "⏳ Filial vaqtincha javob bermayapti. Birozdan keyin qayta urinib ko'ring.";

    private static readonly TimeSpan OwnerCacheTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan KnowsTimeout = TimeSpan.FromSeconds(2);
    private const int MaxCachedOwners = 10_000;

    public sealed record Settings(string? OwnKey, string? Routes, string? Secret);
    public sealed record Failed(long ChatId, string CallbackId);

    /// <summary><c>Handled</c>: a branch took it. <c>Failure</c>: its branch did not answer. Else run <c>Local</c> here.</summary>
    public sealed record Decision(bool Handled, JsonElement Local, Failed? Failure);

    private readonly string _ownKey = settings is null ? AppSecrets.TelegramBranchKey : settings.OwnKey ?? "";
    private readonly string _secret = settings is null ? AppSecrets.TelegramRouterSecret : settings.Secret ?? "";
    private readonly IReadOnlyDictionary<string, string> _routes = TelegramBranchRouting.ParseRoutes(
        settings is null ? AppSecrets.TelegramBranchRoutes : settings.Routes);
    private readonly ConcurrentDictionary<long, (string Key, DateTime Until)> _owners = new();

    /// <summary>Off = no key and no routes: a plain single-instance setup, nothing is tagged or forwarded.</summary>
    public bool Enabled => _ownKey.Length > 0 || _routes.Count > 0;

    public async Task<Decision> RouteAsync(JsonElement upd, Func<long, CancellationToken, Task<bool>> linkedHere, CancellationToken ct)
    {
        var facts = TelegramBranchRouting.Read(upd);
        if (facts is null) return Here(upd);

        if (facts.Tag is { } tag)
        {
            var clean = TelegramBranchRouting.WithoutTag(upd, facts);
            if (tag == _ownKey || !_routes.TryGetValue(tag, out var tagged)) return Here(Parse(clean));
            return await ForwardAsync(tagged, clean, facts, ct);
        }

        if (_routes.Count == 0 || await linkedHere(facts.ChatId, ct)) return Here(upd);

        var owner = await FindOwnerAsync(facts.ChatId, ct);
        return owner is null ? Here(upd) : await ForwardAsync(owner, upd.GetRawText(), facts, ct);
    }

    private static Decision Here(JsonElement upd) => new(false, upd, null);

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private async Task<string?> FindOwnerAsync(long chatId, CancellationToken ct)
    {
        if (_owners.TryGetValue(chatId, out var hit) && hit.Until > DateTime.UtcNow
            && _routes.TryGetValue(hit.Key, out var cached)) return cached;

        foreach (var (key, url) in _routes)
        {
            if (!await KnowsAsync(url, chatId, ct)) continue;
            if (_owners.Count >= MaxCachedOwners) _owners.Clear();
            _owners[chatId] = (key, DateTime.UtcNow + OwnerCacheTtl);
            return url;
        }
        return null;
    }

    private async Task<bool> KnowsAsync(string baseUrl, long chatId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(KnowsTimeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/internal/telegram/knows?chatId={chatId}");
            req.Headers.Add(TelegramBranchRouting.SecretHeader, _secret);
            using var res = await httpFactory.CreateClient(HttpClientName).SendAsync(req, timeout.Token);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("Filial chatni tekshira olmadi: {Status}", (int)res.StatusCode);
                return false;
            }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(timeout.Token));
            return doc.RootElement.TryGetProperty("linked", out var linked) && linked.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Filial javob bermadi (chat tekshiruvi)");
            return false;
        }
    }

    private async Task<Decision> ForwardAsync(string baseUrl, string json, TelegramBranchRouting.UpdateFacts facts, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/internal/telegram/update")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add(TelegramBranchRouting.SecretHeader, _secret);
            using var res = await httpFactory.CreateClient(HttpClientName).SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return new Decision(true, default, null);
            logger.LogWarning("Filial yangilanishni qabul qilmadi: {Status}", (int)res.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Filialga yangilanish yetmadi");
        }
        return new Decision(false, default, new Failed(facts.ChatId, facts.CallbackId));
    }
}
