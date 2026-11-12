using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// LID (SOTUV) TOPSHIRIQLARI — lid sahifasining «keyingi qadam» bloki (sotuv rejasi 1-qism). Topshiriqning o'zi
/// «Topshiriqlar» (HR) bo'limida yashaydi — <see cref="HrTasksService"/> (<c>HrTasksService.Leads.cs</c>); bu yerda
/// faqat lid tomonidan kirish.
///
/// <para>Ruxsat — lidlar bilan BIR XIL (<c>leads.list</c>): HR panel kalitisiz ham lid bilan ishlaydigan xodim
/// lidga topshiriq qo'yadi. Yaratish — POST (<c>create</c>); bajarish, bekor qilish va tahrir — PUT (<c>edit</c>).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("leads.list", "leads.callcenter")]
[Route("api/admin/lead-tasks")]
public class LeadTasksController(
    AppDbContext db, HrTasksService hr, TelegramService telegram, ILogger<LeadTasksController> logger) : ControllerBase
{
    /// <summary>Topshiriqlar. <c>leadId</c> — lid sahifasi; <c>status</c>: open (standart) | done | cancelled | all.</summary>
    [HttpGet]
    public async Task<ActionResult<List<LeadTaskDto>>> List([FromQuery] string? leadId = null, [FromQuery] string? status = null) =>
        await hr.LeadTasksAsync(leadId, status, HttpContext.RequestAborted);

    /// <summary>Turlar + mas'ul tanlovi («Topshiriqlar»dagi faol panel xodimlari).</summary>
    [HttpGet("meta")]
    public async Task<ActionResult<LeadTaskMetaDto>> Meta() =>
        new LeadTaskMetaDto(
            [.. LeadTasks.Types.Select(kv => new LeadTaskTypeDto(kv.Key, kv.Value))],
            await hr.LeadTaskPeopleAsync(HttpContext.RequestAborted),
            Me().UserId);

    [HttpPost]
    public async Task<ActionResult<LeadTaskDto>> Create(LeadTaskPayload p)
    {
        var leadId = (p.LeadId ?? "").Trim();
        var before = await db.Leads.Where(l => l.Id == leadId).Select(l => l.AssigneeUserId).FirstOrDefaultAsync();
        var (res, fail) = await Run(() => hr.CreateLeadTaskAsync(leadId, Draft(p), Me(), HttpContext.RequestAborted));
        if (fail is not null) return fail;
        // Biriktirilmagan lidga mas'ul yozilgan bo'lsa — Telegram lid kartasi ham yangilansin.
        var after = await db.Leads.AsNoTracking().Where(l => l.Id == leadId).Select(l => l.AssigneeUserId).FirstOrDefaultAsync();
        if (after != before) await LeadNotifier.SyncCardAsync(db, telegram, leadId, logger: logger);
        return res!.Task!;
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<LeadTaskDto>> Update(string id, LeadTaskPayload p)
    {
        var (res, fail) = await Run(() => hr.UpdateLeadTaskAsync(id, Draft(p), Me(), HttpContext.RequestAborted));
        if (fail is not null) return fail;
        return res!.Task!;
    }

    /// <summary>Bajarish (natija majburiy) + ixtiyoriy keyingi topshiriq — bitta saqlashda.</summary>
    [HttpPut("{id}/complete")]
    public async Task<ActionResult<CompleteLeadTaskResult>> Complete(string id, CompleteLeadTaskRequest req)
    {
        var (res, fail) = await Run(() =>
            hr.CompleteLeadTaskAsync(id, req.Result, req.Next is null ? null : Draft(req.Next), Me(), HttpContext.RequestAborted));
        if (fail is not null) return fail;
        return new CompleteLeadTaskResult(res!.Task!, res.Next);
    }

    [HttpPut("{id}/cancel")]
    public async Task<ActionResult<LeadTaskDto>> Cancel(string id, CancelLeadTaskRequest? req)
    {
        var (res, fail) = await Run(() => hr.CancelLeadTaskAsync(id, req?.Reason, Me(), HttpContext.RequestAborted));
        if (fail is not null) return fail;
        return res!.Task!;
    }

    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Servis chaqiruvi: natija 400/404 bo'lsa — javob; HR domenining kutilgan xatosi (masalan, parallel so'rovda topshiriq
    /// allaqachon yopilgan) ham 500 emas, sabab matni bilan (`error-visibility.md` §5, `HrTasksController.Fail`).
    /// </summary>
    private async Task<(HrTasksService.LeadTaskResult? Res, ActionResult? Fail)> Run(Func<Task<HrTasksService.LeadTaskResult>> op)
    {
        try
        {
            var res = await op();
            return (res, Fail(res));
        }
        catch (HrTasksException ex) { return (null, HrTasksController.Fail(ex)); }
    }

    private ActionResult? Fail(HrTasksService.LeadTaskResult res) => res.Status switch
    {
        200 => null,
        404 => NotFound(new { message = res.Error }),
        _ => BadRequest(new { message = res.Error }),
    };

    private static LeadTasks.Draft Draft(LeadTaskPayload p) =>
        new(p.Type, p.Text, p.DueAt, p.AllDay, p.ResponsibleUserId);

    private LeadTasks.Actor Me()
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;
        var name = User.Identity?.Name
                   ?? User.FindFirst("name")?.Value
                   ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                   ?? "Admin";
        return new LeadTasks.Actor(string.IsNullOrWhiteSpace(id) ? null : id, name);
    }
}
