#nullable disable warnings

using System;
using System.Linq;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Content.Shared.GhostTypes;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared._WH40K.MetaProgress;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.MetaProgress;

public sealed class WH40KMetaGhostDecorationSystem : EntitySystem
{
	private const string DefaultGhostRsiPath = "/Textures/Mobs/Ghosts/ghost_human.rsi";

	private const string DefaultGhostState = "animated";

	private const float ReapplyIntervalSeconds = 1f;

	[Dependency]
	private readonly SharedGhostSystem _ghosts = default!;

	[Dependency]
	private readonly SharedAppearanceSystem _appearance = default!;

	[Dependency]
	private readonly GhostSpriteStateSystem _ghostSpriteState = default!;

	[Dependency]
	private readonly WH40KMetaProgressSystem _metaProgress = default!;

	[Dependency]
	private readonly IPlayerManager _players = default!;

	[Dependency]
	private readonly SharedMindSystem _minds = default!;

	[Dependency]
	private readonly IConfigurationManager _cfg = default!;

	private ISawmill _sawmill;

	private float _reapplyAccumulator;

	public override void Initialize()
	{
		base.Initialize();
		_sawmill = Logger.GetSawmill("wh40k.meta.ghostdecor");
		SubscribeLocalEvent<GhostComponent, MindAddedMessage>(OnGhostMindAdded);
		_metaProgress.SnapshotPushed += OnSnapshotPushed;
	}

	public override void Shutdown()
	{
		base.Shutdown();
		_metaProgress.SnapshotPushed -= OnSnapshotPushed;
	}

	private void OnGhostMindAdded(Entity<GhostComponent> ent, ref MindAddedMessage args)
	{
		if (!ShouldApplyDecoration(ent.Owner, ent.Comp))
		{
			ClearGhostDecoration((Owner: ent.Owner, Comp: ent.Comp));
			return;
		}
		NetUserId? userId = args.Mind.Comp.UserId;
		if (userId.HasValue)
		{
			NetUserId valueOrDefault = userId.GetValueOrDefault();
			ApplyFromUser((Owner: ent.Owner, Comp: ent.Comp), valueOrDefault);
		}
	}

	private void OnSnapshotPushed(NetUserId userId, WH40KMetaProgressSnapshot snapshot)
	{
		if (!_players.TryGetSessionById(userId, out ICommonSession session))
		{
			return;
		}
		EntityUid? attachedEntity = session.AttachedEntity;
		if (!attachedEntity.HasValue)
		{
			return;
		}
		EntityUid valueOrDefault = attachedEntity.GetValueOrDefault();
		if (valueOrDefault.Valid && TryComp(valueOrDefault, out GhostComponent comp))
		{
			if (!ShouldApplyDecoration(valueOrDefault, comp))
			{
				ClearGhostDecoration((Owner: valueOrDefault, Comp: comp));
			}
			else
			{
				ApplyFromSnapshot((Owner: valueOrDefault, Comp: comp), snapshot, userId);
			}
		}
	}

	private bool ShouldApplyDecoration(EntityUid uid, GhostComponent ghost)
	{
		if (_cfg.GetCVar(CCVars.WH40KMetaAdminPriorityOverDecorations) && ghost.CanGhostInteract)
		{
			return false;
		}
		if (!TryComp(uid, out MetaDataComponent comp))
		{
			return false;
		}
		string text = comp.EntityPrototype?.ID;
		if (text == null)
		{
			return false;
		}
		return (EntProtoId)text == GameTicker.ObserverPrototypeName;
	}

	public override void Update(float frameTime)
	{
		base.Update(frameTime);
		_reapplyAccumulator += frameTime;
		if (_reapplyAccumulator < 1f)
		{
			return;
		}
		_reapplyAccumulator = 0f;
		EntityQueryEnumerator<GhostComponent> entityQueryEnumerator = EntityQueryEnumerator<GhostComponent>();
		EntityUid uid;
		GhostComponent comp;
		while (entityQueryEnumerator.MoveNext(out uid, out comp))
		{
			if (!_minds.TryGetMind(uid, out EntityUid _, out MindComponent mind))
			{
				continue;
			}
			NetUserId? userId = mind.UserId;
			if (userId.HasValue)
			{
				NetUserId valueOrDefault = userId.GetValueOrDefault();
				if (!ShouldApplyDecoration(uid, comp))
				{
					ClearGhostDecoration((Owner: uid, Comp: comp));
				}
				else
				{
					ApplyFromUser((Owner: uid, Comp: comp), valueOrDefault);
				}
			}
		}
	}

	private void ApplyFromUser(Entity<GhostComponent> ghost, NetUserId userId)
	{
		WH40KMetaProgressSnapshot snapshot;
		try
		{
			snapshot = _metaProgress.GetSnapshot(userId);
		}
		catch (Exception ex)
		{
			_sawmill.Warning($"Failed to resolve ghost decoration snapshot for {userId}: {ex.Message}");
			return;
		}
		ApplyFromSnapshot(ghost, snapshot, userId);
	}

	private void ApplyFromSnapshot(Entity<GhostComponent> ghost, WH40KMetaProgressSnapshot snapshot, NetUserId userId)
	{
		WH40KMetaDecorationSnapshotEntry selected = ResolveSelectedGhostDecoration(snapshot);
		Color value = ResolveGhostTint(selected, userId);
		_ghosts.SetGhostColor((Owner: ghost.Owner, Comp: ghost.Comp), value);
		ApplyGhostVisual(ghost.Owner, selected);
	}

	private static WH40KMetaDecorationSnapshotEntry? ResolveSelectedGhostDecoration(WH40KMetaProgressSnapshot snapshot)
	{
		WH40KMetaDecorationSnapshotEntry wH40KMetaDecorationSnapshotEntry = null;
		string selectedId = snapshot.DecorationSelection.SelectedGhostSkinId;
		if (!string.IsNullOrWhiteSpace(selectedId))
		{
			wH40KMetaDecorationSnapshotEntry = snapshot.Decorations.FirstOrDefault((WH40KMetaDecorationSnapshotEntry entry) => entry.Category == WH40KMetaDecorationCategory.GhostSkins && entry.Unlocked && string.Equals(entry.Id, selectedId, StringComparison.Ordinal));
		}
		if (wH40KMetaDecorationSnapshotEntry == null)
		{
			wH40KMetaDecorationSnapshotEntry = (from entry in snapshot.Decorations
				where entry.Category == WH40KMetaDecorationCategory.GhostSkins && entry.Unlocked
				orderby entry.SortOrder
				select entry).ThenBy<WH40KMetaDecorationSnapshotEntry, string>((WH40KMetaDecorationSnapshotEntry entry) => entry.Id, StringComparer.Ordinal).FirstOrDefault();
		}
		return wH40KMetaDecorationSnapshotEntry;
	}

	private Color ResolveGhostTint(WH40KMetaDecorationSnapshotEntry? selected, NetUserId userId)
	{
		if (selected == null || string.IsNullOrWhiteSpace(selected.GhostTintHex))
		{
			return Color.White;
		}
		Color? color = Color.TryFromHex(selected.GhostTintHex.AsSpan());
		if (color.HasValue)
		{
			return color.GetValueOrDefault();
		}
		_sawmill.Warning($"Invalid ghost tint '{selected.GhostTintHex}' for decoration '{selected.Id}' and player {userId}. Fallback to white.");
		return Color.White;
	}

	private void ApplyGhostVisual(EntityUid uid, WH40KMetaDecorationSnapshotEntry? selected)
	{
		string text = selected?.GhostRsiPath;
		string text2 = selected?.GhostState;
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "/Textures/Mobs/Ghosts/ghost_human.rsi";
		}
		if (string.IsNullOrWhiteSpace(text2))
		{
			text2 = "animated";
		}
		SyncGhostDamageVisualData(uid, text);
		WH40KGhostDecorationVisualComponent wH40KGhostDecorationVisualComponent = EnsureComp<WH40KGhostDecorationVisualComponent>(uid);
		if (!string.Equals(wH40KGhostDecorationVisualComponent.GhostRsiPath, text, StringComparison.Ordinal) || !string.Equals(wH40KGhostDecorationVisualComponent.GhostState, text2, StringComparison.Ordinal))
		{
			wH40KGhostDecorationVisualComponent.GhostRsiPath = text;
			wH40KGhostDecorationVisualComponent.GhostState = text2;
			Dirty(uid, wH40KGhostDecorationVisualComponent);
		}
	}

	private void SyncGhostDamageVisualData(EntityUid uid, string rsiPath)
	{
		GhostSpriteStateComponent comp;
		EntityUid mindId;
		MindComponent mind;
		if (!string.Equals(rsiPath, "/Textures/Mobs/Ghosts/ghost_human.rsi", StringComparison.Ordinal))
		{
			_appearance.RemoveData(uid, GhostVisuals.Damage);
		}
		else if (TryComp(uid, out comp) && _minds.TryGetMind(uid, out mindId, out mind))
		{
			_ghostSpriteState.SetGhostSprite((Owner: uid, Comp: comp), mindId);
		}
	}

	private void ClearGhostVisual(EntityUid uid)
	{
		if (HasComp<WH40KGhostDecorationVisualComponent>(uid))
		{
			RemComp<WH40KGhostDecorationVisualComponent>(uid);
		}
	}

	private void ClearGhostDecoration(Entity<GhostComponent?> ghost)
	{
		_ghosts.SetGhostColor(ghost, Color.White);
		ClearGhostVisual(ghost.Owner);
	}
}
