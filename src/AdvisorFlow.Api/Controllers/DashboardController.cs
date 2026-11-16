using AdvisorFlow.Application.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace AdvisorFlow.Api.Controllers;

/// <summary>
/// The practice dashboard (M6, contract v1.5): pipeline by stage with a weighted forecast, won and
/// lost outcomes, follow-up counts and households by status. Read-only.
/// </summary>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await dashboard.GetSummaryAsync(ct));
}
