using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Applications;

public static class ApplicationErrors
{
    public static readonly Error NotFound = Error.NotFound("Application.NotFound", "The application was not found.");

    public static readonly Error InvalidAmount = Error.Validation(
        "Application.InvalidAmount", "The amount must be greater than zero.", "Amount");

    public static readonly Error InvalidTerm = Error.Validation(
        "Application.InvalidTerm", "The term must be at least one month.", "TermMonths");

    public static readonly Error InvalidRate = Error.Validation(
        "Application.InvalidRate", "The interest rate must be 0 to 100 %.", "AnnualRate");

    public static readonly Error InvalidScheduleType = Error.Validation(
        "Application.InvalidScheduleType", "The schedule type is not valid.", "ScheduleType");

    public static readonly Error ProductInactive = Error.Validation(
        "Application.ProductInactive", "This product is not offered any more.", "ProductId");

    public static readonly Error AmountOutOfRange = Error.Validation(
        "Application.AmountOutOfRange", "The amount (GEL equivalent) is outside the product's range.", "Amount");

    public static readonly Error TermOutOfRange = Error.Validation(
        "Application.TermOutOfRange", "The term is outside the product's range.", "TermMonths");

    public static readonly Error RateOutOfRange = Error.Validation(
        "Application.RateOutOfRange", "The interest rate is outside the product's range.", "AnnualRate");

    public static readonly Error CurrencyNotOffered = Error.Validation(
        "Application.CurrencyNotOffered", "The product is not offered in this currency.", "Currency");

    public static readonly Error ScheduleTypeNotOffered = Error.Validation(
        "Application.ScheduleTypeNotOffered", "The product does not offer this schedule type.", "ScheduleType");

    public static readonly Error CollateralRequired = Error.Validation(
        "Application.CollateralRequired", "This product requires collateral.", "Collateral");

    public static readonly Error InvalidCollateral = Error.Validation(
        "Application.InvalidCollateral", "Collateral needs a type and a description (up to 300 characters).", "Description");

    public static readonly Error InvalidValuation = Error.Validation(
        "Application.InvalidValuation", "The market value must be positive and the valuation date not in the future.", "MarketValue");

    public static readonly Error CollateralNotFound = Error.NotFound(
        "Application.CollateralNotFound", "The collateral was not found.");

    public static readonly Error OneBorrower = Error.Validation(
        "Application.OneBorrower", "An application has exactly one borrower.", "Parties");

    public static readonly Error DuplicateParty = Error.Validation(
        "Application.DuplicateParty", "A client can take only one role on an application.", "Parties");

    public static readonly Error PartyNotFound = Error.NotFound(
        "Application.PartyNotFound", "The client is not a co-borrower or guarantor of this application.");

    public static readonly Error OpenApplicationExists = Error.Conflict(
        "Application.OpenApplicationExists", "The borrower already has an open application for this product.");

    public static readonly Error DocumentNotFound = Error.NotFound(
        "Application.DocumentNotFound", "The document was not found.");

    public static readonly Error ChecklistIncomplete = Error.Conflict(
        "Application.ChecklistIncomplete", "Required documents are missing; scoring cannot start.");

    public static readonly Error ReasonRequired = Error.Validation(
        "Application.ReasonRequired", "A reason is required (up to 1000 characters).", "Reason");

    public static readonly Error OverrideReasonRequired = Error.Validation(
        "Application.OverrideReasonRequired",
        "Failed scoring rules can be overridden only with the override permission and a reason.", "OverrideReason");

    public static readonly Error CreatorCannotDecide = Error.Forbidden(
        "Application.CreatorCannotDecide", "The creator of an application cannot decide on it (four-eyes).");

    public static readonly Error AlreadyDecided = Error.Forbidden(
        "Application.AlreadyDecided", "You have already taken a decision on this application (one decision per person).");

    public static readonly Error AuthorityTooLow = Error.Forbidden(
        "Application.AuthorityTooLow", "This application is above your approval authority.");

    public static readonly Error NoPendingOffer = Error.Conflict(
        "Application.NoPendingOffer", "There is no valid offer to accept.");

    public static readonly Error OfferExpired = Error.Conflict(
        "Application.OfferExpired", "The offer has expired.");

    public static readonly Error InvalidCondition = Error.Validation(
        "Application.InvalidCondition", "A condition needs a type and a description (up to 300 characters).", "Conditions");

    public static readonly Error ConditionNotFound = Error.NotFound(
        "Application.ConditionNotFound", "The condition was not found.");

    public static readonly Error ConditionsOutstanding = Error.Conflict(
        "Application.ConditionsOutstanding", "Not every condition is met.");

    public static readonly Error DisbursementInProgress = Error.Conflict(
        "Application.DisbursementInProgress", "A disbursement has been requested; the application can no longer be cancelled.");

    public static Error InvalidTransition(ApplicationStatus from, ApplicationStatus to) => Error.Conflict(
        "Application.InvalidTransition", $"An application cannot move from {from} to {to}.");

    public static Error WrongStatus(ApplicationStatus status) => Error.Conflict(
        "Application.WrongStatus", $"This is not possible while the application is {status}.");

    public static Error PartyTooYoung(string name) => Error.Validation(
        "Application.PartyTooYoung", $"{name} must be at least {LendingPolicy.MinAgeAtApplication} years old.", "Parties");

    public static Error PartyTooOldAtMaturity(string name) => Error.Validation(
        "Application.PartyTooOldAtMaturity",
        $"{name} would be older than {LendingPolicy.MaxAgeAtMaturity} at the last installment.", "TermMonths");

    public static Error IdentityDocumentInvalid(string name) => Error.Validation(
        "Application.IdentityDocumentInvalid", $"The identity document of {name} is not valid today.", "Parties");
}

/// <summary>Fixed lending rules of the task (not configurable).</summary>
public static class LendingPolicy
{
    public const int MinAgeAtApplication = 18;
    public const int MaxAgeAtMaturity = 75;

    /// <summary>An approval or counter-offer is valid this many days.</summary>
    public const int OfferValidityDays = 30;

    /// <summary>A file still in Underwriting this many working days after it arrived is escalated to a senior.</summary>
    public const int EscalationWorkingDays = 2;
}
