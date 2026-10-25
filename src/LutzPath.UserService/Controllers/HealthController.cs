using Microsoft.AspNetCore.Mvc;

namespace LutzPath.UserService.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse("healthy", "UserService"));
    }
}

public record HealthResponse(string Status, string Service);
