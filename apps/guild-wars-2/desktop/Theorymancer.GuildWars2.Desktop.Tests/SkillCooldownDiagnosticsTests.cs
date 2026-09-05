using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class SkillCooldownDiagnosticsTests
{
    [Fact]
    public void CreateSnapshot_ListsWeaponSetsThenNonWeaponSkillsWithSectionBreaks()
    {
        var candidates = new[]
        {
            new SkillCooldownCandidate(SkillBarComponentKind.UtilitySkill1, 50, "Utility", null),
            new SkillCooldownCandidate(SkillBarComponentKind.WeaponSkill2, 20, "Set one two", null, WeaponSet: 1),
            new SkillCooldownCandidate(SkillBarComponentKind.WeaponSkill1, 10, "Set one one", null, WeaponSet: 1),
            new SkillCooldownCandidate(SkillBarComponentKind.WeaponSkill2, 30, "Set two two", null, WeaponSet: 2),
            new SkillCooldownCandidate(SkillBarComponentKind.WeaponSkill1, 11, "Set two one", null, WeaponSet: 2),
            new SkillCooldownCandidate(SkillBarComponentKind.HealSkill, 40, "Heal", null),
        };
        var snapshot = SkillCooldownDiagnostics.CreateSnapshot(
            candidates,
            new Dictionary<SkillBarComponentKind, int>
            {
                [SkillBarComponentKind.WeaponSkill2] = 30,
                [SkillBarComponentKind.HealSkill] = 40,
            },
            new Dictionary<(SkillBarComponentKind, int), SkillCooldownDisplay>
            {
                [(SkillBarComponentKind.WeaponSkill2, 30)] = new(
                    SkillCooldownDisplayState.Cooling,
                    TimeSpan.FromSeconds(2.4)),
                [(SkillBarComponentKind.HealSkill, 40)] = new(
                    SkillCooldownDisplayState.Ready,
                    null),
            });

        Assert.Collection(
            snapshot.Rows,
            row => AssertRow(row, SkillBarComponentKind.WeaponSkill1, 10, isActive: false, SkillCooldownDisplayState.NotOnActiveBar, "-", startsSection: false),
            row => AssertRow(row, SkillBarComponentKind.WeaponSkill2, 20, isActive: false, SkillCooldownDisplayState.NotOnActiveBar, "-", startsSection: false),
            row => AssertRow(row, SkillBarComponentKind.WeaponSkill1, 11, isActive: false, SkillCooldownDisplayState.NotOnActiveBar, "-", startsSection: true),
            row => AssertRow(row, SkillBarComponentKind.WeaponSkill2, 30, isActive: true, SkillCooldownDisplayState.Cooling, "2.4s", startsSection: false),
            row => AssertRow(row, SkillBarComponentKind.HealSkill, 40, isActive: true, SkillCooldownDisplayState.Ready, "-", startsSection: true),
            row => AssertRow(row, SkillBarComponentKind.UtilitySkill1, 50, isActive: false, SkillCooldownDisplayState.NotOnActiveBar, "-", startsSection: false));
    }

    private static void AssertRow(
        SkillCooldownDiagnosticsRow row,
        SkillBarComponentKind kind,
        int skillId,
        bool isActive,
        SkillCooldownDisplayState state,
        string remaining,
        bool startsSection)
    {
        Assert.Equal(kind, row.Kind);
        Assert.Equal(skillId, row.SkillId);
        Assert.Equal(isActive, row.IsActive);
        Assert.Equal(state, row.State);
        Assert.Equal(remaining, row.RemainingText);
        Assert.Equal(startsSection, row.StartsSection);
    }
}
