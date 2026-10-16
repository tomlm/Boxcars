using System.Globalization;
using System.Text;

namespace Boxcars.Services.Reports;

/// <summary>
/// Writes lines of monospace text to a minimal PDF using the built-in Courier font, so plain-text reports need no
/// PDF library. Lines that don't fit on one US Letter page continue on the next.
/// </summary>
public static class TextPdfWriter
{
    private const double PageWidth = 612d;
    private const double PageHeight = 792d;
    private const double Margin = 36d;
    private const double FontSize = 8.5d;
    private const double LineHeight = 9.6d;

    public static int LinesPerPage => (int)((PageHeight - (2 * Margin)) / LineHeight);

    public static byte[] Write(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var pages = lines.Count == 0
            ? [[]]
            : lines.Chunk(LinesPerPage).ToList();

        // Object numbers: 1 catalog, 2 page tree, 3 font, then a page and its content stream per page.
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            BuildPageTree(pages.Count),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"
        };

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var contentObjectNumber = 5 + (pageIndex * 2);
            objects.Add(FormattableString.Invariant(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObjectNumber} 0 R >>"));

            var content = BuildContentStream(pages[pageIndex]);
            objects.Add(string.Concat(
                "<< /Length ", content.Length.ToString(CultureInfo.InvariantCulture), " >>\nstream\n",
                content,
                "\nendstream"));
        }

        return Serialize(objects);
    }

    private static string BuildPageTree(int pageCount)
    {
        var kids = string.Join(' ', Enumerable.Range(0, pageCount).Select(pageIndex => $"{4 + (pageIndex * 2)} 0 R"));
        return $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>";
    }

    private static string BuildContentStream(IReadOnlyList<string> lines)
    {
        var content = new StringBuilder();
        content.Append(FormattableString.Invariant(
            $"BT\n/F1 {FontSize} Tf\n{LineHeight} TL\n{Margin} {PageHeight - Margin - FontSize} Td\n"));

        foreach (var line in lines)
        {
            content.Append('(').Append(EscapeText(line)).Append(") Tj T*\n");
        }

        content.Append("ET");
        return content.ToString();
    }

    private static string EscapeText(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            // Courier with WinAnsiEncoding covers Latin-1; anything else is replaced.
            var safeCharacter = character is >= ' ' and <= 'ÿ' ? character : '?';
            if (safeCharacter is '\\' or '(' or ')')
            {
                escaped.Append('\\');
            }

            escaped.Append(safeCharacter);
        }

        return escaped.ToString();
    }

    private static byte[] Serialize(List<string> objects)
    {
        var encoding = Encoding.Latin1;
        using var stream = new MemoryStream();

        void WriteText(string text)
        {
            var bytes = encoding.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        WriteText("%PDF-1.4\n");

        var offsets = new List<long>(objects.Count);
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(stream.Position);
            WriteText($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var crossReferenceOffset = stream.Position;
        var crossReference = new StringBuilder();
        crossReference.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        crossReference.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            crossReference.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        crossReference.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\n");
        crossReference.Append("startxref\n").Append(crossReferenceOffset).Append("\n%%EOF\n");
        WriteText(crossReference.ToString());

        return stream.ToArray();
    }
}
