using LutzPath.JobApplicationService.DTOs;
using LutzPath.JobApplicationService.Exceptions;
using LutzPath.JobApplicationService.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LutzPath.JobApplicationService.Controllers;

[ApiController]
[Route("api/applications")]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationService _service;

    public ApplicationsController(IApplicationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<ApplicationResponse>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetAllAsync(cancellationToken));
    }

    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<ApplicationResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("user/{userId:int:min(1)}")]
    public async Task<ActionResult<List<ApplicationResponse>>> GetByUserId(int userId, CancellationToken cancellationToken)
    {
        return Ok(await _service.GetByUserIdAsync(userId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ApplicationResponse>> Create(ApplicationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: 400, title: "Invalid application", detail: exception.Message);
        }
        catch (ServiceUnavailableException exception)
        {
            return Problem(statusCode: 503, title: "Dependency unavailable", detail: exception.Message);
        }
    }

    [HttpPut("{id:int:min(1)}")]
    public async Task<ActionResult<ApplicationResponse>> Update(int id, ApplicationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.UpdateAsync(id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: 400, title: "Invalid application", detail: exception.Message);
        }
        catch (ServiceUnavailableException exception)
        {
            return Problem(statusCode: 503, title: "Dependency unavailable", detail: exception.Message);
        }
    }

    [HttpPatch("{id:int:min(1)}/status")]
    public async Task<ActionResult<ApplicationResponse>> UpdateStatus(
        int id, UpdateApplicationStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateStatusAsync(id, request.Status!.Value, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:int:min(1)}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
