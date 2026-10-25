using System.ComponentModel.DataAnnotations;

namespace LutzPath.JobApplicationService.Validation;

// Store timestamps in one time zone. Send JSON values such as 2026-11-05T15:00:00Z.
[AttributeUsage(AttributeTargets.Property)]
public sealed class UtcDateTimeAttribute : ValidationAttribute
{
    public UtcDateTimeAttribute()
    {
        ErrorMessage = "Use a UTC timestamp ending in Z, for example 2026-11-05T15:00:00Z.";
    }

    public override bool IsValid(object? value) =>
        value is null || value is DateTime { Kind: DateTimeKind.Utc };
}
