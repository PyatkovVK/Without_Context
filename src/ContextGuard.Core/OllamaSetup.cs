using System.Text.RegularExpressions;

namespace ContextGuard.Core;

public interface ILocalModelCatalog
{
    Task<IReadOnlyList<string>> ListLocalModelsAsync(string endpoint, CancellationToken ct);
}

public interface IOllamaHost
{
    Task<string?> FindExecutableAsync(string? selectedPath, CancellationToken ct);
    Task StartAsync(string executable, Uri endpoint, CancellationToken ct);
    void OpenDownloadPage();
    void OpenPullTerminal(string executable, Uri endpoint, string model);
}

public enum OllamaAvailability { Unchecked, Missing, Stopped, Running }

public sealed record OllamaSetupResult(OllamaAvailability Availability, string? Executable,
    IReadOnlyList<string> Models);

// Setup uses metadata only. A model listed here still requires validation before raw text is sent.
public sealed class OllamaSetupService(IOllamaHost host, ILocalModelCatalog catalog)
{
    public async Task<OllamaSetupResult> InspectAsync(string endpoint, string? selectedPath, CancellationToken ct)
    {
        ModelEndpointPolicy.Validate(endpoint, true);
        string? executable = await host.FindExecutableAsync(selectedPath, ct);
        ct.ThrowIfCancellationRequested();
        try
        {
            var models = await catalog.ListLocalModelsAsync(endpoint, ct);
            ct.ThrowIfCancellationRequested();
            return new(OllamaAvailability.Running, executable, models);
        }
        catch (SafeOperationException e) when (e.MessageKey == "ConnectionFailed")
        {
            ct.ThrowIfCancellationRequested();
            return new(executable is null ? OllamaAvailability.Missing : OllamaAvailability.Stopped, executable, []);
        }
    }

    public async Task<OllamaSetupResult> StartAsync(string endpoint, string executable, CancellationToken ct)
    {
        Uri address = ValidateLaunchEndpoint(endpoint);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            string? found = await host.FindExecutableAsync(executable, deadline.Token);
            if (found is null) throw new SafeOperationException("OllamaExecutableInvalid");
            // Avoid starting a second server if it became available since the last check.
            var current = await InspectAsync(endpoint, found, deadline.Token);
            if (current.Availability == OllamaAvailability.Running) return current;
            await host.StartAsync(found, address, deadline.Token);
            while (true)
            {
                await Task.Delay(500, deadline.Token);
                current = await InspectAsync(endpoint, found, deadline.Token);
                if (current.Availability == OllamaAvailability.Running) return current;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new SafeOperationException("OllamaStartTimeout"); }
    }

    public void OpenDownloadPage() => host.OpenDownloadPage();

    public async Task OpenPullTerminalAsync(string endpoint, string executable, string model, CancellationToken ct)
    {
        Uri address = ValidateLaunchEndpoint(endpoint);
        if (!IsValidPullModel(model)) throw new SafeOperationException("OllamaPullModelInvalid");
        string? found = await host.FindExecutableAsync(executable, ct);
        ct.ThrowIfCancellationRequested();
        if (found is null) throw new SafeOperationException("OllamaExecutableInvalid");
        host.OpenPullTerminal(found, address, model);
    }

    public static bool IsValidPullModel(string model) => model.Length is > 0 and <= 200 &&
        !model.Contains("cloud", StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(model, @"\A[a-zA-Z0-9][a-zA-Z0-9._/-]*(?::[a-zA-Z0-9][a-zA-Z0-9._-]*)?\z",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static Uri ValidateLaunchEndpoint(string endpoint)
    {
        var address = ModelEndpointPolicy.Validate(endpoint, true);
        if (address.Scheme != "http" || address.AbsolutePath != "/")
            throw new SafeOperationException("OllamaLaunchEndpointInvalid");
        return address;
    }
}
