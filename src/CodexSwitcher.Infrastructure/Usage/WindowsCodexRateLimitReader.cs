using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;
using CodexSwitcher.Infrastructure.Profiles;

namespace CodexSwitcher.Infrastructure.Usage;

public sealed class WindowsCodexRateLimitReader
    : IProfileRateLimitReader
{
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CurrentUserStorageAcl _storageAcl = new();
    private readonly string _probeRoot;
    private AppServerSession? _activeSession;
    private ProfileId? _activeProfileId;
    private string? _codexExecutablePath;
    private bool _probeRootPrepared;
    private bool _disposed;

    public WindowsCodexRateLimitReader()
    {
        _probeRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CodexAccountSwitcher",
            "UsageProbe",
            "v1");
    }

    public async Task<ProfileRateLimitReadResult> ReadAsync(
        ProfileId profileId,
        ReadOnlyMemory<byte> credential,
        bool keepAlive,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);

            AppServerSession? temporarySession = null;
            try
            {
                var session = keepAlive
                    ? await GetActiveSessionAsync(
                        profileId,
                        credential,
                        linked.Token)
                    : temporarySession =
                        await CreateTemporarySessionAsync(
                        credential,
                        linked.Token);

                var result = await session.ReadRateLimitsAsync(
                    credential,
                    linked.Token);

                if (keepAlive && result.Status !=
                    ProfileRateLimitStatus.Available)
                {
                    DisposeActiveSession();
                }

                return result;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                if (keepAlive)
                {
                    DisposeActiveSession();
                }

                return Failed();
            }
            catch (RpcException exception)
            {
                if (keepAlive)
                {
                    DisposeActiveSession();
                }

                return exception.Code == -32601
                    ? new ProfileRateLimitReadResult(
                        ProfileRateLimitStatus.CodexUpdateRequired,
                        [])
                    : Failed();
            }
            catch (Exception exception)
                when (exception is IOException or
                      UnauthorizedAccessException or
                      InvalidOperationException or
                      JsonException)
            {
                if (keepAlive)
                {
                    DisposeActiveSession();
                }

                return Failed();
            }
            finally
            {
                temporarySession?.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void StopActiveSession()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Wait();
        try
        {
            DisposeActiveSession();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Wait();
        try
        {
            DisposeActiveSession();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<AppServerSession> GetActiveSessionAsync(
        ProfileId profileId,
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken)
    {
        if (_activeSession is not null &&
            _activeProfileId == profileId &&
            _activeSession.MatchesCredential(credential.Span))
        {
            return _activeSession;
        }

        DisposeActiveSession();
        _activeSession = await CreateActiveSessionAsync(
            credential,
            cancellationToken);
        _activeProfileId = profileId;
        return _activeSession;
    }

    private async Task<AppServerSession> CreateActiveSessionAsync(
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken)
    {
        var codexHome =
            WindowsLoginAuthenticationSession.GetDefaultCodexHome();
        var authPath = Path.Combine(codexHome, "auth.json");
        if (!File.Exists(authPath))
        {
            throw new FileNotFoundException(
                "현재 Codex 인증을 찾을 수 없습니다.");
        }

        var currentCredential = await File.ReadAllBytesAsync(
            authPath,
            cancellationToken);
        try
        {
            if (!CredentialsEqual(
                    credential.Span,
                    currentCredential))
            {
                throw new InvalidOperationException(
                    "활성 프로필과 현재 Codex 인증이 일치하지 않습니다.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentCredential);
        }

        var executablePath =
            await ResolveCodexExecutablePathAsync(cancellationToken);
        return await AppServerSession.StartAsync(
            executablePath,
            codexHome,
            credential,
            deleteSessionDirectory: _ => { },
            cancellationToken);
    }

    private async Task<AppServerSession> CreateTemporarySessionAsync(
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken)
    {
        PrepareProbeRoot();

        var sessionRoot = Path.Combine(
            _probeRoot,
            Guid.NewGuid().ToString("N"));
        EnsureDirectChild(sessionRoot);
        _storageAcl.EnsureProtectedDirectory(sessionRoot);

        try
        {
            await AtomicFileWriter.WriteAsync(
                Path.Combine(sessionRoot, "auth.json"),
                credential,
                cancellationToken);
            await AtomicFileWriter.WriteAsync(
                Path.Combine(sessionRoot, "config.toml"),
                Encoding.UTF8.GetBytes(
                    "cli_auth_credentials_store = \"file\"\n"),
                cancellationToken);

            var executablePath =
                await ResolveCodexExecutablePathAsync(cancellationToken);
            return await AppServerSession.StartAsync(
                executablePath,
                sessionRoot,
                credential,
                DeleteOwnedSessionDirectory,
                cancellationToken);
        }
        catch
        {
            DeleteOwnedSessionDirectory(sessionRoot);
            throw;
        }
    }

    private void PrepareProbeRoot()
    {
        if (_probeRootPrepared)
        {
            return;
        }

        _storageAcl.EnsureProtectedDirectory(_probeRoot);
        EnsureNotReparsePoint(_probeRoot);

        try
        {
            foreach (var staleDirectory in Directory.EnumerateDirectories(
                         _probeRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                DeleteOwnedSessionDirectory(staleDirectory);
            }
        }
        catch (Exception exception)
            when (UsageProbeDirectoryCleaner
                .IsRecoverableCleanupException(exception))
        {
        }

        _probeRootPrepared = true;
    }

    private async Task<string> ResolveCodexExecutablePathAsync(
        CancellationToken cancellationToken)
    {
        if (_codexExecutablePath is not null &&
            File.Exists(_codexExecutablePath))
        {
            return _codexExecutablePath;
        }

        foreach (var candidate in GetUserLocalCodexCandidates())
        {
            if (File.Exists(candidate))
            {
                _codexExecutablePath = candidate;
                return candidate;
            }
        }

        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershellPath))
        {
            throw new FileNotFoundException(
                "Codex 실행 파일을 찾을 수 없습니다.");
        }

        const string script =
            """
            $command = Get-Command codex.exe -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($null -ne $command -and (Test-Path -LiteralPath $command.Source)) {
                [Console]::Out.Write($command.Source)
                exit 0
            }

            $package = Get-AppxPackage -Name 'OpenAI.Codex' |
                Sort-Object Version -Descending |
                Select-Object -First 1
            if ($null -eq $package) {
                exit 0
            }

            $candidate = Join-Path $package.InstallLocation 'app\resources\codex.exe'
            if (Test-Path -LiteralPath $candidate) {
                [Console]::Out.Write($candidate)
            }
            """;
        var encodedCommand = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(script));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = powershellPath,
                Arguments =
                    $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Codex 위치 확인을 시작하지 못했습니다.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        _ = await errorTask;

        if (process.ExitCode != 0 ||
            output.Length == 0 ||
            !File.Exists(output))
        {
            throw new FileNotFoundException(
                "Codex 실행 파일을 찾을 수 없습니다.");
        }

        _codexExecutablePath = output;
        return output;
    }

    private static IEnumerable<string> GetUserLocalCodexCandidates()
    {
        var configuredHome =
            Environment.GetEnvironmentVariable("CODEX_HOME");
        var defaultHome = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            ".codex");

        foreach (var home in new[]
                 {
                     configuredHome,
                     defaultHome
                 }
                 .Where(home => !string.IsNullOrWhiteSpace(home))
                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(
                home!,
                ".sandbox-bin",
                "codex.exe");
            yield return Path.Combine(
                home!,
                "plugins",
                ".plugin-appserver",
                "codex.exe");
        }
    }

    private void DisposeActiveSession()
    {
        _activeSession?.Dispose();
        _activeSession = null;
        _activeProfileId = null;
    }

    private void EnsureDirectChild(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(path));
        var parent = Directory.GetParent(fullPath)?.FullName;
        if (parent is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(
                Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(parent)),
                Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(_probeRoot))))
        {
            throw new InvalidOperationException(
                "사용량 조회 임시 경로가 관리 범위를 벗어났습니다.");
        }
    }

    private void DeleteOwnedSessionDirectory(string path)
    {
        _ = UsageProbeDirectoryCleaner.TryDeleteSessionDirectory(
            _probeRoot,
            path);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "재분석 지점은 사용량 조회 경로로 사용할 수 없습니다.");
        }
    }

    private static ProfileRateLimitReadResult Failed() =>
        new(ProfileRateLimitStatus.Failed, []);

    private static bool CredentialsEqual(
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual)
    {
        return expected.Length == actual.Length &&
               CryptographicOperations.FixedTimeEquals(
                   expected,
                   actual);
    }

    private sealed class AppServerSession : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _input;
        private readonly StreamReader _output;
        private readonly string _sessionRoot;
        private readonly Action<string> _deleteSessionDirectory;
        private byte[] _credentialHash;
        private long _nextRequestId = 1;
        private bool _disposed;

        private AppServerSession(
            Process process,
            string sessionRoot,
            ReadOnlySpan<byte> credential,
            Action<string> deleteSessionDirectory)
        {
            _process = process;
            _input = process.StandardInput;
            _output = process.StandardOutput;
            _sessionRoot = sessionRoot;
            _deleteSessionDirectory = deleteSessionDirectory;
            _credentialHash = SHA256.HashData(credential);
        }

        public static async Task<AppServerSession> StartAsync(
            string executablePath,
            string sessionRoot,
            ReadOnlyMemory<byte> credential,
            Action<string> deleteSessionDirectory,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--listen");
            startInfo.ArgumentList.Add("stdio://");
            startInfo.Environment["CODEX_HOME"] = sessionRoot;

            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "Codex 사용량 조회 프로세스를 시작하지 못했습니다.");
            }

            _ = process.StandardError.ReadToEndAsync();
            var session = new AppServerSession(
                process,
                sessionRoot,
                credential.Span,
                deleteSessionDirectory);

            try
            {
                var initializeId = session.TakeRequestId();
                await session.SendAsync(
                    new JsonObject
                    {
                        ["method"] = "initialize",
                        ["id"] = initializeId,
                        ["params"] = new JsonObject
                        {
                            ["clientInfo"] = new JsonObject
                            {
                                ["name"] = "codex_account_switcher",
                                ["title"] = "Codex Account Switcher",
                                ["version"] = "1.0"
                            }
                        }
                    },
                    cancellationToken);
                _ = await session.ReadResponseAsync(
                    initializeId,
                    cancellationToken);
                await session.SendAsync(
                    new JsonObject
                    {
                        ["method"] = "initialized",
                        ["params"] = new JsonObject()
                    },
                    cancellationToken);
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        public bool MatchesCredential(ReadOnlySpan<byte> credential)
        {
            var hash = SHA256.HashData(credential);
            try
            {
                return CryptographicOperations.FixedTimeEquals(
                    _credentialHash,
                    hash);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }

        public async Task<ProfileRateLimitReadResult> ReadRateLimitsAsync(
            ReadOnlyMemory<byte> originalCredential,
            CancellationToken cancellationToken)
        {
            var before = await ReadAccountAsync(cancellationToken);
            if (before is null)
            {
                return new ProfileRateLimitReadResult(
                    ProfileRateLimitStatus.AuthenticationExpired,
                    []);
            }

            if (!StringComparer.Ordinal.Equals(
                    before.Type,
                    "chatgpt"))
            {
                return new ProfileRateLimitReadResult(
                    ProfileRateLimitStatus.UnsupportedAuthentication,
                    []);
            }

            var requestId = TakeRequestId();
            await SendAsync(
                new JsonObject
                {
                    ["method"] = "account/rateLimits/read",
                    ["id"] = requestId
                },
                cancellationToken);
            var response = await ReadResponseAsync(
                requestId,
                cancellationToken);
            var result = response["result"]?.AsObject()
                ?? throw new JsonException(
                    "사용량 응답 형식이 올바르지 않습니다.");
            var snapshot = SelectCodexSnapshot(result);
            var windows = ReadWindows(snapshot);

            var after = await ReadAccountAsync(cancellationToken);
            byte[]? refreshedCredential = null;
            if (after is not null && before == after)
            {
                var authPath = Path.Combine(
                    _sessionRoot,
                    "auth.json");
                if (File.Exists(authPath))
                {
                    var current = await File.ReadAllBytesAsync(
                        authPath,
                        cancellationToken);
                    if (!CredentialsEqual(
                            originalCredential.Span,
                            current))
                    {
                        refreshedCredential = current;
                        ReplaceCredentialHash(current);
                    }
                    else
                    {
                        CryptographicOperations.ZeroMemory(current);
                    }
                }
            }

            return new ProfileRateLimitReadResult(
                ProfileRateLimitStatus.Available,
                windows,
                refreshedCredential);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
            }
            catch (Exception exception)
                when (exception is InvalidOperationException or
                      System.ComponentModel.Win32Exception)
            {
                // 이미 종료됐거나 종료와 정리가 겹친 경우다.
            }
            finally
            {
                _input.Dispose();
                _output.Dispose();
                _process.Dispose();
                CryptographicOperations.ZeroMemory(_credentialHash);
                try
                {
                    _deleteSessionDirectory(_sessionRoot);
                }
                catch (Exception exception)
                    when (UsageProbeDirectoryCleaner
                        .IsRecoverableCleanupException(exception))
                {
                }
            }
        }

        private async Task<AccountIdentity?> ReadAccountAsync(
            CancellationToken cancellationToken)
        {
            var requestId = TakeRequestId();
            await SendAsync(
                new JsonObject
                {
                    ["method"] = "account/read",
                    ["id"] = requestId,
                    ["params"] = new JsonObject
                    {
                        ["refreshToken"] = false
                    }
                },
                cancellationToken);
            var response = await ReadResponseAsync(
                requestId,
                cancellationToken);
            var account = response["result"]?["account"];
            if (account is null ||
                account.GetValueKind() == JsonValueKind.Null)
            {
                return null;
            }

            return new AccountIdentity(
                account["type"]?.GetValue<string>() ?? "",
                account["email"]?.GetValue<string>(),
                account["planType"]?.GetValue<string>());
        }

        private static JsonObject SelectCodexSnapshot(JsonObject result)
        {
            var byLimitId = result["rateLimitsByLimitId"] as JsonObject;
            if (byLimitId?["codex"] is JsonObject codex)
            {
                return codex;
            }

            return result["rateLimits"]?.AsObject()
                   ?? throw new JsonException(
                       "Codex 사용량 항목을 찾을 수 없습니다.");
        }

        private static IReadOnlyList<RateLimitWindow> ReadWindows(
            JsonObject snapshot)
        {
            var windows = new List<RateLimitWindow>(2);
            AddWindow(snapshot["primary"], windows);
            AddWindow(snapshot["secondary"], windows);
            return windows;
        }

        private static void AddWindow(
            JsonNode? node,
            ICollection<RateLimitWindow> destination)
        {
            if (node is not JsonObject window ||
                window["usedPercent"] is null)
            {
                return;
            }

            var resetsAt = window["resetsAt"]?.GetValue<long?>();
            destination.Add(
                new RateLimitWindow(
                    window["usedPercent"]!.GetValue<int>(),
                    window["windowDurationMins"]?.GetValue<long?>(),
                    resetsAt is null
                        ? null
                        : DateTimeOffset.FromUnixTimeSeconds(
                            resetsAt.Value)));
        }

        private async Task SendAsync(
            JsonObject message,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var json = message.ToJsonString();
            await _input.WriteLineAsync(
                json.AsMemory(),
                cancellationToken);
            await _input.FlushAsync(cancellationToken);
        }

        private async Task<JsonObject> ReadResponseAsync(
            long requestId,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var line = await _output.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    throw new IOException(
                        "Codex 사용량 조회 프로세스가 종료됐습니다.");
                }

                var message = JsonNode.Parse(line)?.AsObject()
                    ?? throw new JsonException(
                        "Codex 응답을 읽을 수 없습니다.");
                if (message["id"]?.GetValue<long?>() != requestId)
                {
                    continue;
                }

                if (message["error"] is JsonObject error)
                {
                    throw new RpcException(
                        error["code"]?.GetValue<int>() ?? -1);
                }

                return message;
            }
        }

        private long TakeRequestId() =>
            Interlocked.Increment(ref _nextRequestId);

        private void ReplaceCredentialHash(ReadOnlySpan<byte> credential)
        {
            var newHash = SHA256.HashData(credential);
            CryptographicOperations.ZeroMemory(_credentialHash);
            _credentialHash = newHash;
        }

        private static bool CredentialsEqual(
            ReadOnlySpan<byte> expected,
            ReadOnlySpan<byte> actual)
        {
            return expected.Length == actual.Length &&
                   CryptographicOperations.FixedTimeEquals(
                       expected,
                       actual);
        }

        private sealed record AccountIdentity(
            string Type,
            string? Email,
            string? PlanType);
    }

    private sealed class RpcException : Exception
    {
        public RpcException(int code)
        {
            Code = code;
        }

        public int Code { get; }
    }
}
