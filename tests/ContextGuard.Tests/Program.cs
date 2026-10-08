using System.Net;
using System.Text;
using System.Text.Json;
using ContextGuard.Core;

int passed = 0, failed = 0;
await Check("Secrets in JSON, env, bearer, URI and private keys", () =>
{
    var session = Session();
    string source = "api_key=super-private-key\n{\"password\":\"private-password\"}\nAuthorization: Bearer abcdefghijklmnop\npostgres://login:pw@database/app\n-----BEGIN PRIVATE KEY-----\nvery-sensitive-block\n-----END PRIVATE KEY-----";
    string safe = session.Sanitize(source);
    Excludes(safe, "super-private-key", "private-password", "abcdefghijklmnop", "login:pw", "very-sensitive-block");
    Equal(source, session.RestoreForLocalDisplay(safe), "Lossless local restore");
    Equal(safe, session.Sanitize(safe), "Idempotence");
    return Task.CompletedTask;
});
await Check("Personal and internal data", () =>
{
    var session = Session();
    string safe = session.Sanitize("user@example.com +7 (999) 123-45-67\nФИО: Иванов Иван Иванович\nпаспорт: 4510 123456\n4111 1111 1111 1111\n10.20.30.40 https://repo.corp/project\nC:\\Users\\PrivateUser\\Documents\\file.txt");
    Excludes(safe, "user@example.com", "999", "Иванов", "4510", "4111", "10.20.30.40", "repo.corp", "PrivateUser");
    return Task.CompletedTask;
});
await Check("Profile choice preserves unselected categories", () =>
{
    var session = Session(ProtectionProfile.SecretsOnly);
    string safe = session.Sanitize("hello@example.com 10.1.2.3 password=secret-value");
    Contains(safe, "hello@example.com"); Contains(safe, "10.1.2.3"); Excludes(safe, "secret-value");
    return Task.CompletedTask;
});
await Check("Custom values, casing, overlaps and placeholder collisions", () =>
{
    var session = new RedactionSession(new(ProtectionProfile.Everything, new[] { "Acme Ltd", "Acme", "SECRET", "CG" }));
    string original = "ACME LTD Acme SECRET CG\npassword=special-value";
    string safe = session.Sanitize(original);
    Equal(original, session.RestoreForLocalDisplay(safe), "Custom-term restore");
    Equal(safe, session.Sanitize(safe), "Placeholders must survive custom terms");
    Excludes(safe, "ACME LTD", "Acme", "special-value");
    Throws(() => new RedactionSession(new(ProtectionProfile.Everything, new[] { "a" })), "Short term rejected");
    return Task.CompletedTask;
});
await Check("Known credentials removed from plain text and later messages", () =>
{
    var session = Session();
    session.Sanitize("password=standaloneSecret");
    Excludes(session.Sanitize("Please use standaloneSecret in the config"), "standaloneSecret");
    return Task.CompletedTask;
});
await Check("Collector excludes secrets, binaries, large files and file names", async () =>
{
    using var work = new TestFolder();
    await File.WriteAllTextAsync(Path.Combine(work.Path, "private-person@example.com.md"), "email: person@example.com\npassword=folder-secret");
    await File.WriteAllTextAsync(Path.Combine(work.Path, ".env"), "password=excluded-one");
    await File.WriteAllTextAsync(Path.Combine(work.Path, "secrets.json"), "excluded-two");
    await File.WriteAllBytesAsync(Path.Combine(work.Path, "binary.txt"), new byte[] { 0, 255, 3, 0 });
    await File.WriteAllTextAsync(Path.Combine(work.Path, "huge.txt"), new string('a', FolderCollector.MaxFileBytes + 1));
    Directory.CreateDirectory(Path.Combine(work.Path, "node_modules"));
    await File.WriteAllTextAsync(Path.Combine(work.Path, "node_modules", "a.txt"), "excluded-three");
    var inspected = new List<string>();
    var result = await new FolderCollector().CollectAsync(work.Path, Session(), (text, _) => { inspected.Add(text); return Task.CompletedTask; }, null, default);
    Equal(1, result.Included, "Only safe text file included");
    Equal(1, inspected.Count, "Excluded files never reach local agent");
    Equal("file_001.md", result.Files[0].Alias, "Opaque file alias");
    Excludes(result.Context, "person@example.com", "folder-secret", "private-person", work.Path, "excluded-one", "excluded-two", "excluded-three");
});
await Check("Collector skips symlinks and reprocesses earlier files", async () =>
{
    using var work = new TestFolder();
    await File.WriteAllTextAsync(Path.Combine(work.Path, "a.txt"), "PrivateProject is under development");
    await File.WriteAllTextAsync(Path.Combine(work.Path, "b.txt"), "Project name is PrivateProject");
    var session = Session();
    int inspected = 0;
    var scan = await new FolderCollector().CollectAsync(work.Path, session, (text, _) =>
    { if (++inspected == 2) session.AddEntity("PrivateProject"); return Task.CompletedTask; }, null, default);
    Equal(2, scan.Included, "Both files included");
    Excludes(scan.Context, "PrivateProject");
    // Symlink construction is unavailable on Windows without developer mode; check when permitted.
    try
    {
        Directory.CreateSymbolicLink(Path.Combine(work.Path, "linked"), work.Path);
        var linked = await new FolderCollector().CollectAsync(work.Path, Session(), null, null, default);
        Equal(2, linked.Included, "Directory symlink not traversed");
    }
    catch (UnauthorizedAccessException) { }
    catch (IOException) { }
});
await Check("Cancellation and context limits", async () =>
{
    using var work = new TestFolder();
    await File.WriteAllTextAsync(Path.Combine(work.Path, "a.txt"), new string('a', 3500));
    await File.WriteAllTextAsync(Path.Combine(work.Path, "b.txt"), new string('b', 3500));
    var scan = await new FolderCollector().CollectAsync(work.Path, Session(), null, null, default, 4000);
    Equal(1, scan.Included, "Oversize context file skipped");
    True(scan.LimitReached, "Limit reported");
    using var cts = new CancellationTokenSource(); cts.Cancel();
    try { await new FolderCollector().CollectAsync(work.Path, Session(), null, null, cts.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
});
await Check("Endpoint validation rejects remote local-agent URLs and insecure remote APIs", () =>
{
    foreach (string url in new[] { "https://example.com", "http://127.0.0.1.example.com", "file:///tmp/a", "http://user:pw@localhost:11434", "http://localhost:11434?key=secret" })
        Throws(() => ModelClient.ValidateEndpoint(url, true), "Invalid local endpoint");
    ModelClient.ValidateEndpoint("http://127.0.0.1:11434", true);
    ModelClient.ValidateEndpoint("http://[::1]:11434", true);
    ModelClient.ValidateEndpoint("https://api.example.com/v1/chat/completions", false);
    Throws(() => ModelClient.ValidateEndpoint("http://api.example.com/v1/chat/completions", false), "No HTTP remote API");
    return Task.CompletedTask;
});
await Check("Actual HTTP payload sanitizes context, user and assistant history", async () =>
{
    string? payload = null; string? key = null;
    using var client = new ModelClient(new StubHandler(async request =>
    {
        payload = await request.Content!.ReadAsStringAsync(); key = request.Headers.Authorization?.Parameter;
        return Response("data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream");
    }));
    var session = Session();
    string streamed = "";
    string answer = await client.ChatAsync(new(ModelProvider.RemoteApi, "https://api.example.com/v1/chat/completions", "test-model", "authorization-only"),
        "password=context-secret authorization-only", new[] { new ChatMessage("user", "user@example.com"), new ChatMessage("assistant", "password=history-secret") }, session, delta => streamed += delta, default);
    Equal("Hello world", answer, "SSE result"); Equal(answer, streamed, "Streaming callback");
    Equal("authorization-only", key, "Auth header");
    var json = JsonDocument.Parse(payload!);
    string decoded = string.Join("\n", json.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("content").GetString()));
    Excludes(decoded, "context-secret", "user@example.com", "history-secret", "authorization-only");
});
await Check("File names can be restored locally without entering outbound context", () =>
{
    var session = Session(); session.AddFileAlias("file_001.md", "PrivateClient/plan.md");
    Equal("Open PrivateClient/plan.md", session.RestoreForLocalDisplay("Open file_001.md"), "Local file alias restore");
    var messages = ModelClient.BuildOutbound("--- FILE: file_001.md ---\nSafe file contents", new[] { new ChatMessage("user", "Explain file_001.md") }, session);
    Excludes(string.Join("\n", messages.Select(m => m.Content)), "PrivateClient");
    return Task.CompletedTask;
});
await Check("Ollama model list excludes cloud and raw text blocked for remote models", async () =>
{
    int chats = 0;
    using var client = new ModelClient(new StubHandler(request =>
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path == "/api/tags") return Task.FromResult(Response("{\"models\":[{\"name\":\"local:latest\"},{\"name\":\"evil:latest\",\"remote_host\":\"https://ollama.com\"},{\"name\":\"other:cloud\"}]}"));
        if (path == "/api/chat") chats++;
        return Task.FromResult(Response("{\"remote_host\":\"https://ollama.com\",\"model_info\":{\"general.architecture\":\"llama\"}}"));
    }));
    var models = await client.ListLocalModelsAsync("http://localhost:11434", default);
    Equal(1, models.Count, "Only local model shown");
    try { await client.DiscoverEntitiesAsync("raw-sensitive-text", new(ProtectionProfile.Everything, Array.Empty<string>()), Session(), "http://localhost:11434", "evil:latest", default); throw new Exception("Cloud raw request allowed"); }
    catch (SafeOperationException) { }
    Equal(0, chats, "Raw-text request blocked");
});
await Check("Local entity extraction keeps exact values and rejects hallucinations", async () =>
{
    using var client = new ModelClient(new StubHandler(request => Task.FromResult(Response(request.RequestUri!.AbsolutePath == "/api/show" ?
        "{\"model_info\":{\"general.architecture\":\"llama\"}}" :
        "{\"message\":{\"content\":\"{\\\"entities\\\":[\\\"PrivateOrganization\\\",\\\"hallucinated-value\\\"]}\"}}"))));
    var session = Session();
    await client.DiscoverEntitiesAsync("PrivateOrganization works here", new(ProtectionProfile.Everything, Array.Empty<string>()), session, "http://localhost:11434", "local-model", default);
    string safe = session.Sanitize("PrivateOrganization and hallucinated-value");
    Excludes(safe, "PrivateOrganization"); Contains(safe, "hallucinated-value");
});
await Check("Ollama NDJSON streams correctly", async () =>
{
    using var client = new ModelClient(new StubHandler(request => Task.FromResult(Response(request.RequestUri!.AbsolutePath == "/api/show" ?
        "{\"model_info\":{\"general.architecture\":\"llama\"}}" :
        "{\"message\":{\"content\":\"Привет\"},\"done\":false}\n{\"message\":{\"content\":\"!\"},\"done\":true}\n"))));
    Equal("Привет!", await client.ChatAsync(new(ModelProvider.Ollama, "http://localhost:11434", "test-model"), "safe context", Array.Empty<ChatMessage>(), Session(), _ => { }, default), "NDJSON result");
});
await Check("Provider errors and truncated streams never expose their bodies", async () =>
{
    using var client = new ModelClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("echoed-secret") })));
    try { await client.ListLocalModelsAsync("http://localhost:11434", default); throw new Exception("HTTP error ignored"); }
    catch (SafeOperationException e) { Excludes(e.Message, "echoed-secret"); }
    using var broken = new ModelClient(new StubHandler(_ => Task.FromResult(Response("data: {\"choices\":[{\"delta\":{\"content\":\"incomplete\"}}]}\n\n", "text/event-stream"))));
    try { await broken.ChatAsync(new(ModelProvider.RemoteApi, "https://api.example.com/chat", "test"), "safe", Array.Empty<ChatMessage>(), Session(), _ => { }, default); throw new Exception("Truncated stream accepted"); }
    catch (SafeOperationException) { }
});

await Check("English defaults regardless of OS language and independent Russian errors", () =>
{
    var previous = System.Globalization.CultureInfo.CurrentUICulture;
    try
    {
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        var english = new LocalizedText();
        var russian = new LocalizedText(AppLanguage.Russian);
        Equal("Chat", english["ChatTab"], "English is the default even on a Russian OS");
        Equal("Диалог", russian["ChatTab"], "Russian resources are packaged");
        try { ModelClient.ValidateEndpoint("https://example.com", true); throw new Exception("Remote local-agent address accepted"); }
        catch (SafeOperationException e)
        {
            Contains(e.Message, "Ollama allows only a local address");
            Contains(e.GetLocalizedMessage(russian), "Для Ollama разрешён только локальный адрес");
            Excludes(e.GetLocalizedMessage(russian), "example.com");
        }
        Equal("Chat", english["ChatTab"], "Using Russian does not change other catalog instances");
    }
    finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    return Task.CompletedTask;
});
await Check("Localized HTTP errors show only safe status information", async () =>
{
    using var client = new ModelClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = new StringContent("provider-echoed-sensitive-text") })));
    try { await client.ListLocalModelsAsync("http://localhost:11434", default); throw new Exception("HTTP error ignored"); }
    catch (SafeOperationException e)
    {
        foreach (var language in new[] { AppLanguage.English, AppLanguage.Russian })
        {
            string message = e.GetLocalizedMessage(new LocalizedText(language));
            Contains(message, "HTTP 503");
            Excludes(message, "provider-echoed-sensitive-text");
        }
    }
});

Console.WriteLine($"\n{passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

RedactionSession Session(ProtectionProfile profile = ProtectionProfile.Everything) => new(new(profile, Array.Empty<string>()));
async Task Check(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name} — {e.Message}"); }
}
void True(bool condition, string label) { if (!condition) throw new Exception(label); }
void Equal<T>(T expected, T actual, string label) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(label); }
void Contains(string text, string part) => True(text.Contains(part, StringComparison.Ordinal), "Expected text missing");
void Excludes(string text, params string[] parts) { foreach (string part in parts) True(!text.Contains(part, StringComparison.Ordinal), "Sensitive value leaked"); }
void Throws(Action action, string label)
{
    try { action(); } catch (SafeOperationException) { return; }
    throw new Exception(label);
}
HttpResponseMessage Response(string body, string type = "application/json") => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };

sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
}
sealed class TestFolder : IDisposable
{
    private readonly string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.CurrentDirectory, ".test-work"));
    public string Path { get; }
    public TestFolder()
    {
        Path = System.IO.Path.Combine(root, "case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public void Dispose()
    {
        string resolved = System.IO.Path.GetFullPath(Path);
        if (System.IO.Path.GetDirectoryName(resolved) == root && System.IO.Path.GetFileName(resolved).StartsWith("case-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
    }
}
