using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace OpenTiaToolkit.Core.Services;

public sealed class HmiWebViewSession
{
    private readonly string _root;

    public HmiWebViewSession() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTiaToolkit", "hmi-webview", "v1")) { }

    internal HmiWebViewSession(string root) => _root = root;

    internal int Launch()
    {
        var portalRoot = TiaPortalAssemblyResolver.GetTiaPortalRoot()
            ?? throw new InvalidOperationException("TIA Portal V21 installation was not found.");
        if (!File.Exists(Path.Combine(portalRoot, "PublicAPI", "V21", "net48", "Siemens.Engineering.Base.dll")))
            throw new InvalidOperationException("HMI capture requires a TIA Portal V21 installation.");
        var executable = Path.Combine(portalRoot, "Bin", "Siemens.Automation.Portal.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("TIA Portal executable was not found.", executable);

        Directory.CreateDirectory(_root);
        using var launchLock = File.Open(Path.Combine(_root, "launch.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        var profile = Directory.GetDirectories(_root, "profile-*")
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault(path => !IsSessionRunning(path) && !HasWebViewListener(path, listeners))
            ?? Path.Combine(_root, "profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var portFile = ActivePortPath(profile);
        if (File.Exists(portFile)) File.Delete(portFile);

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)
        };
        startInfo.EnvironmentVariables["WEBVIEW2_USER_DATA_FOLDER"] = Path.Combine(profile, "browser");
        startInfo.EnvironmentVariables["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] =
            "--remote-debugging-port=0 --remote-debugging-address=127.0.0.1";
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start capture-ready TIA Portal.");
        try
        {
            File.WriteAllLines(IdentityPath(profile), new[]
            {
                process.Id.ToString(CultureInfo.InvariantCulture),
                process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)
            });
            return process.Id;
        }
        catch
        {
            if (!process.HasExited) process.Kill();
            throw;
        }
    }

    internal (Uri Endpoint, string BrowserPath) GetEndpoint(int instanceId)
    {
        var profile = Directory.Exists(_root)
            ? Directory.GetDirectories(_root, "profile-*").FirstOrDefault(path => IsSessionRunning(path, instanceId))
            : null;
        if (profile == null)
            throw new InvalidOperationException("The selected TIA instance was not launched for HMI capture. Ask before restarting a user's open TIA instance.");

        var portFile = ActivePortPath(profile);
        if (!File.Exists(portFile))
            throw new InvalidOperationException("The WebView2 endpoint is not ready. Open a screen in the Next Gen editor in the selected TIA instance.");

        var lines = File.ReadAllLines(portFile);
        if (lines.Length < 2 || !int.TryParse(lines[0], out var port) || port < 1 || port > 65535 ||
            !lines[1].StartsWith("/devtools/browser/", StringComparison.Ordinal))
            throw new InvalidOperationException("WebView2 has not published a valid local DevTools endpoint.");
        return (new UriBuilder("http", IPAddress.Loopback.ToString(), port).Uri, lines[1]);
    }

    private static bool IsSessionRunning(string profile, int? expectedId = null)
    {
        var path = IdentityPath(profile);
        if (!File.Exists(path)) return false;
        var lines = File.ReadAllLines(path);
        if (lines.Length != 2 || !int.TryParse(lines[0], out var id) ||
            (expectedId.HasValue && id != expectedId.Value) || !long.TryParse(lines[1], out var startedAt))
            return false;
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startedAt;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool HasWebViewListener(string profile, IPEndPoint[] listeners)
    {
        var path = ActivePortPath(profile);
        return File.Exists(path) && int.TryParse(File.ReadLines(path).FirstOrDefault(), out var port) &&
            listeners.Any(listener => listener.Port == port);
    }

    private static string IdentityPath(string profile) => Path.Combine(profile, "session.txt");
    private static string ActivePortPath(string profile) => Path.Combine(profile, "browser", "EBWebView", "DevToolsActivePort");
}
