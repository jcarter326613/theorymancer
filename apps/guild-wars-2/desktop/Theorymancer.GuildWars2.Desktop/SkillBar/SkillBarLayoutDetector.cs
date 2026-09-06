using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public sealed record SkillBarIconTemplate(
    SkillBarComponentKind Kind,
    string Name,
    int SkillId,
    string Path);

public sealed record SkillBarLayoutDebugInfo(
    int? AnchorSkillId,
    double? AnchorScore,
    int? IconSize,
    int? AnchorX,
    int? AnchorY,
    double? SlotSpacing,
    double? MatchScore);

public sealed record SkillBarLayoutDetection(
    SkillBarLayout? Layout,
    double Confidence,
    string Message,
    SkillBarLayoutDebugInfo DebugInfo)
{
    public bool IsUsable => Layout is not null && Layout.HasSkillSlots;
}

public static class SkillBarLayoutDetector
{
    private const double MinimumAnchorScore = 0.60;
    private const double MinimumSlotScore = 0.50;
    private const double ButtonToSpacingRatio = 0.97;

    public static SkillBarLayoutDetection Detect(
        CapturedFrame frame,
        IReadOnlyList<SkillBarIconTemplate> templates)
    {
        var heal = FindBest(frame, FullFrame(frame), templates, SkillBarComponentKind.HealSkill, 32, Math.Min(256, Math.Min(frame.Width, frame.Height)));
        if (heal is null || heal.Match.Score < MinimumAnchorScore)
        {
            return Failed($"Could not confidently find the build's heal icon in this crop (best raw pixel score: {heal?.Match.Score:F3}). Include the full skill bar and avoid transformed states.", heal);
        }

        var templateSize = heal.Match.Bounds.Width;
        var minimumSize = Math.Max(20, (int)Math.Round(templateSize * 0.8));
        var maximumSize = Math.Max(minimumSize, (int)Math.Round(templateSize * 1.2));
        var row = RowRegion(frame, heal.Match.Bounds, templateSize, beforeAnchor: true, afterAnchor: true);

        var utility1 = FindBest(
            frame,
            Region(frame, heal.Match.Bounds.X + templateSize / 2, row.Y, templateSize * 2, row.Height),
            templates,
            SkillBarComponentKind.UtilitySkill1,
            minimumSize,
            maximumSize);
        if (utility1 is null || utility1.Match.Score < MinimumSlotScore)
        {
            return Failed("Found the heal icon, but could not confirm the first utility icon beside it.", heal);
        }

        var rightSpacing = utility1.Match.Bounds.X - heal.Match.Bounds.X;
        if (rightSpacing <= 0)
        {
            return Failed("The heal and first utility icon did not form a usable skill-bar row.", heal);
        }

        var slots = new Dictionary<SkillBarComponentKind, TemplateMatch>
        {
            [SkillBarComponentKind.HealSkill] = heal,
            [SkillBarComponentKind.UtilitySkill1] = utility1,
        };
        if (!FindRightSlot(SkillBarComponentKind.UtilitySkill2, 2) ||
            !FindRightSlot(SkillBarComponentKind.UtilitySkill3, 3) ||
            !FindRightSlot(SkillBarComponentKind.EliteSkill, 4))
        {
            return Failed("Found the right skill group, but could not refine every utility slot.", heal, rightSpacing);
        }

        // Weapon skill 2 is the first weapon anchor because skill 1 can have an autocast overlay.
        var weapon2 = FindBest(
            frame,
            Region(frame, 0, row.Y, Math.Max(0, heal.Match.Bounds.X), row.Height),
            templates,
            SkillBarComponentKind.WeaponSkill2,
            minimumSize,
            maximumSize);
        if (weapon2 is null || weapon2.Match.Score < MinimumSlotScore)
        {
            return Failed("Found the right skill group, but could not locate weapon skill 2 on the same row.", heal, rightSpacing);
        }

        slots[SkillBarComponentKind.WeaponSkill2] = weapon2;
        var weapon3 = FindNear(
            frame,
            weapon2.Match.Bounds.X + rightSpacing,
            weapon2.Match.Bounds.Y,
            templateSize,
            templates,
            SkillBarComponentKind.WeaponSkill3,
            minimumSize,
            maximumSize);
        if (weapon3 is null || weapon3.Match.Score < MinimumSlotScore)
        {
            return Failed("Found weapon skill 2, but could not confirm weapon skill 3 beside it.", heal, rightSpacing);
        }

        slots[SkillBarComponentKind.WeaponSkill3] = weapon3;
        var weaponSpacing = weapon3.Match.Bounds.X - weapon2.Match.Bounds.X;
        if (weaponSpacing <= 0 ||
            !FindWeaponSlot(SkillBarComponentKind.WeaponSkill1, weapon2.Match.Bounds.X - weaponSpacing, required: false) ||
            !FindWeaponSlot(SkillBarComponentKind.WeaponSkill4, weapon2.Match.Bounds.X + weaponSpacing * 2, required: true) ||
            !FindWeaponSlot(SkillBarComponentKind.WeaponSkill5, weapon2.Match.Bounds.X + weaponSpacing * 3, required: true))
        {
            return Failed("Found weapon skills 2 and 3, but could not refine the full weapon group.", heal, rightSpacing);
        }

        var rightGroup = new[]
        {
            (SkillBarComponentKind.HealSkill, 0),
            (SkillBarComponentKind.UtilitySkill1, 1),
            (SkillBarComponentKind.UtilitySkill2, 2),
            (SkillBarComponentKind.UtilitySkill3, 3),
            (SkillBarComponentKind.EliteSkill, 4),
        };
        var weaponGroup = new[]
        {
            // Skill 1 can carry an autocast overlay, so do not let it distort the grid fit.
            (SkillBarComponentKind.WeaponSkill2, 1),
            (SkillBarComponentKind.WeaponSkill3, 2),
            (SkillBarComponentKind.WeaponSkill4, 3),
            (SkillBarComponentKind.WeaponSkill5, 4),
        };
        var rightGeometry = FitGroupGeometry(slots, rightGroup);
        var weaponGeometry = FitGroupGeometry(slots, weaponGroup);
        var buttonSize = Math.Max(1, (int)Math.Round(
            (rightGeometry.Spacing + weaponGeometry.Spacing) / 2 * ButtonToSpacingRatio));
        var rowCenter = Median(slots.Values.Select(match => CenterY(match.Match.Bounds)));
        var centerXs = new Dictionary<SkillBarComponentKind, double>();
        foreach (var (kind, index) in rightGroup)
        {
            centerXs[kind] = rightGeometry.FirstCenter + rightGeometry.Spacing * index;
        }

        foreach (var kind in Enum.GetValues<SkillBarComponentKind>().Where(IsWeaponSkill))
        {
            var index = (int)kind;
            centerXs[kind] = weaponGeometry.FirstCenter + weaponGeometry.Spacing * index;
        }

        var components = Enum.GetValues<SkillBarComponentKind>()
            .Select(kind => SkillBarComponent.FromPixelBounds(
                kind,
                ToButtonBounds(centerXs[kind], buttonSize, rowCenter),
                frame.Width,
                frame.Height,
                slots[kind].Match.Score))
            .ToList();
        var confidence = slots.Values.Average(match => match.Match.Score);
        return new SkillBarLayoutDetection(
            new SkillBarLayout(components),
            confidence,
            "Detected the skill bar from build-icon pixel matches. Confirm that the green boxes cover the icon interiors.",
            new SkillBarLayoutDebugInfo(
                heal.Template.SkillId,
                heal.Match.Score,
                buttonSize,
                ToButtonBounds(centerXs[SkillBarComponentKind.HealSkill], buttonSize, rowCenter).X,
                (int)Math.Round(rowCenter - buttonSize / 2.0),
                (rightGeometry.Spacing + weaponGeometry.Spacing) / 2.0,
                confidence));

        bool FindRightSlot(SkillBarComponentKind kind, int offset)
        {
            var match = FindNear(frame, heal.Match.Bounds.X + rightSpacing * offset, heal.Match.Bounds.Y, templateSize, templates, kind, minimumSize, maximumSize);
            if (match is null || match.Match.Score < MinimumSlotScore)
            {
                return false;
            }

            slots[kind] = match;
            return true;
        }

        bool FindWeaponSlot(SkillBarComponentKind kind, int expectedX, bool required)
        {
            var match = FindNear(frame, expectedX, weapon2.Match.Bounds.Y, templateSize, templates, kind, minimumSize, maximumSize);
            if (match is null || (required && match.Match.Score < MinimumSlotScore))
            {
                return false;
            }

            slots[kind] = match;
            return true;
        }
    }

    private static TemplateMatch? FindNear(
        CapturedFrame frame,
        int expectedX,
        int expectedY,
        int iconSize,
        IReadOnlyList<SkillBarIconTemplate> templates,
        SkillBarComponentKind kind,
        int minimumSize,
        int maximumSize)
    {
        var horizontalSlack = Math.Max(4, (int)Math.Round(iconSize * 0.3));
        var verticalSlack = Math.Max(4, (int)Math.Round(iconSize * 0.2));
        return FindBest(
            frame,
            Region(
                frame,
                expectedX - horizontalSlack,
                expectedY - verticalSlack,
                maximumSize + horizontalSlack * 2,
                maximumSize + verticalSlack * 2),
            templates,
            kind,
            minimumSize,
            maximumSize);
    }

    private static TemplateMatch? FindBest(
        CapturedFrame frame,
        ScreenBounds region,
        IReadOnlyList<SkillBarIconTemplate> templates,
        SkillBarComponentKind kind,
        int minimumSize,
        int maximumSize)
    {
        return templates
            .Where(template => template.Kind == kind)
            .Select(template =>
            {
                var match = IconTemplateMatcher.FindBestMatchInRegion(
                    frame,
                    region,
                    minimumSize,
                    maximumSize,
                    template.Path,
                    template.Name,
                    template.SkillId);
                return match is null ? null : new TemplateMatch(template, match);
            })
            .OfType<TemplateMatch>()
            .MaxBy(match => match.Match.Score);
    }

    private static SkillBarLayoutDetection Failed(string message, TemplateMatch? anchor = null, double? spacing = null) => new(
        null,
        0,
        message,
        new SkillBarLayoutDebugInfo(
            anchor?.Template.SkillId,
            anchor?.Match.Score,
            anchor?.Match.Bounds.Width,
            anchor?.Match.Bounds.X,
            anchor?.Match.Bounds.Y,
            spacing,
            null));

    private static ScreenBounds FullFrame(CapturedFrame frame) => new(0, 0, frame.Width, frame.Height);

    private static GroupGeometry FitGroupGeometry(
        IReadOnlyDictionary<SkillBarComponentKind, TemplateMatch> slots,
        IReadOnlyList<(SkillBarComponentKind Kind, int Index)> group)
    {
        var centers = group
            .Select(entry => (entry.Index, Center: CenterX(slots[entry.Kind].Match.Bounds)))
            .OrderBy(entry => entry.Index)
            .ToList();
        var spacing = Median(centers.SelectMany((left, leftIndex) => centers
            .Skip(leftIndex + 1)
            .Select(right => (right.Center - left.Center) / (right.Index - left.Index))));
        var firstCenter = centers.Average(entry => entry.Center - entry.Index * spacing);
        return new GroupGeometry(firstCenter, spacing);
    }

    private static bool IsWeaponSkill(SkillBarComponentKind kind) => kind is
        SkillBarComponentKind.WeaponSkill1 or
        SkillBarComponentKind.WeaponSkill2 or
        SkillBarComponentKind.WeaponSkill3 or
        SkillBarComponentKind.WeaponSkill4 or
        SkillBarComponentKind.WeaponSkill5;

    private static ScreenBounds ToButtonBounds(double centerX, int size, double rowCenter)
    {
        return new ScreenBounds(
            (int)Math.Round(centerX - size / 2.0),
            (int)Math.Round(rowCenter - size / 2.0),
            size,
            size);
    }

    private static double CenterX(ScreenBounds bounds) => bounds.X + bounds.Width / 2.0;

    private static double CenterY(ScreenBounds bounds) => bounds.Y + bounds.Height / 2.0;

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToList();
        return (ordered[(ordered.Count - 1) / 2] + ordered[ordered.Count / 2]) / 2;
    }

    private static ScreenBounds RowRegion(CapturedFrame frame, ScreenBounds anchor, int iconSize, bool beforeAnchor, bool afterAnchor)
    {
        var verticalSlack = Math.Max(4, (int)Math.Round(iconSize * 0.2));
        var left = beforeAnchor ? 0 : anchor.X;
        var right = afterAnchor ? frame.Width : anchor.Right;
        return Region(frame, left, anchor.Y - verticalSlack, right - left, iconSize + verticalSlack * 2);
    }

    private static ScreenBounds Region(CapturedFrame frame, int x, int y, int width, int height)
    {
        var left = Math.Clamp(x, 0, frame.Width);
        var top = Math.Clamp(y, 0, frame.Height);
        var right = Math.Clamp((long)x + width, 0, frame.Width);
        var bottom = Math.Clamp((long)y + height, 0, frame.Height);
        return new ScreenBounds(left, top, Math.Max(0, (int)right - left), Math.Max(0, (int)bottom - top));
    }

    private sealed record TemplateMatch(SkillBarIconTemplate Template, IconTemplateMatch Match);

    private sealed record GroupGeometry(double FirstCenter, double Spacing);
}
