using System.ComponentModel.DataAnnotations;
using CreditFlow.Application.Users;

namespace CreditFlow.Web.Models;

// Display names and messages are resource keys, localized through SharedResource.

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Login")]
    public string Login { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    [Display(Name = "Field.Password")]
    public string Password { get; set; } = string.Empty;
}

public sealed class TwoFactorViewModel
{
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(7, MinimumLength = 6, ErrorMessage = "Validation.AuthenticatorCode")]
    [Display(Name = "Field.AuthenticatorCode")]
    public string Code { get; set; } = string.Empty;
}

public sealed class RecoveryCodeViewModel
{
    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.RecoveryCode")]
    public string RecoveryCode { get; set; } = string.Empty;
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    [Display(Name = "Field.CurrentPassword")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    [Display(Name = "Field.NewPassword")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Validation.PasswordsDoNotMatch")]
    [Display(Name = "Field.ConfirmPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class EnableAuthenticatorViewModel
{
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(7, MinimumLength = 6, ErrorMessage = "Validation.AuthenticatorCode")]
    [Display(Name = "Field.AuthenticatorCode")]
    public string Code { get; set; } = string.Empty;

    public string SharedKey { get; set; } = string.Empty;

    public string AuthenticatorUri { get; set; } = string.Empty;

    /// <summary>The user is here because MFA is required (shown as an explanation).</summary>
    public bool Required { get; set; }
}

public sealed record RecoveryCodesViewModel(IReadOnlyList<string> Codes, string ContinueUrl);

public sealed record MyAccountViewModel(UserDetails Details, CreditFlow.Infrastructure.Identity.TwoFactorStatus? TwoFactor);
