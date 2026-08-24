using System.Text.Json;
using System.IO;

namespace Theorymancer.GuildWars2.Desktop.Calibration;

public sealed record CollectorSettings(NormalizedCrop? Crop, int RowHeightPixels)
{
    public static CollectorSettings Default { get; } = new(Crop: null, RowHeightPixels: 20);
}

public sealed class CollectorSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Theorymancer",
        "guild-wars-2-screen-collector.json");

    public CollectorSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<CollectorSettings>(File.ReadAllText(_path), JsonOptions) ??
                CollectorSettings.Default;
        }
        catch (IOException)
        {
            return CollectorSettings.Default;
        }
        catch (JsonException)
        {
            return CollectorSettings.Default;
        }
    }

    public void Save(CollectorSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
