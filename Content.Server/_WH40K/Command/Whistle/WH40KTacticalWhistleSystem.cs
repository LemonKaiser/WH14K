using System;
using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Verbs;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.Command.Whistle;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Command.Whistle;

public sealed class WH40KTacticalWhistleSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KTacticalWhistleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KTacticalWhistleComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WH40KTacticalWhistleComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WH40KTacticalWhistleComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<WH40KTacticalWhistleComponent> ent, ref MapInitEvent args)
    {
        _useDelay.SetLength(
            (ent.Owner, CompOrNull<UseDelayComponent>(ent.Owner)),
            ent.Comp.SignalDelay,
            ent.Comp.UseDelayId);
    }

    private void OnUseInHand(Entity<WH40KTacticalWhistleComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TrySendSignal(ent.Owner, args.User, WH40KWhistleSignalType.Regroup, ent.Comp);
    }

    private void OnGetVerbs(Entity<WH40KTacticalWhistleComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !ent.Comp.EnableSignalVerbs)
            return;

        var user = args.User;
        if (!IsHoldingWhistle(user, ent.Owner))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 120,
            Text = Loc.GetString("wh40k-whistle-verb-regroup"),
            Act = () => TrySendSignal(ent.Owner, user, WH40KWhistleSignalType.Regroup, ent.Comp),
        });

        if (!ent.Comp.EnableTacticalVariants)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 119,
            Text = Loc.GetString("wh40k-whistle-verb-attack"),
            Act = () => TrySendSignal(ent.Owner, user, WH40KWhistleSignalType.Attack, ent.Comp),
        });

        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 118,
            Text = Loc.GetString("wh40k-whistle-verb-retreat"),
            Act = () => TrySendSignal(ent.Owner, user, WH40KWhistleSignalType.Retreat, ent.Comp),
        });
    }

    private void OnExamined(Entity<WH40KTacticalWhistleComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(WH40KTacticalWhistleComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-whistle-examine-cooldown",
                ("seconds", Math.Max(1, (int) Math.Ceiling(ent.Comp.SignalDelay.TotalSeconds))),
                ("window", Math.Max(1, (int) Math.Ceiling(ent.Comp.RateLimitWindow.TotalSeconds))),
                ("count", ent.Comp.MaxSignalsPerWindow)));

            args.PushMarkup(Loc.GetString(
                ent.Comp.EnableTacticalVariants
                    ? "wh40k-whistle-examine-variants-enabled"
                    : "wh40k-whistle-examine-variants-disabled"));
        }
    }

    private bool TrySendSignal(
        EntityUid whistleUid,
        EntityUid user,
        WH40KWhistleSignalType signalType,
        WH40KTacticalWhistleComponent whistle)
    {
        if (!IsHoldingWhistle(user, whistleUid))
        {
            PopupCaution(user, "wh40k-whistle-popup-not-in-hand");
            return false;
        }

        if (!TryResolveAuthorizedTeamId(user, whistle, out var teamId))
            return false;

        var mapCoordinates = _transform.GetMapCoordinates(user);
        if (mapCoordinates.MapId == MapId.Nullspace)
        {
            PopupCaution(user, "wh40k-whistle-popup-no-map");
            return false;
        }

        var profile = GetSignalProfile(signalType, whistle);
        if (!_proto.HasIndex<EntityPrototype>(profile.MarkerPrototype))
        {
            PopupCaution(user, "wh40k-whistle-popup-marker-unavailable");
            return false;
        }

        if (!_useDelay.TryResetDelay(whistleUid, checkDelayed: true, id: whistle.UseDelayId))
        {
            var seconds = 1;
            if (_useDelay.TryGetDelayInfo(
                    (whistleUid, CompOrNull<UseDelayComponent>(whistleUid)),
                    out var delayInfo,
                    whistle.UseDelayId))
            {
                seconds = Math.Max(1, (int) Math.Ceiling((delayInfo.EndTime - _timing.CurTime).TotalSeconds));
            }

            PopupCaution(user, "wh40k-whistle-popup-item-cooldown", ("seconds", seconds));
            return false;
        }

        if (!TryConsumeUserThrottle(user, whistle))
            return false;

        SpawnSignalMarker(profile, mapCoordinates, teamId);
        _audio.PlayPredicted(whistle.SignalSound, whistleUid, user);
        PopupSuccess(user, profile.SuccessPopupKey);
        DispatchTeamSignalMessage(teamId, profile.TeamMessageKey, user);
        return true;
    }

    private void SpawnSignalMarker(SignalProfile profile, MapCoordinates coordinates, string teamId)
    {
        var marker = Spawn(profile.MarkerPrototype, coordinates);
        var visual = EnsureComp<WH40KMissionObjectiveVisualComponent>(marker);
        visual.TeamId = teamId;
        visual.Label = profile.LabelKey;
        visual.Radius = profile.MarkerRadius;
        visual.Pulse = true;
        visual.Color = profile.MarkerColor;
        Dirty(marker, visual);
    }

    private bool TryResolveAuthorizedTeamId(
        EntityUid user,
        WH40KTacticalWhistleComponent whistle,
        out string teamId)
    {
        teamId = string.Empty;
        var hasTeam = _teamRule.TryGetTeamIdFromEntity(user, out teamId);
        if (!hasTeam)
        {
            if (!whistle.RequireTeam && whistle.AllowedTeamIds.Count == 0)
                return true;

            PopupCaution(user, "wh40k-whistle-popup-no-team");
            return false;
        }

        if (whistle.AllowedTeamIds.Count == 0)
            return true;

        var resolvedTeam = teamId;
        var allowed = whistle.AllowedTeamIds.Any(allowedTeamId =>
            string.Equals(allowedTeamId, resolvedTeam, StringComparison.OrdinalIgnoreCase));
        if (allowed)
            return true;

        PopupCaution(user, "wh40k-whistle-popup-wrong-team");
        return false;
    }

    private bool TryConsumeUserThrottle(EntityUid user, WH40KTacticalWhistleComponent whistle)
    {
        var throttle = EnsureComp<WH40KTacticalWhistleUserThrottleComponent>(user);
        var now = _timing.CurTime;

        if (throttle.NextAllowedSignalAt > now)
        {
            var seconds = Math.Max(1, (int) Math.Ceiling((throttle.NextAllowedSignalAt - now).TotalSeconds));
            PopupCaution(user, "wh40k-whistle-popup-user-cooldown", ("seconds", seconds));
            return false;
        }

        while (throttle.RecentSignals.Count > 0 &&
               now - throttle.RecentSignals.Peek() > whistle.RateLimitWindow)
        {
            throttle.RecentSignals.Dequeue();
        }

        if (throttle.RecentSignals.Count >= whistle.MaxSignalsPerWindow)
        {
            var nextAt = throttle.RecentSignals.Peek() + whistle.RateLimitWindow;
            var seconds = Math.Max(1, (int) Math.Ceiling((nextAt - now).TotalSeconds));
            PopupCaution(
                user,
                "wh40k-whistle-popup-rate-limit",
                ("seconds", seconds),
                ("count", whistle.MaxSignalsPerWindow));
            return false;
        }

        throttle.RecentSignals.Enqueue(now);
        throttle.NextAllowedSignalAt = now + whistle.UserCooldown;
        return true;
    }

    private void DispatchTeamSignalMessage(string teamId, string messageKey, EntityUid caller)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return;

        var callerName = Name(caller);
        var message = Loc.GetString(messageKey, ("user", callerName));
        foreach (var session in _players.Sessions)
        {
            if (!_teamRule.TryGetTeamIdForUser(session.UserId, out var sessionTeamId))
                continue;

            if (!string.Equals(sessionTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            _chat.DispatchServerMessage(session, message);
        }
    }

    private bool IsHoldingWhistle(EntityUid user, EntityUid whistleUid)
    {
        return _hands.IsHolding((user, CompOrNull<HandsComponent>(user)), whistleUid);
    }

    private void PopupCaution(EntityUid user, string key, params (string, object)[] args)
    {
        _popup.PopupEntity(Loc.GetString(key, args), user, user, PopupType.SmallCaution);
    }

    private void PopupSuccess(EntityUid user, string key)
    {
        _popup.PopupEntity(Loc.GetString(key), user, user, PopupType.Small);
    }

    private static SignalProfile GetSignalProfile(WH40KWhistleSignalType signalType, WH40KTacticalWhistleComponent whistle)
    {
        return signalType switch
        {
            WH40KWhistleSignalType.Attack => new SignalProfile(
                whistle.AttackMarkerPrototype,
                whistle.AttackRadius,
                "wh40k-whistle-marker-label-attack",
                "wh40k-whistle-popup-sent-attack",
                "wh40k-whistle-team-message-attack",
                Color.FromHex("#FF6C6C")),
            WH40KWhistleSignalType.Retreat => new SignalProfile(
                whistle.RetreatMarkerPrototype,
                whistle.RetreatRadius,
                "wh40k-whistle-marker-label-retreat",
                "wh40k-whistle-popup-sent-retreat",
                "wh40k-whistle-team-message-retreat",
                Color.FromHex("#FFD07A")),
            _ => new SignalProfile(
                whistle.RegroupMarkerPrototype,
                whistle.RegroupRadius,
                "wh40k-whistle-marker-label-regroup",
                "wh40k-whistle-popup-sent-regroup",
                "wh40k-whistle-team-message-regroup",
                Color.FromHex("#8ED9FF"))
        };
    }

    private readonly record struct SignalProfile(
        EntProtoId MarkerPrototype,
        float MarkerRadius,
        string LabelKey,
        string SuccessPopupKey,
        string TeamMessageKey,
        Color MarkerColor);
}
