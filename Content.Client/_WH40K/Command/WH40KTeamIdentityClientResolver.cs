using System;
using Content.Shared._WH40K.Command;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Command;

public static class WH40KTeamIdentityClientResolver
{
    private const string TeamIdentityMapId = "WH40KTeamIdentityMap";
    private const string TeamIdentityDefaultProfileId = "WH40KTeamIdentityProfileImperium";

    public static Color ResolveAccentColor(string teamId, Color fallback)
    {
        var proto = IoCManager.Resolve<IPrototypeManager>();
        if (!TryResolveTeamIdentityProfile(proto, teamId, out var profile))
            return fallback;

        if (string.IsNullOrWhiteSpace(profile.AccentColorHex))
            return fallback;

        return Color.FromHex(profile.AccentColorHex, fallback);
    }

    public static bool UsesHereticsDoctrinePresentation(string teamId)
    {
        var proto = IoCManager.Resolve<IPrototypeManager>();
        if (!TryResolveTeamIdentityProfile(proto, teamId, out var profile))
            return teamId.Equals("Heretics", StringComparison.OrdinalIgnoreCase);

        return profile.DoctrinePresentation == WH40KDoctrinePresentationVariant.Heretics;
    }

    private static bool TryResolveTeamIdentityProfile(
        IPrototypeManager proto,
        string teamId,
        out WH40KTeamIdentityProfilePrototype profile)
    {
        profile = default!;
        var profileId = ResolveTeamIdentityProfileId(proto, teamId);
        if (proto.TryIndex(profileId, out WH40KTeamIdentityProfilePrototype? indexedProfile))
        {
            profile = indexedProfile;
            return true;
        }

        if (proto.TryIndex(TeamIdentityDefaultProfileId, out WH40KTeamIdentityProfilePrototype? fallbackProfile))
        {
            profile = fallbackProfile;
            return true;
        }

        return false;
    }

    private static ProtoId<WH40KTeamIdentityProfilePrototype> ResolveTeamIdentityProfileId(
        IPrototypeManager proto,
        string teamId)
    {
        if (!proto.TryIndex(TeamIdentityMapId, out WH40KTeamIdentityMapPrototype? teamMap))
            return TeamIdentityDefaultProfileId;

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            if (teamMap.TeamProfiles.TryGetValue(teamId, out var directProfile))
                return directProfile;

            foreach (var (mappedTeamId, mappedProfile) in teamMap.TeamProfiles)
            {
                if (string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    return mappedProfile;
            }
        }

        return teamMap.DefaultProfile;
    }
}
