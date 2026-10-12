using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Clients;

public static class ClientErrors
{
    public static readonly Error InvalidPersonalNumber = Error.Validation(
        "Client.InvalidPersonalNumber", "A personal number has exactly 11 digits.", "PersonalNumber");

    public static readonly Error DuplicatePersonalNumber = Error.Conflict(
        "Client.DuplicatePersonalNumber", "A client with this personal number is already registered.", "PersonalNumber");

    public static readonly Error InvalidFirstName = Error.Validation(
        "Client.InvalidFirstName", "A first name is required (up to 100 characters).", "FirstName");

    public static readonly Error InvalidLastName = Error.Validation(
        "Client.InvalidLastName", "A last name is required (up to 100 characters).", "LastName");

    public static readonly Error InvalidBirthDate = Error.Validation(
        "Client.InvalidBirthDate", "The birth date must be in the past and less than 120 years ago.", "BirthDate");

    public static readonly Error InvalidPhone = Error.Validation(
        "Client.InvalidPhone", "A phone number is required: digits, spaces and a leading + (up to 20 characters).", "Phone");

    public static readonly Error InvalidEmail = Error.Validation(
        "Client.InvalidEmail", "The email address is not valid.", "Email");

    public static readonly Error InvalidAddress = Error.Validation(
        "Client.InvalidAddress", "An address is required (up to 300 characters).", "Address");

    public static readonly Error InvalidDocumentType = Error.Validation(
        "Client.InvalidDocumentType", "The document type is not valid.", "DocumentType");

    public static readonly Error InvalidDocumentNumber = Error.Validation(
        "Client.InvalidDocumentNumber", "A document number has 1 to 20 letters or digits.", "DocumentNumber");

    public static readonly Error InvalidDocumentDates = Error.Validation(
        "Client.InvalidDocumentDates", "The document must expire after it was issued.", "DocumentExpiresOn");

    public static readonly Error InvalidIncome = Error.Validation(
        "Client.InvalidIncome", "A monthly income must be greater than zero.", "MonthlyAmount");

    public static readonly Error InvalidIncomeSource = Error.Validation(
        "Client.InvalidIncomeSource", "The income source is required (up to 200 characters).", "Source");

    public static readonly Error InvalidObligation = Error.Validation(
        "Client.InvalidObligation",
        "The monthly payment must be greater than zero and the outstanding amount not negative, in the same currency.",
        "MonthlyPayment");

    public static readonly Error InvalidLender = Error.Validation(
        "Client.InvalidLender", "The lender is required (up to 200 characters).", "Lender");

    public static readonly Error IncomeNotFound = Error.NotFound("Client.IncomeNotFound", "The income was not found.");

    public static readonly Error ObligationNotFound = Error.NotFound(
        "Client.ObligationNotFound", "The obligation was not found.");

    public static readonly Error NotFound = Error.NotFound("Client.NotFound", "The client was not found.");
}
