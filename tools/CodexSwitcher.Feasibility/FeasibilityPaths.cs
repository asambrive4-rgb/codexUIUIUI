namespace CodexSwitcher.Feasibility;

internal sealed class FeasibilityPaths
{
    private const string StorageOverrideVariable = "CODEX_SWITCHER_FEASIBILITY_HOME";

    public FeasibilityPaths(string codexHome, string storageRoot)
    {
        CodexHome = Path.GetFullPath(codexHome);
        StorageRoot = Path.GetFullPath(storageRoot);
    }

    public string CodexHome { get; }

    public string StorageRoot { get; }

    public string AuthFile => Path.Combine(CodexHome, "auth.json");

    public string ConfigFile => Path.Combine(CodexHome, "config.toml");

    public string SlotsDirectory => Path.Combine(StorageRoot, "slots");

    public string OriginalCredentialFile => Path.Combine(StorageRoot, "original.credential");

    public string RecoveryCredentialFile => Path.Combine(StorageRoot, "recovery.credential");

    public string RecoveryStateFile => Path.Combine(StorageRoot, "recovery.state.json");

    public string StateFile => Path.Combine(StorageRoot, "state.json");

    public string SnapshotFile => Path.Combine(StorageRoot, "codex-home.snapshot.json");

    public static FeasibilityPaths CreateDefault()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var storageOverride = Environment.GetEnvironmentVariable(StorageOverrideVariable);

        return new FeasibilityPaths(
            string.IsNullOrWhiteSpace(codexHome)
                ? Path.Combine(userProfile, ".codex")
                : codexHome,
            string.IsNullOrWhiteSpace(storageOverride)
                ? Path.Combine(localAppData, "CodexAccountSwitcher", "Feasibility")
                : storageOverride);
    }
}
