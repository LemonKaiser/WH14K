using System;
using Content.Client.Items.UI;
using Content.Client.Message;
using Content.Client.Stylesheets;
using Content.Shared._WH40K.ArmorPlates;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.ArmorPlates;

public sealed class WH40KArmorPlateStatusControl : PollingItemStatusControl<WH40KArmorPlateStatusControl.Data>
{
    private readonly Entity<WH40KArmorPlateComponent> _plate;
    private readonly ProgressBar _bar;
    private readonly RichTextLabel _label;

    public WH40KArmorPlateStatusControl(Entity<WH40KArmorPlateComponent> plate)
    {
        _plate = plate;

        var layout = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
        };

        _bar = new ProgressBar
        {
            MinValue = 0f,
            SetHeight = 4f,
        };

        _label = new RichTextLabel
        {
            StyleClasses = { StyleClass.ItemStatus },
        };

        layout.AddChild(_bar);
        layout.AddChild(_label);
        AddChild(layout);

        UpdateDraw();
    }

    protected override Data PollData()
    {
        return new Data(_plate.Comp.CurrentDurability, _plate.Comp.MaxDurability);
    }

    protected override void Update(in Data data)
    {
        _bar.MaxValue = Math.Max(1, data.MaxDurability);
        _bar.Value = Math.Clamp(data.CurrentDurability, 0, data.MaxDurability);
        _label.SetMarkup(Loc.GetString(
            "wh40k-armor-plate-item-status",
            ("current", data.CurrentDurability),
            ("max", data.MaxDurability)));
    }

    public readonly record struct Data(int CurrentDurability, int MaxDurability);
}
