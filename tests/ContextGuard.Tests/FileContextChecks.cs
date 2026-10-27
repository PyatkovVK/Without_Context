using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using ContextGuard.Core;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

internal static class FileContextChecks
{
    public static async Task Run()
    {
        using var folder = new TestFolder();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "code.unlisted"), "function launch() { return 42; }");
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "README"), "Extensionless text");
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "unicode.custom"), "Unicode document", new UTF32Encoding(false, true, true));
        await File.WriteAllBytesAsync(Path.Combine(folder.Path, "unknown.bin"), [0, 1, 255]);
        WriteZip(Path.Combine(folder.Path, "notes.docx"), ("word/document.xml", "<document><p><t>Word contact reader@example.com</t></p></document>"));
        WriteZip(Path.Combine(folder.Path, "table.xlsx"),
            ("xl/sharedStrings.xml", "<sst><si><t>Revenue</t></si></sst>"),
            ("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row><c t='s'><v>0</v></c><c><v>125</v></c></row></sheetData></worksheet>"));
        WriteZip(Path.Combine(folder.Path, "slides.pptx"), ("ppt/slides/slide1.xml", "<slide><p><t>Launch roadmap</t></p></slide>"));
        WriteZip(Path.Combine(folder.Path, "open.odt"), ("content.xml", "<document><p>Open document content</p></document>"));
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddText("PDF project plan", 12, new PdfPoint(20, 700), builder.AddStandard14Font(Standard14Font.Helvetica));
        File.WriteAllBytes(Path.Combine(folder.Path, "plan.pdf"), builder.Build());
        File.WriteAllBytes(Path.Combine(folder.Path, "photo.png"), Image());
        int imageCalls = 0;
        var session = new RedactionSession(new(ProtectionProfile.Everything, []));
        var scan = await new FolderCollector().CollectAsync(folder.Path, session, null, null, default, 24_000, (bytes, ct) =>
        {
            imageCalls++; ct.ThrowIfCancellationRequested();
            Check(bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 }), "Images are normalized to metadata-free PNG");
            return Task.FromResult("Diagram contact photo@example.com; password=picture-secret");
        });
        Check(scan.Included == 9 && imageCalls == 1, "Code, extensionless and Unicode text, Office, PDF and image all collected");
        foreach (string value in new[] { "launch()", "Extensionless", "Unicode document", "Word contact", "Revenue", "125", "Launch roadmap", "Open document", "PDF project plan", "Diagram contact" })
            Check(scan.Context.Contains(value), "Extracted content is present: " + value);
        Check(!scan.Context.Contains("reader@example.com") && !scan.Context.Contains("photo@example.com") &&
            !scan.Context.Contains("picture-secret") && !scan.Context.Contains(folder.Path), "Documents and image output enter normal anonymization");
        Check(scan.SkippedFiles?.Single().ReasonKey == "FileUnsupported", "Unsupported binaries have an explicit reason");
        Check(!scan.Context.Contains("unlisted") && scan.Files.Single(file => file.LocalRelativePath == "code.unlisted").Alias.EndsWith(".txt"),
            "Unrecognized extensions cannot leak private file-name fragments through aliases");
        var scanned = new PdfDocumentBuilder();
        scanned.AddPage(PageSize.A4).AddPng(Image(), new PdfRectangle(20, 20, 200, 200));
        string scannedText = await ContextFileTextReader.ReadAsync(".pdf", scanned.Build(), (_, _) => Task.FromResult("Scanned page text"), default);
        Check(scannedText.Contains("Scanned page text"), "Image-only PDF pages use the local image reader");
        WriteZip(Path.Combine(folder.Path, "corrupt.docx"), ("word/document.xml", "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///secret'>]><document><p>&e;</p></document>"));
        var rejected = await new FolderCollector().CollectAsync(folder.Path, new(new(ProtectionProfile.Everything, [])), null, null,
            default, 24_000, (_, _) => Task.FromResult("Image text"));
        Check(rejected.SkippedFiles!.Any(file => file.LocalRelativePath == "corrupt.docx" && file.ReasonKey == "FileUnreadable"), "XML external entities are rejected");
        WriteZip(Path.Combine(folder.Path, "oversized.docx"), ("word/document.xml", "<document>" + new string('x', 4 * 1024 * 1024) + "</document>"));
        var bounded = await new FolderCollector().CollectAsync(folder.Path, new(new(ProtectionProfile.Everything, [])), null, null,
            default, 24_000, (_, _) => Task.FromResult("Image text"));
        Check(bounded.SkippedFiles!.Any(file => file.LocalRelativePath == "oversized.docx" && file.ReasonKey == "FileTooLarge"), "Compressed Office content has a decompression limit");
        using var lateCancel = new CancellationTokenSource();
        var lateImage = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readingImage = ContextFileTextReader.ReadAsync(".png", Image(), (_, _) => lateImage.Task, lateCancel.Token);
        lateCancel.Cancel(); lateImage.SetResult("Late original image text");
        await Reject(() => readingImage, cancelled: true);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Reject(() => ContextFileTextReader.ReadAsync(".png", Image(), null, cancel.Token), cancelled: true);
        await Reject(() => ContextFileTextReader.ReadAsync(".png", Image(), null, default), "ImageNeedsLocalModel");
        await Reject(() => ContextFileTextReader.ReadAsync(".pdf", [1, 2, 3], null, default), "FileUnreadable");
        int shows = 0, images = 0;
        bool vision = true, remote = false;
        using var client = new ModelClient(new StubHandler(async request =>
        {
            string payload = await request.Content!.ReadAsStringAsync();
            if (request.RequestUri!.AbsolutePath == "/api/show")
            {
                shows++; Check(!payload.Contains("images"), "Metadata checks contain no image");
                return Response(JsonSerializer.Serialize(new { model_info = new { architecture = "gemma" },
                    capabilities = vision ? new[] { "completion", "vision" } : new[] { "completion" }, remote_host = remote ? "https://example.com" : "" }));
            }
            images++;
            Check(request.RequestUri.Host == "localhost" && payload.Contains("images") && !payload.Contains(folder.Path), "Images stay on loopback without paths");
            return Response(JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(new { text = "Read visible text" }) } }));
        }));
        var model = new ModelSettings(ModelProvider.Ollama, "http://localhost:11434", "gemma3:4b");
        await client.DescribeImageAsync(Image(), model, default); await client.DescribeImageAsync(Image(), model, default);
        Check(shows == 2 && images == 2, "Each image requires fresh local-model validation");
        vision = false; await Reject(() => client.DescribeImageAsync(Image(), model, default), "ImageNeedsVisionModel");
        vision = true; remote = true; await Reject(() => client.DescribeImageAsync(Image(), model, default), "RemoteModelBlocked");
        await Reject(() => client.DescribeImageAsync(Image(), model with { Endpoint = "https://example.com" }, default), "LocalEndpointOnly");
        Check(images == 2, "Unsupported and remote models never receive pixels");
    }

    private static byte[] Image()
    {
        using var bitmap = new SKBitmap(24, 16); bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    private static void WriteZip(string path, params (string Name, string Text)[] parts)
    {
        using var file = File.Create(path); using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var part in parts) { using var writer = new StreamWriter(zip.CreateEntry(part.Name).Open()); writer.Write(part.Text); }
    }
    private static async Task Reject(Func<Task> action, string? key = null, bool cancelled = false)
    {
        try { await action(); }
        catch (OperationCanceledException) when (cancelled) { return; }
        catch (SafeOperationException e) when (e.MessageKey == key) { return; }
        throw new Exception("Expected rejection: " + key);
    }
    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
