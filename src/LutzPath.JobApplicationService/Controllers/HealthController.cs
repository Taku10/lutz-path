using Microsoft.AspNetCore.Mvc;

namespace LutzPath.JobApplicationService.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse("healthy", "JobApplicationService"));
    }
}

public record HealthResponse(string Status, string Service);
