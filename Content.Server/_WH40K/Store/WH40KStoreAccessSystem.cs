using System;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Store.Components;
using Content.Server.Popups;
using Content.Server.Store.Systems;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Store;
using Content.Shared.UserInterface;
using Robust.Shared.Localization;
using Content.Server._WH40K.Localizations;

namespace Content.Server._WH40K.Store;

/// <summary>
/// Blocks enemy faction members from opening or using WH40K team stores.
/// </summary>
public sealed class WH40KStoreAccessSystem : EntitySystem
{
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KStoreTeamComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<WH40KStoreTeamComponent, CurrencyInsertAttemptEvent>(OnCurrencyInsertAttempt);
    }

    private void OnOpenAttempt(EntityUid uid, WH40KStoreTeamComponent component, ref ActivatableUIOpenAttemptEvent args)
    {
        if (IsBuyerAllowedForStore(args.User, component.TeamId))
            return;

        if (!args.Silent)
            _popup.PopupEntity(_culture.GetPlayerString(args.User, "wh40k-access-denied-wrong-team"), uid, args.User);

        args.Cancel();
    }

    private void OnCurrencyInsertAttempt(EntityUid uid, WH40KStoreTeamComponent component, CurrencyInsertAttemptEvent args)
    {
        if (IsBuyerAllowedForStore(args.User, component.TeamId))
            return;

        _popup.PopupEntity(_culture.GetPlayerString(args.User, "wh40k-access-denied-wrong-team"), uid, args.User);
        args.Cancel();
    }

    private bool IsBuyerAllowedForStore(EntityUid buyer, string storeTeamId)
    {
        if (string.IsNullOrWhiteSpace(storeTeamId))
            return true;

        if (TryComp<GhostComponent>(buyer, out var ghost) && ghost.CanGhostInteract)
            return true;

        if (_teamRule.TryGetTeamIdFromEntity(buyer, out var directTeamId) &&
            string.Equals(directTeamId, storeTeamId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryComp<MindComponent>(buyer, out var mind))
            return false;

        if (mind.CurrentEntity is { } currentEntity)
        {
            if (TryComp<GhostComponent>(currentEntity, out var currentGhost) && currentGhost.CanGhostInteract)
                return true;

            if (_teamRule.TryGetTeamIdFromEntity(currentEntity, out var currentTeamId) &&
                string.Equals(currentTeamId, storeTeamId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (mind.UserId is not { } userId)
            return false;

        return _teamRule.TryGetRememberedTeam(userId, out var rememberedTeamId) &&
               string.Equals(rememberedTeamId, storeTeamId, StringComparison.OrdinalIgnoreCase);
    }
}
