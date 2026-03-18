using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaDecorationSelectionSnapshot
{
	public string SelectedGhostSkinId { get; }

	public string SelectedOocTitleId { get; }

	public string SelectedOocNameColorId { get; }

	public WH40KMetaDecorationSelectionSnapshot(string selectedGhostSkinId, string selectedOocTitleId, string selectedOocNameColorId)
	{
		SelectedGhostSkinId = selectedGhostSkinId;
		SelectedOocTitleId = selectedOocTitleId;
		SelectedOocNameColorId = selectedOocNameColorId;
	}
}
