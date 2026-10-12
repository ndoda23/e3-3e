using CreditFlow.Application.Applications;
using CreditFlow.Domain.Applications;

namespace CreditFlow.Application.UnitTests.Applications;

/// <summary>Uploads are recognised by their content, limited to 10 MB, and their names cannot carry paths.</summary>
public sealed class DocumentUploadPolicyTests
{
    private static readonly byte[] Pdf = [.. "%PDF-1.4"u8.ToArray(), 1, 2, 3];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1];

    public static TheoryData<byte[], string> Accepted => new()
    {
        { Pdf, "application/pdf" },
        { Png, "image/png" },
        { Jpeg, "image/jpeg" },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void The_type_comes_from_the_first_bytes(byte[] content, string contentType)
    {
        var upload = DocumentUploadPolicy.Validate("file.exe", content);

        Assert.True(upload.IsSuccess);
        Assert.Equal(contentType, upload.Value.ContentType);
        Assert.Equal(64, upload.Value.Sha256.Length);
    }

    [Fact]
    public void Other_content_is_rejected_whatever_its_name()
    {
        Assert.Equal(DocumentUploadPolicy.UnsupportedType, DocumentUploadPolicy.Validate("scan.pdf", "<html>"u8.ToArray()).Error);
        Assert.Equal(DocumentUploadPolicy.UnsupportedType, DocumentUploadPolicy.Validate("scan.pdf", [0x25, 0x50]).Error);
    }

    [Fact]
    public void Ten_megabytes_is_the_limit()
    {
        var atLimit = new byte[DocumentUploadPolicy.MaxBytes];
        Pdf.CopyTo(atLimit, 0);
        var overLimit = new byte[DocumentUploadPolicy.MaxBytes + 1];
        Pdf.CopyTo(overLimit, 0);

        Assert.True(DocumentUploadPolicy.Validate("a.pdf", atLimit).IsSuccess);
        Assert.Equal(DocumentUploadPolicy.TooLarge, DocumentUploadPolicy.Validate("a.pdf", overLimit).Error);
        Assert.Equal(DocumentUploadPolicy.Empty, DocumentUploadPolicy.Validate("a.pdf", []).Error);
    }

    [Theory]
    [InlineData(@"C:\Users\me\income.pdf", "income.pdf")]
    [InlineData("../../etc/income.pdf", "income.pdf")]
    [InlineData("in\u0000come.pdf", "income.pdf")]
    [InlineData("  ", "document")]
    [InlineData(null, "document")]
    public void File_names_are_reduced_to_a_safe_last_segment(string? name, string expected)
    {
        Assert.Equal(expected, DocumentUploadPolicy.Validate(name, Pdf).Value.FileName);
    }

    [Fact]
    public void Long_file_names_keep_their_end()
    {
        var name = new string('a', ApplicationDocument.FileNameMaxLength + 10) + ".pdf";

        var stored = DocumentUploadPolicy.Validate(name, Pdf).Value.FileName;

        Assert.Equal(ApplicationDocument.FileNameMaxLength, stored.Length);
        Assert.EndsWith(".pdf", stored, StringComparison.Ordinal);
    }
}
