using ContextGuard.Core;

internal static class OllamaSetupChecks
{
    public static async Task Run()
    {
        const string endpoint = "http://localhost:11434";
        var host = new FakeOllamaHost();
        var catalog = new Catalog();
        var service = new OllamaSetupService(host, catalog);
        var missing = await service.InspectAsync(endpoint, null, default);
        Assert(missing.Availability == OllamaAvailability.Missing && missing.Executable is null, "Missing executable and unavailable server");
        host.Executable = "synthetic-ollama";
        var stopped = await service.InspectAsync(endpoint, null, default);
        Assert(stopped.Availability == OllamaAvailability.Stopped && stopped.Models.Count == 0, "Installed but stopped");
        Assert(host.Starts == 0 && host.Pulls == 0 && host.Downloads == 0, "Inspection has no process or browser side effects");
        catalog.Running = true;
        var running = await service.InspectAsync(endpoint, "chosen-ollama", default);
        Assert(running.Availability == OllamaAvailability.Running && running.Models.SequenceEqual(["demo-local"]), "Running metadata list");
        Assert(host.Selected == "chosen-ollama", "Manual path passed to host");
        catalog.Models = [];
        Assert((await service.InspectAsync(endpoint, null, default)).Availability == OllamaAvailability.Running, "Empty list means running without installed models");
        host.Executable = null;
        Assert((await service.InspectAsync(endpoint, null, default)).Availability == OllamaAvailability.Running, "Server can run without a discoverable CLI");
        host.Executable = "synthetic-ollama";
        await service.StartAsync(endpoint, host.Executable, default);
        Assert(host.Starts == 0, "Starting an already running server does not spawn another");
        catalog.Running = false; host.OnStart = () => catalog.Running = true;
        var started = await service.StartAsync(endpoint, host.Executable, default);
        Assert(host.Starts == 1 && started.Availability == OllamaAvailability.Running, "Start waits for metadata readiness");
        await service.OpenPullTerminalAsync(endpoint, host.Executable, "qwen3:4b", default);
        Assert(host.Pulls == 1 && host.Model == "qwen3:4b" && host.Endpoint?.Host == "localhost", "Pull gets validated model and local endpoint");
        service.OpenDownloadPage(); Assert(host.Downloads == 1, "Download explicitly opens browser");
        foreach (string invalid in new[] { "", "-x", "qwen3:cloud", "a; calc", "a\nwhoami", "$(calc)", "a'", "a b", new string('a', 201) })
        {
            Assert(!OllamaSetupService.IsValidPullModel(invalid), "Unsafe model rejected");
            await Reject(() => service.OpenPullTerminalAsync(endpoint, host.Executable, invalid, default), "OllamaPullModelInvalid");
        }
        int reads = catalog.Reads;
        await Reject(() => service.InspectAsync("https://example.com", null, default), "LocalEndpointOnly");
        await Reject(() => service.StartAsync("http://0.0.0.0:11434", host.Executable, default), "LocalEndpointOnly");
        await Reject(() => service.OpenPullTerminalAsync("http://localhost:11434/api", host.Executable, "demo", default), "OllamaLaunchEndpointInvalid");
        Assert(catalog.Reads == reads && host.Starts == 1 && host.Pulls == 1, "Invalid requests cause no external side effects");
        catalog.Error = "AuthenticationRejected";
        await Reject(() => service.InspectAsync(endpoint, null, default), "AuthenticationRejected");
        catalog.Error = null;
        using var cancel = new CancellationTokenSource();
        var late = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.Controlled = late.Task;
        var pending = service.InspectAsync(endpoint, null, cancel.Token);
        cancel.Cancel(); late.SetResult(["late-model"]);
        try { await pending; throw new Exception("Cancelled inspection succeeded"); } catch (OperationCanceledException) { }
        catalog.Controlled = null; catalog.Models = ["fresh-model"];
        Assert((await service.InspectAsync(endpoint, null, default)).Models.Single() == "fresh-model", "Cancellation does not poison the next metadata check");
    }

    private static async Task Reject(Func<Task> action, string key)
    {
        try { await action(); } catch (SafeOperationException e) when (e.MessageKey == key) { return; }
        throw new Exception("Expected safe error: " + key);
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    private sealed class Catalog : ILocalModelCatalog
    {
        public bool Running;
        public string[] Models = ["demo-local"];
        public string? Error;
        public int Reads;
        public Task<IReadOnlyList<string>>? Controlled;
        public Task<IReadOnlyList<string>> ListLocalModelsAsync(string endpoint, CancellationToken ct)
        {
            Reads++;
            if (Controlled is not null) return Controlled;
            if (Error is not null) throw new SafeOperationException(Error);
            if (!Running) throw new SafeOperationException("ConnectionFailed");
            return Task.FromResult<IReadOnlyList<string>>(Models.ToArray());
        }
    }

    private sealed class FakeOllamaHost : IOllamaHost
    {
        public string? Executable, Selected, Model;
        public Uri? Endpoint;
        public int Starts, Pulls, Downloads;
        public Action? OnStart;
        public Task<string?> FindExecutableAsync(string? selectedPath, CancellationToken ct)
        { Selected = selectedPath; ct.ThrowIfCancellationRequested(); return Task.FromResult(Executable); }
        public Task StartAsync(string executable, Uri endpoint, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Starts++; OnStart?.Invoke(); return Task.CompletedTask; }
        public void OpenDownloadPage() => Downloads++;
        public void OpenPullTerminal(string executable, Uri endpoint, string model) { Pulls++; Model = model; Endpoint = endpoint; }
    }
}
