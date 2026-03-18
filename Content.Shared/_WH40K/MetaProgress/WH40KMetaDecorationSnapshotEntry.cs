using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaDecorationSnapshotEntry
{
	public string Id { get; }

	public WH40KMetaDecorationCategory Category { get; }

	public string TitleKey { get; }

	public string PreviewKey { get; }

	public string OocColorHex { get; }

	public List<string> OocGradientColors { get; }

	public bool OocGradientAnimated { get; }

	public int OocGradientDurationMs { get; }

	public string OocAuraHex { get; }

	public int OocAuraRadius { get; }

	public int OocAuraAlphaPercent { get; }

	public string OocTitleEffect { get; }

	public int OocTitleEffectRevealMs { get; }

	public int OocTitleEffectHoldMs { get; }

	public int OocTitleEffectDissolveMs { get; }

	public string OocTitleOutlineHex { get; }

	public int OocTitleOutlineWidth { get; }

	public int OocTitleOutlineAlphaPercent { get; }

	public string GhostRsiPath { get; }

	public string GhostState { get; }

	public string GhostTintHex { get; }

	public int SortOrder { get; }

	public bool SuppressTitlePrefix { get; }

	public bool Unlocked { get; }

	public WH40KMetaDecorationRequirementSnapshot Requirement { get; }

	public WH40KMetaDecorationSnapshotEntry(string id, WH40KMetaDecorationCategory category, string titleKey, string previewKey, string oocColorHex, List<string> oocGradientColors, bool oocGradientAnimated, int oocGradientDurationMs, string oocAuraHex, int oocAuraRadius, int oocAuraAlphaPercent, string oocTitleEffect, int oocTitleEffectRevealMs, int oocTitleEffectHoldMs, int oocTitleEffectDissolveMs, string oocTitleOutlineHex, int oocTitleOutlineWidth, int oocTitleOutlineAlphaPercent, string ghostRsiPath, string ghostState, string ghostTintHex, int sortOrder, bool suppressTitlePrefix, bool unlocked, WH40KMetaDecorationRequirementSnapshot requirement)
	{
		Id = id;
		Category = category;
		TitleKey = titleKey;
		PreviewKey = previewKey;
		OocColorHex = oocColorHex;
		OocGradientColors = oocGradientColors;
		OocGradientAnimated = oocGradientAnimated;
		OocGradientDurationMs = oocGradientDurationMs;
		OocAuraHex = oocAuraHex;
		OocAuraRadius = oocAuraRadius;
		OocAuraAlphaPercent = oocAuraAlphaPercent;
		OocTitleEffect = oocTitleEffect;
		OocTitleEffectRevealMs = oocTitleEffectRevealMs;
		OocTitleEffectHoldMs = oocTitleEffectHoldMs;
		OocTitleEffectDissolveMs = oocTitleEffectDissolveMs;
		OocTitleOutlineHex = oocTitleOutlineHex;
		OocTitleOutlineWidth = oocTitleOutlineWidth;
		OocTitleOutlineAlphaPercent = oocTitleOutlineAlphaPercent;
		GhostRsiPath = ghostRsiPath;
		GhostState = ghostState;
		GhostTintHex = ghostTintHex;
		SortOrder = sortOrder;
		SuppressTitlePrefix = suppressTitlePrefix;
		Unlocked = unlocked;
		Requirement = requirement;
	}
}
