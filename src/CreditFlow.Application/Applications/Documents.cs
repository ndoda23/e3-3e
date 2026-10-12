using System.Security.Cryptography;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Applications;

/// <summary>Stores document contents (the database, in the same transaction as the metadata).</summary>
public interface IDocumentStore
{
    void Add(ApplicationLineId id, byte[] content);

    Task<byte[]?> GetAsync(ApplicationLineId id, CancellationToken cancellationToken);

    Task RemoveAsync(ApplicationLineId id, CancellationToken cancellationToken);
}

/// <summary>An uploaded file after validation: the type comes from the content, never from the client.</summary>
public sealed record ValidatedUpload(string FileName, string ContentType, byte[] Content, string Sha256);

/// <summary>
/// Upload rules: at most 10 MB; only PDF, PNG and JPEG, recognised by their first bytes (the declared content type and
/// the extension are ignored); the file name is reduced to its last segment without control characters.
/// </summary>
public static class DocumentUploadPolicy
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly (byte[] Signature, string ContentType)[] Signatures =
    [
        ("%PDF-"u8.ToArray(), "application/pdf"),
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png"),
        ([0xFF, 0xD8, 0xFF], "image/jpeg"),
    ];

    public static readonly Error Empty = Error.Validation(
        "Document.Empty", "The file is empty.", "File");

    public static readonly Error TooLarge = Error.Validation(
        "Document.TooLarge", "A document may be at most 10 MB.", "File");

    public static readonly Error UnsupportedType = Error.Validation(
        "Document.UnsupportedType", "Only PDF, PNG and JPEG documents are accepted.", "File");

    public static Result<ValidatedUpload> Validate(string? fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length == 0)
        {
            return Result.Failure<ValidatedUpload>(Empty);
        }

        if (content.Length > MaxBytes)
        {
            return Result.Failure<ValidatedUpload>(TooLarge);
        }

        var contentType = Signatures.FirstOrDefault(s => content.AsSpan().StartsWith(s.Signature)).ContentType;
        if (contentType is null)
        {
            return Result.Failure<ValidatedUpload>(UnsupportedType);
        }

        return Result.Success(new ValidatedUpload(
            SafeFileName(fileName), contentType, content, Convert.ToHexStringLower(SHA256.HashData(content))));
    }

    private static string SafeFileName(string? fileName)
    {
        // Windows and Unix separators alike: a client may send a full path.
        var lastSegment = (fileName ?? string.Empty).Split('/', '\\')[^1];
        var name = new string(lastSegment.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "document";
        }

        return name.Length <= ApplicationDocument.FileNameMaxLength ? name : name[^ApplicationDocument.FileNameMaxLength..];
    }
}
