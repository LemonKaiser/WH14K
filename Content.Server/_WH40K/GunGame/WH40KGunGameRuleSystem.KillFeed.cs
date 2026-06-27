using System;
using System.Collections.Generic;
using Content.Shared.Mind;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared._WH40K.GunGame;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem
{
    private static readonly TimeSpan KillFeedWeaponMemory = TimeSpan.FromSeconds(8);

    private readonly Dictionary<NetUserId, RecentWeaponUse> _recentWeaponUses = new();

    private void InitializeKillFeed()
    {
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void ClearKillFeedState()
    {
        _recentWeaponUses.Clear();
    }

    private void OnGunShot(Entity<GunComponent> ent, ref GunShotEvent args)
    {
        RecordRecentWeaponUse(args.User, ent.Owner);
    }

    private void OnMeleeHit(Entity<MeleeWeaponComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        RecordRecentWeaponUse(args.User, args.Weapon);
    }

    private void RecordRecentWeaponUse(EntityUid user, EntityUid weapon)
    {
        if (!HasComp<WH40KGunGamePlayerComponent>(user))
            return;

        if (!TryGetUserId(user, out var userId))
            return;

        var prototypeId = MetaData(weapon).EntityPrototype?.ID;
        if (string.IsNullOrWhiteSpace(prototypeId))
            return;

        _recentWeaponUses[userId] = new RecentWeaponUse(prototypeId, _timing.CurTime);
    }

    private void SendKillFeedEntry(NetUserId killerId, NetUserId victimId)
    {
        if (!_player.TryGetPlayerData(killerId, out var killerData) ||
            !_player.TryGetPlayerData(victimId, out var victimData))
        {
            return;
        }

        var weaponPrototypeId = ResolveKillFeedWeaponPrototype(killerId);

        foreach (var session in _player.Sessions)
        {
            var ev = new WH40KGunGameKillFeedEvent(
                killerData.UserName,
                victimData.UserName,
                weaponPrototypeId,
                false,
                session.UserId == killerId,
                session.UserId == victimId);

            RaiseNetworkEvent(ev, session);
        }
    }

    private void SendFallbackKillFeedEntry(NetUserId victimId, bool selfKill)
    {
        if (!_player.TryGetPlayerData(victimId, out var victimData))
            return;

        foreach (var session in _player.Sessions)
        {
            var isVictim = session.UserId == victimId;
            var ev = new WH40KGunGameKillFeedEvent(
                victimData.UserName,
                victimData.UserName,
                null,
                true,
                selfKill && isVictim,
                isVictim);

            RaiseNetworkEvent(ev, session);
        }
    }

    private string? ResolveKillFeedWeaponPrototype(NetUserId killerId)
    {
        if (_recentWeaponUses.TryGetValue(killerId, out var recent) &&
            _timing.CurTime - recent.RecordedAt <= KillFeedWeaponMemory)
        {
            return recent.PrototypeId;
        }

        if (!_player.TryGetSessionById(killerId, out var session))
            return null;

        var ownedEntity = session.AttachedEntity;
        if (ownedEntity == null ||
            !TryComp<WH40KGunGamePlayerComponent>(ownedEntity.Value, out var playerComp) ||
            playerComp.CurrentWeapon == null ||
            TerminatingOrDeleted(playerComp.CurrentWeapon.Value))
        {
            return null;
        }

        return MetaData(playerComp.CurrentWeapon.Value).EntityPrototype?.ID;
    }

    private bool TryGetUserId(EntityUid entity, out NetUserId userId)
    {
        if (TryComp<ActorComponent>(entity, out var actor))
        {
            userId = actor.PlayerSession.UserId;
            return true;
        }

        if (_mind.TryGetMind(entity, out _, out var mind) && mind.UserId is { } mindUserId)
        {
            userId = mindUserId;
            return true;
        }

        userId = default;
        return false;
    }

    private readonly record struct RecentWeaponUse(string PrototypeId, TimeSpan RecordedAt);
}
