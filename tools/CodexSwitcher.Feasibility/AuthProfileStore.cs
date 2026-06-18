using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexSwitcher.Feasibility;

internal sealed record FeasibilityState(
    string? ActiveSlot,
    DateTimeOffset? OriginalCapturedAt,
    DateTimeOffset? LastMutationAt);

internal sealed partial class AuthProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly FeasibilityPaths _paths;
    private readonly CurrentUserDataProtector _protector;

    public AuthProfileStore(FeasibilityPaths paths, CurrentUserDataProtector protector)
    {
        _paths = paths;
        _protector = protector;
    }

    public void Capture(string slot)
    {
        ValidateSlot(slot);
        var authBytes = ReadAuthFile();

        try
        {
            WriteProtected(SlotPath(slot), authBytes);
            SaveState(LoadState() with
            {
                ActiveSlot = slot,
                LastMutationAt = DateTimeOffset.UtcNow
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authBytes);
        }
    }

    public bool PrepareLogin()
    {
        var authBytes = ReadAuthFile();

        try
        {
            BackupCurrentForRecovery();
            var capturedOriginal = false;
            if (!File.Exists(_paths.OriginalCredentialFile))
            {
                WriteProtected(_paths.OriginalCredentialFile, authBytes);
                capturedOriginal = true;
            }

            File.Delete(_paths.AuthFile);

            if (File.Exists(_paths.AuthFile))
            {
                throw new IOException("로그인 준비 중 auth.json을 제거하지 못했습니다.");
            }

            var state = LoadState();
            SaveState(state with
            {
                ActiveSlot = null,
                OriginalCapturedAt = capturedOriginal
                    ? DateTimeOffset.UtcNow
                    : state.OriginalCapturedAt,
                LastMutationAt = DateTimeOffset.UtcNow
            });

            return capturedOriginal;
        }
        catch
        {
            RestoreRecovery();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authBytes);
        }
    }

    public void Activate(string slot)
    {
        ValidateSlot(slot);
        var slotPath = SlotPath(slot);

        if (!File.Exists(slotPath))
        {
            throw new FileNotFoundException("해당 슬롯에 저장된 인증 상태가 없습니다.", slot);
        }

        BackupCurrentForRecovery();

        try
        {
            RestoreFromProtectedFile(slotPath);
            SaveState(LoadState() with
            {
                ActiveSlot = slot,
                LastMutationAt = DateTimeOffset.UtcNow
            });
        }
        catch
        {
            RestoreRecovery();
            throw;
        }
    }

    public void RestoreOriginal()
    {
        if (!File.Exists(_paths.OriginalCredentialFile))
        {
            throw new FileNotFoundException("최초 인증 상태의 보관본이 없습니다.");
        }

        BackupCurrentForRecovery();

        try
        {
            RestoreFromProtectedFile(_paths.OriginalCredentialFile);
            SaveState(LoadState() with
            {
                ActiveSlot = "original",
                LastMutationAt = DateTimeOffset.UtcNow
            });
        }
        catch
        {
            RestoreRecovery();
            throw;
        }
    }

    public void RestoreRecovery()
    {
        if (File.Exists(_paths.RecoveryCredentialFile))
        {
            RestoreFromProtectedFile(_paths.RecoveryCredentialFile);
        }
        else if (File.Exists(_paths.AuthFile))
        {
            File.Delete(_paths.AuthFile);
        }

        if (File.Exists(_paths.RecoveryStateFile))
        {
            var previousState = File.ReadAllText(_paths.RecoveryStateFile);
            AtomicFile.WriteAllText(_paths.StateFile, previousState);
        }
    }

    public string? FindExactCurrentSlot()
    {
        if (!File.Exists(_paths.AuthFile) || !Directory.Exists(_paths.SlotsDirectory))
        {
            return null;
        }

        var current = File.ReadAllBytes(_paths.AuthFile);

        try
        {
            foreach (var slotFile in Directory.EnumerateFiles(
                         _paths.SlotsDirectory,
                         "*.credential",
                         SearchOption.TopDirectoryOnly))
            {
                var protectedBytes = File.ReadAllBytes(slotFile);
                var candidate = _protector.Unprotect(protectedBytes);

                try
                {
                    if (current.Length == candidate.Length &&
                        CryptographicOperations.FixedTimeEquals(current, candidate))
                    {
                        return Path.GetFileNameWithoutExtension(slotFile);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(candidate);
                    CryptographicOperations.ZeroMemory(protectedBytes);
                }
            }

            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(current);
        }
    }

    public IReadOnlyList<string> GetSlots()
    {
        if (!Directory.Exists(_paths.SlotsDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(_paths.SlotsDirectory, "*.credential", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void VerifyRollbackOnInvalidCredential()
    {
        var current = ReadAuthFile();
        const string testSlot = "__rollback_test__";
        var testSlotPath = SlotPath(testSlot);

        try
        {
            Directory.CreateDirectory(_paths.SlotsDirectory);
            AtomicFile.WriteAllBytes(testSlotPath, "invalid credential payload"u8);

            try
            {
                Activate(testSlot);
                throw new InvalidOperationException("손상된 인증 보관본이 예상과 달리 적용됐습니다.");
            }
            catch (InvalidDataException)
            {
                // 예상된 실패다. Activate가 직전 인증 상태를 복구해야 한다.
            }

            var restored = ReadAuthFile();
            try
            {
                if (restored.Length != current.Length ||
                    !CryptographicOperations.FixedTimeEquals(restored, current))
                {
                    throw new IOException("실패 주입 뒤 직전 인증 상태가 복구되지 않았습니다.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(restored);
            }
        }
        finally
        {
            if (File.Exists(testSlotPath))
            {
                File.Delete(testSlotPath);
            }

            CryptographicOperations.ZeroMemory(current);
        }
    }

    private void BackupCurrentForRecovery()
    {
        if (File.Exists(_paths.AuthFile))
        {
            var current = File.ReadAllBytes(_paths.AuthFile);
            try
            {
                WriteProtected(_paths.RecoveryCredentialFile, current);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(current);
            }
        }
        else if (File.Exists(_paths.RecoveryCredentialFile))
        {
            File.Delete(_paths.RecoveryCredentialFile);
        }

        AtomicFile.WriteAllText(
            _paths.RecoveryStateFile,
            JsonSerializer.Serialize(LoadState(), JsonOptions));
    }

    private void RestoreFromProtectedFile(string protectedFile)
    {
        var protectedBytes = File.ReadAllBytes(protectedFile);
        var plaintext = _protector.Unprotect(protectedBytes);

        try
        {
            AtomicFile.WriteAllBytes(_paths.AuthFile, plaintext);
            var written = File.ReadAllBytes(_paths.AuthFile);

            try
            {
                if (written.Length != plaintext.Length ||
                    !CryptographicOperations.FixedTimeEquals(written, plaintext))
                {
                    throw new IOException("인증 파일 교체 후 검증에 실패했습니다.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(written);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private void WriteProtected(string destinationPath, ReadOnlySpan<byte> plaintext)
    {
        var protectedBytes = _protector.Protect(plaintext);

        try
        {
            AtomicFile.WriteAllBytes(destinationPath, protectedBytes);
            var stored = File.ReadAllBytes(destinationPath);
            var verified = _protector.Unprotect(stored);

            try
            {
                if (verified.Length != plaintext.Length ||
                    !CryptographicOperations.FixedTimeEquals(verified, plaintext))
                {
                    throw new IOException("암호화된 인증 보관본 검증에 실패했습니다.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(stored);
                CryptographicOperations.ZeroMemory(verified);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private byte[] ReadAuthFile()
    {
        if (!File.Exists(_paths.AuthFile))
        {
            throw new FileNotFoundException("auth.json이 없습니다. 먼저 Codex 로그인을 완료하세요.");
        }

        return File.ReadAllBytes(_paths.AuthFile);
    }

    private string SlotPath(string slot)
    {
        return Path.Combine(_paths.SlotsDirectory, $"{slot}.credential");
    }

    private FeasibilityState LoadState()
    {
        if (!File.Exists(_paths.StateFile))
        {
            return new FeasibilityState(null, null, null);
        }

        return JsonSerializer.Deserialize<FeasibilityState>(
                   File.ReadAllText(_paths.StateFile),
                   JsonOptions)
               ?? new FeasibilityState(null, null, null);
    }

    private void SaveState(FeasibilityState state)
    {
        AtomicFile.WriteAllText(
            _paths.StateFile,
            JsonSerializer.Serialize(state, JsonOptions));
    }

    internal static void ValidateSlot(string slot)
    {
        if (!SlotPattern().IsMatch(slot))
        {
            throw new ArgumentException(
                "슬롯 이름은 영문, 숫자, 하이픈, 밑줄만 사용해 1~40자로 입력하세요.",
                nameof(slot));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlotPattern();
}
