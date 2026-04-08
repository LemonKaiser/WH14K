using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.Localization;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeDoctrineWindow : FancyWindow, ILocalizedControl
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("wh40k.command");
    private static readonly Color ImperiumColor = Color.FromHex("#F3C548");
    private static readonly Color ActiveColor = Color.FromHex("#7E88A6");
    private const string DoctrineTeamMapId = "WH40KCommandDoctrineTeamMap";
    private const string DoctrineDefaultProfileId = "WH40KCommandDoctrineProfileDefault";

    private readonly record struct DoctrinePreset(
        string Id,
        string NameImperiumKey,
        string NameHereticsKey,
        string BriefFocusKey,
        string BriefEffectKey,
        string DebuffKey,
        string SummaryKey,
        string PositiveKey,
        string NegativeKey,
        string LockKey,
        string FullBriefingKey,
        string ThemeImperiumKey,
        string ThemeHereticsKey,
        string LockedDomainId,
        bool IsNeutral);

    private readonly record struct DoctrineConfiguration(
        int UnlockLevel,
        string DefaultDoctrineId,
        IReadOnlyList<DoctrinePreset> Presets,
        string ProfileId);

    public readonly record struct DoctrineDisplay(
        string Id,
        string Name,
        string BriefFocus,
        string BriefEffect,
        string DebuffText,
        string Summary,
        string Positive,
        string Negative,
        string LockText,
        string FullBriefing,
        string ThemeText,
        bool IsNeutral);

    private static readonly DoctrinePreset FallbackPreset = new(
        "doctrine_adaptive_reserve",
        "w40k-cmd-doctrine-adaptive-reserve-name-imperium",
        "w40k-cmd-doctrine-adaptive-reserve-name-heretics",
        "w40k-cmd-doctrine-adaptive-reserve-brief-focus",
        "w40k-cmd-doctrine-adaptive-reserve-brief-effect",
        "w40k-cmd-doctrine-adaptive-reserve-debuff",
        "w40k-cmd-doctrine-adaptive-reserve-summary",
        "w40k-cmd-doctrine-adaptive-reserve-positive",
        "w40k-cmd-doctrine-adaptive-reserve-negative",
        "w40k-cmd-doctrine-adaptive-reserve-lock",
        "w40k-cmd-doctrine-adaptive-reserve-full-briefing",
        "w40k-cmd-doctrine-adaptive-reserve-theme-imperium",
        "w40k-cmd-doctrine-adaptive-reserve-theme-heretics",
        string.Empty,
        true);

    public event Action<string>? OnDoctrineAssigned;

    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _headerTitleLabel;
    private readonly Label _teamLine;
    private readonly Label _phaseLine;
    private readonly Label _availabilityLine;
    private readonly Label _activeDoctrineLine;
    private readonly PanelContainer _teamBadge;
    private readonly Label _teamBadgeLabel;
    private readonly PanelContainer _phaseBadge;
    private readonly Label _phaseBadgeLabel;
    private readonly Label _cardsHeaderLabel;
    private readonly BoxContainer _cardsRow;
    private readonly Dictionary<string, StyleBoxFlat> _rowStyles = new();
    private readonly Dictionary<string, Label> _rowTitleLabels = new();
    private readonly Dictionary<string, Label> _rowFocusLabels = new();
    private readonly Dictionary<string, Label> _rowEffectLabels = new();
    private readonly Dictionary<string, Label> _rowLockLabels = new();
    private readonly Dictionary<string, Label> _rowDebuffLabels = new();
    private readonly Dictionary<string, Button> _rowButtons = new();
    private readonly List<DoctrinePreset> _presets = new();
    private DoctrineDetailWindow? _detailWindow;
    private Color _accent = ImperiumColor;
    private string _teamId = string.Empty;
    private string _activeDoctrineId = string.Empty;
    private string _selectedDoctrineId = string.Empty;
    private bool _doctrineLocked;
    private int _baseLevel;
    private int _doctrineUnlockLevel = 3;
    private string _defaultDoctrineId = FallbackPreset.Id;
    private string _activeProfileId = string.Empty;
    private WH40KCommandNodeBoundUserInterfaceState? _latestState;
    private string _latestActiveDoctrineId = string.Empty;
    private bool _latestDoctrineLocked;

    public WH40KCommandNodeDoctrineWindow()
    {
        Title = Loc.GetString("w40k-cmd-doctrine-window-title");
        MinSize = new Vector2(940, 600);
        SetSize = new Vector2(980, 620);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(6)
        };
        ContentsContainer.AddChild(root);

        var header = new PanelContainer
        {
            PanelOverride = _headerStyle = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.HeaderBackground,
                ImperiumColor,
                2)
        };
        root.AddChild(header);

        var headerBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(10, 8),
            VerticalAlignment = VAlignment.Center
        };
        header.AddChild(headerBox);

        var headerInfo = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            HorizontalExpand = true
        };
        headerBox.AddChild(headerInfo);

        _headerTitleLabel = new Label
        {
            Text = Loc.GetString("w40k-cmd-doctrine-window-title"),
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        _teamLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _phaseLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _availabilityLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _activeDoctrineLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_headerTitleLabel);
        headerInfo.AddChild(_teamLine);
        headerInfo.AddChild(_phaseLine);
        headerInfo.AddChild(_availabilityLine);
        headerInfo.AddChild(_activeDoctrineLine);

        var badgeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            VerticalAlignment = VAlignment.Center
        };
        headerBox.AddChild(badgeRow);

        _teamBadge = new PanelContainer();
        _teamBadgeLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true
        };
        _teamBadge.AddChild(_teamBadgeLabel);
        badgeRow.AddChild(_teamBadge);

        _phaseBadge = new PanelContainer();
        _phaseBadgeLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true
        };
        _phaseBadge.AddChild(_phaseBadgeLabel);
        badgeRow.AddChild(_phaseBadge);

        var cardsSection = new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.PanelBackgroundAlt,
                WH40KCommandUiStyles.StrongBorder,
                2)
        };
        root.AddChild(cardsSection);

        var cardsRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
            VerticalExpand = true
        };
        cardsSection.AddChild(cardsRoot);

        var cardsHeader = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateHeaderStripStyle(WH40KCommandUiStyles.MutedBorder)
        };
        cardsRoot.AddChild(cardsHeader);
        _cardsHeaderLabel = new Label
        {
            Text = Loc.GetString("w40k-cmd-doctrine-window-list-header"),
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        cardsHeader.AddChild(_cardsHeaderLabel);

        var cardsSectionBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
            Margin = new Thickness(8),
            VerticalExpand = true
        };
        cardsRoot.AddChild(cardsSectionBox);

        var cardsScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SetHeight = 300f
        };
        cardsSectionBox.AddChild(cardsScroll);

        _cardsRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true
        };
        cardsScroll.AddChild(_cardsRow);

        Relocalize();
    }

    public void Relocalize()
    {
        Title = Loc.GetString("w40k-cmd-doctrine-window-title");
        _headerTitleLabel.Text = Loc.GetString("w40k-cmd-doctrine-window-title");
        _cardsHeaderLabel.Text = Loc.GetString("w40k-cmd-doctrine-window-list-header");

        if (_presets.Count > 0)
            RebuildDoctrineCards();

        if (_latestState != null)
        {
            UpdateState(_latestState, _latestActiveDoctrineId, _latestDoctrineLocked);

            if (_detailWindow != null)
                OpenDetailWindowForSelected();
        }
    }

    public static DoctrineDisplay ResolveDoctrineDisplay(string? doctrineId, string teamId)
    {
        var config = ResolveDoctrineConfiguration(teamId);
        var preset = FindPreset(doctrineId, config.Presets, config.DefaultDoctrineId);
        var useHereticsPresentation = UsesHereticsDoctrinePresentation(teamId);
        var nameKey = useHereticsPresentation ? preset.NameHereticsKey : preset.NameImperiumKey;
        var themeKey = useHereticsPresentation ? preset.ThemeHereticsKey : preset.ThemeImperiumKey;

        return new DoctrineDisplay(
            preset.Id,
            Loc.GetString(nameKey),
            Loc.GetString(preset.BriefFocusKey),
            Loc.GetString(preset.BriefEffectKey),
            Loc.GetString(preset.DebuffKey),
            Loc.GetString(preset.SummaryKey),
            Loc.GetString(preset.PositiveKey),
            Loc.GetString(preset.NegativeKey),
            Loc.GetString(preset.LockKey),
            Loc.GetString(preset.FullBriefingKey),
            Loc.GetString(themeKey),
            preset.IsNeutral);
    }

    public static string ResolveDoctrineLockedDomainId(string? doctrineId, string teamId)
    {
        var config = ResolveDoctrineConfiguration(teamId);
        var preset = FindPreset(doctrineId, config.Presets, config.DefaultDoctrineId);
        return preset.LockedDomainId;
    }

    public void UpdateState(
        WH40KCommandNodeBoundUserInterfaceState state,
        string activeDoctrineId,
        bool doctrineLocked)
    {
        _latestState = state;
        _latestActiveDoctrineId = activeDoctrineId;
        _latestDoctrineLocked = doctrineLocked;
        _teamId = state.TeamId;
        _baseLevel = Math.Max(1, state.BaseLevel);
        _doctrineLocked = doctrineLocked;
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, ImperiumColor);
        EnsureProfileLoaded(_teamId);

        _headerStyle.BorderColor = _accent;
        _headerTitleLabel.ModulateSelfOverride = _accent;
        var resolvedTeam = WH40KCommandUiStyles.ResolveLocalizedOrRaw(state.TeamName);
        _teamLine.Text = Loc.GetString("w40k-cmd-team", ("team", resolvedTeam));
        _phaseLine.Text = Loc.GetString("w40k-cmd-phase",
            ("phase", Loc.GetString(GetPhaseKey(state.Phase))));
        _teamBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#203227".AsSpan()), _accent);
        _teamBadgeLabel.Text = string.IsNullOrWhiteSpace(state.TeamName) ? "?" : resolvedTeam.ToUpperInvariant();
        _phaseBadge.PanelOverride = ResolvePhaseBadgeStyle(state.Phase);
        _phaseBadgeLabel.Text = Loc.GetString(GetPhaseKey(state.Phase));

        _activeDoctrineId = string.IsNullOrWhiteSpace(activeDoctrineId)
            ? string.Empty
            : FindLoadedPreset(activeDoctrineId).Id;

        if (!string.IsNullOrWhiteSpace(_activeDoctrineId))
            _selectedDoctrineId = _activeDoctrineId;
        else if (string.IsNullOrWhiteSpace(_selectedDoctrineId) || !_rowStyles.ContainsKey(_selectedDoctrineId))
            _selectedDoctrineId = _defaultDoctrineId;

        _availabilityLine.Text = _doctrineLocked
            ? Loc.GetString("w40k-cmd-doctrine-window-state-locked")
            : _baseLevel < _doctrineUnlockLevel
                ? Loc.GetString("w40k-cmd-doctrine-window-state-wait-level",
                    ("level", _doctrineUnlockLevel))
                : Loc.GetString("w40k-cmd-doctrine-window-state-open");

        _activeDoctrineLine.Text = string.IsNullOrWhiteSpace(_activeDoctrineId)
            ? Loc.GetString("w40k-cmd-doctrine-window-active-none")
            : Loc.GetString("w40k-cmd-doctrine-window-active-set",
                ("doctrine", ResolveDoctrineDisplay(_activeDoctrineId, _teamId).Name));

        RefreshRows();
        RefreshCardDetails();
    }

    private void EnsureProfileLoaded(string teamId)
    {
        var config = ResolveDoctrineConfiguration(teamId);
        if (_presets.Count > 0 &&
            string.Equals(_activeProfileId, config.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeProfileId = config.ProfileId;
        _doctrineUnlockLevel = config.UnlockLevel;
        _defaultDoctrineId = config.DefaultDoctrineId;
        _presets.Clear();
        _presets.AddRange(config.Presets);
        RebuildDoctrineCards();

        if (string.IsNullOrWhiteSpace(_selectedDoctrineId) || !_rowStyles.ContainsKey(_selectedDoctrineId))
            _selectedDoctrineId = _defaultDoctrineId;

        if (!_rowStyles.ContainsKey(_activeDoctrineId))
            _activeDoctrineId = string.Empty;

        _detailWindow?.Close();
        _detailWindow = null;
    }

    private void RebuildDoctrineCards()
    {
        _cardsRow.RemoveAllChildren();
        _rowStyles.Clear();
        _rowTitleLabels.Clear();
        _rowFocusLabels.Clear();
        _rowEffectLabels.Clear();
        _rowLockLabels.Clear();
        _rowDebuffLabels.Clear();
        _rowButtons.Clear();

        foreach (var preset in _presets)
        {
            AddDoctrineCard(preset);
        }
    }

    private void AddDoctrineCard(DoctrinePreset preset)
    {
        var rowStyle = WH40KCommandUiStyles.CreateCardStyle(
            WH40KCommandUiStyles.CardBackgroundAlt,
            WH40KCommandUiStyles.MutedBorder);

        var card = new PanelContainer
        {
            MinWidth = 280,
            MinHeight = 240,
            VerticalAlignment = VAlignment.Top,
            PanelOverride = rowStyle
        };
        _cardsRow.AddChild(card);

        var cardBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6
        };
        card.AddChild(cardBox);

        var title = new Label
        {
            Text = UsesHereticsDoctrinePresentation(_teamId)
                ? Loc.GetString(preset.NameHereticsKey)
                : Loc.GetString(preset.NameImperiumKey),
            StyleClasses = { "LabelBig" },
            ClipText = true
        };
        cardBox.AddChild(title);
        cardBox.AddChild(new HSeparator());

        var detailStack = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2
        };
        cardBox.AddChild(detailStack);

        var focusLabel = AddCardSection(
            detailStack,
            Loc.GetString("w40k-cmd-doctrine-window-card-focus-header"),
            Color.FromHex("#D0D8EF"));
        var effectLabel = AddCardSection(
            detailStack,
            Loc.GetString("w40k-cmd-doctrine-window-card-effect-header"),
            Color.FromHex("#9BC4FF"));
        var lockLabel = AddCardSection(
            detailStack,
            Loc.GetString("w40k-cmd-doctrine-window-card-lock-header"),
            Color.FromHex("#E7BFAF"));
        var debuffLabel = AddCardSection(
            detailStack,
            Loc.GetString("w40k-cmd-doctrine-window-card-debuff-header"),
            Color.FromHex("#E19797"));

        var selectButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("w40k-cmd-doctrine-window-select-button")
        };
        selectButton.OnPressed += _ => OpenDoctrineDetail(preset.Id);
        cardBox.AddChild(selectButton);

        _rowStyles[preset.Id] = rowStyle;
        _rowTitleLabels[preset.Id] = title;
        _rowFocusLabels[preset.Id] = focusLabel;
        _rowEffectLabels[preset.Id] = effectLabel;
        _rowLockLabels[preset.Id] = lockLabel;
        _rowDebuffLabels[preset.Id] = debuffLabel;
        _rowButtons[preset.Id] = selectButton;
    }

    private void OpenDoctrineDetail(string doctrineId)
    {
        _selectedDoctrineId = FindLoadedPreset(doctrineId).Id;
        RefreshRows();
        OpenDetailWindowForSelected();
    }

    private void AssignDoctrine(string doctrineId)
    {
        if (_doctrineLocked || _baseLevel < _doctrineUnlockLevel)
            return;

        _selectedDoctrineId = FindLoadedPreset(doctrineId).Id;
        RefreshRows();
        RefreshCardDetails();
        _detailWindow?.Close();
        _detailWindow = null;
        OnDoctrineAssigned?.Invoke(_selectedDoctrineId);
    }

    private void OpenDetailWindowForSelected()
    {
        var display = ResolveDoctrineDisplay(_selectedDoctrineId, _teamId);
        var isActive = _doctrineLocked &&
                       string.Equals(_selectedDoctrineId, _activeDoctrineId, StringComparison.OrdinalIgnoreCase);
        var canAssign = !_doctrineLocked &&
                        _baseLevel >= _doctrineUnlockLevel &&
                        !isActive;

        var blockReason = _doctrineLocked
            ? Loc.GetString("w40k-cmd-doctrine-window-state-locked")
            : _baseLevel < _doctrineUnlockLevel
                ? Loc.GetString("w40k-cmd-doctrine-window-state-wait-level",
                    ("level", _doctrineUnlockLevel))
                : string.Empty;

        _detailWindow?.Close();
        _detailWindow = new DoctrineDetailWindow(display, _accent, canAssign, isActive, blockReason);
        _detailWindow.OnConfirm += () => AssignDoctrine(display.Id);
        _detailWindow.OnClose += () => _detailWindow = null;
        _detailWindow.OpenCentered();
    }

    private void RefreshRows()
    {
        foreach (var preset in _presets)
        {
            if (!_rowStyles.TryGetValue(preset.Id, out var rowStyle) ||
                !_rowTitleLabels.TryGetValue(preset.Id, out var title) ||
                !_rowButtons.TryGetValue(preset.Id, out var button))
            {
                continue;
            }

            var isSelected = string.Equals(preset.Id, _selectedDoctrineId, StringComparison.OrdinalIgnoreCase);
            var isActive = _doctrineLocked &&
                           string.Equals(preset.Id, _activeDoctrineId, StringComparison.OrdinalIgnoreCase);

            rowStyle.BackgroundColor = isSelected
                ? WH40KCommandUiStyles.CardBackground
                : isActive
                    ? WH40KCommandUiStyles.CardBackgroundMuted
                    : WH40KCommandUiStyles.CardBackgroundAlt;
            rowStyle.BorderColor = isSelected
                ? _accent
                : isActive
                    ? ActiveColor
                    : WH40KCommandUiStyles.MutedBorder;

            title.ModulateSelfOverride = isSelected ? _accent : Color.White;

            button.Text = isActive
                ? Loc.GetString("w40k-cmd-doctrine-window-row-active-button")
                : Loc.GetString("w40k-cmd-doctrine-window-select-button");
            button.Disabled = false;
        }
    }

    private void RefreshCardDetails()
    {
        foreach (var preset in _presets)
        {
            if (!_rowTitleLabels.ContainsKey(preset.Id))
                continue;

            var display = ResolveDoctrineDisplay(preset.Id, _teamId);
            _rowTitleLabels[preset.Id].Text = display.Name;
            _rowFocusLabels[preset.Id].Text = CompactText(display.BriefFocus, 56);
            _rowEffectLabels[preset.Id].Text = CompactText(display.BriefEffect, 56);
            _rowLockLabels[preset.Id].Text = CompactText(display.LockText, 56);
            _rowDebuffLabels[preset.Id].Text = CompactText(display.DebuffText, 56);
        }
    }

    private DoctrinePreset FindLoadedPreset(string? doctrineId)
    {
        return FindPreset(doctrineId, _presets, _defaultDoctrineId);
    }

    private static DoctrinePreset FindPreset(
        string? doctrineId,
        IReadOnlyList<DoctrinePreset> presets,
        string defaultDoctrineId)
    {
        if (!string.IsNullOrWhiteSpace(doctrineId))
        {
            foreach (var preset in presets)
            {
                if (string.Equals(preset.Id, doctrineId, StringComparison.OrdinalIgnoreCase))
                    return preset;
            }
        }

        if (!string.IsNullOrWhiteSpace(defaultDoctrineId))
        {
            foreach (var preset in presets)
            {
                if (string.Equals(preset.Id, defaultDoctrineId, StringComparison.OrdinalIgnoreCase))
                    return preset;
            }
        }

        return presets.Count > 0 ? presets[0] : FallbackPreset;
    }

    private static DoctrineConfiguration ResolveDoctrineConfiguration(string teamId)
    {
        var prototype = IoCManager.Resolve<IPrototypeManager>();
        var profileId = ResolveProfileIdForTeam(prototype, teamId);
        if (!prototype.TryIndex(profileId, out WH40KCommandDoctrineProfilePrototype? profile))
        {
            Sawmill.Error($"Missing command-doctrine profile prototype '{profileId}'.");
            return BuildFallbackConfiguration(DoctrineDefaultProfileId);
        }

        var presets = new List<DoctrinePreset>(profile.Doctrines.Count);
        foreach (var doctrine in profile.Doctrines)
        {
            if (string.IsNullOrWhiteSpace(doctrine.Id) ||
                string.IsNullOrWhiteSpace(doctrine.NameImperiumKey) ||
                string.IsNullOrWhiteSpace(doctrine.NameHereticsKey) ||
                string.IsNullOrWhiteSpace(doctrine.BriefFocusKey) ||
                string.IsNullOrWhiteSpace(doctrine.BriefEffectKey) ||
                string.IsNullOrWhiteSpace(doctrine.DebuffKey) ||
                string.IsNullOrWhiteSpace(doctrine.SummaryKey) ||
                string.IsNullOrWhiteSpace(doctrine.PositiveKey) ||
                string.IsNullOrWhiteSpace(doctrine.NegativeKey) ||
                string.IsNullOrWhiteSpace(doctrine.LockKey) ||
                string.IsNullOrWhiteSpace(doctrine.FullBriefingKey) ||
                string.IsNullOrWhiteSpace(doctrine.ThemeImperiumKey) ||
                string.IsNullOrWhiteSpace(doctrine.ThemeHereticsKey))
            {
                continue;
            }

            presets.Add(new DoctrinePreset(
                doctrine.Id,
                doctrine.NameImperiumKey,
                doctrine.NameHereticsKey,
                doctrine.BriefFocusKey,
                doctrine.BriefEffectKey,
                doctrine.DebuffKey,
                doctrine.SummaryKey,
                doctrine.PositiveKey,
                doctrine.NegativeKey,
                doctrine.LockKey,
                doctrine.FullBriefingKey,
                doctrine.ThemeImperiumKey,
                doctrine.ThemeHereticsKey,
                doctrine.LockedDomain,
                doctrine.IsNeutral));
        }

        if (presets.Count == 0)
        {
            Sawmill.Error($"Doctrine profile '{profileId}' has no valid doctrine entries.");
            return BuildFallbackConfiguration(profileId);
        }

        var unlockLevel = Math.Max(1, profile.UnlockLevel);
        var defaultDoctrineId = FindPreset(profile.DefaultDoctrineId, presets, FallbackPreset.Id).Id;

        return new DoctrineConfiguration(unlockLevel, defaultDoctrineId, presets, profile.ID);
    }

    private static DoctrineConfiguration BuildFallbackConfiguration(string profileId)
    {
        return new DoctrineConfiguration(
            UnlockLevel: 3,
            DefaultDoctrineId: FallbackPreset.Id,
            Presets: new[] { FallbackPreset },
            ProfileId: profileId);
    }

    private static string ResolveProfileIdForTeam(IPrototypeManager prototype, string teamId)
    {
        if (!prototype.TryIndex(DoctrineTeamMapId, out WH40KCommandDoctrineTeamMapPrototype? teamMap))
            return DoctrineDefaultProfileId;

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            if (teamMap.TeamProfiles.TryGetValue(teamId, out var directProfile))
                return directProfile;

            foreach (var (mappedTeamId, mappedProfile) in teamMap.TeamProfiles)
            {
                if (string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    return mappedProfile;
            }
        }

        return teamMap.DefaultProfile;
    }

    private sealed class DoctrineDetailWindow : FancyWindow
    {
        public event Action? OnConfirm;

        public DoctrineDetailWindow(
            DoctrineDisplay doctrine,
            Color accent,
            bool canAssign,
            bool isActive,
            string blockReason)
        {
            Title = Loc.GetString("w40k-cmd-doctrine-detail-window-title", ("doctrine", doctrine.Name));
            MinSize = SetSize = new Vector2(760, 600);

            var root = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 6,
                Margin = new Thickness(6)
            };
            ContentsContainer.AddChild(root);

            var summaryPanel = new PanelContainer
            {
                PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
                    WH40KCommandUiStyles.HeaderBackground,
                    accent,
                    2)
            };
            root.AddChild(summaryPanel);

            var summaryBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 5,
                Margin = new Thickness(10, 8)
            };
            summaryPanel.AddChild(summaryBox);

            summaryBox.AddChild(new Label
            {
                Text = doctrine.Name,
                StyleClasses = { "LabelHeading" },
                ModulateSelfOverride = accent,
                ClipText = true
            });

            var summaryText = new Label
            {
                ClipText = true,
                Text = CompactText(doctrine.Summary, 120)
            };
            summaryBox.AddChild(summaryText);

            var sectionsScroll = new ScrollContainer
            {
                VerticalExpand = true
            };
            root.AddChild(sectionsScroll);

            var sections = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 8
            };
            sectionsScroll.AddChild(sections);

            AddSection(
                sections,
                Loc.GetString("w40k-cmd-doctrine-window-detail-briefing-header"),
                doctrine.FullBriefing,
                Color.White);
            AddSection(
                sections,
                Loc.GetString("w40k-cmd-doctrine-window-positive-header"),
                doctrine.Positive,
                accent);
            AddSection(
                sections,
                Loc.GetString("w40k-cmd-doctrine-window-negative-header"),
                doctrine.Negative,
                Color.FromHex("#E2AF9D"));
            AddSection(
                sections,
                Loc.GetString("w40k-cmd-doctrine-window-card-debuff-header"),
                doctrine.DebuffText,
                Color.FromHex("#E19797"));
            AddSection(
                sections,
                Loc.GetString("w40k-cmd-doctrine-window-lock-header"),
                doctrine.LockText,
                Color.White);
            AddSection(
                sections,
                Loc.GetString("w40k-cmd-doctrine-window-theme-header"),
                doctrine.ThemeText,
                Color.FromHex("#AAB3CC"));

            if (!string.IsNullOrWhiteSpace(blockReason))
            {
                var blockerPanel = new PanelContainer
                {
                    PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                        WH40KCommandUiStyles.CardBackgroundAlt,
                        WH40KCommandUiStyles.WarningBadge)
                };
                var blocker = new Label
                {
                    ClipText = true,
                    Text = CompactText(blockReason, 120)
                };
                blockerPanel.AddChild(blocker);
                root.AddChild(blockerPanel);
            }

            var actionsPanel = new PanelContainer
            {
                PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
                    WH40KCommandUiStyles.FooterBackground,
                    WH40KCommandUiStyles.MutedBorder,
                    1)
            };
            root.AddChild(actionsPanel);

            var actions = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 6
            };
            actionsPanel.AddChild(actions);

            var backButton = new Button
            {
                HorizontalExpand = true,
                Text = Loc.GetString("w40k-cmd-doctrine-window-detail-close-button")
            };
            backButton.OnPressed += _ => Close();
            actions.AddChild(backButton);

            var confirmButton = new Button
            {
                HorizontalExpand = true,
                Disabled = !canAssign,
                Text = isActive
                    ? Loc.GetString("w40k-cmd-doctrine-window-row-active-button")
                    : Loc.GetString("w40k-cmd-doctrine-window-assign-button")
            };
            confirmButton.OnPressed += _ =>
            {
                OnConfirm?.Invoke();
                Close();
            };
            actions.AddChild(confirmButton);
        }

        private static void AddSection(BoxContainer parent, string header, string body, Color bodyColor)
        {
            var panel = new PanelContainer
            {
                PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                    WH40KCommandUiStyles.CardBackground,
                    WH40KCommandUiStyles.MutedBorder)
            };
            parent.AddChild(panel);

            var box = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 4
            };
            panel.AddChild(box);

            box.AddChild(new Label
            {
                Text = header,
                StyleClasses = { "LabelHeading" },
                ClipText = true
            });

            var text = new RichTextLabel
            {
                SetHeight = 42f
            };
            WH40KCommandUiStyles.SetWrappedText(text, body, bodyColor);
            box.AddChild(text);
        }
    }

    private static bool UsesHereticsDoctrinePresentation(string teamId)
    {
        return WH40KTeamIdentityClientResolver.UsesHereticsDoctrinePresentation(teamId);
    }

    private static string GetPhaseKey(WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Preparation => "wh40k-phase-preparation-name",
            WH40KBattlePhase.Assault => "wh40k-phase-assault-name",
            WH40KBattlePhase.Apocalypse => "wh40k-phase-apocalypse-name",
            _ => "wh40k-phase-preparation-name"
        };
    }

    private static Label AddCardSection(BoxContainer parent, string header, Color headerColor)
    {
        var panel = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackground,
                WH40KCommandUiStyles.MutedBorder)
        };
        parent.AddChild(panel);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2
        };
        panel.AddChild(box);

        box.AddChild(new Label
        {
            Text = header,
            StyleClasses = { "LabelSubText" },
            ModulateSelfOverride = headerColor,
            ClipText = true
        });
        box.AddChild(new HSeparator());

        var body = new Label
        {
            ClipText = true
        };
        box.AddChild(body);
        return body;
    }

    private static string CompactText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var compact = text.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        while (compact.Contains("  ", StringComparison.Ordinal))
        {
            compact = compact.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (compact.Length <= maxLength)
            return compact;

        return compact[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private static StyleBoxFlat ResolvePhaseBadgeStyle(WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Preparation => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#26314A".AsSpan()),
                WH40KCommandUiStyles.InfoBadge),
            WH40KBattlePhase.Assault => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#3A2E1D".AsSpan()),
                WH40KCommandUiStyles.WarningBadge),
            WH40KBattlePhase.Apocalypse => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#3A2A2A".AsSpan()),
                WH40KCommandUiStyles.DangerBadge),
            _ => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#26314A".AsSpan()),
                WH40KCommandUiStyles.InfoBadge)
        };
    }
}
