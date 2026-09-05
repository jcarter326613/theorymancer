using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.ArenaNet;

public sealed record BuildSkillCandidates(
    string CharacterName,
    string BuildName,
    string Profession,
    IReadOnlyDictionary<SkillBarComponentKind, IReadOnlyList<int>> SkillIdsBySlot,
    IReadOnlyDictionary<(SkillBarComponentKind Kind, int SkillId), int>? WeaponSetBySlot = null)
{
    public IReadOnlyList<int> GetSkillIds(SkillBarComponentKind kind) =>
        SkillIdsBySlot.TryGetValue(kind, out var skillIds) ? skillIds : [];

    public int? GetWeaponSet(SkillBarComponentKind kind, int skillId) =>
        WeaponSetBySlot?.GetValueOrDefault((kind, skillId));
}

public sealed class ArenaNetBuildLoader
{
    private static readonly string[] WeaponSetSlots = ["WeaponA1", "WeaponA2", "WeaponB1", "WeaponB2"];
    private readonly IArenaNetApiClient _client;

    public ArenaNetBuildLoader(IArenaNetApiClient client)
    {
        _client = client;
    }

    public async Task<BuildSkillCandidates> LoadAsync(string apiKey, string characterName, CancellationToken cancellationToken)
    {
        var buildTask = _client.GetActiveBuildAsync(apiKey, characterName, cancellationToken);
        var equipmentTask = _client.GetActiveEquipmentAsync(apiKey, characterName, cancellationToken);
        var buildTab = await buildTask;
        var equipmentTab = await equipmentTask;
        var weaponItemIds = equipmentTab.Equipment
            .Where(equipment => WeaponSetSlots.Contains(equipment.Slot, StringComparer.Ordinal))
            .Select(equipment => equipment.Id)
            .Distinct()
            .ToList();
        var itemsTask = _client.GetItemsAsync(weaponItemIds, cancellationToken);
        var professionTask = _client.GetProfessionAsync(buildTab.Build.Profession, cancellationToken);
        await Task.WhenAll(itemsTask, professionTask);

        return BuildSkillCandidateResolver.Resolve(
            characterName,
            buildTab.Build,
            equipmentTab,
            await itemsTask,
            await professionTask);
    }
}

public static class BuildSkillCandidateResolver
{
    public static BuildSkillCandidates Resolve(
        string characterName,
        ArenaNetBuild build,
        ArenaNetEquipmentTab equipmentTab,
        IReadOnlyList<ArenaNetItem> items,
        ArenaNetProfession profession)
    {
        var candidates = new Dictionary<SkillBarComponentKind, HashSet<int>>();
        var weaponSets = new Dictionary<(SkillBarComponentKind Kind, int SkillId), int>();
        Add(candidates, SkillBarComponentKind.HealSkill, build.Skills.Heal);
        Add(candidates, SkillBarComponentKind.UtilitySkill1, build.Skills.Utilities.ElementAtOrDefault(0));
        Add(candidates, SkillBarComponentKind.UtilitySkill2, build.Skills.Utilities.ElementAtOrDefault(1));
        Add(candidates, SkillBarComponentKind.UtilitySkill3, build.Skills.Utilities.ElementAtOrDefault(2));
        Add(candidates, SkillBarComponentKind.EliteSkill, build.Skills.Elite);

        var itemTypes = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Details?.Type))
            .ToDictionary(item => item.Id, item => item.Details!.Type!, EqualityComparer<int>.Default);
        var selectedSpecializations = build.Specializations
            .Where(specialization => specialization?.Id is not null)
            .Select(specialization => specialization!.Id!.Value)
            .ToHashSet();
        AddWeaponSetCandidates(candidates, weaponSets, equipmentTab.Equipment, itemTypes, "WeaponA", 1, selectedSpecializations, profession);
        AddWeaponSetCandidates(candidates, weaponSets, equipmentTab.Equipment, itemTypes, "WeaponB", 2, selectedSpecializations, profession);

        return new BuildSkillCandidates(
            characterName,
            build.Name,
            build.Profession,
            candidates.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<int>)pair.Value.Order().ToList()),
            weaponSets);
    }

    private static void AddWeaponSetCandidates(
        IDictionary<SkillBarComponentKind, HashSet<int>> candidates,
        IDictionary<(SkillBarComponentKind Kind, int SkillId), int> weaponSets,
        IReadOnlyList<ArenaNetEquipment> equipment,
        IReadOnlyDictionary<int, string> itemTypes,
        string set,
        int setNumber,
        IReadOnlySet<int> selectedSpecializations,
        ArenaNetProfession profession)
    {
        var mainhand = GetWeaponType(equipment, itemTypes, $"{set}1");
        if (mainhand is null)
        {
            return;
        }

        var offhand = GetWeaponType(equipment, itemTypes, $"{set}2");
        var equippedWeaponTypes = new[] { mainhand, offhand }
            .Where(type => type is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (weaponType, weapon) in profession.Weapons)
        {
            if (!equippedWeaponTypes.Contains(weaponType) ||
                weapon.Specialization is { } specialization && !selectedSpecializations.Contains(specialization))
            {
                continue;
            }

            foreach (var skill in weapon.Skills)
            {
                if (!TryGetWeaponSlot(skill.Slot, out var slot) ||
                    !string.IsNullOrWhiteSpace(skill.Offhand) && !string.Equals(skill.Offhand, offhand, StringComparison.Ordinal))
                {
                    continue;
                }

                Add(candidates, slot, skill.Id);
                weaponSets[(slot, skill.Id)] = setNumber;
            }
        }
    }

    private static string? GetWeaponType(
        IReadOnlyList<ArenaNetEquipment> equipment,
        IReadOnlyDictionary<int, string> itemTypes,
        string slot) => equipment
        .Where(entry => string.Equals(entry.Slot, slot, StringComparison.Ordinal))
        .Select(entry => itemTypes.GetValueOrDefault(entry.Id))
        .FirstOrDefault(type => type is not null);

    private static bool TryGetWeaponSlot(string slot, out SkillBarComponentKind kind)
    {
        kind = slot switch
        {
            "Weapon_1" => SkillBarComponentKind.WeaponSkill1,
            "Weapon_2" => SkillBarComponentKind.WeaponSkill2,
            "Weapon_3" => SkillBarComponentKind.WeaponSkill3,
            "Weapon_4" => SkillBarComponentKind.WeaponSkill4,
            "Weapon_5" => SkillBarComponentKind.WeaponSkill5,
            _ => default,
        };
        return slot is "Weapon_1" or "Weapon_2" or "Weapon_3" or "Weapon_4" or "Weapon_5";
    }

    private static void Add(IDictionary<SkillBarComponentKind, HashSet<int>> candidates, SkillBarComponentKind kind, int? skillId)
    {
        if (skillId is not > 0)
        {
            return;
        }

        if (!candidates.TryGetValue(kind, out var values))
        {
            values = [];
            candidates.Add(kind, values);
        }

        values.Add(skillId.Value);
    }
}
