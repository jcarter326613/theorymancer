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
    public void Resolve_IncludesEverySkillFromEquippedScepterTorchAndGreatswordSets()
    {
        var build = new ArenaNetBuild(
            "Scepter torch build",
            "Necromancer",
            [new ArenaNetSpecialization(34, [null, null, null])],
            new ArenaNetEquippedSkills(null, [], null));
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
            new ArenaNetItem(1, new ArenaNetItemDetails("Scepter")),
            new ArenaNetItem(2, new ArenaNetItemDetails("Torch")),
            new ArenaNetItem(3, new ArenaNetItemDetails("Greatsword")),
        };
        var profession = new ArenaNetProfession(
            "Necromancer",
            new Dictionary<string, ArenaNetWeapon>
            {
                ["Scepter"] = new(["Mainhand"], null, [
                    new ArenaNetWeaponSkill(10698, "Weapon_1", null),
                    new ArenaNetWeaponSkill(10532, "Weapon_2", null),
                    new ArenaNetWeaponSkill(10709, "Weapon_3", null),
                ]),
                ["Torch"] = new(["Offhand"], 60, [
                    new ArenaNetWeaponSkill(45846, "Weapon_4", null),
                    new ArenaNetWeaponSkill(44296, "Weapon_5", null),
                ]),
                ["Greatsword"] = new(["TwoHand"], 34, [
                    new ArenaNetWeaponSkill(29705, "Weapon_1", null),
                    new ArenaNetWeaponSkill(30163, "Weapon_2", null),
                    new ArenaNetWeaponSkill(30860, "Weapon_3", null),
                    new ArenaNetWeaponSkill(29855, "Weapon_4", null),
                    new ArenaNetWeaponSkill(29740, "Weapon_5", null),
                ]),
            });

        var candidates = BuildSkillCandidateResolver.Resolve("Tester", build, equipment, items, profession);

        Assert.Equal([10698, 29705], candidates.GetSkillIds(SkillBarComponentKind.WeaponSkill1));
        Assert.Equal([10532, 30163], candidates.GetSkillIds(SkillBarComponentKind.WeaponSkill2));
        Assert.Equal([10709, 30860], candidates.GetSkillIds(SkillBarComponentKind.WeaponSkill3));
        Assert.Equal([29855, 45846], candidates.GetSkillIds(SkillBarComponentKind.WeaponSkill4));
        Assert.Equal([29740, 44296], candidates.GetSkillIds(SkillBarComponentKind.WeaponSkill5));
        Assert.Equal(1, candidates.GetWeaponSet(SkillBarComponentKind.WeaponSkill4, 45846));
        Assert.Equal(1, candidates.GetWeaponSet(SkillBarComponentKind.WeaponSkill5, 44296));
        Assert.Equal(2, candidates.GetWeaponSet(SkillBarComponentKind.WeaponSkill4, 29855));
    }

    [Fact]
    public void Resolve_IncludesAWeaponThatTheEquipmentTabReportsAsEquipped()
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

        Assert.Equal([401], candidates.GetSkillIds(SkillBarComponentKind.WeaponSkill1));
    }
}
