#nullable disable warnings

using System;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;

namespace Content.Server._WH40K.MetaProgress;

public sealed class WH40KMetaProgressManager : ISharedWH40KMetaProgressManager
{
	[Dependency]
	private readonly IEntitySystemManager _entitySystems = default!;

	public bool TryGetMetaLevel(ICommonSession session, out int level)
	{
		WH40KMetaProgressSnapshot snapshot = _entitySystems.GetEntitySystem<WH40KMetaProgressSystem>().GetSnapshot(session.UserId);
		level = Math.Max(1, snapshot.Level);
		return true;
	}
}
