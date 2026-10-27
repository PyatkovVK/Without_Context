using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ContextGuard.Core;

// Parses bounded in-memory files. No scripts, macros, linked content or embedded attachments run.
public static class ContextFileTextReader
{
    public const int MaxDocumentBytes = 8 * 1024 * 1024;
    public const int MaxImagePixels = 16_000_000;
    private const int MaxXmlCharacters = 4 * 1024 * 1024;
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm", ".odt", ".ods", ".odp" };
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico" };
    private static readonly HashSet<string> TextAliasExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".markdown", ".cs", ".xaml", ".axaml", ".csproj", ".fs", ".vb", ".py", ".js", ".jsx", ".ts", ".tsx", ".html", ".css", ".scss", ".json", ".yaml", ".yml", ".toml", ".xml", ".sql", ".go", ".rs", ".java", ".kt", ".php", ".rb", ".c", ".cpp", ".h", ".hpp", ".sh", ".ps1", ".bat", ".ini", ".cfg", ".conf", ".csv", ".tsv", ".log", ".r", ".vue", ".svelte", ".svg" };
    public static bool IsAttachment(string extension) => Documents.Contains(extension) || Images.Contains(extension);
    // Unknown extensions can contain private names. They are readable, but never enter outbound aliases.
    public static string AliasExtension(string extension) => extension.Length == 0 || IsAttachment(extension) || TextAliasExtensions.Contains(extension)
        ? extension.ToLowerInvariant() : ".txt";

    public static async Task<string> ReadAsync(string extension, byte[] bytes,
        Func<byte[], CancellationToken, Task<string>>? inspectImage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Images.Contains(extension)) return await ReadImageAsync(bytes, inspectImage, ct);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            try { return await ReadPdfAsync(bytes, inspectImage, ct); }
            catch (Exception e) when (e is not SafeOperationException and not OperationCanceledException and not OutOfMemoryException)
            { throw new SafeOperationException("FileUnreadable"); }
        }
        if (Documents.Contains(extension)) return ReadOffice(extension.ToLowerInvariant(), bytes, ct);
        try
        {
            string text;
            if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) text = new UTF32Encoding(false, true, true).GetString(bytes, 4, bytes.Length - 4);
            else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) text = new UTF32Encoding(true, true, true).GetString(bytes, 4, bytes.Length - 4);
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) text = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) text = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
            else text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            if (text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t' and not '\f'))
                throw new SafeOperationException("FileUnsupported");
            return text;
        }
        catch (DecoderFallbackException) { throw new SafeOperationException("FileUnsupported"); }
    }

    private static async Task<string> ReadImageAsync(byte[] bytes,
        Func<byte[], CancellationToken, Task<string>>? inspectImage, CancellationToken ct)
    {
        if (inspectImage is null) throw new SafeOperationException("ImageNeedsLocalModel");
        using var stream = new MemoryStream(bytes, false);
        using var codec = SKCodec.Create(stream);
        if (codec is null) throw new SafeOperationException("FileUnreadable");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > MaxImagePixels)
            throw new SafeOperationException("FileTooLarge");
        ct.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null) throw new SafeOperationException("FileUnreadable");
        double scale = Math.Min(1, 2048d / Math.Max(bitmap.Width, bitmap.Height));
        using var resized = scale < 1 ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)),
            Math.Max(1, (int)(bitmap.Height * scale))), SKFilterQuality.Medium) : null;
        using var image = SKImage.FromBitmap(resized ?? bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null || encoded.Size > MaxDocumentBytes) throw new SafeOperationException("FileTooLarge");
        ct.ThrowIfCancellationRequested();
        // Re-encoding strips image metadata; the selected validated local model sees pixels only.
        string text = await inspectImage(encoded.ToArray(), ct);
        ct.ThrowIfCancellationRequested();
        return Limit(text);
    }

    private static async Task<string> ReadPdfAsync(byte[] bytes,
        Func<byte[], CancellationToken, Task<string>>? inspectImage, CancellationToken ct)
    {
        PdfDocument document;
        try { document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false, MaxStackDepth = 64 }); }
        catch { throw new SafeOperationException("FileUnreadable"); }
        using (document)
        {
            if (document.NumberOfPages > 100) throw new SafeOperationException("FileTooLarge");
            var text = new StringBuilder();
            for (int pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                ct.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);
                string pageText = ContentOrderTextExtractor.GetText(page);
                if (string.IsNullOrWhiteSpace(pageText))
                {
                    int count = 0;
                    foreach (var image in page.GetImages())
                    {
                        if (++count > 8 || (long)image.WidthInSamples * image.HeightInSamples > MaxImagePixels)
                            throw new SafeOperationException("FileTooLarge");
                        ct.ThrowIfCancellationRequested();
                        byte[] imageBytes = image.RawBytes.ToArray();
                        if (imageBytes.Length > MaxDocumentBytes) throw new SafeOperationException("FileTooLarge");
                        if (imageBytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8 }))
                            pageText += await ReadImageAsync(imageBytes, inspectImage, ct) + "\n";
                        else if (image.TryGetPng(out var png) && png.Length <= MaxDocumentBytes)
                            pageText += await ReadImageAsync(png, inspectImage, ct) + "\n";
                    }
                }
                Append(text, pageText + "\n");
            }
            return text.ToString();
        }
    }

    private static string ReadOffice(string extension, byte[] bytes, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > 512) throw new SafeOperationException("FileTooLarge");
        long expanded = 0;
        XDocument Read(ZipArchiveEntry entry)
        {
            ct.ThrowIfCancellationRequested();
            expanded += entry.Length;
            if (entry.Length > MaxXmlCharacters || expanded > 16 * 1024 * 1024) throw new SafeOperationException("FileTooLarge");
            using var input = entry.Open();
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlCharacters });
            return XDocument.Load(reader);
        }
        var text = new StringBuilder();
        if (extension is ".xlsx" or ".xlsm")
        {
            var sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
            string[] shared = sharedEntry is null ? [] : Read(sharedEntry).Descendants().Where(e => e.Name.LocalName == "si")
                .Take(10_001).Select(e => string.Concat(e.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value))).ToArray();
            if (shared.Length > 10_000 || shared.Sum(s => s.Length) > FolderCollector.MaxContextChars) throw new SafeOperationException("FileTooLarge");
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                foreach (var row in Read(entry).Descendants().Where(e => e.Name.LocalName == "row"))
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c"))
                    {
                        string value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ??
                            string.Concat(cell.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                        if ((string?)cell.Attribute("t") == "s")
                            value = int.TryParse(value, out int index) && index >= 0 && index < shared.Length ? shared[index] : "";
                        Append(text, ((string?)cell.Attribute("r") is { } reference ? reference + ": " : "") + value + "\t");
                    }
                    Append(text, "\n");
                }
            }
        }
        else
        {
            IEnumerable<ZipArchiveEntry> entries = extension switch
            {
                ".docx" or ".docm" => archive.Entries.Where(e => e.FullName == "word/document.xml" ||
                    (e.FullName.StartsWith("word/header", StringComparison.Ordinal) || e.FullName.StartsWith("word/footer", StringComparison.Ordinal) ||
                    e.FullName is "word/footnotes.xml" or "word/endnotes.xml") && e.FullName.EndsWith(".xml", StringComparison.Ordinal)),
                ".pptx" or ".pptm" => archive.Entries.Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal)),
                _ => archive.Entries.Where(e => e.FullName == "content.xml")
            };
            foreach (var entry in entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                var document = Read(entry);
                foreach (var paragraph in document.Descendants().Where(e => e.Name.LocalName is "p" or "h"))
                {
                    ct.ThrowIfCancellationRequested();
                    string value = extension is ".odt" or ".ods" or ".odp" ? paragraph.Value :
                        string.Concat(paragraph.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                    Append(text, value + "\n");
                }
            }
        }
        return text.ToString();
    }

    private static string Limit(string text)
    {
        if (text.Length > FolderCollector.MaxContextChars) throw new SafeOperationException("FileTooLarge");
        return text;
    }
    private static void Append(StringBuilder output, string text)
    {
        if ((long)output.Length + text.Length > FolderCollector.MaxContextChars) throw new SafeOperationException("FileTooLarge");
        output.Append(text);
    }
}
