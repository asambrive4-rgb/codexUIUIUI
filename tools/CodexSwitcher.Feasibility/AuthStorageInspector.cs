using System.Text.RegularExpressions;

namespace CodexSwitcher.Feasibility;

internal enum AuthStorageKind
{
    FileConfigured,
    FileObserved,
    KeyringConfigured,
    Undetermined
}

internal sealed record AuthStorageStatus(
    AuthStorageKind Kind,
    bool AuthFileExists,
    bool SupportsOpaqueFileSwitching,
    string Description);

internal sealed partial class AuthStorageInspector
{
    public AuthStorageStatus Inspect(FeasibilityPaths paths)
    {
        var configuredStore = ReadConfiguredStore(paths.ConfigFile);
        var authFileExists = File.Exists(paths.AuthFile);

        return configuredStore switch
        {
            "file" => new AuthStorageStatus(
                AuthStorageKind.FileConfigured,
                authFileExists,
                true,
                authFileExists
                    ? "설정에서 파일 저장을 사용하며 auth.json이 있습니다."
                    : "설정에서 파일 저장을 사용하지만 auth.json이 아직 없습니다."),
            "keyring" => new AuthStorageStatus(
                AuthStorageKind.KeyringConfigured,
                authFileExists,
                false,
                "설정에서 Windows 자격 증명 저장소를 사용합니다. 파일 교체 실험을 중단해야 합니다."),
            "auto" when authFileExists => new AuthStorageStatus(
                AuthStorageKind.FileObserved,
                true,
                true,
                "자동 저장 설정에서 auth.json 사용이 관찰됐습니다."),
            "auto" => new AuthStorageStatus(
                AuthStorageKind.Undetermined,
                false,
                false,
                "자동 저장 설정이며 auth.json이 없습니다. 자격 증명 저장소 사용 여부를 확정할 수 없습니다."),
            null when authFileExists => new AuthStorageStatus(
                AuthStorageKind.FileObserved,
                true,
                true,
                "저장 방식이 명시되지 않았지만 auth.json 사용이 관찰됐습니다."),
            _ => new AuthStorageStatus(
                AuthStorageKind.Undetermined,
                false,
                false,
                "인증 저장 방식을 확정할 수 없습니다.")
        };
    }

    private static string? ReadConfiguredStore(string configFile)
    {
        if (!File.Exists(configFile))
        {
            return null;
        }

        foreach (var line in File.ReadLines(configFile))
        {
            var match = CredentialStorePattern().Match(line);
            if (match.Success)
            {
                return match.Groups["value"].Value.ToLowerInvariant();
            }
        }

        return null;
    }

    [GeneratedRegex(
        "^\\s*cli_auth_credentials_store\\s*=\\s*\"(?<value>file|keyring|auto)\"\\s*(?:#.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialStorePattern();
}

