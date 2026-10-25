using System.Text.Json.Serialization;

namespace LutzPath.ReminderService.Clients;

// This API owns its HTTP contract; it does not reference another service's entity project.
public record ApplicationReference(
    [property: JsonRequired] int Id,
    [property: JsonRequired] int UserId);
