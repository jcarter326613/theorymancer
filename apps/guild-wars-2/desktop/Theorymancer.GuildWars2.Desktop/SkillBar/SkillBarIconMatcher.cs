using Theorymancer.GuildWars2.Desktop.ArenaNet;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public sealed record SkillBarSlotMatch(
    SkillBarComponentKind Kind,
    ReferenceSkillIcon? Skill,
    double Score,
    string Message);

public sealed class SkillBarIconMatcher
{
    private const double MinimumScore = 0.60;
    private const double MinimumMargin = 0.035;
    private readonly ReferenceIcons _referenceIcons;

    public SkillBarIconMatcher(ReferenceIcons referenceIcons)
    {
        _referenceIcons = referenceIcons;
    }

    public async Task<IReadOnlyList<SkillBarSlotMatch>> MatchAsync(
        CapturedFrame frame,
        SkillBarLayout layout,
        BuildSkillCandidates candidates,
        CancellationToken cancellationToken)
    {
        var matches = await Task.WhenAll(layout.Components.Select(component =>
            MatchSlotAsync(frame, component, candidates.GetSkillIds(component.Kind), cancellationToken)));
        return matches.OrderBy(match => match.Kind).ToList();
    }

    private async Task<SkillBarSlotMatch> MatchSlotAsync(
        CapturedFrame frame,
        SkillBarComponent component,
        IReadOnlyList<int> candidateSkillIds,
        CancellationToken cancellationToken)
    {
        if (candidateSkillIds.Count == 0)
        {
            return new SkillBarSlotMatch(component.Kind, null, 0, "No build candidate");
        }

        var bounds = component.ToPixelBounds(frame.Width, frame.Height);
        var scores = new List<(ReferenceSkillIcon Skill, double Score)>();
        foreach (var skillId in candidateSkillIds)
        {
            var skill = _referenceIcons.FindSkill(skillId);
            if (skill is null)
            {
                continue;
            }

            var path = await _referenceIcons.GetSkillPathAsync(skillId, cancellationToken);
            var match = IconTemplateMatcher.MatchAt(frame, bounds, path, skill.Name, skill.SkillId);
            scores.Add((skill, match.Score));
        }

        var ranked = scores.OrderByDescending(score => score.Score).ToList();
        if (ranked.Count == 0)
        {
            return new SkillBarSlotMatch(component.Kind, null, 0, "Candidate icons unavailable");
        }

        var best = ranked[0];
        var margin = ranked.Count > 1 ? best.Score - ranked[1].Score : double.PositiveInfinity;
        if (best.Score < MinimumScore)
        {
            return new SkillBarSlotMatch(component.Kind, null, best.Score, "Low confidence");
        }

        if (margin < MinimumMargin)
        {
            return new SkillBarSlotMatch(component.Kind, null, best.Score, "Ambiguous candidate");
        }

        return new SkillBarSlotMatch(component.Kind, best.Skill, best.Score, best.Skill.Name);
    }
}
