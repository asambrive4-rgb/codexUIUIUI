using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace CodexSwitcher.Feasibility;

internal enum CodexInstallKind
{
    MicrosoftStore,
    Standalone,
    NotFound
}

internal sealed record CodexInstallation(
    CodexInstallKind Kind,
    string? Version,
    string? AppUserModelId,
    string? ExecutablePath)
{
    public bool IsSupportedStoreInstallation =>
        Kind == CodexInstallKind.MicrosoftStore &&
        !string.IsNullOrWhiteSpace(AppUserModelId);
}

internal sealed class CodexInstallationLocator
{
    public CodexInstallation Locate()
    {
        var storeInstallation = TryLocateStorePackage();
        if (storeInstallation is not null)
        {
            return storeInstallation;
        }

        var commandPath = FindOnPath("codex.exe");
        if (commandPath is null)
        {
            return new CodexInstallation(CodexInstallKind.NotFound, null, null, null);
        }

        var packageRoot = FindPackageRoot(commandPath);
        if (packageRoot is null)
        {
            return new CodexInstallation(
                CodexInstallKind.Standalone,
                null,
                null,
                commandPath);
        }

        return ParseStoreManifest(packageRoot, Path.Combine(packageRoot, "AppxManifest.xml"));
    }

    internal static CodexInstallation ParseStoreManifest(string packageRoot, string manifestPath)
    {
        var document = XDocument.Load(manifestPath, LoadOptions.None);
        var package = document.Root
            ?? throw new InvalidDataException("AppxManifest.xml의 Package 요소가 없습니다.");

        var identity = package.Elements().FirstOrDefault(element => element.Name.LocalName == "Identity")
            ?? throw new InvalidDataException("AppxManifest.xml의 Identity 요소가 없습니다.");
        var application = package
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Application" &&
                string.Equals(
                    element.Attribute("Executable")?.Value.Replace('\\', '/'),
                    "app/Codex.exe",
                    StringComparison.OrdinalIgnoreCase))
            ?? package.Descendants().FirstOrDefault(element => element.Name.LocalName == "Application")
            ?? throw new InvalidDataException("AppxManifest.xml의 Application 요소가 없습니다.");

        var packageName = identity.Attribute("Name")?.Value
            ?? throw new InvalidDataException("패키지 이름이 없습니다.");
        var version = identity.Attribute("Version")?.Value;
        var applicationId = application.Attribute("Id")?.Value
            ?? throw new InvalidDataException("애플리케이션 ID가 없습니다.");
        var executable = application.Attribute("Executable")?.Value
            ?? throw new InvalidDataException("애플리케이션 실행 파일이 없습니다.");
        var publisherId = new DirectoryInfo(packageRoot).Name.Split('_').LastOrDefault();

        if (string.IsNullOrWhiteSpace(publisherId))
        {
            throw new InvalidDataException("패키지 게시자 ID를 확인할 수 없습니다.");
        }

        var packageFamilyName = $"{packageName}_{publisherId}";
        var appUserModelId = $"{packageFamilyName}!{applicationId}";
        var executablePath = Path.GetFullPath(
            Path.Combine(packageRoot, executable.Replace('/', Path.DirectorySeparatorChar)));

        return new CodexInstallation(
            CodexInstallKind.MicrosoftStore,
            version,
            appUserModelId,
            executablePath);
    }

    internal static CodexInstallation ParseStoreProbeJson(string json)
    {
        var probe = JsonSerializer.Deserialize<AppxPackageProbe>(json)
            ?? throw new InvalidDataException("Store 패키지 조사 결과를 읽을 수 없습니다.");

        if (string.IsNullOrWhiteSpace(probe.PackageFamilyName) ||
            string.IsNullOrWhiteSpace(probe.InstallLocation))
        {
            throw new InvalidDataException("Store 패키지 조사 결과에 필수 값이 없습니다.");
        }

        var appUserModelId = string.IsNullOrWhiteSpace(probe.AppUserModelId)
            ? $"{probe.PackageFamilyName}!App"
            : probe.AppUserModelId;
        var executablePath = Path.Combine(probe.InstallLocation, "app", "Codex.exe");

        return new CodexInstallation(
            CodexInstallKind.MicrosoftStore,
            probe.Version,
            appUserModelId,
            File.Exists(executablePath) ? executablePath : null);
    }

    private static CodexInstallation? TryLocateStorePackage()
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (!File.Exists(powershell))
        {
            return null;
        }

        const string script =
            """
            $ErrorActionPreference = 'Stop'
            $package = Get-AppxPackage -Name 'OpenAI.Codex' |
                Sort-Object Version -Descending |
                Select-Object -First 1
            if ($null -ne $package) {
                $app = Get-StartApps |
                    Where-Object { $_.AppID -like ($package.PackageFamilyName + '!*') } |
                    Select-Object -First 1
                [pscustomobject]@{
                    Version = [string]$package.Version
                    PackageFamilyName = [string]$package.PackageFamilyName
                    InstallLocation = [string]$package.InstallLocation
                    AppUserModelId = [string]$app.AppID
                } | ConvertTo-Json -Compress
            }
            """;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            if (!process.Start())
            {
                return null;
            }

            if (!process.WaitForExit(milliseconds: 5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                ? ParseStoreProbeJson(output)
                : null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            Win32Exception or
            JsonException)
        {
            return null;
        }
    }

    private static string? FindPackageRoot(string commandPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(commandPath)!);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AppxManifest.xml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        var buffer = new StringBuilder(32768);
        var result = SearchPath(
            path: null,
            fileName,
            extension: null,
            buffer.Capacity,
            buffer,
            out _);

        return result is > 0 and < 32768 ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SearchPath(
        string? path,
        string fileName,
        string? extension,
        int bufferLength,
        StringBuilder buffer,
        out IntPtr filePart);

    private sealed record AppxPackageProbe(
        string? Version,
        string? PackageFamilyName,
        string? InstallLocation,
        string? AppUserModelId);
}
