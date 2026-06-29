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

    public PopupPlacementStore()
        : this(CreateDefaultPath())
    {
    }

    public PopupPlacementStore(string filePath)
    {
        _filePath = filePath;
    }

    public PopupPlacement? Read()
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

    public void Save(double left, double top)
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
                new PopupPlacement(left, top),
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
