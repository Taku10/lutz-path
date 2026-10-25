using Microsoft.AspNetCore.Mvc;

namespace LutzPath.ReminderService.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse("healthy", "ReminderService"));
    }
}

public record HealthResponse(string Status, string Service);
