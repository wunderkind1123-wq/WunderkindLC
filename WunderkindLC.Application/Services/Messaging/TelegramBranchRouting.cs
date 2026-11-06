using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// One Telegram bot for every branch (owner, 2026-11-06). Only the home instance may call getUpdates, while each branch
/// has its own database, so home forwards each update to the branch that owns it. Pure rules, tested without a network;
/// the transport is <see cref="TelegramBranchRouter"/>. Rules: .claude/rules/branches.md.
///
/// <para>A person may be linked in several branches. Buttons and link requests therefore carry their branch in the
/// payload (<c>@yakkatut|hrack:…</c>); home strips the tag before the handler sees it, so handlers stay unchanged.</para>
/// </summary>
public static partial class TelegramBranchRouting
{
    public const string SecretHeader = "X-Wlc-Router-Secret";

    /// <summary>Telegram's limit for <c>callback_data</c>; the tag is skipped rather than break the button.</summary>
    public const int CallbackDataLimit = 64;

    [GeneratedRegex("^[a-z0-9]{2,20}$")]
    private static partial Regex KeyRule();

    public sealed record UpdateFacts(long ChatId, string CallbackId, bool IsCallback, string? Tag, string? Data);

    /// <summary><c>"yakkatut|http://app_yakkatut:8080;furqat|http://app_furqat:8080"</c> → key → base address.</summary>
    public static IReadOnlyDictionary<string, string> ParseRoutes(string? raw)
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw)) return routes;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bar = part.IndexOf('|');
            if (bar <= 0) continue;
            var key = part[..bar].Trim();
            var url = part[(bar + 1)..].Trim().TrimEnd('/');
            if (!KeyRule().IsMatch(key)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            routes[key] = url;
        }
        return routes;
    }

    public static string Tag(string data, string? key)
    {
        if (string.IsNullOrEmpty(key) || !KeyRule().IsMatch(key)) return data;
        var tagged = $"@{key}|{data}";
        return Encoding.UTF8.GetByteCount(tagged) <= CallbackDataLimit ? tagged : data;
    }

    /// <summary>Tags a button or link payload with THIS instance's branch; unchanged where routing is off.</summary>
    public static string TagOwn(string data) => Tag(data, AppSecrets.TelegramBranchKey);

    public static (string? Key, string? Data) Untag(string? data)
    {
        if (data is null || data.Length < 2 || data[0] != '@') return (null, data);
        var bar = data.IndexOf('|');
        if (bar <= 1) return (null, data);
        var key = data[1..bar];
        return KeyRule().IsMatch(key) ? (key, data[(bar + 1)..]) : (null, data);
    }

    /// <summary>Who an update belongs to; null = not a private conversation (group lead cards stay on home).</summary>
    public static UpdateFacts? Read(JsonElement upd)
    {
        if (upd.TryGetProperty("callback_query", out var cq))
        {
            if (cq.TryGetProperty("message", out var cqMsg) && cqMsg.TryGetProperty("chat", out var cqChat)
                && cqChat.TryGetProperty("type", out var cqType) && cqType.GetString() != "private") return null;
            if (!cq.TryGetProperty("from", out var from) || !from.TryGetProperty("id", out var fromId)) return null;
            var (tag, data) = Untag(cq.TryGetProperty("data", out var d) ? d.GetString() : null);
            return new UpdateFacts(fromId.GetInt64(), cq.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "", true, tag, data);
        }
        if (!upd.TryGetProperty("message", out var msg)) return null;
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatId)) return null;
        if (!chat.TryGetProperty("type", out var type) || type.GetString() != "private") return null;
        var (appTag, appData) = msg.TryGetProperty("web_app_data", out var app) && app.TryGetProperty("data", out var ad)
            ? Untag(ad.GetString())
            : (null, null);
        return new UpdateFacts(chatId.GetInt64(), "", false, appTag, appData);
    }

    /// <summary>The update as JSON with the tag removed, so the branch's handlers see their own original payload.</summary>
    public static string WithoutTag(JsonElement upd, UpdateFacts facts)
    {
        if (facts.Tag is null) return upd.GetRawText();
        var node = JsonNode.Parse(upd.GetRawText())!;
        if (facts.IsCallback) node["callback_query"]!["data"] = facts.Data;
        else node["message"]!["web_app_data"]!["data"] = facts.Data;
        return node.ToJsonString();
    }

    /// <summary>A branch's bot buttons open its own site, the home instance keeps its fixed address.</summary>
    public static string PanelUrl(string homeUrl, bool isBranch, string? host)
    {
        if (!isBranch || string.IsNullOrWhiteSpace(host)) return homeUrl;
        var clean = host.Trim();
        var scheme = clean.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) clean = clean[(scheme + 3)..];
        return $"https://{clean.TrimEnd('/')}/";
    }

    public static bool SecretMatches(string? given, string? expected)
    {
        if (string.IsNullOrEmpty(given) || string.IsNullOrEmpty(expected)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }
}
