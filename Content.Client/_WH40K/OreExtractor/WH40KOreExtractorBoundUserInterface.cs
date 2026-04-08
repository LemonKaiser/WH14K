using System;
using Content.Client._WH40K.OreExtractor.UI;
using Content.Shared._WH40K.OreExtractor;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.OreExtractor;

[UsedImplicitly]
public sealed class WH40KOreExtractorBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WH40KOreExtractorWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WH40KOreExtractorWindow>();
        _window.OnSetEnabledPressed += enabled => SendMessage(new WH40KOreExtractorSetEnabledMessage(enabled));
        _window.OnSetRandomModePressed += () => SendMessage(new WH40KOreExtractorSetRandomModeMessage());
        _window.OnOreSelected += oreId => SendMessage(new WH40KOreExtractorSelectOreMessage(oreId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KOreExtractorBuiState cast)
            return;

        _window.ApplyState(cast);
    }
}
