using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public enum WH40KMetaProgressResetAccountStatus : byte
{
	Success,
	CooldownActive,
	Unavailable,
	Failed
}

[Serializable]
[NetSerializable]
public sealed class WH40KMetaProgressResetAccountResultEvent : EntityEventArgs
{
	public WH40KMetaProgressResetAccountStatus Status { get; }

	public int CooldownRemainingSeconds { get; }

	public WH40KMetaProgressResetAccountResultEvent(WH40KMetaProgressResetAccountStatus status, int cooldownRemainingSeconds = 0)
	{
		Status = status;
		CooldownRemainingSeconds = Math.Max(0, cooldownRemainingSeconds);
	}
}
