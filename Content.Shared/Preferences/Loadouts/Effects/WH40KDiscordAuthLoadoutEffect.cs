using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Content.Shared.CCVar;
using Content.Shared._WH40K.DiscordAuth;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences.Loadouts.Effects;

/// <summary>
/// Restricts a loadout item behind linked Discord membership or specific Discord roles.
/// </summary>
public sealed partial class WH40KDiscordAuthLoadoutEffect : LoadoutEffect
{
    [DataField("requireGuildMember")]
    public bool RequireGuildMember;

    [DataField("requiredRoleIds")]
    public List<string> RequiredRoleIds = new();

    public override bool Validate(
        HumanoidCharacterProfile profile,
        RoleLoadout loadout,
        ICommonSession? session,
        IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        var normalizedRoleIds = WH40KDiscordAuthRequirementEvaluator.NormalizeRoleIds(RequiredRoleIds);
        if (!RequireGuildMember && normalizedRoleIds.Count == 0)
        {
            reason = FormattedMessage.Empty;
            return true;
        }

        var config = collection.Resolve<IConfigurationManager>();
        var unlockRequirementsBypassed = config.GetCVar(CCVars.WH40KMetaUnlocksEnforced);

        if (session == null || unlockRequirementsBypassed)
        {
            reason = FormattedMessage.Empty;
            return true;
        }

        var discordAuth = collection.Resolve<ISharedWH40KDiscordAuthManager>();
        if (!discordAuth.TryGetSnapshot(session.UserId, out var snapshot))
        {
            reason = FormattedMessage.FromUnformatted(Loc.GetString("loadout-group-wh40k-discord-loading"));
            return false;
        }

        if (snapshot.CacheStale)
        {
            reason = FormattedMessage.FromUnformatted(Loc.GetString("loadout-group-wh40k-discord-stale-restriction"));
            return false;
        }

        if (WH40KDiscordAuthRequirementEvaluator.MeetsRequirements(snapshot, RequireGuildMember, normalizedRoleIds))
        {
            reason = FormattedMessage.Empty;
            return true;
        }

        reason = normalizedRoleIds.Count > 0
            ? FormattedMessage.FromUnformatted(Loc.GetString("loadout-group-wh40k-discord-role-restriction"))
            : FormattedMessage.FromUnformatted(Loc.GetString("loadout-group-wh40k-discord-guild-restriction"));
        return false;
    }
}
