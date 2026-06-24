using System.Diagnostics.CodeAnalysis;
using Content.Shared.CCVar;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences.Loadouts.Effects;

/// <summary>
/// Restricts a loadout item behind a completed WH40K meta achievement.
/// </summary>
public sealed partial class WH40KMetaAchievementLoadoutEffect : LoadoutEffect
{
    [DataField(required: true)]
    public ProtoId<WH40KMetaAchievementPrototype> Achievement;

    public override bool Validate(
        HumanoidCharacterProfile profile,
        RoleLoadout loadout,
        ICommonSession? session,
        IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        var config = collection.Resolve<IConfigurationManager>();
        var unlockRequirementsBypassed = config.GetCVar(CCVars.WH40KMetaUnlocksEnforced);

        if (session == null || unlockRequirementsBypassed)
        {
            reason = FormattedMessage.Empty;
            return true;
        }

        var metaProgress = collection.Resolve<ISharedWH40KMetaProgressManager>();
        if (!metaProgress.TryHasCompletedAchievement(session, Achievement, out var completed))
        {
            // Don't strip persisted loadout selections while meta progress is still loading.
            reason = FormattedMessage.Empty;
            return true;
        }

        if (completed)
        {
            reason = FormattedMessage.Empty;
            return true;
        }

        var achievement = collection.Resolve<IPrototypeManager>().Index(Achievement);
        reason = FormattedMessage.FromUnformatted(Loc.GetString(
            "loadout-group-wh40k-achievement-restriction",
            ("achievement", Loc.GetString(achievement.TitleKey))));
        return false;
    }
}
