using System;
using System.Numerics;
using Content.Server.Administration;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.Chat;
using Content.Shared._WH40K.Command.Comms.Megaphone;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Speech;
using Content.Shared.Timing;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Server._WH40K.Localizations;

namespace Content.Server._WH40K.Command.Comms.Megaphone;

public sealed partial class WH40KMegaphoneSystem : EntitySystem
{
    [Dependency] private  ChatSystem _chat = default!;
    [Dependency] private  SharedHandsSystem _hands = default!;
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  QuickDialogSystem _quickDialog = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;
    [Dependency] private  UseDelaySystem _useDelay = default!;
    [Dependency] private  WH40KPlayerCultureTracker _culture = default!;

    private readonly Queue<MegaphoneOrderLogEntry> _orderLog = new();
    private static readonly TimeSpan GlobalLogRetention = TimeSpan.FromMinutes(5);
    private const int GlobalLogMaxEntries = 256;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KMegaphoneComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KMegaphoneComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WH40KMegaphoneComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WH40KMegaphoneComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<WH40KMegaphoneComponent> ent, ref MapInitEvent args)
    {
        _useDelay.SetLength(
            (ent.Owner, CompOrNull<UseDelayComponent>(ent.Owner)),
            ent.Comp.BroadcastDelay,
            ent.Comp.BroadcastUseDelayId);
    }

    private void OnUseInHand(Entity<WH40KMegaphoneComponent> ent, ref UseInHandEvent args)
    {
        args.Handled = true;
        var user = args.User;

        if (!TryComp(user, out ActorComponent? actor))
            return;

        if (!IsHoldingMegaphone(user, ent.Owner))
        {
            PopupCaution(user, "wh40k-megaphone-popup-not-in-hand");
            return;
        }

        using var scope = _culture.CreateScope(user);
        _quickDialog.OpenDialog(
            actor.PlayerSession,
            Loc.GetString("wh40k-megaphone-dialog-title"),
            Loc.GetString("wh40k-megaphone-dialog-prompt", ("max", ent.Comp.InputMaxLength)),
            (LongString text) =>
            {
                if (Deleted(ent.Owner) || Deleted(user))
                    return;

                TryBroadcastOrder(ent.Owner, user, text.String);
            });
    }

    private void OnGetVerbs(Entity<WH40KMegaphoneComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        if (!IsHoldingMegaphone(user, ent.Owner))
            return;

        using var scope = _culture.CreateScope(user);

        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 110,
            Text = Loc.GetString("wh40k-megaphone-verb-order-push"),
            Act = () => TryBroadcastOrder(ent.Owner, user, Loc.GetString("wh40k-megaphone-order-push")),
        });

        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 109,
            Text = Loc.GetString("wh40k-megaphone-verb-order-hold"),
            Act = () => TryBroadcastOrder(ent.Owner, user, Loc.GetString("wh40k-megaphone-order-hold")),
        });

        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 108,
            Text = Loc.GetString("wh40k-megaphone-verb-order-fall-back"),
            Act = () => TryBroadcastOrder(ent.Owner, user, Loc.GetString("wh40k-megaphone-order-fall-back")),
        });

        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 107,
            Text = Loc.GetString("wh40k-megaphone-verb-replay"),
            Act = () => TryReplayRecentOrders(ent.Owner, user),
        });
    }

    private void OnExamined(Entity<WH40KMegaphoneComponent> ent, ref ExaminedEvent args)
    {
        using var scope = _culture.CreateScope(args.Examiner);
        using (args.PushGroup(nameof(WH40KMegaphoneComponent)))
        {
            args.PushMarkup(Loc.GetString("wh40k-megaphone-examine-use", ("max", ent.Comp.InputMaxLength)));
            args.PushMarkup(Loc.GetString(
                "wh40k-megaphone-examine-replay",
                ("seconds", (int) Math.Ceiling(ent.Comp.ReplayWindow.TotalSeconds)),
                ("radius", (int) Math.Ceiling(ent.Comp.ReplayRadius))));
        }
    }

    private bool TryBroadcastOrder(EntityUid megaphoneUid, EntityUid user, string rawMessage)
    {
        if (!TryComp(megaphoneUid, out WH40KMegaphoneComponent? megaphone))
            return false;

        if (!IsHoldingMegaphone(user, megaphoneUid))
        {
            PopupCaution(user, "wh40k-megaphone-popup-not-in-hand");
            return false;
        }

        var message = NormalizeMessage(rawMessage, megaphone.InputMaxLength);
        if (string.IsNullOrWhiteSpace(message))
        {
            PopupCaution(user, "wh40k-megaphone-popup-empty");
            return false;
        }

        if (!_useDelay.TryResetDelay(megaphoneUid, checkDelayed: true, id: megaphone.BroadcastUseDelayId))
        {
            var seconds = 1;
            if (_useDelay.TryGetDelayInfo((megaphoneUid, CompOrNull<UseDelayComponent>(megaphoneUid)), out var delayInfo, megaphone.BroadcastUseDelayId))
            {
                seconds = Math.Max(1, (int) Math.Ceiling((delayInfo.EndTime - _timing.CurTime).TotalSeconds));
            }

            PopupCaution(user, "wh40k-megaphone-popup-item-cooldown", ("seconds", seconds));
            return false;
        }

        if (!TryConsumeUserThrottle(user, megaphone))
            return false;

        var speechChanged = false;
        ProtoId<SpeechVerbPrototype> originalVerb = SharedChatSystem.DefaultSpeechVerb;
        ProtoId<SpeechSoundsPrototype>? originalSounds = null;
        Dictionary<string, ProtoId<SpeechVerbPrototype>>? originalSuffix = null;
        if (TryComp(user, out SpeechComponent? speech))
        {
            speechChanged = true;
            originalVerb = speech.SpeechVerb;
            originalSounds = speech.SpeechSounds;
            originalSuffix = speech.SuffixSpeechVerbs;

            speech.SpeechVerb = megaphone.SpeechVerb;
            speech.SpeechSounds = megaphone.SpeechSounds;
            speech.SuffixSpeechVerbs = new Dictionary<string, ProtoId<SpeechVerbPrototype>>(megaphone.SuffixSpeechVerbs);
            Dirty(user, speech);
        }

        _chat.TrySendInGameICMessage(
            user,
            message,
            InGameICChatType.Speak,
            ChatTransmitRange.Normal,
            checkRadioPrefix: false);

        if (speechChanged && speech != null)
        {
            speech.SpeechVerb = originalVerb;
            speech.SpeechSounds = originalSounds;
            speech.SuffixSpeechVerbs = originalSuffix ?? new();
            Dirty(user, speech);
        }

        RecordOrder(user, message);
        return true;
    }

    private bool TryConsumeUserThrottle(EntityUid user, WH40KMegaphoneComponent megaphone)
    {
        var throttle = EnsureComp<WH40KMegaphoneUserThrottleComponent>(user);
        var now = _timing.CurTime;

        if (throttle.NextAllowedBroadcastAt > now)
        {
            var seconds = Math.Max(1, (int) Math.Ceiling((throttle.NextAllowedBroadcastAt - now).TotalSeconds));
            PopupCaution(user, "wh40k-megaphone-popup-user-cooldown", ("seconds", seconds));
            return false;
        }

        while (throttle.RecentBroadcasts.Count > 0 &&
               now - throttle.RecentBroadcasts.Peek() > megaphone.RateLimitWindow)
        {
            throttle.RecentBroadcasts.Dequeue();
        }

        if (throttle.RecentBroadcasts.Count >= megaphone.MaxBroadcastsPerWindow)
        {
            var nextFreeAt = throttle.RecentBroadcasts.Peek() + megaphone.RateLimitWindow;
            var seconds = Math.Max(1, (int) Math.Ceiling((nextFreeAt - now).TotalSeconds));
            PopupCaution(
                user,
                "wh40k-megaphone-popup-rate-limit",
                ("seconds", seconds),
                ("count", megaphone.MaxBroadcastsPerWindow));
            return false;
        }

        throttle.RecentBroadcasts.Enqueue(now);
        throttle.NextAllowedBroadcastAt = now + megaphone.UserCooldown;
        return true;
    }

    private void TryReplayRecentOrders(EntityUid megaphoneUid, EntityUid user)
    {
        if (!TryComp(megaphoneUid, out WH40KMegaphoneComponent? megaphone))
            return;

        if (!IsHoldingMegaphone(user, megaphoneUid))
        {
            PopupCaution(user, "wh40k-megaphone-popup-not-in-hand");
            return;
        }

        if (!TryComp(user, out ActorComponent? actor))
            return;

        TrimLog();

        var now = _timing.CurTime;
        var userMapPos = _transform.GetMapCoordinates(user);
        var userTeamId = GetTeamId(user);
        var entries = _orderLog.ToArray();
        var matched = new List<MegaphoneOrderLogEntry>();

        for (var i = entries.Length - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (now - entry.Timestamp > megaphone.ReplayWindow)
                break;

            if (entry.MapId != userMapPos.MapId)
                continue;

            if (Vector2.Distance(entry.Position, userMapPos.Position) > megaphone.ReplayRadius)
                continue;

            if (!CanReplayForTeam(userTeamId, entry.TeamId))
                continue;

            matched.Add(entry);
            if (matched.Count >= megaphone.ReplayEntryLimit)
                break;
        }

        if (matched.Count == 0)
        {
            PopupCaution(user, "wh40k-megaphone-popup-no-orders");
            return;
        }

        matched.Reverse();
        RaiseNetworkEvent(new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-megaphone-replay-header",
            LocArgs = new Dictionary<string, string>
            {
                ["count"] = matched.Count.ToString()
            }
        }, actor.PlayerSession);

        foreach (var entry in matched)
        {
            var age = Math.Max(0, (int) Math.Ceiling((now - entry.Timestamp).TotalSeconds));
            RaiseNetworkEvent(new WH40KLocalizedChatEvent
            {
                LocKey = "wh40k-megaphone-replay-line",
                LocArgs = new Dictionary<string, string>
                {
                    ["seconds"] = age.ToString(),
                    ["speaker"] = entry.SpeakerName,
                    ["message"] = entry.Message
                }
            }, actor.PlayerSession);
        }
    }

    private void RecordOrder(EntityUid user, string message)
    {
        var mapPos = _transform.GetMapCoordinates(user);
        if (mapPos.MapId == MapId.Nullspace)
            return;

        _orderLog.Enqueue(new MegaphoneOrderLogEntry(
            _timing.CurTime,
            mapPos.MapId,
            mapPos.Position,
            Name(user),
            message,
            GetTeamId(user)));

        TrimLog();
    }

    private void TrimLog()
    {
        var cutoff = _timing.CurTime - GlobalLogRetention;
        while (_orderLog.Count > 0)
        {
            if (_orderLog.Count <= GlobalLogMaxEntries && _orderLog.Peek().Timestamp >= cutoff)
                break;

            _orderLog.Dequeue();
        }
    }

    private bool IsHoldingMegaphone(EntityUid user, EntityUid megaphoneUid)
    {
        return _hands.IsHolding((user, CompOrNull<HandsComponent>(user)), megaphoneUid);
    }

    private static string NormalizeMessage(string rawMessage, int maxLength)
    {
        var normalized = rawMessage.Trim()
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        if (normalized.Length > maxLength)
            normalized = normalized[..maxLength];

        return normalized.Trim();
    }

    private void PopupCaution(EntityUid user, string key, params (string, object)[] args)
    {
        _popup.PopupEntity(_culture.GetPlayerString(user, key, args), user, user, PopupType.SmallCaution);
    }

    private static bool CanReplayForTeam(string? userTeamId, string? entryTeamId)
    {
        if (string.IsNullOrWhiteSpace(userTeamId) || string.IsNullOrWhiteSpace(entryTeamId))
            return true;

        return string.Equals(userTeamId, entryTeamId, StringComparison.OrdinalIgnoreCase);
    }

    private string? GetTeamId(EntityUid uid)
    {
        if (!TryComp(uid, out WH40KTeamMemberComponent? member))
            return null;

        return string.IsNullOrWhiteSpace(member.TeamId) ? null : member.TeamId;
    }

    private readonly record struct MegaphoneOrderLogEntry(
        TimeSpan Timestamp,
        MapId MapId,
        Vector2 Position,
        string SpeakerName,
        string Message,
        string? TeamId);
}
