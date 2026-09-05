using Theorymancer.GuildWars2.Desktop.ArenaNet;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class BuildSkillCandidateResolverTests
{
    [Fact]
    public void Resolve_UsesBuildSkillsAndBothEquippedWeaponSets()
    {
        var build = new ArenaNetBuild(
            "Test build",
            "Guardian",
            [new ArenaNetSpecialization(62, [null, null, null])],
            new ArenaNetEquippedSkills(100, [101, 102, 103], 104));
        var equipment = new ArenaNetEquipmentTab(
            1,
            true,
            [
                new ArenaNetEquipment(1, "WeaponA1"),
                new ArenaNetEquipment(2, "WeaponA2"),
                new ArenaNetEquipment(3, "WeaponB1"),
            ]);
        var items = new[]
        {
            new ArenaNetItem(1, new ArenaNetItemDetails("Axe")),
            new ArenaNetItem(2, new ArenaNetItemDetails("Shield")),
            new ArenaNetItem(3, new ArenaNetItemDetails("Greatsword")),
        };
        var profession = new ArenaNetProfession(
            "Guardian",
            new Dictionary<string, ArenaNetWeapon>
            {
                ["Axe"] = new(["Mainhand"], null, [
                    new ArenaNetWeaponSkill(201, "Weapon_1", null),
                    new ArenaNetWeaponSkill(202, "Weapon_2", null),
                    new ArenaNetWeaponSkill(203, "Weapon_3", null),
                ]),
                ["Shield"] = new(["Offhand"], null, [
                    new ArenaNetWeaponSkill(204, "Weapon_4", null),
                    new ArenaNetWeaponSkill(205, "Weapon_5", null),
                ]),
                ["Greatsword"] = new(["TwoHand"], null, [
                    new ArenaNetWeaponSkill(301, "Weapon_1", null),
                    new ArenaNetWeaponSkill(302, "Weapon_2", null),
                    new ArenaNetWeaponSkill(303, "Weapon_3", null),
                    new ArenaNetWeaponSkill(304, "Weapon_4", null),
                    new ArenaNetWeaponSkill(305, "Weapon_5", null),
                ]),
            });

        var candidates = BuildSkillCandidateResolver.Resolve("Tester", build, equipment, items, profession);

        Assert.Equal([201, 301], candidates.GetSkillIds(SkillBarComponentKind.WeaponSkill1));
        Assert.Equal([204, 304], candidates.GetSkillIds(SkillBarComponentKind.WeaponSkill4));
        Assert.Equal(1, candidates.GetWeaponSet(SkillBarComponentKind.WeaponSkill1, 201));
        Assert.Equal(2, candidates.GetWeaponSet(SkillBarComponentKind.WeaponSkill1, 301));
        Assert.Equal([100], candidates.GetSkillIds(SkillBarComponentKind.HealSkill));
        Assert.Equal([101], candidates.GetSkillIds(SkillBarComponentKind.UtilitySkill1));
        Assert.Equal([104], candidates.GetSkillIds(SkillBarComponentKind.EliteSkill));
    }

    [Fact]
    public void Resolve_ExcludesWeaponsLockedByAnUnselectedSpecialization()
    {
        var build = new ArenaNetBuild(
            "Test build",
            "Guardian",
            [new ArenaNetSpecialization(16, [null, null, null])],
            new ArenaNetEquippedSkills(null, [], null));
        var equipment = new ArenaNetEquipmentTab(1, true, [new ArenaNetEquipment(1, "WeaponA1")]);
        var profession = new ArenaNetProfession(
            "Guardian",
            new Dictionary<string, ArenaNetWeapon>
            {
                ["Rifle"] = new(["TwoHand"], 62, [new ArenaNetWeaponSkill(401, "Weapon_1", null)]),
            });

        var candidates = BuildSkillCandidateResolver.Resolve(
            "Tester",
            build,
            equipment,
            [new ArenaNetItem(1, new ArenaNetItemDetails("Rifle"))],
            profession);

        Assert.Empty(candidates.GetSkillIds(SkillBarComponentKind.WeaponSkill1));
    }
}
