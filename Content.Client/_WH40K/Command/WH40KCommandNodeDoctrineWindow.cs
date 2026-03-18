using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
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
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeDoctrineWindow : FancyWindow
{
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
        "wh40k-command-node-doctrine-adaptive-reserve-name-imperium",
        "wh40k-command-node-doctrine-adaptive-reserve-name-heretics",
        "wh40k-command-node-doctrine-adaptive-reserve-brief-focus",
        "wh40k-command-node-doctrine-adaptive-reserve-brief-effect",
        "wh40k-command-node-doctrine-adaptive-reserve-debuff",
        "wh40k-command-node-doctrine-adaptive-reserve-summary",
        "wh40k-command-node-doctrine-adaptive-reserve-positive",
        "wh40k-command-node-doctrine-adaptive-reserve-negative",
        "wh40k-command-node-doctrine-adaptive-reserve-lock",
        "wh40k-command-node-doctrine-adaptive-reserve-full-briefing",
        "wh40k-command-node-doctrine-adaptive-reserve-theme-imperium",
        "wh40k-command-node-doctrine-adaptive-reserve-theme-heretics",
        string.Empty,
        true);

    public event Action<string>? OnDoctrineAssigned;

    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _teamLine;
    private readonly Label _phaseLine;
    private readonly Label _availabilityLine;
    private readonly Label _activeDoctrineLine;
    private readonly BoxContainer _cardsRow;
    private readonly Dictionary<string, StyleBoxFlat> _rowStyles = new();
    private readonly Dictionary<string, Label> _rowTitleLabels = new();
    private readonly Dictionary<string, RichTextLabel> _rowFocusLabels = new();
    private readonly Dictionary<string, RichTextLabel> _rowEffectLabels = new();
    private readonly Dictionary<string, RichTextLabel> _rowLockLabels = new();
    private readonly Dictionary<string, RichTextLabel> _rowDebuffLabels = new();
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

    public WH40KCommandNodeDoctrineWindow()
    {
        Title = Loc.GetString("wh40k-command-node-doctrine-window-title");
        MinSize = new Vector2(980, 560);
        SetSize = new Vector2(1000, 580);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6
        };
        ContentsContainer.AddChild(root);

        var header = new PanelContainer
        {
            PanelOverride = _headerStyle = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#2B3246"),
                BorderColor = ImperiumColor,
                BorderThickness = new Thickness(1)
            }
        };
        root.AddChild(header);

        var headerBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            Margin = new Thickness(8)
        };
        header.AddChild(headerBox);

        _teamLine = new Label();
        _phaseLine = new Label();
        _availabilityLine = new Label();
        _activeDoctrineLine = new Label();
        headerBox.AddChild(_teamLine);
        headerBox.AddChild(_phaseLine);
        headerBox.AddChild(_availabilityLine);
        headerBox.AddChild(_activeDoctrineLine);

        var cardsSection = new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1F2433"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        root.AddChild(cardsSection);

        var cardsSectionBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(6),
            VerticalExpand = true
        };
        cardsSection.AddChild(cardsSectionBox);
        cardsSectionBox.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-doctrine-window-list-header")
        });

        var cardsScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SetHeight = 360f
        };
        cardsSectionBox.AddChild(cardsScroll);

        _cardsRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true
        };
        cardsScroll.AddChild(_cardsRow);
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
        _teamId = state.TeamId;
        _baseLevel = Math.Max(1, state.BaseLevel);
        _doctrineLocked = doctrineLocked;
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, ImperiumColor);
        EnsureProfileLoaded(_teamId);

        _headerStyle.BorderColor = _accent;
        _teamLine.Text = Loc.GetString("wh40k-command-node-team", ("team", state.TeamName));
        _phaseLine.Text = Loc.GetString("wh40k-command-node-phase",
            ("phase", Loc.GetString(GetPhaseKey(state.Phase))));

        _activeDoctrineId = string.IsNullOrWhiteSpace(activeDoctrineId)
            ? string.Empty
            : FindLoadedPreset(activeDoctrineId).Id;

        if (!string.IsNullOrWhiteSpace(_activeDoctrineId))
            _selectedDoctrineId = _activeDoctrineId;
        else if (string.IsNullOrWhiteSpace(_selectedDoctrineId) || !_rowStyles.ContainsKey(_selectedDoctrineId))
            _selectedDoctrineId = _defaultDoctrineId;

        _availabilityLine.Text = _doctrineLocked
            ? Loc.GetString("wh40k-command-node-doctrine-window-state-locked")
            : _baseLevel < _doctrineUnlockLevel
                ? Loc.GetString("wh40k-command-node-doctrine-window-state-wait-level",
                    ("level", _doctrineUnlockLevel))
                : Loc.GetString("wh40k-command-node-doctrine-window-state-open");

        _activeDoctrineLine.Text = string.IsNullOrWhiteSpace(_activeDoctrineId)
            ? Loc.GetString("wh40k-command-node-doctrine-window-active-none")
            : Loc.GetString("wh40k-command-node-doctrine-window-active-set",
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
        var rowStyle = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#232A3B"),
            BorderColor = Color.FromHex("#59617B"),
            BorderThickness = new Thickness(1)
        };

        var card = new PanelContainer
        {
            MinWidth = 292,
            MinHeight = 360,
            VerticalAlignment = VAlignment.Top,
            PanelOverride = rowStyle
        };
        _cardsRow.AddChild(card);

        var cardBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(6)
        };
        card.AddChild(cardBox);

            var title = new Label
            {
                Text = UsesHereticsDoctrinePresentation(_teamId)
                    ? Loc.GetString(preset.NameHereticsKey)
                    : Loc.GetString(preset.NameImperiumKey)
            };
        cardBox.AddChild(title);
        cardBox.AddChild(new HSeparator());

        var detailStack = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4
        };
        cardBox.AddChild(detailStack);

        var focusLabel = AddCardSection(
            detailStack,
            Loc.GetString("wh40k-command-node-doctrine-window-card-focus-header"),
            Color.FromHex("#D0D8EF"));
        var effectLabel = AddCardSection(
            detailStack,
            Loc.GetString("wh40k-command-node-doctrine-window-card-effect-header"),
            Color.FromHex("#9BC4FF"));
        var lockLabel = AddCardSection(
            detailStack,
            Loc.GetString("wh40k-command-node-doctrine-window-card-lock-header"),
            Color.FromHex("#E7BFAF"));
        var debuffLabel = AddCardSection(
            detailStack,
            Loc.GetString("wh40k-command-node-doctrine-window-card-debuff-header"),
            Color.FromHex("#E19797"));

        var selectButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-doctrine-window-select-button")
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
            ? Loc.GetString("wh40k-command-node-doctrine-window-state-locked")
            : _baseLevel < _doctrineUnlockLevel
                ? Loc.GetString("wh40k-command-node-doctrine-window-state-wait-level",
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
                ? Color.FromHex("#2A344D")
                : isActive
                    ? Color.FromHex("#283043")
                    : Color.FromHex("#232A3B");
            rowStyle.BorderColor = isSelected
                ? _accent
                : isActive
                    ? ActiveColor
                    : Color.FromHex("#59617B");

            title.ModulateSelfOverride = isSelected ? _accent : Color.White;

            button.Text = isActive
                ? Loc.GetString("wh40k-command-node-doctrine-window-row-active-button")
                : Loc.GetString("wh40k-command-node-doctrine-window-select-button");
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
            SetWrappedText(_rowFocusLabels[preset.Id], display.BriefFocus, _accent);
            SetWrappedText(_rowEffectLabels[preset.Id], display.BriefEffect, Color.FromHex("#D9E6FF"));
            SetWrappedText(_rowLockLabels[preset.Id], display.LockText, Color.FromHex("#E7BFAF"));
            SetWrappedText(_rowDebuffLabels[preset.Id], display.DebuffText, Color.FromHex("#E19797"));
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
            Logger.ErrorS("wh40k.command", $"Missing command-doctrine profile prototype '{profileId}'.");
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
            Logger.ErrorS("wh40k.command", $"Doctrine profile '{profileId}' has no valid doctrine entries.");
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
            Title = Loc.GetString("wh40k-command-node-doctrine-detail-window-title", ("doctrine", doctrine.Name));
            MinSize = SetSize = new Vector2(760, 620);

            var root = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 8
            };
            ContentsContainer.AddChild(root);

            var summaryPanel = new PanelContainer
            {
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#232A3B"),
                    BorderColor = accent,
                    BorderThickness = new Thickness(1)
                }
            };
            root.AddChild(summaryPanel);

            var summaryBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 3,
                Margin = new Thickness(8)
            };
            summaryPanel.AddChild(summaryBox);

            summaryBox.AddChild(new Label
            {
                Text = doctrine.Name,
                ModulateSelfOverride = accent
            });

            var summaryText = new RichTextLabel();
            SetWrappedText(summaryText, doctrine.Summary);
            summaryBox.AddChild(summaryText);

            var sectionsScroll = new ScrollContainer
            {
                VerticalExpand = true
            };
            root.AddChild(sectionsScroll);

            var sections = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 6
            };
            sectionsScroll.AddChild(sections);

            AddSection(
                sections,
                Loc.GetString("wh40k-command-node-doctrine-window-detail-briefing-header"),
                doctrine.FullBriefing,
                Color.White);
            AddSection(
                sections,
                Loc.GetString("wh40k-command-node-doctrine-window-positive-header"),
                doctrine.Positive,
                accent);
            AddSection(
                sections,
                Loc.GetString("wh40k-command-node-doctrine-window-negative-header"),
                doctrine.Negative,
                Color.FromHex("#E2AF9D"));
            AddSection(
                sections,
                Loc.GetString("wh40k-command-node-doctrine-window-card-debuff-header"),
                doctrine.DebuffText,
                Color.FromHex("#E19797"));
            AddSection(
                sections,
                Loc.GetString("wh40k-command-node-doctrine-window-lock-header"),
                doctrine.LockText,
                Color.White);
            AddSection(
                sections,
                Loc.GetString("wh40k-command-node-doctrine-window-theme-header"),
                doctrine.ThemeText,
                Color.FromHex("#AAB3CC"));

            if (!string.IsNullOrWhiteSpace(blockReason))
            {
                var blocker = new RichTextLabel();
                SetWrappedText(blocker, blockReason, Color.FromHex("#E7BFAF"));
                root.AddChild(blocker);
            }

            var actions = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 8
            };
            root.AddChild(actions);

            var backButton = new Button
            {
                HorizontalExpand = true,
                Text = Loc.GetString("wh40k-command-node-doctrine-window-detail-close-button")
            };
            backButton.OnPressed += _ => Close();
            actions.AddChild(backButton);

            var confirmButton = new Button
            {
                HorizontalExpand = true,
                Disabled = !canAssign,
                Text = isActive
                    ? Loc.GetString("wh40k-command-node-doctrine-window-row-active-button")
                    : Loc.GetString("wh40k-command-node-doctrine-window-assign-button")
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
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#202636"),
                    BorderColor = Color.FromHex("#59617B"),
                    BorderThickness = new Thickness(1)
                }
            };
            parent.AddChild(panel);

            var box = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 3,
                Margin = new Thickness(6)
            };
            panel.AddChild(box);

            box.AddChild(new Label
            {
                Text = header
            });

            var text = new RichTextLabel();
            SetWrappedText(text, body, bodyColor);
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

    private static void SetWrappedText(RichTextLabel label, string text, Color? color = null)
    {
        var normalized = text.Replace("\\n", "\n", StringComparison.Ordinal);
        label.SetMessage(
            FormattedMessage.FromMarkupPermissive(FormattedMessage.EscapeText(normalized)),
            tagsAllowed: null,
            defaultColor: color ?? Color.White);
    }

    private static RichTextLabel AddCardSection(BoxContainer parent, string header, Color headerColor)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#262E42"),
                BorderColor = Color.FromHex("#55607A"),
                BorderThickness = new Thickness(1)
            }
        };
        parent.AddChild(panel);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            Margin = new Thickness(6, 4)
        };
        panel.AddChild(box);

        box.AddChild(new Label
        {
            Text = header,
            ModulateSelfOverride = headerColor
        });
        box.AddChild(new HSeparator());

        var body = new RichTextLabel();
        box.AddChild(body);
        return body;
    }
}
