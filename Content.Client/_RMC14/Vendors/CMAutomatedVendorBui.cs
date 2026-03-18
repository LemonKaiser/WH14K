using System;
using System.Linq;
using Content.Shared._RMC14.Vendors;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static System.StringComparison;
using static Robust.Client.UserInterface.Controls.LineEdit;

namespace Content.Client._RMC14.Vendors;

[UsedImplicitly]
public sealed class CMAutomatedVendorBui : BoundUserInterface
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IResourceCache _resource = default!;

    private readonly SharedJobSystem _job;
    private readonly SharedMindSystem _mind;

    private CMAutomatedVendorWindow? _window;

    public CMAutomatedVendorBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _job = EntMan.System<SharedJobSystem>();
        _mind = EntMan.System<SharedMindSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CMAutomatedVendorWindow>();
        _window.Title = EntMan.GetComponentOrNull<MetaDataComponent>(Owner)?.EntityName
            ?? Loc.GetString("cm-automated-vendor-ui-title-fallback");

        BuildEntries();
        _window.Search.OnTextChanged += OnSearchChanged;
        Refresh();
    }

    private void BuildEntries()
    {
        if (_window == null)
            return;

        _window.Sections.DisposeAllChildren();
        var user = EntMan.GetComponentOrNull<CMVendorUserComponent>(_player.LocalEntity);

        if (!EntMan.TryGetComponent(Owner, out CMAutomatedVendorComponent? vendor))
            return;

        for (var sectionIndex = 0; sectionIndex < vendor.Sections.Count; sectionIndex++)
        {
            var section = vendor.Sections[sectionIndex];
            var uiSection = new CMAutomatedVendorSection { Section = section };
            uiSection.Label.SetMessage(GetSectionName(user, section));

            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                var entry = section.Entries[entryIndex];
                var uiEntry = new CMAutomatedVendorEntry();
                uiEntry.Panel.Button.TextLabel.Text = entry.Name ?? entry.Id;

                if (_prototype.TryIndex(entry.Id, out var entity))
                {
                    uiEntry.Texture.Textures = SpriteComponent.GetPrototypeTextures(entity, _resource)
                        .Select(layer => layer.Default)
                        .ToList();
                    if (entity.TryGetComponent<SpriteComponent>("Sprite", out var sprites))
                        uiEntry.Texture.Modulate = sprites.AllLayers.First().Color;

                    if (entry.Name == null)
                        uiEntry.Panel.Button.TextLabel.Text = entity.Name;

                    var tooltipText = string.IsNullOrWhiteSpace(entity.Description)
                        ? entity.Name
                        : $"{entity.Name}\n{entity.Description}";
                    uiEntry.TooltipLabel.ToolTip = tooltipText;
                }

                var sectionI = sectionIndex;
                var entryI = entryIndex;
                uiEntry.Panel.Button.OnPressed += _ => SendMessage(new CMVendorVendBuiMsg(sectionI, entryI));

                if (entry.Recommended)
                {
                    uiEntry.Panel.Button.TextLabel.Text = $"* {uiEntry.Panel.Button.TextLabel.Text}";
                    uiEntry.Panel.Color = Color.FromHex("#102919");
                    uiEntry.Panel.BorderColor = Color.FromHex("#3A9B52");
                    uiEntry.Panel.HoveredColor = Color.FromHex("#3A9B52");
                }

                if (section.TakeAll != null || section.TakeOne != null)
                {
                    uiEntry.Panel.Color = Color.FromHex("#251A0C");
                    uiEntry.Panel.BorderColor = Color.FromHex("#805300");
                    uiEntry.Panel.HoveredColor = Color.FromHex("#805300");
                }

                uiSection.Entries.AddChild(uiEntry);
            }

            _window.Sections.AddChild(uiSection);
        }
    }

    private bool IsSectionValid(CMVendorSection section)
    {
        if (section.Jobs.Count == 0)
            return true;

        if (_player.LocalSession == null || !_mind.TryGetMind(_player.LocalSession.UserId, out var mindId))
            return false;

        foreach (var job in section.Jobs)
        {
            if (_job.MindHasJobWithId(mindId, job.Id))
                return true;
        }

        return false;
    }

    private void OnSearchChanged(LineEditEventArgs args)
    {
        if (_window == null)
            return;

        foreach (var sectionControl in _window.Sections.Children)
        {
            if (sectionControl is not CMAutomatedVendorSection section)
                continue;

            var anyVisible = false;
            foreach (var entryControl in section.Entries.Children)
            {
                if (entryControl is not CMAutomatedVendorEntry entry)
                    continue;

                entry.Visible = string.IsNullOrWhiteSpace(args.Text) ||
                                (entry.Panel.Button.TextLabel.Text?.Contains(args.Text, OrdinalIgnoreCase) ?? false);
                anyVisible |= entry.Visible;
            }

            section.Visible = anyVisible && (section.Section == null || IsSectionValid(section.Section));
        }
    }

    public void Refresh()
    {
        if (_window == null || !EntMan.TryGetComponent(Owner, out CMAutomatedVendorComponent? vendor))
            return;

        var user = EntMan.GetComponentOrNull<CMVendorUserComponent>(_player.LocalEntity);
        var userPoints = user?.Points ?? 0;
        var anyEntryWithPoints = false;

        for (var sectionIndex = 0; sectionIndex < vendor.Sections.Count; sectionIndex++)
        {
            var section = vendor.Sections[sectionIndex];
            var uiSection = (CMAutomatedVendorSection) _window.Sections.GetChild(sectionIndex);
            uiSection.Label.SetMessage(GetSectionName(user, section));

            var sectionDisabled = !IsSectionValid(section);
            if (section.Choices is { } choices)
            {
                var picked = user?.Choices.GetValueOrDefault(choices.Id) ?? 0;
                if (picked >= choices.Amount)
                    sectionDisabled = true;
            }

            var anyAmount = false;
            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                var entry = section.Entries[entryIndex];
                var uiEntry = (CMAutomatedVendorEntry) uiSection.Entries.GetChild(entryIndex);

                var disabled = sectionDisabled || entry.Amount is <= 0;
                if (section.TakeAll is { Length: > 0 } takeAllId)
                {
                    var key = $"{takeAllId}:{entry.Id}";
                    if (user?.TakeAll.GetValueOrDefault(key) == true)
                        disabled = true;
                }

                if (section.TakeOne is { Length: > 0 } takeOneId && user?.TakeOne.GetValueOrDefault(takeOneId) == true)
                    disabled = true;

                if (entry.Points is { } points)
                {
                    anyEntryWithPoints = true;
                    uiEntry.Amount.Text = $"{points}P";
                    if (user == null || userPoints < points)
                        disabled = true;
                }
                else if (entry.Amount is { } amount)
                {
                    uiEntry.Amount.Text = amount.ToString();
                }
                else
                {
                    uiEntry.Amount.Text = Loc.GetString("cm-automated-vendor-ui-unlimited");
                }

                uiEntry.Amount.Modulate = disabled ? Color.Red : Color.White;
                uiEntry.Panel.Button.Disabled = disabled;

                if (!string.IsNullOrWhiteSpace(uiEntry.Amount.Text))
                    anyAmount = true;
            }

            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                var uiEntry = (CMAutomatedVendorEntry) uiSection.Entries.GetChild(entryIndex);
                uiEntry.Amount.Visible = anyAmount;
            }
        }

        _window.PointsLabel.Text = anyEntryWithPoints
            ? Loc.GetString("cm-automated-vendor-ui-points", ("points", userPoints))
            : string.Empty;
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is CMVendorRefreshBuiMsg)
            Refresh();
    }

    private static FormattedMessage GetSectionName(CMVendorUserComponent? user, CMVendorSection section)
    {
        var name = new FormattedMessage();
        var sectionName = Loc.TryGetString(section.Name, out var localizedName)
            ? localizedName
            : section.Name;
        name.PushTag(new MarkupNode("bold", null, null));
        name.AddText(sectionName.ToUpperInvariant());

        if (section.TakeAll is { Length: > 0 })
        {
            var pending = false;
            foreach (var entry in section.Entries)
            {
                var key = $"{section.TakeAll}:{entry.Id}";
                if (user?.TakeAll.GetValueOrDefault(key) != true)
                {
                    pending = true;
                    break;
                }
            }

            if (pending)
                name.AddText($" ({Loc.GetString("cm-automated-vendor-ui-take-all")})");
        }
        else if (section.TakeOne is { Length: > 0 })
        {
            if (user?.TakeOne.GetValueOrDefault(section.TakeOne) != true)
                name.AddText($" ({Loc.GetString("cm-automated-vendor-ui-take-one")})");
        }
        else if (section.Choices is { } choices)
        {
            var selected = user?.Choices.GetValueOrDefault(choices.Id) ?? 0;
            var left = Math.Max(0, choices.Amount - selected);
            if (left > 0)
                name.AddText(
                    $" ({Loc.GetString("cm-automated-vendor-ui-choose-left", ("count", left))})");
        }

        name.Pop();
        return name;
    }
}

