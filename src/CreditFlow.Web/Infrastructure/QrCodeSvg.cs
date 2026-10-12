using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Html;
using QRCoder;

namespace CreditFlow.Web.Infrastructure;

/// <summary>
/// Renders a QR code as inline SVG built from the module matrix: one path, presentation attributes only (no style
/// attributes, so it is allowed by the CSP), and no external image request.
/// </summary>
public static class QrCodeSvg
{
    public static IHtmlContent Render(string text, string accessibleLabel)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;

        var path = new StringBuilder();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (matrix[y][x])
                {
                    path.Append(CultureInfo.InvariantCulture, $"M{x} {y}h1v1h-1z");
                }
            }
        }

        var label = System.Net.WebUtility.HtmlEncode(accessibleLabel);
        return new HtmlString(
            $"""<svg class="qr" role="img" aria-label="{label}" viewBox="0 0 {size} {size}" width="200" height="200" shape-rendering="crispEdges" xmlns="http://www.w3.org/2000/svg"><rect width="{size}" height="{size}" fill="#ffffff"/><path d="{path}" fill="#111827"/></svg>""");
    }
}
