using System.Text.Json;
using System.IO;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Calibration;

public sealed record CollectorSettings(
    IReadOnlyList<CalibratedRegion> Regions,
    SkillBarLayout? SkillBarLayout = null)
{
    public static CollectorSettings Default { get; } = new(Array.Empty<CalibratedRegion>());

    public NormalizedCrop? CombatLogCrop => Regions
        .FirstOrDefault(region => region.Id == CalibratedRegion.CombatLogId)
        ?.Crop;

    public NormalizedCrop? SkillBarCrop => Regions
        .FirstOrDefault(region => region.Id == CalibratedRegion.SkillBarId)
        ?.Crop;
}

public sealed class CollectorSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public CollectorSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Theorymancer",
            "guild-wars-2-screen-collector.json");
    }

    public CollectorSettings Load()
    {
        try
        {
            var json = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("Regions", out _))
            {
                var settings = JsonSerializer.Deserialize<CollectorSettings>(json, JsonOptions);
                return settings?.Regions is not null ? settings : CollectorSettings.Default;
            }

            var legacySettings = JsonSerializer.Deserialize<LegacyCollectorSettings>(json, JsonOptions);
            return legacySettings?.Crop is { } crop
                ? new CollectorSettings(
                    [new CalibratedRegion(CalibratedRegion.CombatLogId, "Combat log", crop)])
                : legacySettings is not null
                    ? new CollectorSettings(Array.Empty<CalibratedRegion>())
                    : CollectorSettings.Default;
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

    private sealed record LegacyCollectorSettings(NormalizedCrop? Crop);
}
