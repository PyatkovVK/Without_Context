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

await Check("Connection probes validate local metadata and API model IDs without inference", async () =>
{
    int calls = 0;
    using var client = new ModelClient(new StubHandler(async request =>
    {
        calls++;
        if (request.RequestUri!.AbsolutePath == "/api/show")
        {
            string payload = await request.Content!.ReadAsStringAsync();
            Contains(payload, "test-local"); Excludes(payload, "messages", "folder_context");
            return Response("{\"model_info\":{\"general.architecture\":\"llama\"}}");
        }
        Equal("/prefix/v1/models", request.RequestUri.AbsolutePath, "Model endpoint derives from the configured API prefix");
        Equal(HttpMethod.Get, request.Method, "Metadata check is read-only");
        True(request.Content is null, "No context or prompt payload in probe");
        Equal("probe-authorization-only", request.Headers.Authorization?.Parameter, "API probe authorization");
        return Response("{\"data\":[{\"id\":\"test-remote\"}]}");
    }));
    await client.CheckChatModelAsync(new(ModelProvider.Ollama, "http://localhost:11434", "test-local"), default);
    await client.CheckChatModelAsync(new(ModelProvider.RemoteApi, "https://api.example.com/prefix/v1/chat/completions", "test-remote", "probe-authorization-only"), default);
    Equal(2, calls, "Only the two metadata requests were made");
});
await Check("Unavailable API checks and missing model IDs cannot be reported as connected", async () =>
{
    int calls = 0;
    using var client = new ModelClient(new StubHandler(_ =>
    {
        calls++; return Task.FromResult(Response("{\"data\":[{\"id\":\"other-model\"}]}"));
    }));
    try { await client.CheckChatModelAsync(new(ModelProvider.RemoteApi, "https://api.example.com/custom", "wanted"), default); throw new Exception("Unsupported check accepted"); }
    catch (SafeOperationException e) { Equal("ConnectionCheckUnavailable", e.MessageKey, "Unsupported probe is distinct from verified connectivity"); }
    Equal(0, calls, "Unsupported check sends no request");
    try { await client.CheckChatModelAsync(new(ModelProvider.RemoteApi, "https://api.example.com/v1/chat/completions", "wanted"), default); throw new Exception("Missing model accepted"); }
    catch (SafeOperationException e) { Equal("ModelNotListed", e.MessageKey, "Selected model must appear in the list"); }
    using var unavailable = new ModelClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("provider-echoed-private-data") })));
    try { await unavailable.CheckChatModelAsync(new(ModelProvider.RemoteApi, "https://api.example.com/v1/chat/completions", "wanted"), default); throw new Exception("Missing model-list route accepted"); }
    catch (SafeOperationException e) { Equal("ConnectionCheckUnavailable", e.MessageKey, "Missing model-list route stays unverified"); Excludes(e.Message, "provider-echoed-private-data"); }
});
await Check("Connection checks reject malformed model lists and unauthorized credentials safely", async () =>
{
    foreach (string data in new[] { "[]", "{}", "{\"data\":{}}" })
    {
        using var client = new ModelClient(new StubHandler(_ => Task.FromResult(Response(data))));
        try { await client.CheckChatModelAsync(new(ModelProvider.RemoteApi, "https://api.example.com/v1/chat/completions", "wanted"), default); throw new Exception("Malformed model list accepted"); }
        catch (SafeOperationException e) { Equal("ConnectionCheckUnavailable", e.MessageKey, "Malformed response cannot prove model availability"); }
    }
    using var rejected = new ModelClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("echoed-probe-secret") })));
    try { await rejected.CheckChatModelAsync(new(ModelProvider.RemoteApi, "https://api.example.com/v1/chat/completions", "wanted"), default); throw new Exception("Rejected credentials accepted"); }
    catch (SafeOperationException e) { Equal("AuthenticationRejected", e.MessageKey, "Authorization failure preserved"); Excludes(e.Message, "echoed-probe-secret"); }
});

await Check("Context trimming keeps file aliases, bounds size and leaves short context intact", () =>
{
    var snapshot = new ScanSummary(2, 0, 32_000, false, new[]
    {
        new SafeFile("file_001.md", "Goals and product facts.\n" + new string('a', 16_000), "private-client-name.md"),
        new SafeFile("file_002.cs", "Relevant source code.\n" + new string('b', 16_000), "private-source-name.cs")
    });
    var sizes = new List<int>();
    foreach (var mode in new[] { ContextTrimmingMode.Minimal, ContextTrimmingMode.Moderate, ContextTrimmingMode.Maximum })
    {
        int target = ContextReducer.TargetCharacters(24_000, mode);
        string reduced = ContextReducer.Reduce(snapshot, target, default);
        True(reduced.Length <= target && reduced.Length < snapshot.Context.Length, "Reduced context is bounded");
        Contains(reduced, "file_001.md"); Contains(reduced, "file_002.cs"); Contains(reduced, "Goals and product facts");
        Excludes(reduced, "private-client-name", "private-source-name"); sizes.Add(reduced.Length);
    }
    True(sizes[0] > sizes[1] && sizes[1] > sizes[2], "Stronger trimming retains less text");
    var shortSnapshot = new ScanSummary(1, 0, 5, false, new[] { new SafeFile("file_001.md", "short") });
    Equal(shortSnapshot.Context, ContextReducer.Reduce(shortSnapshot, 6000, default), "Short context is preserved");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { ContextReducer.Reduce(shortSnapshot, 6000, cancelled.Token); throw new Exception("Cancelled trimming accepted"); }
    catch (OperationCanceledException) { }
    return Task.CompletedTask;
});
await Check("Context clipping never splits placeholders or Unicode surrogate pairs", () =>
{
    const string token = "[CG_SECRET_abcdef_001]";
    string value = "before " + token + " after a very long paragraph";
    for (int budget = 8; budget < 8 + token.Length; budget++)
    {
        string clipped = ContextReducer.LimitText(value, budget);
        True(clipped.Length <= budget && !clipped.Contains("[CG"), "A partial placeholder cannot survive");
    }
    string unicode = ContextReducer.LimitText("abc😀defghi", 5);
    True(!unicode.Any(char.IsSurrogate), "A clipped emoji cannot leave a dangling surrogate");
    return Task.CompletedTask;
});
await Check("Only recent complete message pairs enter outbound requests after trimming", async () =>
{
    var history = Enumerable.Range(0, 8).SelectMany(i => new[] { new ChatMessage("user", "task-" + i), new ChatMessage("assistant", "reply-" + i) }).ToArray();
    var latest = new ChatMessage("user", "new-task with email user@example.com");
    foreach (var mode in new[] { ContextTrimmingMode.Minimal, ContextTrimmingMode.Moderate, ContextTrimmingMode.Maximum, ContextTrimmingMode.Custom })
    {
        var selected = ContextReducer.SelectHistory(history, latest, mode);
        Equal(ContextReducer.RetainedMessages(mode) + 1, selected.Count, "History retention");
        Equal("user", selected[0].Role, "Pairs begin with a user"); Equal(latest, selected[^1], "Current request is retained");
    }
    using var client = new ModelClient(new StubHandler(async request =>
    {
        string payload = await request.Content!.ReadAsStringAsync();
        Excludes(payload, "task-0", "task-5", "reply-6", "user@example.com"); Contains(payload, "task-7"); Contains(payload, "new-task");
        return Response("{\"choices\":[{\"message\":{\"content\":\"bounded reply\"}}]}");
    }));
    await client.ChatAsync(new(ModelProvider.RemoteApi, "https://api.example.com/v1/chat/completions", "demo"), "short context",
        ContextReducer.SelectHistory(history, latest, ContextTrimmingMode.Maximum), Session(), _ => { }, default);
});
await Check("Custom trimming uses a validated local model and sanitizes its result before review", async () =>
{
    using var folder = new TestFolder();
    File.WriteAllText(Path.Combine(folder.Path, "document.md"), "Product goals. Contact user@example.com. password=private-demo-password");
    int summaries = 0, validations = 0;
    using var client = new ModelClient(new StubHandler(async request =>
    {
        if (request.RequestUri!.AbsolutePath == "/api/show") { validations++; return Response("{\"model_info\":{\"general.architecture\":\"llama\"}}"); }
        Equal("localhost", request.RequestUri.Host, "All local requests stay on loopback");
        string payload = await request.Content!.ReadAsStringAsync();
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        if (json.RootElement.TryGetProperty("format", out _)) return Response("{\"message\":{\"content\":\"{\\\"entities\\\":[]}\"}}");
        summaries++;
        Excludes(payload, "user@example.com", "private-demo-password", "known-api-credential", "document.md");
        Contains(payload, "file_001.md"); Contains(payload, "Preserve product goals");
        return Response(System.Text.Json.JsonSerializer.Serialize(new { message = new { content = "file_001.md: Product goals. user@example.com known-api-credential" } }));
    }));
    var preparation = new ContextPreparationService(new FolderCollector(), client);
    var prepared = await preparation.PrepareAsync(new(folder.Path, new(ProtectionProfile.Everything, Array.Empty<string>()), 24_000,
        ContextTrimmingMode.Custom, "Preserve product goals and user@example.com", true,
        new(ModelProvider.Ollama, "http://localhost:11434", "local-cleaner"), "known-api-credential"), null, default);
    True(validations >= 2 && summaries == 1, "Privacy detection and summary each validate the local model");
    Contains(prepared.Text, "Product goals"); Excludes(prepared.Text, "user@example.com", "known-api-credential");
    True(prepared.Text.Length <= prepared.TargetCharacters, "Custom result is bounded");
});
await Check("Custom trimming fails closed for missing rules and cloud models", async () =>
{
    using var folder = new TestFolder(); File.WriteAllText(Path.Combine(folder.Path, "context.md"), "Project context");
    int requests = 0;
    using var client = new ModelClient(new StubHandler(_ => { requests++; return Task.FromResult(Response("{\"remote_host\":\"https://example.com\",\"model_info\":{\"general.architecture\":\"llama\"}}")); }));
    var preparation = new ContextPreparationService(new FolderCollector(), client);
    var settings = new ContextPreparationSettings(folder.Path, new(ProtectionProfile.Everything, Array.Empty<string>()), 24_000,
        ContextTrimmingMode.Custom, "Keep goals", false, new(ModelProvider.Ollama, "http://localhost:11434", "cleaner"), "");
    try { await preparation.PrepareAsync(settings, null, default); throw new Exception("Disabled custom model accepted"); }
    catch (SafeOperationException e) { Equal("CustomTrimmingNeedsLocalModel", e.MessageKey, "Local model is required"); }
    try { await preparation.PrepareAsync(settings with { UseLocalModel = true, Instructions = "" }, null, default); throw new Exception("Empty rules accepted"); }
    catch (SafeOperationException e) { Equal("TrimmingInstructionsRequired", e.MessageKey, "Rules are required"); }
    Equal(0, requests, "Invalid configuration sends no requests");
    try { await client.SummarizeContextAsync("safe context", "Keep goals", 1000, settings.LocalModel, default); throw new Exception("Cloud model accepted"); }
    catch (SafeOperationException e) { Equal("RemoteModelBlocked", e.MessageKey, "Cloud models are rejected before summary text is sent"); }
    Equal(1, requests, "Only metadata was requested");
});

await Check("Cancelled custom preparation cannot publish a late summary", async () =>
{
    using var folder = new TestFolder(); File.WriteAllText(Path.Combine(folder.Path, "context.md"), "Project goals");
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var client = new ModelClient(new StubHandler(async request =>
    {
        if (request.RequestUri!.AbsolutePath == "/api/show") return Response("{\"model_info\":{\"general.architecture\":\"llama\"}}");
        string payload = await request.Content!.ReadAsStringAsync();
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        if (json.RootElement.TryGetProperty("format", out _)) return Response(System.Text.Json.JsonSerializer.Serialize(new { message = new { content = "{\"entities\":[]}" } }));
        started.SetResult(); return await completion.Task;
    }));
    using var cancellation = new CancellationTokenSource();
    var preparation = new ContextPreparationService(new FolderCollector(), client);
    var task = preparation.PrepareAsync(new(folder.Path, new(ProtectionProfile.Everything, Array.Empty<string>()), 24_000,
        ContextTrimmingMode.Custom, "Keep goals", true, new(ModelProvider.Ollama, "http://localhost:11434", "local-cleaner"), ""), null, cancellation.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    cancellation.Cancel(); completion.SetResult(Response("{\"message\":{\"content\":\"late summary\"}}"));
    try { await task; throw new Exception("Cancelled summary published"); } catch (OperationCanceledException) { }
});

await Check("Durable chats round-trip, export and delete without touching unrelated files", async () =>
{
    using var work = new TestFolder();
    var store = new JsonDirectoryChatStore(Path.Combine(work.Path, "chats"));
    var redaction = Session();
    string safe = redaction.Sanitize("Contact private@example.com password=persistent-only-secret");
    var id = Guid.NewGuid(); var time = DateTimeOffset.UtcNow;
    var chat = new StoredConversation(1, id, 7, time, true, safe,
        new[] { new StoredMessage("user", safe, time), new StoredMessage("assistant", "Ответ: " + safe, time) });
    await store.ReplaceAsync([chat], default);
    File.WriteAllText(Path.Combine(store.Folder, "keep-me.txt"), "Unrelated file");
    var restored = (await store.LoadAsync(default)).Single();
    Equal(id, restored.Id, "Opaque identifier survives restart"); Equal(true, restored.Archived, "Archive flag survives restart");
    Equal(time, restored.Messages[0].CreatedAt, "Message timestamp survives restart"); Equal(safe, restored.Context, "Safe context retained");
    string exported = Path.Combine(work.Path, "exports");
    await store.ExportAsync([restored], exported, default);
    Equal(2, Directory.GetFiles(exported).Length, "JSON and Markdown exported");
    foreach (string file in Directory.GetFiles(exported))
        Excludes(File.ReadAllText(file), "private@example.com", "persistent-only-secret", "ApiKey", "LocalRelativePath");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await store.ReplaceAsync([], cancelled.Token); throw new Exception("Cancelled replacement succeeded"); }
    catch (OperationCanceledException) { }
    Equal(1, (await store.LoadAsync(default)).Count, "Cancellation leaves saved chat intact");
    await store.ReplaceAsync([], default);
    Equal(0, (await store.LoadAsync(default)).Count, "Delete removes saved chat");
    True(File.Exists(Path.Combine(store.Folder, "keep-me.txt")) && Directory.GetFiles(exported).Length == 2,
        "Deletion preserves unrelated files and exported copies");
});

await Check("Storage rejects malformed, oversized, incomplete and mismatched chats", async () =>
{
    using var work = new TestFolder(); var store = new JsonDirectoryChatStore(work.Path);
    var time = DateTimeOffset.UtcNow;
    var chat = new StoredConversation(1, Guid.NewGuid(), 1, time, false, "safe", []);
    await store.ReplaceAsync([chat], default);
    async Task Rejected(Func<Task> action)
    {
        try { await action(); throw new Exception("Invalid storage was accepted"); } catch (SafeOperationException) { }
    }
    await Rejected(() => store.ReplaceAsync([chat with { Messages = [new("user", "unfinished", time)] }], default));
    await Rejected(() => store.ReplaceAsync([chat with { Context = new string('x', FolderCollector.MaxContextChars + 1) }], default));
    await Rejected(() => store.ReplaceAsync([chat with { Messages = [new("system", "bad role", time), new("assistant", "reply", time)] }], default));
    await Rejected(() => store.ReplaceAsync(Enumerable.Range(1, JsonDirectoryChatStore.MaxChats + 1)
        .Select(n => chat with { Id = Guid.NewGuid(), Number = n }).ToArray(), default));
    Equal(1, (await store.LoadAsync(default)).Count, "Failed validation never replaces valid files");
    string path = Path.Combine(work.Path, chat.Id.ToString("N") + ".json");
    await File.WriteAllTextAsync(path, "{\"ApiKey\":\"should-not-be-read\"}");
    await Rejected(async () => { await store.LoadAsync(default); });
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(chat with { Id = Guid.NewGuid() }));
    await Rejected(async () => { await store.LoadAsync(default); });
    await File.WriteAllTextAsync(path, new string(' ', JsonDirectoryChatStore.MaxFileBytes + 1));
    await Rejected(async () => { await store.LoadAsync(default); });
});

await Check("A pending deletion supersedes an old save and snapshots are immutable", async () =>
{
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var persisted = new List<IReadOnlyList<StoredConversation>>(); int writes = 0;
    var store = new ControlledChatStore(async (chats, ct) =>
    {
        if (++writes == 1) { started.SetResult(); await release.Task.WaitAsync(ct); }
        persisted.Add(chats);
    });
    using var service = new ChatArchiveService(store);
    var messages = new List<StoredMessage> { new("user", "safe", DateTimeOffset.UtcNow), new("assistant", "reply", DateTimeOffset.UtcNow) };
    var original = new StoredConversation(1, Guid.NewGuid(), 1, DateTimeOffset.UtcNow, false, "safe context", messages);
    Task old = service.SaveAsync([original]);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    messages[0] = messages[0] with { Content = "mutated later" };
    _ = service.SaveAsync([original with { Archived = true }]);
    Task deletion = service.SaveAsync([]);
    release.SetResult(); await deletion; await old;
    Equal("safe", persisted[0][0].Messages[0].Content, "Nested messages copied before queueing");
    Equal(0, persisted[^1].Count, "Late old save cannot restore a deleted chat");
    Equal(2, writes, "Only latest pending snapshot is retained");
});

await Check("Storage failures can retry and disposal cancels late loading", async () =>
{
    int attempts = 0;
    var store = new ControlledChatStore((_, _) => ++attempts == 1 ? Task.FromException(new SafeOperationException("ChatStorageWriteFailed")) : Task.CompletedTask);
    using var service = new ChatArchiveService(store);
    try { await service.SaveAsync([]); throw new Exception("Write failure ignored"); } catch (SafeOperationException) { }
    await service.SaveAsync([]); Equal(2, attempts, "A failed writer does not poison retries");
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var completion = new TaskCompletionSource<IReadOnlyList<StoredConversation>>(TaskCreationOptions.RunContinuationsAsynchronously);
    store.Loader = _ => { started.SetResult(); return completion.Task; };
    var load = service.LoadAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    service.Dispose(); completion.SetResult([]);
    try { await load; throw new Exception("Late loaded state published after disposal"); } catch (OperationCanceledException) { }
});

await Check("Earlier-session placeholders remain opaque after reopening", () =>
{
    var previous = Session(); string safe = previous.Sanitize("password=original-value");
    var reopened = new RedactionSession(new(ProtectionProfile.Everything, new[] { "CG", "SECRET" }));
    Equal(safe, reopened.Sanitize(safe), "Custom values cannot rewrite older placeholders");
    Excludes(reopened.RestoreForLocalDisplay(safe), "original-value");
    return Task.CompletedTask;
});

await Check("Chat titles capture the goal, preserve language and reject unsuitable model output", () =>
{
    Equal("Create a product launch plan", ConversationTitleService.Fallback("Hello! Please help me create a product launch plan. My contact is hidden."), "English goal without greeting");
    Equal("Составить план запуска продукта", ConversationTitleService.Fallback("Привет! Помоги мне составить план запуска продукта."), "Russian task remains Russian");
    True(ConversationTitleService.Normalize("An explanation\nwith multiple lines") is null, "Multi-line answers are not titles");
    True(ConversationTitleService.Normalize(new string('x', 257)) is null, "Excessive title text is rejected");
    Equal("Product launch plan", ConversationTitleService.Normalize("\"Product launch plan\""), "Quotes removed");
    True(ConversationTitleService.Fallback(new string('x', 2000)).Length <= ConversationTitleService.MaxCharacters, "Fallback is bounded");
    var session = Session(); string safe = session.Sanitize("Plan for contact@example.com password=title-secret");
    Excludes(ConversationTitleService.Fallback(safe), "contact@example.com", "title-secret", "[CG_");
    return Task.CompletedTask;
});

await Check("Title requests use bounded anonymized input, JSON output and header-only API credentials", async () =>
{
    var session = Session(); string safe = session.Sanitize("Create a launch plan for private@example.com password=title-private-value");
    foreach (var provider in new[] { ModelProvider.Ollama, ModelProvider.RemoteApi })
    {
        int generated = 0;
        using var client = new ModelClient(new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/show") return Response("{\"model_info\":{\"general.architecture\":\"llama\"}}");
            generated++;
            string payload = await request.Content!.ReadAsStringAsync();
            Excludes(payload, "private@example.com", "title-private-value", "title-header-only-key");
            using var json = JsonDocument.Parse(payload);
            Equal(false, json.RootElement.GetProperty("stream").GetBoolean(), "Titles are a short non-streaming request");
            string input = json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            True(input.Length < 6200, "Title prompt is bounded independently of chat context");
            if (provider == ModelProvider.RemoteApi) Equal("title-header-only-key", request.Headers.Authorization?.Parameter, "API key in header only");
            return provider == ModelProvider.Ollama ? Response("{\"message\":{\"content\":\"Product launch plan\"}}") :
                Response("{\"choices\":[{\"message\":{\"content\":\"Product launch plan\"}}]}");
        }));
        var service = new ConversationTitleService(client);
        var settings = new ModelSettings(provider, provider == ModelProvider.Ollama ? "http://localhost:11434" : "https://example.com/v1/chat/completions", "demo-local", "title-header-only-key");
        Equal("Product launch plan", await service.GenerateAsync(settings, safe, safe + new string('x', 10_000), default), "Meaningful short title");
        Equal(1, generated, "One title inference per invocation");
    }
});

await Check("Title failures are optional and cancellation rejects a late model result", async () =>
{
    using var badClient = new ModelClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
    var service = new ConversationTitleService(badClient);
    True(await service.GenerateAsync(new(ModelProvider.RemoteApi, "https://example.com/v1/chat/completions", "demo"), "Plan a launch", "Safe context", default) is null,
        "Title HTTP failure leaves fallback usable");
    var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new ControlledTitleModel((_, _, _, _) => completion.Task);
    using var cancellation = new CancellationTokenSource();
    var late = new ConversationTitleService(fake).GenerateAsync(new(ModelProvider.Ollama, "http://localhost:11434", "demo"), "Goal", "Context", cancellation.Token);
    cancellation.Cancel(); completion.SetResult("Late generated title");
    try { await late; throw new Exception("Cancelled title returned successfully"); } catch (OperationCanceledException) { }
});

await Check("Legacy chats load without titles and version 2 titles survive save and export", async () =>
{
    using var work = new TestFolder(); var store = new JsonDirectoryChatStore(Path.Combine(work.Path, "chats"));
    var time = DateTimeOffset.UtcNow; var id = Guid.NewGuid(); Directory.CreateDirectory(store.Folder);
    var legacy = new { Version = 1, Id = id, Number = 1, UpdatedAt = time, Archived = false, Context = "safe", Messages = Array.Empty<StoredMessage>() };
    await File.WriteAllTextAsync(Path.Combine(store.Folder, id.ToString("N") + ".json"), JsonSerializer.Serialize(legacy));
    var loaded = (await store.LoadAsync(default)).Single(); Equal("", loaded.Title, "Version 1 doesn't require a title field");
    await store.ReplaceAsync([loaded with { Version = 2, Title = "Product launch plan" }], default);
    var updated = (await store.LoadAsync(default)).Single(); Equal("Product launch plan", updated.Title, "Version 2 stores the goal title");
    string export = Path.Combine(work.Path, "export"); await store.ExportAsync([updated], export, default);
    True(File.ReadAllText(Directory.GetFiles(export, "*.md").Single()).StartsWith("# Product launch plan"), "Markdown export carries the title");
    try { await store.ReplaceAsync([updated with { Title = new string('x', ConversationTitleService.MaxCharacters + 1) }], default); throw new Exception("Oversized title saved"); }
    catch (SafeOperationException) { }
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

sealed class ControlledChatStore(Func<IReadOnlyList<StoredConversation>, CancellationToken, Task> writer) : IChatStore
{
    public string Folder => "synthetic-store";
    public Func<CancellationToken, Task<IReadOnlyList<StoredConversation>>> Loader { get; set; } = _ => Task.FromResult<IReadOnlyList<StoredConversation>>([]);
    public Task<IReadOnlyList<StoredConversation>> LoadAsync(CancellationToken ct) => Loader(ct);
    public Task ReplaceAsync(IReadOnlyList<StoredConversation> chats, CancellationToken ct) => writer(chats, ct);
    public Task ExportAsync(IReadOnlyList<StoredConversation> chats, string folder, CancellationToken ct) => Task.CompletedTask;
}

sealed class ControlledTitleModel(Func<ModelSettings, string, string, CancellationToken, Task<string>> generate) : IConversationTitleModel
{
    public Task<string> GenerateTitleAsync(ModelSettings settings, string safeGoal, string safeContext, CancellationToken ct) => generate(settings, safeGoal, safeContext, ct);
}
