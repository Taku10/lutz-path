using System.ComponentModel.DataAnnotations;
using LutzPath.JobApplicationService.Models;

namespace LutzPath.JobApplicationService.DTOs;

public class UpdateApplicationStatusRequest
{
    [Required, EnumDataType(typeof(ApplicationStatus))]
    public ApplicationStatus? Status { get; set; }
}
