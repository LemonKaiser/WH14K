using System;
using System.Diagnostics.CodeAnalysis;
using Content.Shared.CCVar;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences.Loadouts.Effects;

/// <summary>
/// Restricts a loadout item by WH40K meta progression level.
/// </summary>
public sealed partial class WH40KMetaLevelLoadoutEffect : LoadoutEffect
{
    [DataField(required: true)]
    public int RequiredLevel = 1;

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

        var requiredLevel = Math.Max(1, RequiredLevel);
        var metaProgress = collection.Resolve<ISharedWH40KMetaProgressManager>();

        if (metaProgress.TryGetMetaLevel(session, out var currentLevel) &&
            currentLevel >= requiredLevel)
        {
            reason = FormattedMessage.Empty;
            return true;
        }

        reason = FormattedMessage.FromUnformatted(Loc.GetString(
            "loadout-group-wh40k-meta-level-restriction",
            ("requiredLevel", requiredLevel)));
        return false;
    }
}
