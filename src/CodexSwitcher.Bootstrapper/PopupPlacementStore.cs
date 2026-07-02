using System.IO;
using System.Text.Json;

namespace CodexSwitcher.Bootstrapper;

public sealed class PopupPlacementStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly object _cacheGate = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private PopupPlacement? _cachedPlacement;
    private bool _cacheLoaded;

    public PopupPlacementStore()
        : this(CreateDefaultPath())
    {
    }

    public PopupPlacementStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var placement = await Task.Run(
                ReadFromDisk,
                cancellationToken)
            .ConfigureAwait(false);

        lock (_cacheGate)
        {
            _cachedPlacement = placement;
            _cacheLoaded = true;
        }
    }

    public PopupPlacement? ReadCached()
    {
        lock (_cacheGate)
        {
            return _cacheLoaded
                ? _cachedPlacement
                : null;
        }
    }

    public async Task SaveAsync(
        double left,
        double top,
        CancellationToken cancellationToken)
    {
        var placement = new PopupPlacement(left, top);
        lock (_cacheGate)
        {
            _cachedPlacement = placement;
            _cacheLoaded = true;
        }

        await _saveGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await Task.Run(
                    () => WriteToDisk(placement),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private PopupPlacement? ReadFromDisk()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<PopupPlacement>(
                stream,
                JsonOptions);
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  JsonException)
        {
            return null;
        }
    }

    private void WriteToDisk(PopupPlacement placement)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(_filePath);
            JsonSerializer.Serialize(
                stream,
                placement,
                JsonOptions);
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException)
        {
            // 위치 저장 실패가 앱 사용을 막으면 안 된다.
        }
    }

    private static string CreateDefaultPath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localAppData,
            "CodexSwitcher",
            "ui-settings.json");
    }
}

public sealed record PopupPlacement(
    double Left,
    double Top);
