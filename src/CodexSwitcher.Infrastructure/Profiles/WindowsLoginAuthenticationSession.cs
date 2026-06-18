using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Infrastructure.Profiles;

public sealed partial class WindowsLoginAuthenticationSession
    : IAuthenticationSession
{
    private const string StateFileName = "recovery.json";
    private const string CredentialFileName = "previous-credential.bin";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _codexHome;
    private readonly string _recoveryRoot;
    private readonly CurrentUserDataProtector _protector = new();
    private readonly CurrentUserStorageAcl _storageAcl = new();

    public WindowsLoginAuthenticationSession()
        : this(GetDefaultCodexHome(), GetDefaultRecoveryRoot())
    {
    }

    public WindowsLoginAuthenticationSession(
        string codexHome,
        string recoveryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codexHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryRoot);
        _codexHome = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(codexHome));
        _recoveryRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(recoveryRoot));
    }

    public Task<bool> HasPendingRecoveryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            File.Exists(StatePath) ||
            File.Exists(RecoveryCredentialPath));
    }

    public async Task PrepareForLoginAsync(
        CancellationToken cancellationToken)
    {
        await PrepareChangeAsync(
            replacementCredential: null,
            cancellationToken);
    }

    public async Task PrepareForProfileAsync(
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken)
    {
        await PrepareChangeAsync(
            credential,
            cancellationToken);
    }

    private async Task PrepareChangeAsync(
        ReadOnlyMemory<byte>? replacementCredential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFileAuthenticationSupported();
        EnsureCodexHomeIsSafe();
        EnsureRecoveryRoot();

        if (File.Exists(StatePath) ||
            File.Exists(RecoveryCredentialPath))
        {
            throw new InvalidOperationException(
                "이전 로그인 작업 복구가 먼저 필요합니다.");
        }

        var authPath = AuthPath;
        var hadCredential = File.Exists(authPath);
        byte[]? credential = null;
        byte[]? protectedCredential = null;

        try
        {
            if (hadCredential)
            {
                EnsureFileIsNotReparsePoint(authPath);
                credential = await File.ReadAllBytesAsync(
                    authPath,
                    cancellationToken);
                protectedCredential = _protector.Protect(credential);
                await AtomicFileWriter.WriteAsync(
                    RecoveryCredentialPath,
                    protectedCredential,
                    cancellationToken);
            }

            var stateBytes = JsonSerializer.SerializeToUtf8Bytes(
                new RecoveryState(hadCredential),
                JsonOptions);
            try
            {
                await AtomicFileWriter.WriteAsync(
                    StatePath,
                    stateBytes,
                    cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(stateBytes);
            }

            if (replacementCredential is not null)
            {
                await AtomicFileWriter.WriteAsync(
                    authPath,
                    replacementCredential.Value,
                    cancellationToken);
            }
            else if (File.Exists(authPath))
            {
                File.Delete(authPath);
            }
        }
        catch
        {
            if (File.Exists(StatePath))
            {
                try
                {
                    await RestorePreviousAsync(CancellationToken.None);
                    await ClearRecoveryAsync(CancellationToken.None);
                }
                catch
                {
                    // 복구 정보는 다음 앱 실행에서 다시 사용할 수 있게 남긴다.
                }
            }
            else if (File.Exists(RecoveryCredentialPath))
            {
                File.Delete(RecoveryCredentialPath);
            }

            throw;
        }
        finally
        {
            if (credential is not null)
            {
                CryptographicOperations.ZeroMemory(credential);
            }

            if (protectedCredential is not null)
            {
                CryptographicOperations.ZeroMemory(protectedCredential);
            }
        }
    }

    public async Task<byte[]?> ReadCurrentCredentialAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCodexHomeIsSafe();

        if (!File.Exists(AuthPath))
        {
            return null;
        }

        EnsureFileIsNotReparsePoint(AuthPath);
        return await File.ReadAllBytesAsync(
            AuthPath,
            cancellationToken);
    }

    public async Task RestorePreviousAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCodexHomeIsSafe();

        var state = await ReadRecoveryStateAsync(cancellationToken);
        if (!state.HadCredential)
        {
            if (File.Exists(AuthPath))
            {
                File.Delete(AuthPath);
            }

            return;
        }

        if (!File.Exists(RecoveryCredentialPath))
        {
            throw new InvalidDataException(
                "복구할 인증 사본을 찾을 수 없습니다.");
        }

        EnsureFileIsNotReparsePoint(RecoveryCredentialPath);
        var protectedCredential = await File.ReadAllBytesAsync(
            RecoveryCredentialPath,
            cancellationToken);

        try
        {
            var credential = _protector.Unprotect(protectedCredential);

            try
            {
                await AtomicFileWriter.WriteAsync(
                    AuthPath,
                    credential,
                    cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(credential);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedCredential);
        }
    }

    public Task ClearRecoveryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRecoveryRoot();

        DeleteExpectedFile(RecoveryCredentialPath);
        DeleteExpectedFile(StatePath);
        return Task.CompletedTask;
    }

    public static string GetDefaultCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            ".codex");
    }

    public static string GetDefaultRecoveryRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CodexAccountSwitcher",
            "LoginRecovery",
            "v1");
    }

    private string AuthPath => Path.Combine(_codexHome, "auth.json");

    private string ConfigPath => Path.Combine(_codexHome, "config.toml");

    private string StatePath => Path.Combine(
        _recoveryRoot,
        StateFileName);

    private string RecoveryCredentialPath => Path.Combine(
        _recoveryRoot,
        CredentialFileName);

    private async Task<RecoveryState> ReadRecoveryStateAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(StatePath))
        {
            throw new InvalidDataException(
                "로그인 작업 복구 정보를 찾을 수 없습니다.");
        }

        EnsureFileIsNotReparsePoint(StatePath);
        await using var stream = new FileStream(
            StatePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await JsonSerializer.DeserializeAsync<RecoveryState>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException(
                   "로그인 작업 복구 정보를 읽을 수 없습니다.");
    }

    private void EnsureFileAuthenticationSupported()
    {
        var configuredStore = ReadConfiguredStore();
        var authExists = File.Exists(AuthPath);
        var supported = configuredStore switch
        {
            "file" => true,
            "auto" when authExists => true,
            null when authExists => true,
            _ => false
        };

        if (!supported)
        {
            throw new InvalidOperationException(
                "현재 Codex 인증 저장 방식에서는 프로필 로그인을 안전하게 준비할 수 없습니다.");
        }
    }

    private string? ReadConfiguredStore()
    {
        if (!File.Exists(ConfigPath))
        {
            return null;
        }

        EnsureFileIsNotReparsePoint(ConfigPath);
        foreach (var line in File.ReadLines(ConfigPath))
        {
            var match = CredentialStorePattern().Match(line);
            if (match.Success)
            {
                return match.Groups["value"].Value.ToLowerInvariant();
            }
        }

        return null;
    }

    private void EnsureCodexHomeIsSafe()
    {
        Directory.CreateDirectory(_codexHome);
        if ((File.GetAttributes(_codexHome) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "재분석 지점은 Codex 데이터 폴더로 사용할 수 없습니다.");
        }
    }

    private void EnsureRecoveryRoot()
    {
        if (Directory.Exists(_recoveryRoot) &&
            (File.GetAttributes(_recoveryRoot) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "재분석 지점은 복구 저장소로 사용할 수 없습니다.");
        }

        _storageAcl.EnsureProtectedDirectory(_recoveryRoot);
    }

    private static void EnsureFileIsNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "재분석 지점의 인증 파일은 사용할 수 없습니다.");
        }
    }

    private static void DeleteExpectedFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        EnsureFileIsNotReparsePoint(path);
        File.Delete(path);
    }

    [GeneratedRegex(
        "^\\s*cli_auth_credentials_store\\s*=\\s*\"(?<value>file|keyring|auto)\"\\s*(?:#.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialStorePattern();

    private sealed record RecoveryState(bool HadCredential);
}
