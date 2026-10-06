using System.ComponentModel.DataAnnotations;
using Lams.Application.Branches;
using Lams.Domain.Branches;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Lams.Web.Areas.Admin.Models;

/// <summary>Create / edit form of a branch. The code is set once and never changes.</summary>
public sealed class BranchForm
{
    [Display(Name = "Field.BranchCode")]
    public string? Code { get; set; }

    [Display(Name = "Field.BranchName")]
    public string? Name { get; set; }

    [Display(Name = "Field.City")]
    public string? City { get; set; }

    /// <summary>The branch being edited (<c>null</c> on create).</summary>
    [BindNever]
    public BranchSummary? Existing { get; set; }

    public static BranchForm From(BranchSummary branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return new() { Code = branch.Code, Name = branch.Name, City = branch.City, Existing = branch };
    }

    public CreateBranchCommand ToCreateCommand() => new(Code ?? string.Empty, Name ?? string.Empty, City ?? string.Empty);

    public UpdateBranchCommand ToUpdateCommand(BranchId id) => new(id, Name ?? string.Empty, City ?? string.Empty);
}
