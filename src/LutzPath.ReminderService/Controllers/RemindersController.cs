using LutzPath.ReminderService.DTOs;
using LutzPath.ReminderService.Exceptions;
using LutzPath.ReminderService.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LutzPath.ReminderService.Controllers;

[ApiController]
[Route("api/reminders")]
public class RemindersController : ControllerBase
{
    private readonly IReminderService _service;

    public RemindersController(IReminderService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<ReminderResponse>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetAllAsync(cancellationToken));
    }

    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<ReminderResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("user/{userId:int:min(1)}")]
    public async Task<ActionResult<List<ReminderResponse>>> GetByUserId(int userId, CancellationToken cancellationToken)
    {
        return Ok(await _service.GetByUserIdAsync(userId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ReminderResponse>> Create(ReminderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: 400, title: "Invalid reminder", detail: exception.Message);
        }
        catch (ServiceUnavailableException exception)
        {
            return Problem(statusCode: 503, title: "Dependency unavailable", detail: exception.Message);
        }
    }

    [HttpPut("{id:int:min(1)}")]
    public async Task<ActionResult<ReminderResponse>> Update(int id, ReminderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.UpdateAsync(id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: 400, title: "Invalid reminder", detail: exception.Message);
        }
        catch (ServiceUnavailableException exception)
        {
            return Problem(statusCode: 503, title: "Dependency unavailable", detail: exception.Message);
        }
    }

    [HttpDelete("{id:int:min(1)}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
