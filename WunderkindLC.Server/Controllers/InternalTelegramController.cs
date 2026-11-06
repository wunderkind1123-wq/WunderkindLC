using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Branch side of the single bot: the home instance forwards updates here over the internal Docker network. There is no
/// user session, the shared secret is the gate. Closed (404) without a secret and on an instance that polls by itself,
/// and nginx does not serve this path at all. Rules: .claude/rules/branches.md.
/// </summary>
[ApiController]
[AllowAnonymous]
[DisableRateLimiting]
[Route("api/internal/telegram")]
public class InternalTelegramController(TelegramBotService bot, IHostApplicationLifetime lifetime) : ControllerBase
{
    private bool Open() =>
        AppSecrets.TelegramPollingDisabled
        && TelegramBranchRouting.SecretMatches(Request.Headers[TelegramBranchRouting.SecretHeader], AppSecrets.TelegramRouterSecret);

    [HttpPost("update")]
    public async Task<IActionResult> Update([FromBody] JsonElement update)
    {
        if (!Open()) return NotFound();
        // Not RequestAborted: a slow answer must not be cancelled half way because home stopped waiting.
        await bot.HandleForwardedUpdateAsync(update, lifetime.ApplicationStopping);
        return Ok();
    }

    [HttpGet("knows")]
    public async Task<IActionResult> Knows([FromQuery] long chatId, [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (!Open()) return NotFound();
        return Ok(new { linked = await db.TelegramRegistrations.AnyAsync(r => r.ChatId == chatId, ct) });
    }
}
