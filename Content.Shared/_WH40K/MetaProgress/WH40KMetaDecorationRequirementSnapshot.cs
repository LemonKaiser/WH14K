using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaDecorationRequirementSnapshot
{
	public int RequiredLevel { get; }

	public List<string> RequiredAchievements { get; }

	public bool RequiredDiscordGuildMember { get; }

	public List<string> RequiredDiscordRoleIds { get; }

	public bool AdminOnly { get; }

	public WH40KMetaDecorationRequirementSnapshot(int requiredLevel, List<string> requiredAchievements, bool requiredDiscordGuildMember, List<string> requiredDiscordRoleIds, bool adminOnly)
	{
		RequiredLevel = requiredLevel;
		RequiredAchievements = requiredAchievements;
		RequiredDiscordGuildMember = requiredDiscordGuildMember;
		RequiredDiscordRoleIds = requiredDiscordRoleIds;
		AdminOnly = adminOnly;
	}
}
