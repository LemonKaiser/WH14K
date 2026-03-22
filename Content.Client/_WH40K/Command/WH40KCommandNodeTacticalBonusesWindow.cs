using System;
using System.Collections.Generic;
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

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeTacticalBonusesWindow : FancyWindow
{
    private static readonly Color ImperiumColor = Color.FromHex("#F3C548");
    private const string TacticalPresetTeamMapId = "WH40KCommandTacticalPresetTeamMap";
    private const string TacticalPresetDefaultProfileId = "WH40KCommandTacticalPresetProfileDefault";

    private readonly record struct TacticalPreset(
        string Id,
        string TitleKey,
        string DescriptionKey,
        int ChancePreparation,
        int ChanceAssault,
        int ChanceApocalypse);

    private readonly record struct TacticalPresetConfiguration(
        string ProfileId,
        int ForecastCount,
        int ActiveDurationMinSeconds,
        int ActiveDurationMaxSeconds,
        int QueueDurationMinSeconds,
        int QueueDurationMaxSeconds,
        int QueueEtaBaseSeconds,
        int QueueEtaStepSeconds,
        int QueueEtaJitterSeconds,
        int QueueChancePenaltyPerIndex,
        IReadOnlyList<TacticalPreset> Presets);

    private readonly record struct RandomBonusEntry(
        string PresetId,
        string TitleKey,
        string DescriptionKey,
        int RemainingSeconds,
        int DurationSeconds,
        int EtaSeconds,
        int ChancePercent);

    private static readonly TacticalPreset FallbackPreset = new(
        "logistics_surge",
        "wh40k-command-node-tactical-bonuses-random-logistics_surge-title",
        "wh40k-command-node-tactical-bonuses-random-logistics_surge-description",
        16,
        16,
        16);

    private readonly IPrototypeManager _prototype = IoCManager.Resolve<IPrototypeManager>();
    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _headerTitleLabel;
    private readonly Label _teamLine;
    private readonly Label _phaseLine;
    private readonly Label _summaryLine;
    private readonly PanelContainer _teamBadge;
    private readonly Label _teamBadgeLabel;
    private readonly PanelContainer _phaseBadge;
    private readonly Label _phaseBadgeLabel;
    private readonly BoxContainer _activeRows;
    private readonly BoxContainer _tierRows;
    private readonly BoxContainer _forecastRows;
    private readonly Label _forecastSummary;
    private readonly List<TacticalPreset> _presetPool = new();
    private Color _accent = ImperiumColor;
    private string _activeDoctrineId = string.Empty;
    private string _activeProfileId = string.Empty;
    private TacticalPresetConfiguration _presetConfiguration =
        BuildFallbackConfiguration(TacticalPresetDefaultProfileId);

    public WH40KCommandNodeTacticalBonusesWindow()
    {
        Title = Loc.GetString("wh40k-command-node-tactical-bonuses-window-title");
        MinSize = SetSize = new Vector2(920, 600);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
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

        var body = new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.PanelBackgroundAlt,
                WH40KCommandUiStyles.StrongBorder,
                2)
        };
        root.AddChild(body);

        var bodyRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            VerticalExpand = true
        };
        body.AddChild(bodyRoot);

        var topRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        bodyRoot.AddChild(topRow);

        var headerBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Margin = new Thickness(10, 8),
            VerticalAlignment = VAlignment.Center
        };
        header.AddChild(headerBox);

        var headerInfo = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true
        };
        headerBox.AddChild(headerInfo);

        _headerTitleLabel = new Label
        {
            Text = Loc.GetString("wh40k-command-node-tactical-bonuses-window-title"),
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
        _summaryLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_headerTitleLabel);
        headerInfo.AddChild(_teamLine);
        headerInfo.AddChild(_phaseLine);
        headerInfo.AddChild(_summaryLine);

        var badgeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 5,
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

        var activeSection = CreateSection(
            Loc.GetString("wh40k-command-node-tactical-bonuses-active-header"),
            out var activeContent,
            verticalExpand: true);
        activeSection.HorizontalExpand = true;
        activeSection.SizeFlagsStretchRatio = 1.1f;
        topRow.AddChild(activeSection);

        var activeScroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true
        };
        activeContent.AddChild(activeScroll);

        _activeRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            VerticalExpand = true
        };
        activeScroll.AddChild(_activeRows);

        var tiersSection = CreateSection(
            Loc.GetString("wh40k-command-node-tactical-bonuses-tiers-header"),
            out var tierContent,
            verticalExpand: true);
        tiersSection.MinWidth = 240;
        tiersSection.SizeFlagsStretchRatio = 0.85f;
        tiersSection.VerticalExpand = true;
        topRow.AddChild(tiersSection);

        var tiersScroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true
        };
        tierContent.AddChild(tiersScroll);

        _tierRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        tiersScroll.AddChild(_tierRows);

        var forecastSection = CreateSection(
            Loc.GetString("wh40k-command-node-tactical-bonuses-forecast-header"),
            out var forecastContent,
            verticalExpand: false);
        bodyRoot.AddChild(forecastSection);

        _forecastSummary = new Label
        {
            HorizontalExpand = true,
            ClipText = true,
            StyleClasses = { "LabelSubText" }
        };
        forecastContent.AddChild(_forecastSummary);
        forecastContent.AddChild(new HSeparator());

        var forecastScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SetHeight = 128f
        };
        forecastContent.AddChild(forecastScroll);

        _forecastRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
            HorizontalExpand = true
        };
        forecastScroll.AddChild(_forecastRows);
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state, string activeDoctrineId)
    {
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, ImperiumColor);
        _activeDoctrineId = activeDoctrineId;
        EnsurePresetProfileLoaded(state.TeamId);

        _headerStyle.BorderColor = _accent;
        _headerTitleLabel.ModulateSelfOverride = _accent;
        _teamLine.Text = CompactLine(Loc.GetString("wh40k-command-node-team", ("team", state.TeamName)));
        _phaseLine.Text = CompactLine(Loc.GetString("wh40k-command-node-phase",
            ("phase", Loc.GetString(GetPhaseKey(state.Phase)))));
        _teamBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#203227".AsSpan()), _accent);
        _teamBadgeLabel.Text = string.IsNullOrWhiteSpace(state.TeamName) ? "?" : state.TeamName.ToUpperInvariant();
        _phaseBadge.PanelOverride = ResolvePhaseBadgeStyle(state.Phase);
        _phaseBadgeLabel.Text = CompactLine(Loc.GetString(GetPhaseKey(state.Phase)));

        var doctrineName = string.IsNullOrWhiteSpace(_activeDoctrineId)
            ? Loc.GetString("wh40k-command-node-tactical-bonuses-doctrine-none-short")
            : WH40KCommandNodeDoctrineWindow.ResolveDoctrineDisplay(_activeDoctrineId, state.TeamId).Name;

        _summaryLine.Text = CompactLine(Loc.GetString("wh40k-command-node-tactical-bonuses-summary-line",
            ("node_tier", Math.Clamp(state.UpgradeLevel + 1, 1, 5)),
            ("development_points", state.CommandPoints),
            ("doctrine", doctrineName)));

        RandomBonusEntry activeRandomBonus;
        List<RandomBonusEntry> nextBonuses;
        var nextRollSeconds = 0;
        var useRuntimeSnapshot = state.TeamEventRuntime.HasProfile;
        if (useRuntimeSnapshot)
        {
            var runtimePreview = BuildRandomBonusRuntime(state.TeamEventRuntime);
            activeRandomBonus = runtimePreview.Active;
            nextBonuses = runtimePreview.Next;
            nextRollSeconds = runtimePreview.NextRollSeconds;
        }
        else
        {
            (activeRandomBonus, nextBonuses) = BuildRandomBonusPreview(state);
        }

        RebuildActiveRows(state, activeRandomBonus);
        RebuildTierRows(state);
        RebuildForecastRows(activeRandomBonus, nextBonuses, nextRollSeconds, useRuntimeSnapshot);
    }

    private PanelContainer CreateSection(string title, out BoxContainer content, bool verticalExpand)
    {
        var section = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.PanelBackground,
                WH40KCommandUiStyles.StrongBorder,
                2)
        };

        var sectionRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = verticalExpand
        };
        section.AddChild(sectionRoot);

        var titleBar = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateHeaderStripStyle(WH40KCommandUiStyles.MutedBorder)
        };
        sectionRoot.AddChild(titleBar);

        titleBar.AddChild(new Label
        {
            Text = title,
            StyleClasses = { "LabelHeading" },
            ClipText = true
        });

        content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            VerticalExpand = verticalExpand
        };
        sectionRoot.AddChild(content);

        return section;
    }

    private void RebuildActiveRows(
        WH40KCommandNodeBoundUserInterfaceState state,
        RandomBonusEntry activeRandomBonus)
    {
        _activeRows.RemoveAllChildren();
        var intel = state.BonusIntel;

        var phaseMultiplier = state.Phase switch
        {
            WH40KBattlePhase.Preparation => 1,
            WH40KBattlePhase.Assault => 2,
            WH40KBattlePhase.Apocalypse => 3,
            _ => 1
        };

        AddInfoCard(
            _activeRows,
            Loc.GetString("wh40k-command-node-tactical-bonuses-active-phase-title"),
            Loc.GetString("wh40k-command-node-tactical-bonuses-active-phase-value", ("multiplier", phaseMultiplier)),
            Loc.GetString(GetPhaseInfoKey(state.Phase)),
            emphasized: false);

        if (string.IsNullOrWhiteSpace(_activeDoctrineId))
        {
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-doctrine-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-doctrine-none-value"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-doctrine-none-detail"));
        }
        else
        {
            var doctrine = WH40KCommandNodeDoctrineWindow.ResolveDoctrineDisplay(_activeDoctrineId, state.TeamId);
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-doctrine-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-doctrine-value",
                    ("doctrine", doctrine.Name)),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-doctrine-detail",
                    ("positive", doctrine.Positive),
                    ("negative", doctrine.Negative),
                    ("lock", doctrine.LockText)));
        }

        var activeBonusName = Loc.GetString(activeRandomBonus.TitleKey);
        AddInfoCard(
            _activeRows,
            Loc.GetString("wh40k-command-node-tactical-bonuses-active-random-title"),
            Loc.GetString("wh40k-command-node-tactical-bonuses-active-random-value",
                ("bonus", activeBonusName),
                ("time_left", FormatDuration(activeRandomBonus.RemainingSeconds)),
                ("duration", FormatDuration(activeRandomBonus.DurationSeconds))),
            Loc.GetString(activeRandomBonus.DescriptionKey),
            emphasized: true);

        if (intel.HasEngineeringProfile)
        {
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-engineering-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-engineering-value",
                    ("tier", intel.EngineeringTier),
                    ("speed", intel.EngineeringSpeedBonusPercent)),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-engineering-detail",
                    ("min_seconds", FormatDecimal(intel.EngineeringMinProcessSeconds)),
                    ("storage_limit", FormatStorageLimit(intel.EngineeringMaterialStorageLimit)),
                    ("global_multiplier", FormatDecimal(intel.EngineeringGlobalTimeMultiplier)),
                    ("gain", intel.NodePassiveFrontPointsPerTick),
                    ("interval", FormatDecimal(intel.NodePassiveIntervalSeconds))));
        }
        else
        {
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-engineering-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-profile-unavailable"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-profile-unavailable-detail"));
        }

        if (intel.HasOreExtractorProfile)
        {
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-ore-extractor-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-ore-extractor-value",
                    ("tier", intel.OreExtractorTier),
                    ("interval", FormatDecimal(intel.OreExtractorSpawnIntervalSeconds)),
                    ("count", intel.OreExtractorSpawnCount)),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-ore-extractor-detail",
                    ("ores", intel.OreExtractorAllowedOreNames)));
        }
        else
        {
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-ore-extractor-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-profile-unavailable"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-profile-unavailable-detail"));
        }

        if (intel.HasLogisticsProfile)
        {
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-logistics-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-logistics-value",
                    ("tier", intel.LogisticsTier),
                    ("items", intel.LogisticsTierMaxItemsBonus),
                    ("minutes", intel.LogisticsTierDeliveryReductionMinutes)),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-logistics-detail",
                    ("speed", intel.LogisticsExternalDeliverySpeedBonusPercent),
                    ("cap", intel.LogisticsExternalMaxItemsBonusPercent),
                    ("discount", intel.LogisticsExternalPriceDiscountPercent)));
        }
        else
        {
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-logistics-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-profile-unavailable"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-profile-unavailable-detail"));
        }

        if (intel.HasSpecialLatheProfile)
        {
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-special-lathe-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-special-lathe-value",
                    ("tier", intel.SpecialLatheTier),
                    ("speed", intel.SpecialLatheSpeedBonusPercent),
                    ("seconds", FormatDecimal(intel.SpecialLatheProcessSeconds)),
                    ("storage", intel.SpecialLatheMaterialStorageLimit)),
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-special-lathe-detail",
                    ("output", intel.SpecialLatheOutputMultiplier)));
        }
        else
        {
            AddInfoCard(
                _activeRows,
                Loc.GetString("wh40k-command-node-tactical-bonuses-active-special-lathe-title"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-profile-unavailable"),
                Loc.GetString("wh40k-command-node-tactical-bonuses-profile-unavailable-detail"));
        }
    }

    private void RebuildTierRows(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _tierRows.RemoveAllChildren();
        var intel = state.BonusIntel;

        AddInfoCard(
            _tierRows,
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-engineering-title"),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-engineering-value",
                ("tier", intel.EngineeringTier),
                ("speed", intel.EngineeringSpeedBonusPercent)),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-engineering-detail",
                ("min_seconds", FormatDecimal(intel.EngineeringMinProcessSeconds)),
                ("storage_limit", FormatStorageLimit(intel.EngineeringMaterialStorageLimit)),
                ("global_multiplier", FormatDecimal(intel.EngineeringGlobalTimeMultiplier))));

        AddInfoCard(
            _tierRows,
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-logistics-title"),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-logistics-value",
                ("tier", intel.LogisticsTier),
                ("items", intel.LogisticsTierMaxItemsBonus),
                ("minutes", intel.LogisticsTierDeliveryReductionMinutes)),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-logistics-detail",
                ("speed", intel.LogisticsExternalDeliverySpeedBonusPercent),
                ("cap", intel.LogisticsExternalMaxItemsBonusPercent),
                ("discount", intel.LogisticsExternalPriceDiscountPercent)));

        AddInfoCard(
            _tierRows,
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-ore-extractor-title"),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-ore-extractor-value",
                ("tier", intel.OreExtractorTier),
                ("interval", FormatDecimal(intel.OreExtractorSpawnIntervalSeconds)),
                ("count", intel.OreExtractorSpawnCount)),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-ore-extractor-detail",
                ("ores", intel.OreExtractorAllowedOreNames)));

        AddInfoCard(
            _tierRows,
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-special-lathe-title"),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-special-lathe-value",
                ("tier", intel.SpecialLatheTier),
                ("speed", intel.SpecialLatheSpeedBonusPercent),
                ("seconds", FormatDecimal(intel.SpecialLatheProcessSeconds)),
                ("storage", intel.SpecialLatheMaterialStorageLimit)),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-special-lathe-detail",
                ("output", intel.SpecialLatheOutputMultiplier)));

        AddInfoCard(
            _tierRows,
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-node-title"),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-node-value",
                ("level", state.BaseLevel),
                ("node_tier", Math.Clamp(state.UpgradeLevel + 1, 1, 5))),
            Loc.GetString("wh40k-command-node-tactical-bonuses-tier-node-detail",
                ("gain", intel.NodePassiveFrontPointsPerTick),
                ("interval", FormatDecimal(intel.NodePassiveIntervalSeconds))));
    }

    private void RebuildForecastRows(
        RandomBonusEntry activeRandomBonus,
        List<RandomBonusEntry> nextBonuses,
        int nextRollSeconds,
        bool useRuntimeSnapshot)
    {
        _forecastRows.RemoveAllChildren();

        if (useRuntimeSnapshot)
        {
            _forecastSummary.Text = CompactLine(Loc.GetString(
                "wh40k-command-node-tactical-bonuses-runtime-forecast-summary",
                ("next_roll", FormatDuration(nextRollSeconds)),
                ("cooldowns", nextBonuses.Count)));
        }
        else
        {
            _forecastSummary.Text = CompactLine(Loc.GetString(
                "wh40k-command-node-tactical-bonuses-forecast-summary",
                ("time_left", FormatDuration(activeRandomBonus.RemainingSeconds)),
                ("visible", nextBonuses.Count)));
        }

        AddInfoCard(
            _forecastRows,
            Loc.GetString("wh40k-command-node-tactical-bonuses-forecast-current-title"),
            Loc.GetString("wh40k-command-node-tactical-bonuses-forecast-current-value",
                ("bonus", Loc.GetString(activeRandomBonus.TitleKey)),
                ("time_left", FormatDuration(activeRandomBonus.RemainingSeconds)),
                ("duration", FormatDuration(activeRandomBonus.DurationSeconds))),
            Loc.GetString(activeRandomBonus.DescriptionKey),
            emphasized: true);

        foreach (var bonus in nextBonuses)
        {
            var entryValue = bonus.ChancePercent > 0
                ? Loc.GetString("wh40k-command-node-tactical-bonuses-forecast-entry-value",
                    ("eta", FormatDuration(bonus.EtaSeconds)),
                    ("duration", FormatDuration(bonus.DurationSeconds)),
                    ("chance", bonus.ChancePercent))
                : Loc.GetString("wh40k-command-node-tactical-bonuses-forecast-entry-cooldown-value",
                    ("cooldown", FormatDuration(bonus.EtaSeconds)));

            AddInfoCard(
                _forecastRows,
                Loc.GetString(bonus.TitleKey),
                entryValue,
                Loc.GetString(bonus.DescriptionKey));
        }
    }

    private (RandomBonusEntry Active, List<RandomBonusEntry> Next, int NextRollSeconds) BuildRandomBonusRuntime(
        WH40KCommandTeamEventRuntimeState runtime)
    {
        RandomBonusEntry active;
        if (runtime.HasActiveEvent)
        {
            active = new RandomBonusEntry(
                runtime.ActiveEventId,
                runtime.ActiveEventTitle,
                runtime.ActiveEventDescription,
                runtime.ActiveRemainingSeconds,
                Math.Max(1, runtime.ActiveDurationSeconds),
                0,
                0);
        }
        else
        {
            active = new RandomBonusEntry(
                "no_active_runtime_event",
                "wh40k-command-node-tactical-bonuses-runtime-no-active-title",
                "wh40k-command-node-tactical-bonuses-runtime-no-active-detail",
                0,
                1,
                0,
                0);
        }

        var next = new List<RandomBonusEntry>(runtime.Cooldowns.Length);
        foreach (var cooldown in runtime.Cooldowns)
        {
            next.Add(new RandomBonusEntry(
                cooldown.EventId,
                cooldown.Title,
                cooldown.Description,
                0,
                0,
                Math.Max(0, cooldown.RemainingSeconds),
                0));
        }

        return (active, next, Math.Max(0, runtime.NextRollSeconds));
    }

    private void EnsurePresetProfileLoaded(string teamId)
    {
        var config = ResolvePresetConfiguration(teamId);
        if (_presetPool.Count > 0 &&
            string.Equals(_activeProfileId, config.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            _presetConfiguration = config;
            return;
        }

        _activeProfileId = config.ProfileId;
        _presetConfiguration = config;
        _presetPool.Clear();
        _presetPool.AddRange(config.Presets);
    }

    private TacticalPresetConfiguration ResolvePresetConfiguration(string teamId)
    {
        var profileId = ResolveProfileIdForTeam(teamId);
        if (!_prototype.TryIndex(profileId, out WH40KCommandTacticalPresetProfilePrototype? profile))
        {
            Logger.ErrorS("wh40k.command", $"Missing tactical-preset profile prototype '{profileId}'.");
            return BuildFallbackConfiguration(TacticalPresetDefaultProfileId);
        }

        var presets = new List<TacticalPreset>(profile.Presets.Count);
        foreach (var preset in profile.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id) ||
                string.IsNullOrWhiteSpace(preset.TitleKey) ||
                string.IsNullOrWhiteSpace(preset.DescriptionKey))
            {
                continue;
            }

            presets.Add(new TacticalPreset(
                preset.Id,
                preset.TitleKey,
                preset.DescriptionKey,
                preset.ChancePreparation,
                preset.ChanceAssault,
                preset.ChanceApocalypse));
        }

        if (presets.Count == 0)
        {
            Logger.ErrorS("wh40k.command", $"Tactical-preset profile '{profileId}' has no valid entries.");
            return BuildFallbackConfiguration(profileId);
        }

        return new TacticalPresetConfiguration(
            profile.ID,
            Math.Max(0, profile.ForecastCount),
            Math.Max(1, profile.ActiveDurationMinSeconds),
            Math.Max(profile.ActiveDurationMinSeconds, profile.ActiveDurationMaxSeconds),
            Math.Max(1, profile.QueueDurationMinSeconds),
            Math.Max(profile.QueueDurationMinSeconds, profile.QueueDurationMaxSeconds),
            Math.Max(1, profile.QueueEtaBaseSeconds),
            Math.Max(1, profile.QueueEtaStepSeconds),
            Math.Max(0, profile.QueueEtaJitterSeconds),
            Math.Max(0, profile.QueueChancePenaltyPerIndex),
            presets);
    }

    private string ResolveProfileIdForTeam(string teamId)
    {
        if (!_prototype.TryIndex(TacticalPresetTeamMapId, out WH40KCommandTacticalPresetTeamMapPrototype? teamMap))
            return TacticalPresetDefaultProfileId;

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

    private static TacticalPresetConfiguration BuildFallbackConfiguration(string profileId)
    {
        return new TacticalPresetConfiguration(
            profileId,
            ForecastCount: 3,
            ActiveDurationMinSeconds: 75,
            ActiveDurationMaxSeconds: 140,
            QueueDurationMinSeconds: 65,
            QueueDurationMaxSeconds: 140,
            QueueEtaBaseSeconds: 35,
            QueueEtaStepSeconds: 55,
            QueueEtaJitterSeconds: 20,
            QueueChancePenaltyPerIndex: 2,
            Presets: new[] { FallbackPreset });
    }

    private (RandomBonusEntry Active, List<RandomBonusEntry> Next) BuildRandomBonusPreview(
        WH40KCommandNodeBoundUserInterfaceState state)
    {
        if (_presetPool.Count == 0)
            _presetPool.Add(FallbackPreset);

        var seed = Math.Abs(HashCode.Combine(
            state.TeamId,
            state.Phase,
            state.BaseLevel,
            state.CommandPoints,
            state.UpgradeLevel,
            _activeDoctrineId));

        var phaseOffset = state.Phase switch
        {
            WH40KBattlePhase.Preparation => 0,
            WH40KBattlePhase.Assault => 1,
            WH40KBattlePhase.Apocalypse => 2,
            _ => 0
        };

        var doctrineOffset = string.IsNullOrWhiteSpace(_activeDoctrineId)
            ? 0
            : Math.Abs(_activeDoctrineId.GetHashCode(StringComparison.OrdinalIgnoreCase));
        var activeIndex = (seed + phaseOffset + doctrineOffset) % _presetPool.Count;
        var activePreset = _presetPool[activeIndex];
        var activeDuration = RollDuration(
            seed,
            _presetConfiguration.ActiveDurationMinSeconds,
            _presetConfiguration.ActiveDurationMaxSeconds,
            divisor: 5);
        var activeRemaining = 20 + Math.Abs(seed / 7) % Math.Max(10, activeDuration - 15);

        var active = new RandomBonusEntry(
            activePreset.Id,
            activePreset.TitleKey,
            activePreset.DescriptionKey,
            activeRemaining,
            activeDuration,
            0,
            ComputeChance(state.Phase, activePreset, queueIndex: 0));

        var maxNext = Math.Max(0, _presetPool.Count - 1);
        var nextCount = Math.Clamp(_presetConfiguration.ForecastCount, 0, maxNext);
        var next = new List<RandomBonusEntry>(nextCount);
        var usedIndices = new HashSet<int> { activeIndex };
        var cursor = activeIndex;

        for (var i = 0; i < nextCount; i++)
        {
            var candidate = -1;
            for (var step = 1; step <= _presetPool.Count; step++)
            {
                var index = (cursor + step + Math.Abs(seed / (i + 3)) % 2) % _presetPool.Count;
                if (!usedIndices.Contains(index))
                {
                    candidate = index;
                    break;
                }
            }

            if (candidate < 0)
                candidate = (cursor + 1) % _presetPool.Count;

            usedIndices.Add(candidate);
            cursor = candidate;

            var preset = _presetPool[candidate];
            var eta = activeRemaining +
                      _presetConfiguration.QueueEtaBaseSeconds +
                      i * _presetConfiguration.QueueEtaStepSeconds;
            if (_presetConfiguration.QueueEtaJitterSeconds > 0)
            {
                eta += Math.Abs(seed / (i + 9)) % (_presetConfiguration.QueueEtaJitterSeconds + 1);
            }

            var duration = RollDuration(
                seed,
                _presetConfiguration.QueueDurationMinSeconds,
                _presetConfiguration.QueueDurationMaxSeconds,
                divisor: i + 7);

            next.Add(new RandomBonusEntry(
                preset.Id,
                preset.TitleKey,
                preset.DescriptionKey,
                0,
                duration,
                eta,
                ComputeChance(state.Phase, preset, i + 1)));
        }

        return (active, next);
    }

    private int ComputeChance(WH40KBattlePhase phase, TacticalPreset preset, int queueIndex)
    {
        var baseChance = phase switch
        {
            WH40KBattlePhase.Preparation => preset.ChancePreparation,
            WH40KBattlePhase.Assault => preset.ChanceAssault,
            WH40KBattlePhase.Apocalypse => preset.ChanceApocalypse,
            _ => preset.ChancePreparation
        };

        var chance = baseChance - queueIndex * _presetConfiguration.QueueChancePenaltyPerIndex;
        return Math.Clamp(chance, 1, 95);
    }

    private static int RollDuration(int seed, int minSeconds, int maxSeconds, int divisor)
    {
        var safeMin = Math.Max(1, minSeconds);
        var safeMax = Math.Max(safeMin, maxSeconds);
        if (safeMin == safeMax)
            return safeMin;

        var spread = safeMax - safeMin + 1;
        var offset = Math.Abs(seed / Math.Max(1, divisor)) % spread;
        return safeMin + offset;
    }

    private void AddInfoCard(
        BoxContainer target,
        string title,
        string value,
        string description,
        bool emphasized = false)
    {
        var card = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                emphasized ? WH40KCommandUiStyles.CardBackground : WH40KCommandUiStyles.CardBackgroundAlt,
                emphasized ? _accent : WH40KCommandUiStyles.MutedBorder)
        };
        target.AddChild(card);

        var cardBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4
        };
        card.AddChild(cardBox);

        cardBox.AddChild(new Label
        {
            Text = title,
            StyleClasses = { emphasized ? "LabelBig" : "LabelHeading" },
            ModulateSelfOverride = emphasized ? _accent : Color.White,
            ClipText = true
        });

        var valueLabel = new Label
        {
            HorizontalExpand = true,
            ClipText = true,
            StyleClasses = { "LabelBig" },
            ModulateSelfOverride = _accent
        };
        valueLabel.Text = CompactLine(value);
        cardBox.AddChild(valueLabel);

        var descriptionLabel = new Label
        {
            HorizontalExpand = true,
            ClipText = true,
            StyleClasses = { "LabelSubText" }
        };
        descriptionLabel.Text = CompactLine(description);
        cardBox.AddChild(descriptionLabel);
    }

    private static string CompactLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var compact = text
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        var line = string.Join(' ', compact.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= 180 ? line : line[..177] + "...";
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

    private static string FormatDecimal(float value)
    {
        return value.ToString("0.##");
    }

    private static string FormatStorageLimit(int limit)
    {
        return limit > 0
            ? limit.ToString()
            : Loc.GetString("wh40k-command-node-tactical-bonuses-storage-unlimited");
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

    private static string GetPhaseInfoKey(WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Preparation => "wh40k-command-node-tactical-bonuses-active-phase-detail-preparation",
            WH40KBattlePhase.Assault => "wh40k-command-node-tactical-bonuses-active-phase-detail-assault",
            WH40KBattlePhase.Apocalypse => "wh40k-command-node-tactical-bonuses-active-phase-detail-apocalypse",
            _ => "wh40k-command-node-tactical-bonuses-active-phase-detail-preparation"
        };
    }

    private static string FormatDuration(int seconds)
    {
        var clamped = Math.Max(0, seconds);
        var minutes = clamped / 60;
        var secs = clamped % 60;
        return $"{minutes:00}:{secs:00}";
    }
}
