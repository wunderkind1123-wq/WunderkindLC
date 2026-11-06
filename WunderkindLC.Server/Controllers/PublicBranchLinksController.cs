using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Branch list for the login page of the single Telegram bot's Mini App (.claude/rules/branches.md). It is read BEFORE
/// login, so it is anonymous on purpose: only names and public site addresses, the same values anyone can resolve.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/branch-links")]
public class PublicBranchLinksController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public ActionResult<List<BranchLinks.Link>> Get() =>
        BranchLinks.Parse(config["Branches:Links"], Request.Host.Host);
}
