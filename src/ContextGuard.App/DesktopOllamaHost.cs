using System.Diagnostics;
using System.Text;
using ContextGuard.Core;

namespace ContextGuard.App;

// OS integration only; setup sequencing and endpoint policy belong to OllamaSetupService.
public sealed class DesktopOllamaHost : IOllamaHost
{
    public async Task<string?> FindExecutableAsync(string? selectedPath, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        return await Task.Run(() =>
        {
            string name = OperatingSystem.IsWindows() ? "ollama.exe" : "ollama";
            IEnumerable<string> candidates;
            if (!string.IsNullOrWhiteSpace(selectedPath)) candidates = [selectedPath];
            else
            {
                string[] standard = OperatingSystem.IsWindows()
                    ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", name),
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", name)]
                    : ["/usr/local/bin/ollama", "/usr/bin/ollama", "/Applications/Ollama.app/Contents/Resources/ollama"];
                candidates = standard.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Take(64).Select(p => Path.Combine(p.Trim('"'), name)));
            }
            foreach (string candidate in candidates)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    if (candidate.Length > 4096 || !Path.IsPathFullyQualified(candidate) ||
                        !Path.GetFileName(candidate).Equals(name, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                    string full = Path.GetFullPath(candidate);
                    if (File.Exists(full)) return full;
                }
                catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
            }
            if (!string.IsNullOrWhiteSpace(selectedPath)) throw new SafeOperationException("OllamaExecutableInvalid");
            return null;
        }, deadline.Token);
    }

    public Task StartAsync(string executable, Uri endpoint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory };
        start.ArgumentList.Add("serve");
        start.Environment["OLLAMA_HOST"] = endpoint.GetLeftPart(UriPartial.Authority);
        start.Environment["OLLAMA_NO_CLOUD"] = "1";
        Launch(start);
        return Task.CompletedTask;
    }

    public void OpenDownloadPage() => Launch(new ProcessStartInfo(OperatingSystem.IsWindows()
        ? "https://ollama.com/download/windows" : "https://ollama.com/download") { UseShellExecute = true });

    public void OpenPullTerminal(string executable, Uri endpoint, string model)
    {
        if (!OperatingSystem.IsWindows()) throw new SafeOperationException("OllamaTerminalUnsupported");
        Launch(CreatePullStartInfo(executable, endpoint, model));
    }

    public static ProcessStartInfo CreatePullStartInfo(string executable, Uri endpoint, string model)
    {
        OllamaSetupService.ValidateLaunchEndpoint(endpoint.AbsoluteUri);
        if (!Path.IsPathFullyQualified(executable) || executable.Length > 4096 || !OllamaSetupService.IsValidPullModel(model))
            throw new SafeOperationException("OllamaPullModelInvalid");
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        string script = "$env:OLLAMA_HOST = " + Quote(endpoint.GetLeftPart(UriPartial.Authority)) +
            "; & " + Quote(executable) + " pull " + Quote(model);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NoExit");
        start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        return start;
    }

    private static void Launch(ProcessStartInfo start)
    {
        try { using var process = Process.Start(start); if (process is null) throw new SafeOperationException("OllamaLaunchFailed"); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { throw new SafeOperationException("OllamaLaunchFailed"); }
    }
}
