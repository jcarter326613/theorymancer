using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public enum SkillBarComponentKind
{
    WeaponSkill1,
    WeaponSkill2,
    WeaponSkill3,
    WeaponSkill4,
    WeaponSkill5,
    HealSkill,
    UtilitySkill1,
    UtilitySkill2,
    UtilitySkill3,
    EliteSkill,
}

public sealed record SkillBarComponent(
    SkillBarComponentKind Kind,
    double X,
    double Y,
    double Width,
    double Height,
    double Confidence)
{
    public ScreenBounds ToPixelBounds(int skillBarWidth, int skillBarHeight) => new(
        (int)Math.Round(X * skillBarWidth),
        (int)Math.Round(Y * skillBarHeight),
        Math.Max(1, (int)Math.Round(Width * skillBarWidth)),
        Math.Max(1, (int)Math.Round(Height * skillBarHeight)));

    public static SkillBarComponent FromPixelBounds(
        SkillBarComponentKind kind,
        ScreenBounds bounds,
        int skillBarWidth,
        int skillBarHeight,
        double confidence) => new(
        kind,
        (double)bounds.X / skillBarWidth,
        (double)bounds.Y / skillBarHeight,
        (double)bounds.Width / skillBarWidth,
        (double)bounds.Height / skillBarHeight,
        confidence);
}

public sealed record SkillBarLayout(IReadOnlyList<SkillBarComponent> Components)
{
    public bool HasSkillSlots => Components
        .Select(component => component.Kind)
        .Order()
        .SequenceEqual(Enum.GetValues<SkillBarComponentKind>());

}
