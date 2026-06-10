using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server._WH40K.Cargo.Components;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.Command.Pinpointer;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.OreExtractor.Components;
using Content.Server._WH40K.Research.Components;
using Content.Server._WH40K.Stats;
using Content.Server._WH40K.StrategicPoints;
using Content.Server._WH40K.Store.Components;
using Content.Server.GameTicking.Events;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Ghost.Roles.Raffles;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Research.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Clothing;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.Notifications;
using Content.Shared.Ghost.Roles.Raffles;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Ghost;
using Content.Shared.GameTicking;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mining;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Cargo.Components;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Command;

public sealed partial class WH40KCommandNodeSystem : EntitySystem
{
    private const string TeamIdentityMapId = "WH40KTeamIdentityMap";
    private const string TeamIdentityDefaultProfileId = "WH40KTeamIdentityProfileImperium";
    private const string ReinforcementTeamMapId = "WH40KCommandReinforcementTeamMap";
    private const string ReinforcementDefaultProfileId = "WH40KCommandReinforcementProfileImperium";
    private const string TeamCompositionTeamMapId = "WH40KCommandTeamCompositionTeamMap";
    private const string TeamCompositionDefaultProfileId = "WH40KCommandTeamCompositionProfileImperium";
    private const string OreExtractorIntelTeamMapId = "WH40KCommandOreExtractorIntelTeamMap";
    private const string OreExtractorIntelDefaultProfileId = "WH40KCommandOreExtractorIntelProfileDefault";
    private const string CommandTreeTeamMapId = "WH40KCommandTreeTeamMap";
    private const string CommandTreeDefaultProfileId = "WH40KCommandTreeProfileDefault";
    private const int MissionBoardOfferCount = 3;
    private const uint ReinforcementRaffleDurationSeconds = 15;
    private static readonly TimeSpan UiRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly record struct TeamMemberInfo(string RoleId, string RoleName, string Name);
    private readonly record struct StaffingRolePlan(string RoleId, int Target);
    private readonly record struct TeamCompositionData(
        string Summary,
        string[] LegacyLines,
        string[] StaffingLines,
        WH40KTeamCompositionStaffingData StaffingData,
        WH40KTeamCompositionRoleEntry[] OfficerRoles,
        WH40KTeamCompositionRoleEntry[] CoreRoles,
        WH40KTeamCompositionRoleEntry[] MechanicusRoles,
        WH40KTeamCompositionMemberEntry[] Members);

    private readonly List<EntityCoordinates> _reinforcementSpawnPoints = new();
    private readonly List<EntityCoordinates> _reinforcementMapSpawnPoints = new();

    [Dependency] private  WH40KTeamRuleFacadeSystem _teamRule = default!;
    [Dependency] private  WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private  UserInterfaceSystem _ui = default!;
    [Dependency] private  IPrototypeManager _proto = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  IPlayerManager _players = default!;
    [Dependency] private  MindSystem _mind = default!;
    [Dependency] private  SharedJobSystem _jobs = default!;
    [Dependency] private  MobStateSystem _mobState = default!;
    [Dependency] private  WH40KCommandEventMissionRuntimeSystem _runtime = default!;
    [Dependency] private  WH40KMissionPinpointerSystem _missionPinpointer = default!;
    [Dependency] private  ResearchSystem _research = default!;
    [Dependency] private  CargoSystem _cargo = default!;
    [Dependency] private  StationSystem _stations = default!;
    [Dependency] private  StationSpawningSystem _stationSpawning = default!;
    [Dependency] private  WH40KCommandTreeBonusSystem _treeBonuses = default!;
    [Dependency] private  WH40KPlayerStatsSystem _stats = default!;
    [Dependency] private  WH40KStrategicPointSystem _strategicPoints = default!;
    [Dependency] private  IRobustRandom _random = default!;
    [Dependency] private  WH40KTeamNpcFactionSystem _teamNpcFactions = default!;
    [Dependency] private  WH40KReinforcementAiSystem _reinforcementAi = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KCommandNodeComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WH40KCommandNodeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<IsRoleAllowedEvent>(OnIsRoleAllowed);
        SubscribeLocalEvent<WH40KReinforcementGhostRoleOneShotComponent, MindAddedMessage>(OnReinforcementMindAdded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        SubscribeLocalEvent<WH40KCommandNodeComponent, BoundUIOpenedEvent>(OnAnyUiOpened);

        Subs.BuiEvents<WH40KCommandNodeComponent>(WH40KCommandNodeUiKey.Key, subs =>
        {
            subs.Event<WH40KCommandNodeUpgradePressedMessage>(OnUpgradePressed);
            subs.Event<WH40KCommandNodeTeamCompositionPressedMessage>(OnTeamCompositionPressed);
        });

        InitializeReinforcementUi();

        Subs.BuiEvents<WH40KCommandNodeComponent>(WH40KCommandNodeUiKey.UpgradeTree, subs =>
        {
            subs.Event<WH40KCommandNodePurchaseTreeNodeMessage>(OnTreeNodePurchaseRequested);
        });

        Subs.BuiEvents<WH40KCommandNodeComponent>(WH40KCommandNodeUiKey.MissionBoard, subs =>
        {
            subs.Event<WH40KCommandNodeAssignMissionTaskMessage>(OnMissionTaskAssigned);
            subs.Event<WH40KCommandNodeSyncMissionPinpointerMessage>(OnMissionPinpointerSyncRequested);
        });
    }

    private void OnMapInit(EntityUid uid, WH40KCommandNodeComponent component, MapInitEvent args)
    {
        ResetCommandNodeRoundState(component);
        var interval = TimeSpan.FromSeconds(GetPassiveIntervalSeconds(component));
        component.NextPassivePointTick = _timing.CurTime + interval;
        RefreshTeamCargoLogisticsBonuses(component.TeamId);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        ResetReinforcementRuntime();

        var query = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out var uid, out var node))
        {
            ResetCommandNodeRoundState(node);
            _ui.CloseUis(uid);
        }
    }

    private static void ResetCommandNodeRoundState(WH40KCommandNodeComponent component)
    {
        component.UpgradeLevel = 0;
        component.PurchasedTreeNodeIds.Clear();
        component.ActiveMissionTaskId = string.Empty;
        component.MissionBoardOfferedTaskIds.Clear();
        component.MissionBoardHadActiveFactionMission = false;
        component.NextReinforcementAvailable = TimeSpan.Zero;
        component.NextPassivePointTick = TimeSpan.Zero;
        component.NextUiRefresh = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        UpdateReinforcementRuntime();
        var query = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out var uid, out var node))
        {
            var passiveInterval = TimeSpan.FromSeconds(GetPassiveIntervalSeconds(node));
            if (node.NextPassivePointTick == TimeSpan.Zero)
                node.NextPassivePointTick = now + passiveInterval;

            if (node.PassiveFrontPointsPerInterval > 0)
            {
                while (node.NextPassivePointTick <= now)
                {
                    if (!string.IsNullOrWhiteSpace(node.TeamId))
                        GrantPassiveFallbackIncome(uid, node);

                    passiveInterval = TimeSpan.FromSeconds(GetPassiveIntervalSeconds(node));
                    node.NextPassivePointTick += passiveInterval;
                }
            }
            else
            {
                node.NextPassivePointTick = now + passiveInterval;
            }

            if (!_ui.IsUiOpen(uid, WH40KCommandNodeUiKey.Key)
                && !_ui.IsUiOpen(uid, WH40KCommandNodeUiKey.Reinforcement)
                && !_ui.IsUiOpen(uid, WH40KCommandNodeUiKey.UpgradeTree)
                && !_ui.IsUiOpen(uid, WH40KCommandNodeUiKey.MissionBoard))
                continue;

            if (node.NextUiRefresh > now)
                continue;

            node.NextUiRefresh = now + UiRefreshInterval;
            UpdateUi((uid, node));
        }
    }

    private void OnAnyUiOpened(Entity<WH40KCommandNodeComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            _ui.CloseUi(ent.Owner, args.UiKey, args.Actor);
            return;
        }

        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        UpdateUi(ent);
    }

    private void OnGetVerbs(Entity<WH40KCommandNodeComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        if (!IsUserAllowedForTeam(user, ent.Comp.TeamId))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("w40k-cmd-open-verb"),
            Priority = 2,
            Act = () =>
            {
                if (_ui.TryOpenUi(ent.Owner, WH40KCommandNodeUiKey.Key, user))
                    UpdateUi(ent);
            }
        });
    }

    private void UpdateUi(Entity<WH40KCommandNodeComponent> ent)
    {
        if (!_teamRule.TryGetTeamEconomySnapshot(ent.Owner, ent.Comp.TeamId, out var economy))
            return;

        var teamName = ent.Comp.TeamId;
        if (_teamRule.TryGetTeamDisplayName(ent.Comp.TeamId, out var localizedTeamName))
            teamName = localizedTeamName;

        var thresholds = Array.Empty<int>();
        if (_teamRule.TryGetBaseLevelThresholds(out var ruleThresholds))
            thresholds = ruleThresholds.ToArray();

        var unlockLines = Array.Empty<string>();
        var composition = BuildTeamComposition(ent.Comp.TeamId);
        var bonusIntel = BuildBonusIntel(ent.Comp.TeamId, ent.Comp);
        var teamEventRuntime = _runtime.BuildTeamEventRuntimeState(ent.Comp.TeamId);
        var globalMissionRuntime = _runtime.BuildGlobalMissionRuntimeState();
        var teamMissionRuntime = _runtime.BuildTeamMissionRuntimeState(ent.Comp.TeamId);
        var currentPhase = _teamRule.GetCurrentPhase();
        var missionBoard = BuildMissionBoardState(ent.Comp, globalMissionRuntime, teamMissionRuntime);
        var reinforcementUiState = BuildReinforcementUiState(ent.Owner, ent.Comp.TeamId, teamName);
        var upgradeBaseCost = GetUpgradeCost(ent.Comp);
        var minimumReinforcementInfluenceCost = GetMinimumReinforcementCost(ent.Comp.TeamId, ent.Comp.ReinforcementCost);
        var (teamXpIncomePerSecond, influenceIncomePerSecond, fundsIncomePerSecond, researchIncomePerSecond) =
            GetIncomeRates(ent.Comp);

        var state = new WH40KCommandNodeBoundUserInterfaceState(
            ent.Comp.TeamId,
            teamName,
            currentPhase,
            economy.BaseLevel,
            economy.TeamXp,
            economy.Influence,
            economy.Influence,
            economy.Funds,
            economy.ResearchPoints,
            teamXpIncomePerSecond,
            influenceIncomePerSecond,
            fundsIncomePerSecond,
            researchIncomePerSecond,
            ent.Comp.UpgradeLevel,
            upgradeBaseCost,
            WH40KCommandEconomyCalculator.GetCommandNodeUpgradeFundsCost(upgradeBaseCost),
            WH40KCommandEconomyCalculator.GetCommandNodeUpgradeResearchCost(upgradeBaseCost),
            minimumReinforcementInfluenceCost,
            WH40KCommandEconomyCalculator.GetReinforcementFundsCost(minimumReinforcementInfluenceCost),
            minimumReinforcementInfluenceCost,
            GetRemainingReinforcementCooldown(ent.Comp.TeamId),
            _teamRule.GetRoundElapsedSeconds(),
            economy.PointsToNextLevel,
            thresholds,
            Array.Empty<WH40KCommandNodeReinforcementOptionState>(),
            unlockLines,
            ent.Comp.PurchasedTreeNodeIds.ToArray(),
            composition.Summary,
            composition.LegacyLines,
            composition.StaffingLines,
            composition.OfficerRoles,
            composition.CoreRoles,
            composition.MechanicusRoles,
            composition.Members,
            composition.StaffingData,
            bonusIntel,
            teamEventRuntime,
            globalMissionRuntime,
            teamMissionRuntime,
            missionBoard);

        _ui.SetUiState(ent.Owner, WH40KCommandNodeUiKey.Key, state);
        _ui.SetUiState(ent.Owner, WH40KCommandNodeUiKey.Reinforcement, reinforcementUiState);
        _ui.SetUiState(ent.Owner, WH40KCommandNodeUiKey.UpgradeTree, state);
        _ui.SetUiState(ent.Owner, WH40KCommandNodeUiKey.MissionBoard, state);
    }

    private (float TeamXpPerSecond, float InfluencePerSecond, float FundsPerSecond, float ResearchPerSecond)
        GetIncomeRates(WH40KCommandNodeComponent component)
    {
        var teamXpPerSecond = 0f;
        var influencePerSecond = 0f;
        var fundsPerSecond = 0f;
        var researchPerSecond = 0f;

        if (_strategicPoints.TryGetTeamIncomeRates(
                component.TeamId,
                out var strategicTeamXp,
                out var strategicInfluence,
                out var strategicResearch,
                out var strategicFunds))
        {
            teamXpPerSecond += strategicTeamXp;
            influencePerSecond += strategicInfluence;
            researchPerSecond += strategicResearch;
            fundsPerSecond += strategicFunds;
        }

        var passiveIntervalSeconds = GetPassiveIntervalSeconds(component);
        if (passiveIntervalSeconds > 0f)
        {
            var passiveFrontPoints = GetPassiveFrontPointGain(component);
            teamXpPerSecond += passiveFrontPoints / passiveIntervalSeconds;
            influencePerSecond += passiveFrontPoints / passiveIntervalSeconds;
            fundsPerSecond += WH40KCommandEconomyCalculator.GetPassiveFallbackFundsReward(passiveFrontPoints) /
                              passiveIntervalSeconds;
        }

        return (teamXpPerSecond, influencePerSecond, fundsPerSecond, researchPerSecond);
    }

    private void OnTreeNodePurchaseRequested(
        Entity<WH40KCommandNodeComponent> ent,
        ref WH40KCommandNodePurchaseTreeNodeMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        if (!TryResolveTreeNodeForTeam(ent.Comp.TeamId, args.NodeId, out _, out var nodeConfig))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-tree-node-missing"), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        if (nodeConfig.Cost <= 0)
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-tree-node-inactive"), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        var canonicalNodeId = nodeConfig.Id;
        if (ContainsNodeId(ent.Comp.PurchasedTreeNodeIds, canonicalNodeId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-tree-node-already-purchased"), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        foreach (var parentId in nodeConfig.Parents)
        {
            if (ContainsNodeId(ent.Comp.PurchasedTreeNodeIds, parentId))
                continue;

            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-tree-node-parent-locked"), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        var requiredLevel = Math.Max(1, nodeConfig.MinBaseLevel);
        var currentLevel = 1;
        if (_teamRule.TryGetTeamProgress(ent.Comp.TeamId, out var teamLevel, out _, out _))
            currentLevel = Math.Max(1, teamLevel);

        if (currentLevel < requiredLevel)
        {
            _popup.PopupEntity(
                Loc.GetString(
                    "w40k-cmd-tree-node-level-locked",
                    ("level", requiredLevel)),
                ent.Owner,
                args.Actor);
            UpdateUi(ent);
            return;
        }

        var baseCost = Math.Max(1, nodeConfig.Cost);
        var fundsCost = WH40KCommandEconomyCalculator.GetCommandTreeFundsCost(baseCost);
        var researchCost = WH40KCommandEconomyCalculator.GetCommandTreeResearchCost(baseCost);
        if (!TrySpendTeamFundsAndResearch(ent.Owner, ent.Comp.TeamId, fundsCost, researchCost, "tree-node"))
        {
            _popup.PopupEntity(
                Loc.GetString(
                    "w40k-cmd-tree-node-denied",
                    ("funds", fundsCost),
                    ("research", researchCost)),
                ent.Owner,
                args.Actor);
            UpdateUi(ent);
            return;
        }

        ent.Comp.PurchasedTreeNodeIds.Add(canonicalNodeId);
        ApplyTreeNodeUnlocks(ent.Comp.TeamId, nodeConfig);
        RecordEconomySpendStats(
            args.Actor,
            ent.Comp.TeamId,
            WH40KPlayerStatKeys.EconomyCommandTreePurchaseCount,
            WH40KPlayerStatKeys.EconomyCommandTreePurchaseCost,
            fundsCost + researchCost,
            "tree-node",
            nodeConfig.Id);

        _popup.PopupEntity(
            Loc.GetString(
                "w40k-cmd-tree-node-purchased",
                ("node", Loc.GetString(nodeConfig.TitleKey)),
                ("funds", fundsCost),
                ("research", researchCost)),
            ent.Owner,
            args.Actor);
        UpdateUi(ent);
    }

    private void OnUpgradePressed(Entity<WH40KCommandNodeComponent> ent, ref WH40KCommandNodeUpgradePressedMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        if (ent.Comp.UpgradeLevel >= Math.Max(0, ent.Comp.UpgradeMaxLevel))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-upgrade-max"), ent.Owner, args.Actor);
            return;
        }

        var cost = GetUpgradeCost(ent.Comp);
        var fundsCost = WH40KCommandEconomyCalculator.GetCommandNodeUpgradeFundsCost(cost);
        var researchCost = WH40KCommandEconomyCalculator.GetCommandNodeUpgradeResearchCost(cost);
        if (!TrySpendTeamFundsAndResearch(ent.Owner, ent.Comp.TeamId, fundsCost, researchCost, "command-upgrade"))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-upgrade-denied"), ent.Owner, args.Actor);
            return;
        }

        ent.Comp.UpgradeLevel++;
        var nextTick = _timing.CurTime + TimeSpan.FromSeconds(GetPassiveIntervalSeconds(ent.Comp));
        if (ent.Comp.NextPassivePointTick == TimeSpan.Zero || ent.Comp.NextPassivePointTick > nextTick)
            ent.Comp.NextPassivePointTick = nextTick;

        RecordEconomySpendStats(
            args.Actor,
            ent.Comp.TeamId,
            WH40KPlayerStatKeys.EconomyCommandUpgradeCount,
            WH40KPlayerStatKeys.EconomyCommandUpgradeCost,
            fundsCost + researchCost,
            "command-upgrade",
            null);

        _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-upgrade-ok", ("level", ent.Comp.UpgradeLevel)), ent.Owner, args.Actor);
        UpdateUi(ent);
    }

    private void OnReinforcementCalled(Entity<WH40KCommandNodeComponent> ent, ref WH40KCommandNodeCallReinforcementMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        var phase = _teamRule.GetCurrentPhase();
        if (phase < WH40KBattlePhase.Assault)
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-phase-lock"), ent.Owner, args.Actor);
            return;
        }

        if (phase >= WH40KBattlePhase.Apocalypse)
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-apocalypse-lock"), ent.Owner, args.Actor);
            return;
        }

        if (_timing.CurTime < ent.Comp.NextReinforcementAvailable)
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-cooldown"), ent.Owner, args.Actor);
            return;
        }

        if (!TryResolveReinforcementProfileForTeam(ent.Comp.TeamId, out var reinforcementProfile) ||
            !TryResolveReinforcementOption(reinforcementProfile, args.OptionId, out var option))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-option-invalid"), ent.Owner, args.Actor);
            return;
        }

        if (!IsReinforcementOptionUnlocked(ent.Comp.TeamId, option))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-option-locked"), ent.Owner, args.Actor);
            return;
        }

        var maxCount = Math.Max(1, option.MaxCount);
        if (args.Count < 1 || args.Count > maxCount)
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-option-invalid"), ent.Owner, args.Actor);
            return;
        }

        var influenceCost = GetCurrentReinforcementCost(ent.Comp, option, args.Count);
        var fundsCost = WH40KCommandEconomyCalculator.GetReinforcementFundsCost(influenceCost);
        if (!TrySpendTeamFundsAndInfluence(ent.Owner, ent.Comp.TeamId, fundsCost, influenceCost, "reinforcement"))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-denied"), ent.Owner, args.Actor);
            return;
        }

        if (!TrySpawnReinforcementSquad(ent, option, args.Count, out var spawnedCount))
        {
            TryAdjustTeamFunds(ent.Owner, ent.Comp.TeamId, fundsCost, "reinforcement-refund");
            _teamRule.TryAdjustTeamInfluence(ent.Comp.TeamId, influenceCost, out _, out _, source: "reinforcement-refund");
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-spawnpoints-missing"), ent.Owner, args.Actor);
            return;
        }

        ent.Comp.NextReinforcementAvailable =
            _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, ent.Comp.ReinforcementCooldownSeconds));

        RecordEconomySpendStats(
            args.Actor,
            ent.Comp.TeamId,
            WH40KPlayerStatKeys.EconomyCommandReinforcementCallCount,
            WH40KPlayerStatKeys.EconomyCommandReinforcementCost,
            fundsCost + influenceCost,
            "reinforcement",
            option.Id);

        _popup.PopupEntity(
            Loc.GetString(
                "w40k-cmd-reinforcement-ok-spawned",
                ("count", spawnedCount),
                ("option", ResolveLocalizedOrRaw(option.NameKey))),
            ent.Owner,
            args.Actor);

        UpdateUi(ent);
    }

    private void OnTeamCompositionPressed(Entity<WH40KCommandNodeComponent> ent, ref WH40KCommandNodeTeamCompositionPressedMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        UpdateUi(ent);
    }

    private void OnMissionTaskAssigned(Entity<WH40KCommandNodeComponent> ent, ref WH40KCommandNodeAssignMissionTaskMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        if (_runtime.BuildTeamMissionRuntimeState(ent.Comp.TeamId).IsActive)
        {
            _popup.PopupEntity(
                Loc.GetString("w40k-cmd-mission-board-select-denied-active"),
                ent.Owner,
                args.Actor);
            UpdateUi(ent);
            return;
        }

        EnsureMissionBoardOfferSet(ent.Comp);

        if (!TryMatchOfferedTaskId(ent.Comp, args.TaskId, out var taskId))
        {
            _popup.PopupEntity(
                Loc.GetString("w40k-cmd-mission-board-select-denied-unavailable"),
                ent.Owner,
                args.Actor);
            UpdateUi(ent);
            return;
        }

        if (!_runtime.TryStartFactionMission(ent.Comp.TeamId, taskId, out var startedMission))
        {
            _popup.PopupEntity(
                Loc.GetString("w40k-cmd-mission-board-select-denied-unavailable"),
                ent.Owner,
                args.Actor);
            RerollMissionBoardOfferSet(ent.Comp);
            UpdateUi(ent);
            return;
        }

        ent.Comp.ActiveMissionTaskId = startedMission.MissionId;
        ent.Comp.MissionBoardHadActiveFactionMission = true;
        _popup.PopupEntity(
            Loc.GetString(
                "w40k-cmd-mission-board-select-ok",
                ("task", ResolveLocalizedOrRaw(startedMission.MissionTitle))),
            ent.Owner,
            args.Actor);

        _missionPinpointer.TryForceRefreshForTeam(ent.Comp.TeamId, out _);
        UpdateUi(ent);
    }

    private void OnMissionPinpointerSyncRequested(
        Entity<WH40KCommandNodeComponent> ent,
        ref WH40KCommandNodeSyncMissionPinpointerMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        var teamMission = _runtime.BuildTeamMissionRuntimeState(ent.Comp.TeamId);
        var globalMission = _runtime.BuildGlobalMissionRuntimeState();
        if (!teamMission.IsActive && !globalMission.IsActive)
        {
            _popup.PopupEntity(
                Loc.GetString("w40k-cmd-mission-board-pinpointer-sync-no-mission"),
                ent.Owner,
                args.Actor);
            return;
        }

        if (!_missionPinpointer.TryForceRefreshForTeam(ent.Comp.TeamId, out var refreshedCount))
        {
            _popup.PopupEntity(
                Loc.GetString("w40k-cmd-mission-board-pinpointer-sync-empty"),
                ent.Owner,
                args.Actor);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString(
                "w40k-cmd-mission-board-pinpointer-sync-ok",
                ("count", refreshedCount)),
            ent.Owner,
            args.Actor);
    }

    private int GetUpgradeCost(WH40KCommandNodeComponent component)
    {
        return Math.Max(1, component.UpgradeBaseCost + component.UpgradeCostStep * component.UpgradeLevel);
    }

    private int GetRemainingReinforcementCooldown(WH40KCommandNodeComponent component)
    {
        if (_timing.CurTime >= component.NextReinforcementAvailable)
            return 0;

        return (int) Math.Ceiling((component.NextReinforcementAvailable - _timing.CurTime).TotalSeconds);
    }

    private int GetCurrentReinforcementCost(
        WH40KCommandNodeComponent component,
        IReadOnlyCollection<WH40KCommandNodeReinforcementOptionState> optionStates)
    {
        if (optionStates.Count == 0)
            return Math.Max(1, component.ReinforcementCost);

        var minCost = int.MaxValue;
        foreach (var option in optionStates)
        {
            if (option.CostX1 <= 0)
                continue;

            minCost = Math.Min(minCost, option.CostX1);
        }

        if (minCost == int.MaxValue)
            minCost = Math.Max(1, component.ReinforcementCost);

        return Math.Max(1, minCost);
    }

    private int GetCurrentReinforcementCost(
        WH40KCommandNodeComponent component,
        WH40KCommandReinforcementOptionPrototype option,
        int count)
    {
        var baseCost = CalculateReinforcementBaseCost(option, count);
        if (baseCost <= 0)
            baseCost = Math.Max(1, component.ReinforcementCost);

        return Math.Max(1, baseCost);
    }

    private static int CalculateReinforcementBaseCost(WH40KCommandReinforcementOptionPrototype option, int count)
    {
        var unitBaseCost = Math.Max(1, option.BaseCost);
        var safeCount = Math.Clamp(count, 1, Math.Max(1, option.MaxCount));
        var multiplierStep = Math.Max(0f, option.AdditionalUnitCostMultiplier);

        var total = 0f;
        for (var i = 0; i < safeCount; i++)
        {
            var multiplier = 1f + i * multiplierStep;
            total += unitBaseCost * multiplier;
        }

        return Math.Max(1, (int) MathF.Round(total, MidpointRounding.AwayFromZero));
    }

    private WH40KCommandMissionBoardState BuildMissionBoardState(
        WH40KCommandNodeComponent component,
        WH40KCommandMissionRuntimeState globalMissionRuntime,
        WH40KCommandMissionRuntimeState teamMissionRuntime)
    {
        var teamMissionActive = teamMissionRuntime.IsActive;

        if (component.MissionBoardHadActiveFactionMission && !teamMissionActive)
        {
            component.ActiveMissionTaskId = string.Empty;
            RerollMissionBoardOfferSet(component);
        }

        if (teamMissionActive)
        {
            component.ActiveMissionTaskId = teamMissionRuntime.MissionId;
        }
        else
        {
            component.ActiveMissionTaskId = string.Empty;
            EnsureMissionBoardOfferSet(component);
        }

        component.MissionBoardHadActiveFactionMission = teamMissionActive;

        var selectedTaskId = teamMissionActive
            ? component.ActiveMissionTaskId
            : string.Empty;

        var systemTasks = BuildMissionBoardSystemTasks(globalMissionRuntime, component.TeamId);
        var selectableTasks = BuildMissionBoardSelectableTasks(component, selectedTaskId);

        return new WH40KCommandMissionBoardState(
            "w40k-cmd-mission-board-no-active-title",
            "w40k-cmd-mission-board-no-active-description",
            0,
            "w40k-cmd-mission-board-no-active-timer",
            "w40k-cmd-mission-board-no-active-timer",
            selectedTaskId,
            systemTasks,
            selectableTasks);
    }

    private void EnsureMissionBoardOfferSet(WH40KCommandNodeComponent component)
    {
        if (IsMissionBoardOfferSetValid(component))
            return;

        RerollMissionBoardOfferSet(component);
    }

    private bool IsMissionBoardOfferSetValid(WH40KCommandNodeComponent component)
    {
        if (component.MissionBoardOfferedTaskIds.Count != MissionBoardOfferCount)
            return false;

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var offeredTaskId in component.MissionBoardOfferedTaskIds)
        {
            if (string.IsNullOrWhiteSpace(offeredTaskId))
                return false;

            if (!unique.Add(offeredTaskId))
                return false;

            if (!_runtime.TryGetFactionMissionOffer(component.TeamId, offeredTaskId, out _))
                return false;
        }

        return true;
    }

    private void RerollMissionBoardOfferSet(WH40KCommandNodeComponent component)
    {
        component.MissionBoardOfferedTaskIds.Clear();

        var offers = _runtime.RollFactionMissionOffers(component.TeamId, MissionBoardOfferCount);
        foreach (var offer in offers)
        {
            if (string.IsNullOrWhiteSpace(offer.MissionId))
                continue;

            component.MissionBoardOfferedTaskIds.Add(offer.MissionId);
        }
    }

    private WH40KCommandMissionBoardSystemTaskState[] BuildMissionBoardSystemTasks(
        WH40KCommandMissionRuntimeState globalMissionRuntime,
        string observerTeamId)
    {
        var tasks = new List<WH40KCommandMissionBoardSystemTaskState>();
        if (globalMissionRuntime.IsActive)
        {
            var rewardLine = BuildMissionRewardLine(
                globalMissionRuntime.RewardMajorDevelopmentPoints,
                globalMissionRuntime.RewardMinorDevelopmentPoints,
                globalMissionRuntime.RewardTimeoutDevelopmentPoints,
                globalMissionRuntime.RewardFailureDevelopmentPoints,
                globalMissionRuntime.RewardTempoBonusPercent,
                globalMissionRuntime.RewardTokenId,
                globalMissionRuntime.RewardTokenDurationSeconds);

            tasks.Add(new WH40KCommandMissionBoardSystemTaskState(
                globalMissionRuntime.MissionId,
                globalMissionRuntime.MissionTitle,
                rewardLine,
                globalMissionRuntime.MissionDescription,
                WH40KCommandMissionBoardTaskStatus.Active));
        }

        foreach (var counter in _runtime.BuildEnemyFactionCounterMissions(observerTeamId))
        {
            tasks.Add(new WH40KCommandMissionBoardSystemTaskState(
                counter.MissionId,
                counter.Title,
                "w40k-cmd-mission-board-counter-reward",
                counter.Description,
                WH40KCommandMissionBoardTaskStatus.Queued));
        }

        return tasks.ToArray();
    }

    private WH40KCommandMissionBoardSelectableTaskState[] BuildMissionBoardSelectableTasks(
        WH40KCommandNodeComponent component,
        string selectedTaskId)
    {
        if (component.MissionBoardOfferedTaskIds.Count == 0)
            return Array.Empty<WH40KCommandMissionBoardSelectableTaskState>();

        var hasSelectedTask = !string.IsNullOrWhiteSpace(selectedTaskId);
        var hasSelectedOffer = hasSelectedTask &&
            component.MissionBoardOfferedTaskIds.Any(id =>
                string.Equals(id, selectedTaskId, StringComparison.OrdinalIgnoreCase));

        var tasks = new List<WH40KCommandMissionBoardSelectableTaskState>(component.MissionBoardOfferedTaskIds.Count);
        foreach (var offeredTaskId in component.MissionBoardOfferedTaskIds)
        {
            if (hasSelectedOffer &&
                !string.Equals(offeredTaskId, selectedTaskId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_runtime.TryGetFactionMissionOffer(component.TeamId, offeredTaskId, out var offer))
                continue;

            var rewardLine = BuildMissionRewardLine(
                offer.RewardMajorDevelopmentPoints,
                offer.RewardMinorDevelopmentPoints,
                offer.RewardTimeoutDevelopmentPoints,
                offer.RewardFailureDevelopmentPoints,
                offer.RewardTempoBonusPercent,
                offer.RewardTokenId,
                offer.RewardTokenDurationSeconds);

            tasks.Add(new WH40KCommandMissionBoardSelectableTaskState(
                offer.MissionId,
                offer.Title,
                rewardLine,
                Loc.GetString("w40k-cmd-mission-board-duration-line", ("duration", FormatClock(offer.DurationSeconds))),
                offer.Description));
        }

        return tasks.ToArray();
    }

    private string BuildMissionRewardLine(
        int major,
        int minor,
        int timeout,
        int failure,
        int tempoBonusPercent,
        string tokenId,
        int tokenDurationSeconds)
    {
        var token = string.IsNullOrWhiteSpace(tokenId)
            ? "-"
            : $"{ResolveLocalizedOrRaw(tokenId)} ({FormatClock(tokenDurationSeconds)})";

        return Loc.GetString(
            "w40k-cmd-mission-board-reward-line",
            ("major", Math.Max(0, major)),
            ("minor", Math.Max(0, minor)),
            ("timeout", Math.Max(0, timeout)),
            ("failure", Math.Max(0, failure)),
            ("tempo", Math.Max(0, tempoBonusPercent)),
            ("token", token));
    }

    private static bool TryMatchOfferedTaskId(
        WH40KCommandNodeComponent component,
        string requestedTaskId,
        out string taskId)
    {
        taskId = string.Empty;
        if (string.IsNullOrWhiteSpace(requestedTaskId))
            return false;

        foreach (var offeredTaskId in component.MissionBoardOfferedTaskIds)
        {
            if (!string.Equals(offeredTaskId, requestedTaskId, StringComparison.OrdinalIgnoreCase))
                continue;

            taskId = offeredTaskId;
            return true;
        }

        return false;
    }

    private string ResolveLocalizedOrRaw(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (Loc.TryGetString(value, out var localized) && !string.IsNullOrWhiteSpace(localized))
            return localized!;

        return value;
    }

    private bool TryResolveReinforcementProfileForTeam(string teamId, out WH40KCommandReinforcementProfilePrototype profile)
    {
        profile = default!;
        var profileId = ResolveReinforcementProfileIdForTeam(teamId);
        if (_proto.TryIndex(profileId, out WH40KCommandReinforcementProfilePrototype? indexedProfile))
        {
            profile = indexedProfile;
            return true;
        }

        if (_proto.TryIndex(ReinforcementDefaultProfileId, out WH40KCommandReinforcementProfilePrototype? fallbackProfile))
        {
            profile = fallbackProfile;
            return true;
        }

        return false;
    }

    private ProtoId<WH40KCommandReinforcementProfilePrototype> ResolveReinforcementProfileIdForTeam(string teamId)
    {
        if (TryResolveTeamIdentityProfileForTeam(teamId, out var teamIdentityProfile) &&
            teamIdentityProfile.ReinforcementProfile is { } identityProfile)
        {
            return identityProfile;
        }

        if (!_proto.TryIndex(ReinforcementTeamMapId, out WH40KCommandReinforcementTeamMapPrototype? teamMap))
            return ReinforcementDefaultProfileId;

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

    private bool TryResolveTreeProfileForTeam(string teamId, out WH40KCommandTreeProfilePrototype profile)
    {
        profile = default!;
        var profileId = ResolveTreeProfileIdForTeam(teamId);
        if (_proto.TryIndex(profileId, out WH40KCommandTreeProfilePrototype? indexedProfile))
        {
            profile = indexedProfile;
            return true;
        }

        if (_proto.TryIndex(CommandTreeDefaultProfileId, out WH40KCommandTreeProfilePrototype? fallbackProfile))
        {
            profile = fallbackProfile;
            return true;
        }

        return false;
    }

    private ProtoId<WH40KCommandTreeProfilePrototype> ResolveTreeProfileIdForTeam(string teamId)
    {
        if (TryResolveTeamIdentityProfileForTeam(teamId, out var teamIdentityProfile) &&
            teamIdentityProfile.CommandTreeProfile is { } identityProfile)
        {
            return identityProfile;
        }

        if (!_proto.TryIndex(CommandTreeTeamMapId, out WH40KCommandTreeTeamMapPrototype? teamMap))
            return CommandTreeDefaultProfileId;

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

    private bool TryResolveTreeNodeForTeam(
        string teamId,
        string nodeId,
        out WH40KCommandTreeProfilePrototype profile,
        out WH40KCommandTreeNodeConfig nodeConfig)
    {
        profile = default!;
        nodeConfig = default!;

        if (string.IsNullOrWhiteSpace(nodeId) || !TryResolveTreeProfileForTeam(teamId, out profile))
            return false;

        foreach (var node in profile.Nodes)
        {
            if (!string.Equals(node.Id, nodeId, StringComparison.OrdinalIgnoreCase))
                continue;

            nodeConfig = node;
            return true;
        }

        return false;
    }


    private void ApplyTreeNodeUnlocks(string teamId, WH40KCommandTreeNodeConfig nodeConfig)
    {
        var technologies = CollectTreeTechnologyUnlocks(nodeConfig, teamId);
        var recipes = CollectTreeRecipeUnlocks(nodeConfig, teamId);
        var cargoProducts = CollectTreeCargoUnlocks(nodeConfig, teamId);
        var researchPointGrant = Math.Max(0, nodeConfig.ResearchPointGrant);

        if (technologies.Count > 0 || recipes.Count > 0)
            ApplyResearchUnlocks(teamId, technologies, recipes);

        if (researchPointGrant > 0)
            ApplyResearchPointGrant(teamId, researchPointGrant);

        if (cargoProducts.Count > 0)
            ApplyCargoUnlocks(teamId, cargoProducts);

        RefreshTeamCargoLogisticsBonuses(teamId);
    }

    private void ApplyResearchUnlocks(
        string teamId,
        IReadOnlyList<ProtoId<TechnologyPrototype>> technologies,
        IReadOnlyList<ProtoId<LatheRecipePrototype>> recipes)
    {
        var query = EntityQueryEnumerator<ResearchServerComponent, TechnologyDatabaseComponent, WH40KResearchTeamComponent>();
        while (query.MoveNext(out var uid, out _, out var database, out var team))
        {
            if (!string.Equals(team.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            var changed = false;

            foreach (var technologyId in technologies)
            {
                if (string.IsNullOrWhiteSpace(technologyId) || _research.IsTechnologyUnlocked(uid, technologyId, database))
                    continue;

                _research.AddTechnology(uid, technologyId, database);
                changed = true;
            }

            foreach (var recipeId in recipes)
            {
                if (string.IsNullOrWhiteSpace(recipeId) || _research.IsLatheRecipeUnlocked(uid, recipeId, database))
                    continue;

                _research.AddLatheRecipe(uid, recipeId, database);
                changed = true;
            }

            if (changed)
            {
                _research.UpdateTechnologyCards(uid, database);
                var refreshEvent = new TechnologyDatabaseModifiedEvent(null);
                RaiseLocalEvent(uid, ref refreshEvent);
            }
        }
    }

    private void ApplyCargoUnlocks(string teamId, IReadOnlyList<ProtoId<CargoProductPrototype>> cargoProducts)
    {
        if (!TryResolveCargoAccountForTeam(teamId, out var account))
            return;

        var stationsToRefresh = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WH40KCargoProductUnlocksComponent>();
        while (query.MoveNext(out var uid, out var unlocks))
        {
            if (!unlocks.UnlockedProductsByAccount.TryGetValue(account, out var unlocked))
            {
                unlocked = new List<ProtoId<CargoProductPrototype>>();
                unlocks.UnlockedProductsByAccount[account] = unlocked;
            }

            var changed = false;
            foreach (var productId in cargoProducts)
            {
                if (string.IsNullOrWhiteSpace(productId) || unlocked.Contains(productId))
                    continue;

                unlocked.Add(productId);
                changed = true;
            }

            if (!changed)
                continue;

            Dirty(uid, unlocks);
            stationsToRefresh.Add(uid);
        }

        foreach (var stationUid in stationsToRefresh)
        {
            _cargo.RefreshOrderStateForStation(stationUid);
        }
    }

    private void ApplyResearchPointGrant(string teamId, int researchPoints)
    {
        if (researchPoints <= 0)
            return;

        _teamRule.TryAdjustTeamResearchPoints(teamId, researchPoints, out _, out _, "command-tree-grant");
    }

    private void RefreshTeamCargoLogisticsBonuses(string teamId)
    {
        if (!TryResolveCargoAccountForTeam(teamId, out var account))
            return;

        var bonuses = _treeBonuses.GetTeamBonuses(teamId);
        var query = EntityQueryEnumerator<CargoLogisticsTierComponent>();
        while (query.MoveNext(out var uid, out var logistics))
        {
            if (!logistics.AccountTiers.ContainsKey(account) &&
                !logistics.AccountTeams.ContainsKey(account))
            {
                continue;
            }

            _cargo.SetCargoLogisticsExternalBonuses(
                uid,
                account,
                bonuses.CargoDeliverySpeedBonusPercent,
                bonuses.CargoMaxItemsBonusPercent,
                bonuses.CargoPriceDiscountPercent);
        }
    }

    private static List<ProtoId<TechnologyPrototype>> CollectTreeTechnologyUnlocks(
        WH40KCommandTreeNodeConfig nodeConfig,
        string teamId)
    {
        var unlocks = new List<ProtoId<TechnologyPrototype>>(nodeConfig.TechnologyUnlocks);
        AddTeamSpecificUnlocks(unlocks, nodeConfig.TeamTechnologyUnlocks, teamId);
        return unlocks.Distinct().ToList();
    }

    private static List<ProtoId<LatheRecipePrototype>> CollectTreeRecipeUnlocks(
        WH40KCommandTreeNodeConfig nodeConfig,
        string teamId)
    {
        var unlocks = new List<ProtoId<LatheRecipePrototype>>(nodeConfig.LatheRecipeUnlocks);
        AddTeamSpecificUnlocks(unlocks, nodeConfig.TeamLatheRecipeUnlocks, teamId);
        return unlocks.Distinct().ToList();
    }

    private static List<ProtoId<CargoProductPrototype>> CollectTreeCargoUnlocks(
        WH40KCommandTreeNodeConfig nodeConfig,
        string teamId)
    {
        var unlocks = new List<ProtoId<CargoProductPrototype>>(nodeConfig.CargoProductUnlocks);
        AddTeamSpecificUnlocks(unlocks, nodeConfig.TeamCargoProductUnlocks, teamId);
        return unlocks.Distinct().ToList();
    }

    private static void AddTeamSpecificUnlocks<TProto>(
        List<ProtoId<TProto>> target,
        IReadOnlyDictionary<string, List<ProtoId<TProto>>> mappedUnlocks,
        string teamId)
        where TProto : class, IPrototype
    {
        foreach (var (mappedTeamId, unlocks) in mappedUnlocks)
        {
            if (!string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            target.AddRange(unlocks);
            return;
        }
    }

    private bool TrySpendTeamFundsAndResearch(
        EntityUid? sourceUid,
        string teamId,
        int fundsCost,
        int researchCost,
        string source)
    {
        fundsCost = Math.Max(0, fundsCost);
        researchCost = Math.Max(0, researchCost);

        if (fundsCost <= 0 && researchCost <= 0)
            return true;

        if (!TryGetTeamFunds(sourceUid, teamId, out var funds) || funds < fundsCost)
            return false;

        if (!_teamRule.TryGetTeamResearchPoints(teamId, out var research) || research < researchCost)
            return false;

        if (fundsCost > 0 && !TryAdjustTeamFunds(sourceUid, teamId, -fundsCost, source))
            return false;

        if (researchCost <= 0)
            return true;

        if (_teamRule.TrySpendTeamResearchPoints(teamId, researchCost, out _, source))
            return true;

        if (fundsCost > 0)
            TryAdjustTeamFunds(sourceUid, teamId, fundsCost, $"{source}-refund");

        return false;
    }

    private bool TrySpendTeamFundsAndInfluence(
        EntityUid? sourceUid,
        string teamId,
        int fundsCost,
        int influenceCost,
        string source)
    {
        fundsCost = Math.Max(0, fundsCost);
        influenceCost = Math.Max(0, influenceCost);

        if (fundsCost <= 0 && influenceCost <= 0)
            return true;

        if (!TryGetTeamFunds(sourceUid, teamId, out var funds) || funds < fundsCost)
            return false;

        if (!_teamRule.TryGetTeamInfluencePoints(teamId, out var influence) || influence < influenceCost)
            return false;

        if (fundsCost > 0 && !TryAdjustTeamFunds(sourceUid, teamId, -fundsCost, source))
            return false;

        if (influenceCost <= 0)
            return true;

        if (_teamRule.TrySpendTeamInfluence(teamId, influenceCost, out _, source))
            return true;

        if (fundsCost > 0)
            TryAdjustTeamFunds(sourceUid, teamId, fundsCost, $"{source}-refund");

        return false;
    }

    private bool TryGetTeamFunds(EntityUid? sourceUid, string teamId, out int funds)
    {
        funds = 0;

        if (!TryResolveCargoAccountForTeam(teamId, out var account) ||
            !TryGetTeamBank(sourceUid, out var bank))
        {
            return false;
        }

        funds = Math.Max(0, _cargo.GetBalanceFromAccount(bank, account));
        return true;
    }

    private bool TryAdjustTeamFunds(EntityUid? sourceUid, string teamId, int delta, string? source = null)
    {
        if (delta == 0)
            return true;

        if (!TryResolveCargoAccountForTeam(teamId, out var account) ||
            !TryGetTeamBank(sourceUid, out var bank))
        {
            return false;
        }

        var current = Math.Max(0, _cargo.GetBalanceFromAccount(bank, account));
        var next = Math.Max(0, current + delta);
        return _cargo.TrySetBankAccount(bank, account, next, createAccount: true);
    }

    private bool TryGetTeamBank(EntityUid? sourceUid, out Entity<StationBankAccountComponent?> bank)
    {
        bank = default;

        if (sourceUid is { } source &&
            _stations.GetOwningStation(source) is { } stationUid &&
            TryComp<StationBankAccountComponent>(stationUid, out var sourceBank))
        {
            bank = (stationUid, sourceBank);
            return true;
        }

        var query = EntityQueryEnumerator<StationBankAccountComponent>();
        while (query.MoveNext(out var uid, out var fallbackBank))
        {
            bank = (uid, fallbackBank);
            return true;
        }

        return false;
    }

    private static bool TryResolveCargoAccountForTeam(string teamId, out ProtoId<CargoAccountPrototype> account)
    {
        account = default;

        if (string.Equals(teamId, "Imperium", StringComparison.OrdinalIgnoreCase))
        {
            account = "WH40KImperium";
            return true;
        }

        if (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase))
        {
            account = "WH40KHeretics";
            return true;
        }

        return false;
    }

    private static bool ContainsNodeId(IReadOnlyCollection<string> purchasedNodeIds, string nodeId)
    {
        return purchasedNodeIds.Any(existing => string.Equals(existing, nodeId, StringComparison.OrdinalIgnoreCase));
    }

    private void RecordEconomySpendStats(
        EntityUid actor,
        string teamId,
        string countStatKey,
        string costStatKey,
        int cost,
        string action,
        string? nodeId)
    {
        if (!_players.TryGetSessionByEntity(actor, out var session))
            return;

        var elapsedSeconds = Math.Max(0, _teamRule.GetRoundElapsedSeconds());
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["team"] = teamId,
            ["phase"] = _teamRule.GetCurrentPhase().ToString(),
            ["action"] = action,
            ["cost"] = Math.Max(0, cost).ToString(),
            ["roundSeconds"] = elapsedSeconds.ToString()
        };

        if (!string.IsNullOrWhiteSpace(nodeId))
            metadata["nodeId"] = nodeId;

        _stats.Record(session.UserId, countStatKey, 1, metadata);
        _stats.Record(session.UserId, costStatKey, Math.Max(0, cost), metadata);
    }

    private static string FormatClock(int totalSeconds)
    {
        var safeSeconds = Math.Max(0, totalSeconds);
        var minutes = safeSeconds / 60;
        var seconds = safeSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private WH40KCommandNodeReinforcementOptionState[] BuildReinforcementOptionStates(string teamId)
    {
        if (!TryResolveReinforcementProfileForTeam(teamId, out var profile) || profile.Options.Count == 0)
            return Array.Empty<WH40KCommandNodeReinforcementOptionState>();

        var options = new List<WH40KCommandNodeReinforcementOptionState>(profile.Options.Count);
        foreach (var option in profile.Options)
        {
            if (string.IsNullOrWhiteSpace(option.Id))
                continue;

            if (!IsReinforcementOptionUnlocked(teamId, option))
                continue;

            var maxCount = Math.Clamp(option.MaxCount, 1, 3);
            var costX1 = CalculateReinforcementBaseCost(option, 1);
            var costX2 = CalculateReinforcementBaseCost(option, 2);
            var costX3 = CalculateReinforcementBaseCost(option, 3);

            options.Add(new WH40KCommandNodeReinforcementOptionState(
                option.Id,
                option.NameKey,
                option.DescriptionKey,
                BuildReinforcementEquipmentSummary(option.Job),
                option.PreviewPrototype.ToString(),
                Math.Max(1, costX1),
                Math.Max(1, costX2),
                Math.Max(1, costX3),
                maxCount));
        }

        return options.ToArray();
    }

    private static bool TryResolveReinforcementOption(
        WH40KCommandReinforcementProfilePrototype profile,
        string optionId,
        out WH40KCommandReinforcementOptionPrototype option)
    {
        foreach (var configured in profile.Options)
        {
            if (!string.Equals(configured.Id, optionId, StringComparison.OrdinalIgnoreCase))
                continue;

            option = configured;
            return true;
        }

        option = default!;
        return false;
    }

    private bool TrySpawnReinforcementSquad(
        Entity<WH40KCommandNodeComponent> ent,
        WH40KCommandReinforcementOptionPrototype option,
        int count,
        out int spawnedCount)
    {
        spawnedCount = 0;
        if (!TryCollectReinforcementSpawnPoints(ent.Comp.TeamId, ent.Owner))
            return false;

        var station = _stations.GetOwningStation(ent.Owner);
        var safeCount = Math.Clamp(count, 1, Math.Max(1, option.MaxCount));
        for (var i = 0; i < safeCount; i++)
        {
            var coordinates = _reinforcementMapSpawnPoints.Count > 0
                ? _random.Pick(_reinforcementMapSpawnPoints)
                : _random.Pick(_reinforcementSpawnPoints);

            var profile = HumanoidCharacterProfile.RandomWithSpecies(HumanoidCharacterProfile.DefaultSpecies);
            var spawned = _stationSpawning.SpawnPlayerMob(coordinates, option.Job, profile, station);
            ApplySpawnedReinforcementTeamData(spawned, ent.Comp.TeamId, option);
            _reinforcementAi.TryReadyWeapon(spawned);
            _reinforcementAi.Enable(spawned, coordinates);
            spawnedCount++;
        }

        return spawnedCount > 0;
    }

    private bool TryCollectReinforcementSpawnPoints(string teamId, EntityUid commandNode)
    {
        _reinforcementSpawnPoints.Clear();
        _reinforcementMapSpawnPoints.Clear();

        var commandTransform = Transform(commandNode);
        var mapId = commandTransform.MapID;
        var commandCoordinates = commandTransform.Coordinates;
        var query = EntityQueryEnumerator<WH40KReinforcementSpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out _, out var marker, out var xform))
        {
            if (!string.Equals(marker.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            _reinforcementSpawnPoints.Add(xform.Coordinates);
            if (xform.MapID == mapId)
                _reinforcementMapSpawnPoints.Add(xform.Coordinates);
        }

        // Prevent spawning directly on the command terminal tile when map markers include that coordinate.
        if (_reinforcementSpawnPoints.Count > 1)
            _reinforcementSpawnPoints.RemoveAll(coords => IsSameMapTile(coords, commandCoordinates));

        if (_reinforcementMapSpawnPoints.Count > 1)
            _reinforcementMapSpawnPoints.RemoveAll(coords => IsSameMapTile(coords, commandCoordinates));

        return _reinforcementSpawnPoints.Count > 0;
    }

    private static bool IsSameMapTile(EntityCoordinates first, EntityCoordinates second)
    {
        return first.Equals(second);
    }

    private void ApplySpawnedReinforcementTeamData(
        EntityUid entity,
        string teamId,
        WH40KCommandReinforcementOptionPrototype option)
    {
        var teamMember = EnsureComp<WH40KTeamMemberComponent>(entity);
        teamMember.TeamId = teamId;
        _teamNpcFactions.ApplyTeamFaction(entity, teamId);

        var icon = EnsureComp<WH40KTeamBattleFactionIconComponent>(entity);
        if (!string.Equals(icon.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            icon.TeamId = teamId;
            Dirty(entity, icon);
        }

        var rewardState = EnsureComp<WH40KReinforcementRewardStateComponent>(entity);
        rewardState.WasClaimedByPlayer = false;
        rewardState.ClaimedUserId = null;

        EnsureComp<GhostTakeoverAvailableComponent>(entity);
        var ghostRole = EnsureComp<GhostRoleComponent>(entity);
        ghostRole.JobProto = option.Job;
        ghostRole.RoleName = ResolveLocalizedOrRaw(option.NameKey);
        ghostRole.RoleDescription = ResolveLocalizedOrRaw(option.DescriptionKey);
        ghostRole.RaffleConfig ??= new GhostRoleRaffleConfig(new GhostRoleRaffleSettings
        {
            InitialDuration = ReinforcementRaffleDurationSeconds,
            JoinExtendsDurationBy = 0,
            MaxDuration = ReinforcementRaffleDurationSeconds
        });
        EnsureComp<WH40KReinforcementGhostRoleOneShotComponent>(entity);
    }

    private bool IsReinforcementOptionUnlocked(string teamId, WH40KCommandReinforcementOptionPrototype option)
    {
        var minLevel = Math.Max(1, option.MinBaseLevel);
        if (minLevel <= 1)
            return true;

        var currentLevel = 1;
        if (_teamRule.TryGetTeamProgress(teamId, out var level, out _, out _))
            currentLevel = Math.Max(1, level);

        return currentLevel >= minLevel;
    }

    private string BuildReinforcementEquipmentSummary(ProtoId<JobPrototype> jobId)
    {
        if (!_proto.TryIndex(jobId, out JobPrototype? job) ||
            job.StartingGear is not { } gearId ||
            !_proto.TryIndex(gearId, out StartingGearPrototype? gear))
        {
            return "-";
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleLoadoutId = LoadoutSystem.GetJobPrototype(job.ID);
        RoleLoadout? defaultRoleLoadout = null;
        RoleLoadoutPrototype? defaultRoleLoadoutProto = null;
        if (_proto.TryIndex(roleLoadoutId, out defaultRoleLoadoutProto))
        {
            defaultRoleLoadout = new RoleLoadout(roleLoadoutId);
            defaultRoleLoadout.SetDefault(HumanoidCharacterProfile.DefaultWithSpecies(), null, _proto, force: true);
        }

        var excludedSlots = defaultRoleLoadout != null && defaultRoleLoadoutProto != null
            ? _stationSpawning.GetLoadoutEquipmentOverrides(defaultRoleLoadout, defaultRoleLoadoutProto)
            : null;

        AddGearNames(gear, names, excludedSlots);

        if (defaultRoleLoadout != null && defaultRoleLoadoutProto != null)
        {
            foreach (var groupId in defaultRoleLoadoutProto.Groups)
            {
                if (!defaultRoleLoadout.SelectedLoadouts.TryGetValue(groupId, out var selections))
                    continue;

                foreach (var selection in selections)
                {
                    if (!_proto.TryIndex(selection.Prototype, out LoadoutPrototype? loadout))
                        continue;

                    if (loadout.StartingGear is { } startingGearId &&
                        _proto.TryIndex(startingGearId, out StartingGearPrototype? startingGear))
                    {
                        AddGearNames(startingGear, names);
                    }

                    AddGearNames(loadout, names);
                }
            }
        }

        if (names.Count == 0)
            return "-";

        const int maxShown = 5;
        var ordered = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (ordered.Length <= maxShown)
            return string.Join(", ", ordered);

        return $"{string.Join(", ", ordered.Take(maxShown))}, +{ordered.Length - maxShown}";
    }

    private void AddGearNames(IEquipmentLoadout gear, HashSet<string> names, ISet<string>? excludedSlots = null)
    {
        foreach (var (slot, proto) in gear.Equipment)
        {
            if (excludedSlots?.Contains(slot) == true)
                continue;

            AddGearName(proto, names);
        }

        foreach (var proto in gear.Inhand)
            AddGearName(proto, names);

        foreach (var storage in gear.Storage.Values)
        {
            foreach (var proto in storage)
                AddGearName(proto, names);
        }
    }

    private void AddGearName(EntProtoId entityId, HashSet<string> names)
    {
        if (!_proto.TryIndex<EntityPrototype>(entityId, out _))
            return;

        // Send FTL entity key so the client can resolve the name in its own culture.
        var locKey = $"ent-{entityId}";
        names.Add(locKey);
    }

    private bool IsUserAllowedForTeam(EntityUid user, string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return true;

        if (TryComp<GhostComponent>(user, out var ghost) && ghost.CanGhostInteract)
            return true;

        if (_teamRule.TryGetTeamIdFromEntity(user, out var userTeamId) &&
            string.Equals(userTeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryComp<MindComponent>(user, out var mind))
            return false;

        if (mind.CurrentEntity is { } currentEntity)
        {
            if (TryComp<GhostComponent>(currentEntity, out var currentGhost) && currentGhost.CanGhostInteract)
                return true;

            if (_teamRule.TryGetTeamIdFromEntity(currentEntity, out var currentTeamId) &&
                string.Equals(currentTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (mind.UserId is not { } userId)
            return false;

        return _teamRule.TryGetRememberedTeam(userId, out var rememberedTeamId) &&
               string.Equals(rememberedTeamId, teamId, StringComparison.OrdinalIgnoreCase);
    }

    private void OnIsRoleAllowed(ref IsRoleAllowedEvent args)
    {
        if (!TryGetReinforcementTeam(args, out var requiredTeamId))
            return;

        if (args.Player.AttachedEntity is { Valid: true } attachedEntity &&
            TryComp<GhostComponent>(attachedEntity, out var ghost) &&
            ghost.CanGhostInteract)
        {
            return;
        }

        // Latejoin checks also raise IsRoleAllowedEvent.
        // If player has no assigned/remembered team yet (fresh latejoin flow),
        // do not block regular jobs that are reused by reinforcement options.
        if (!_teamRule.TryGetTeamIdForUser(args.Player.UserId, out var playerTeamId))
            return;

        if (string.Equals(playerTeamId, requiredTeamId, StringComparison.OrdinalIgnoreCase))
            return;

        args.Cancelled = true;
    }

    private bool TryGetReinforcementTeam(IsRoleAllowedEvent args, out string teamId)
    {
        teamId = string.Empty;
        if (args.Jobs == null || args.Jobs.Count != 1)
            return false;

        return TryResolveReinforcementTeamForJob(args.Jobs[0], out teamId);
    }

    private bool TryResolveReinforcementTeamForJob(ProtoId<JobPrototype> job, out string teamId)
    {
        teamId = string.Empty;
        if (!_proto.TryIndex(ReinforcementTeamMapId, out WH40KCommandReinforcementTeamMapPrototype? teamMap))
            return false;

        if (TryResolveReinforcementProfile(teamMap.DefaultProfile, out var defaultProfile) &&
            ProfileContainsReinforcementJob(defaultProfile, job))
        {
            teamId = defaultProfile.TeamId;
            if (!string.IsNullOrWhiteSpace(teamId))
                return true;
        }

        foreach (var (mappedTeamId, profileId) in teamMap.TeamProfiles)
        {
            if (!TryResolveReinforcementProfile(profileId, out var profile) ||
                !ProfileContainsReinforcementJob(profile, job))
            {
                continue;
            }

            teamId = string.IsNullOrWhiteSpace(profile.TeamId) ? mappedTeamId : profile.TeamId;
            if (!string.IsNullOrWhiteSpace(teamId))
                return true;
        }

        return false;
    }

    private bool TryResolveReinforcementProfile(
        ProtoId<WH40KCommandReinforcementProfilePrototype> profileId,
        out WH40KCommandReinforcementProfilePrototype profile)
    {
        profile = default!;
        if (!_proto.TryIndex(profileId, out WH40KCommandReinforcementProfilePrototype? indexedProfile))
            return false;

        profile = indexedProfile;
        return true;
    }

    private static bool ProfileContainsReinforcementJob(
        WH40KCommandReinforcementProfilePrototype profile,
        ProtoId<JobPrototype> job)
    {
        foreach (var option in profile.Options)
        {
            if (option.Job == job)
                return true;
        }

        return false;
    }

    private void OnReinforcementMindAdded(
        EntityUid uid,
        WH40KReinforcementGhostRoleOneShotComponent component,
        MindAddedMessage args)
    {
        _reinforcementAi.Disable(uid);

        var rewardState = EnsureComp<WH40KReinforcementRewardStateComponent>(uid);
        rewardState.WasClaimedByPlayer = true;
        rewardState.ClaimedUserId = args.Mind.Comp.UserId;

        // Reinforcement takeover is strictly one-time: after first control transfer
        // remove ghost-role hooks so body remains a regular player character.
        RemCompDeferred<GhostTakeoverAvailableComponent>(uid);
        RemCompDeferred<GhostRoleComponent>(uid);
        RemCompDeferred<WH40KReinforcementGhostRoleOneShotComponent>(uid);
    }

    private TeamCompositionData BuildTeamComposition(string teamId)
    {
        var roleIdCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var members = new List<TeamMemberInfo>();
        var teamRoleIds = GetTeamRoleIds(teamId)
            .Select(roleId => roleId.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var teamRoleIdSet = teamRoleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasCompositionProfile = TryResolveTeamCompositionProfileForTeam(teamId, out var compositionProfile);

        var teamMembers = EntityQueryEnumerator<WH40KTeamMemberComponent>();
        while (teamMembers.MoveNext(out var memberUid, out var teamMember))
        {
            if (!string.Equals(teamMember.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!_mobState.IsAlive(memberUid) && !_mobState.IsCritical(memberUid))
                continue;

            var (roleId, roleName) = GetRoleInfo(memberUid, teamId);
            var memberName = GetMemberName(memberUid);

            if (teamRoleIdSet.Contains(roleId))
                roleIdCounts[roleId] = roleIdCounts.GetValueOrDefault(roleId) + 1;

            members.Add(new TeamMemberInfo(roleId, roleName, memberName));
        }

        var officerRoleIds = hasCompositionProfile
            ? GetOrderedRoleIdsFromProfile(teamRoleIdSet, compositionProfile.OfficerRoles)
            : new List<string>();
        var mechanicusRoleIds = hasCompositionProfile
            ? GetOrderedRoleIdsFromProfile(teamRoleIdSet, compositionProfile.MechanicusRoles)
            : new List<string>();
        var coreRoleIds = hasCompositionProfile
            ? GetOrderedCoreRoleIds(teamRoleIdSet, compositionProfile.CoreRoles, officerRoleIds, mechanicusRoleIds)
            : GetOrderedCoreRoleIds(teamRoleIdSet, Array.Empty<ProtoId<JobPrototype>>(), officerRoleIds, mechanicusRoleIds);
        var staffingResult = BuildStaffingOverview(
            roleIdCounts,
            teamRoleIdSet,
            hasCompositionProfile ? compositionProfile : null);

        var officerRoles = BuildRoleEntries(officerRoleIds, roleIdCounts);
        var coreRoles = BuildRoleEntries(coreRoleIds, roleIdCounts);
        var mechanicusRoles = BuildRoleEntries(mechanicusRoleIds, roleIdCounts);
        var memberEntries = BuildMemberEntries(members, officerRoleIds, coreRoleIds, mechanicusRoleIds);

        var lines = new List<string>();
        lines.AddRange(staffingResult.Lines);
        lines.Add(string.Empty);
        lines.Add(Loc.GetString("w40k-cmd-team-composition-roles-header"));
        AppendRoleGroupLines(lines,
            Loc.GetString("w40k-cmd-team-composition-role-group-officers"),
            officerRoles);
        AppendRoleGroupLines(lines,
            Loc.GetString("w40k-cmd-team-composition-role-group-core"),
            coreRoles);
        AppendRoleGroupLines(lines,
            Loc.GetString("w40k-cmd-team-composition-role-group-mechanicus"),
            mechanicusRoles);
        lines.Add(string.Empty);
        lines.Add(Loc.GetString("w40k-cmd-team-composition-members-header"));

        if (memberEntries.Length == 0)
        {
            lines.Add(Loc.GetString("w40k-cmd-team-composition-empty"));
        }
        else
        {
            foreach (var member in memberEntries)
            {
                lines.Add(Loc.GetString("w40k-cmd-team-composition-member-line",
                    ("name", member.Name),
                    ("role", Loc.GetString(member.RoleName))));
            }
        }

        var summary = Loc.GetString("w40k-cmd-team-composition-summary",
            ("members", memberEntries.Length),
            ("roles", teamRoleIds.Length));

        var staffingData = new WH40KTeamCompositionStaffingData(
            memberEntries.Length,
            teamRoleIds.Length,
            staffingResult.CommandCurrent,
            staffingResult.CommandMax,
            staffingResult.LineCurrent,
            staffingResult.LineMax);

        return new TeamCompositionData(
            summary,
            lines.ToArray(),
            staffingResult.Lines,
            staffingData,
            officerRoles,
            coreRoles,
            mechanicusRoles,
            memberEntries);
    }

    private (string[] Lines, int CommandCurrent, int CommandMax, int LineCurrent, int LineMax) BuildStaffingOverview(
        IReadOnlyDictionary<string, int> roleIdCounts,
        IReadOnlyCollection<string> teamRoleIds,
        WH40KCommandTeamCompositionProfilePrototype? profile)
    {
        var lines = new List<string>();

        var commandPlans = BuildCommandStaffingPlan(teamRoleIds, profile).ToArray();
        var commandRoleIds = commandPlans
            .Select(x => x.RoleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var commandMax = commandPlans.Sum(x => Math.Max(1, x.Target));
        var commandCurrent = commandRoleIds.Sum(roleId => roleIdCounts.GetValueOrDefault(roleId));

        var lineRoleIds = teamRoleIds
            .Where(roleId => !commandRoleIds.Contains(roleId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var lineMax = lineRoleIds.Length;
        var lineCurrent = lineRoleIds.Sum(roleId => roleIdCounts.GetValueOrDefault(roleId));

        lines.Add(Loc.GetString("w40k-cmd-team-composition-command-staff-line",
            ("current", commandCurrent),
            ("max", commandMax)));
        lines.Add(Loc.GetString("w40k-cmd-team-composition-line-staff-line",
            ("current", lineCurrent),
            ("max", lineMax)));

        return (lines.ToArray(), commandCurrent, commandMax, lineCurrent, lineMax);
    }

    private List<StaffingRolePlan> BuildCommandStaffingPlan(
        IReadOnlyCollection<string> teamRoleIds,
        WH40KCommandTeamCompositionProfilePrototype? profile)
    {
        if (profile == null || profile.CommandStaffing.Count == 0)
            return new List<StaffingRolePlan>();

        var plans = new List<StaffingRolePlan>(profile.CommandStaffing.Count);
        var seenRoleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in profile.CommandStaffing)
        {
            var roleId = config.RoleId.Id;
            if (string.IsNullOrWhiteSpace(roleId) ||
                !teamRoleIds.Contains(roleId, StringComparer.OrdinalIgnoreCase) ||
                !seenRoleIds.Add(roleId))
            {
                continue;
            }

            plans.Add(new StaffingRolePlan(roleId, Math.Max(1, config.Target)));
        }

        return plans;
    }

    private List<string> GetOrderedRoleIdsFromProfile(
        IReadOnlyCollection<string> teamRoleIds,
        IReadOnlyCollection<ProtoId<JobPrototype>> configuredRoleIds)
    {
        var ordered = new List<string>(configuredRoleIds.Count);
        foreach (var roleId in configuredRoleIds)
        {
            AddRoleIdIfPresent(ordered, teamRoleIds, roleId.Id);
        }

        return ordered;
    }

    private List<string> GetOrderedCoreRoleIds(
        IReadOnlyCollection<string> teamRoleIds,
        IReadOnlyCollection<ProtoId<JobPrototype>> configuredCoreRoleIds,
        IReadOnlyCollection<string> officerRoleIds,
        IReadOnlyCollection<string> mechanicusRoleIds)
    {
        var ordered = new List<string>();
        var excluded = officerRoleIds.Concat(mechanicusRoleIds).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var roleId in configuredCoreRoleIds.Select(x => x.Id))
        {
            if (excluded.Contains(roleId))
                continue;

            AddRoleIdIfPresent(ordered, teamRoleIds, roleId);
        }

        foreach (var roleId in teamRoleIds.OrderBy(GetRoleDisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (excluded.Contains(roleId))
                continue;

            AddRoleIdIfPresent(ordered, teamRoleIds, roleId);
        }

        return ordered;
    }

    private WH40KTeamCompositionRoleEntry[] BuildRoleEntries(
        IReadOnlyCollection<string> roleIds,
        Dictionary<string, int> roleIdCounts)
    {
        if (roleIds.Count == 0)
            return Array.Empty<WH40KTeamCompositionRoleEntry>();

        var entries = new List<WH40KTeamCompositionRoleEntry>(roleIds.Count);
        foreach (var roleId in roleIds)
        {
            entries.Add(new WH40KTeamCompositionRoleEntry(
                GetRoleDisplayName(roleId),
                Math.Max(0, roleIdCounts.GetValueOrDefault(roleId))));
        }

        return entries.ToArray();
    }

    private WH40KTeamCompositionMemberEntry[] BuildMemberEntries(
        IReadOnlyCollection<TeamMemberInfo> members,
        IReadOnlyCollection<string> officerRoleIds,
        IReadOnlyCollection<string> coreRoleIds,
        IReadOnlyCollection<string> mechanicusRoleIds)
    {
        if (members.Count == 0)
            return Array.Empty<WH40KTeamCompositionMemberEntry>();

        var roleOrder = new Dictionary<string, (int GroupOrder, int RoleOrder)>(StringComparer.Ordinal);
        var order = 0;
        foreach (var roleId in officerRoleIds)
            roleOrder[roleId] = (0, order++);

        order = 0;
        foreach (var roleId in coreRoleIds)
            roleOrder[roleId] = (1, order++);

        order = 0;
        foreach (var roleId in mechanicusRoleIds)
            roleOrder[roleId] = (2, order++);

        var ordered = members
            .OrderBy(member =>
            {
                if (roleOrder.TryGetValue(member.RoleId, out var rolePos))
                    return rolePos.GroupOrder;

                return 3;
            })
            .ThenBy(member =>
            {
                if (roleOrder.TryGetValue(member.RoleId, out var rolePos))
                    return rolePos.RoleOrder;

                return int.MaxValue;
            })
            .ThenBy(member => member.RoleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .Select(member => new WH40KTeamCompositionMemberEntry(member.Name, member.RoleName))
            .ToArray();

        return ordered;
    }

    private void AppendRoleGroupLines(
        List<string> lines,
        string groupName,
        IReadOnlyCollection<WH40KTeamCompositionRoleEntry> roles)
    {
        lines.Add(groupName);

        if (roles.Count == 0)
        {
            lines.Add(Loc.GetString("w40k-cmd-team-composition-empty"));
            return;
        }

        foreach (var role in roles)
        {
            lines.Add(Loc.GetString("w40k-cmd-team-composition-role-line",
                ("role", role.RoleName),
                ("count", role.Count)));
        }
    }

    private static void AddRoleIdIfPresent(List<string> target, IReadOnlyCollection<string> teamRoleIds, string roleId)
    {
        if (!teamRoleIds.Contains(roleId, StringComparer.OrdinalIgnoreCase) ||
            target.Contains(roleId, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        target.Add(roleId);
    }

    private bool TryResolveTeamCompositionProfileForTeam(
        string teamId,
        out WH40KCommandTeamCompositionProfilePrototype profile)
    {
        profile = default!;
        var profileId = ResolveTeamCompositionProfileIdForTeam(teamId);
        if (_proto.TryIndex(profileId, out WH40KCommandTeamCompositionProfilePrototype? indexedProfile))
        {
            profile = indexedProfile;
            return true;
        }

        if (_proto.TryIndex(TeamCompositionDefaultProfileId, out WH40KCommandTeamCompositionProfilePrototype? fallbackProfile))
        {
            profile = fallbackProfile;
            return true;
        }

        return false;
    }

    private string ResolveTeamCompositionProfileIdForTeam(string teamId)
    {
        if (!_proto.TryIndex(TeamCompositionTeamMapId, out WH40KCommandTeamCompositionTeamMapPrototype? teamMap))
            return TeamCompositionDefaultProfileId;

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

    private bool TryResolveTeamIdentityProfileForTeam(
        string teamId,
        out WH40KTeamIdentityProfilePrototype profile)
    {
        profile = default!;
        var profileId = ResolveTeamIdentityProfileIdForTeam(teamId);
        if (_proto.TryIndex(profileId, out WH40KTeamIdentityProfilePrototype? indexedProfile))
        {
            profile = indexedProfile;
            return true;
        }

        if (_proto.TryIndex(TeamIdentityDefaultProfileId, out WH40KTeamIdentityProfilePrototype? fallbackProfile))
        {
            profile = fallbackProfile;
            return true;
        }

        return false;
    }

    private ProtoId<WH40KTeamIdentityProfilePrototype> ResolveTeamIdentityProfileIdForTeam(string teamId)
    {
        if (!_proto.TryIndex(TeamIdentityMapId, out WH40KTeamIdentityMapPrototype? teamMap))
            return TeamIdentityDefaultProfileId;

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

    private bool TryResolveOreExtractorIntelProfileForTeam(
        string teamId,
        out WH40KCommandOreExtractorIntelProfilePrototype profile)
    {
        profile = default!;
        var profileId = ResolveOreExtractorIntelProfileIdForTeam(teamId);
        if (_proto.TryIndex(profileId, out WH40KCommandOreExtractorIntelProfilePrototype? indexedProfile))
        {
            profile = indexedProfile;
            return true;
        }

        if (_proto.TryIndex(OreExtractorIntelDefaultProfileId, out WH40KCommandOreExtractorIntelProfilePrototype? fallbackProfile))
        {
            profile = fallbackProfile;
            return true;
        }

        return false;
    }

    private string ResolveOreExtractorIntelProfileIdForTeam(string teamId)
    {
        if (!_proto.TryIndex(OreExtractorIntelTeamMapId, out WH40KCommandOreExtractorIntelTeamMapPrototype? teamMap))
            return OreExtractorIntelDefaultProfileId;

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

    private IReadOnlyCollection<ProtoId<JobPrototype>> GetTeamRoleIds(string teamId)
    {
        var roles = new HashSet<ProtoId<JobPrototype>>();
        if (!_teamRule.TryGetTeamDepartments(teamId, out var departments))
            return roles;

        foreach (var departmentId in departments)
        {
            if (!_proto.TryIndex<DepartmentPrototype>(departmentId, out var department))
                continue;

            foreach (var roleId in department.Roles)
            {
                roles.Add(roleId);
            }
        }

        return roles;
    }

    private (string RoleId, string RoleName) GetRoleInfo(EntityUid attached, string teamId)
    {
        if (_mind.TryGetMind(attached, out var mindId, out _) &&
            _jobs.MindTryGetJobId(mindId, out var jobId) &&
            jobId != null)
        {
            var roleId = jobId.Value;
            if (_proto.TryIndex<JobPrototype>(jobId.Value, out var job))
                return (roleId, job.Name);

            return (roleId, roleId);
        }

        var fallbackRole = GetDefaultLineRoleId(teamId);
        return (fallbackRole, GetRoleDisplayName(fallbackRole));
    }

    private string GetRoleDisplayName(string roleId)
    {
        if (_proto.TryIndex<JobPrototype>(roleId, out var job))
            return job.Name;

        return roleId;
    }

    private static float GetPassiveIntervalSeconds(WH40KCommandNodeComponent component)
    {
        var baseInterval = Math.Max(1f, component.PassivePointIntervalSeconds);
        var minInterval = Math.Max(1f, component.PassivePointMinIntervalSeconds);
        var reduction = Math.Max(0f, component.PassiveIntervalReductionPerUpgradeSeconds) *
                        Math.Max(0, component.UpgradeLevel);

        return Math.Max(minInterval, baseInterval - reduction);
    }

    private static int GetPassiveFrontPointGain(WH40KCommandNodeComponent component)
    {
        var baseAmount = Math.Max(1, component.PassiveFrontPointsPerInterval);
        var upgradeBonus = Math.Max(0, component.UpgradeLevel) / 2;
        return Math.Max(1, baseAmount + upgradeBonus);
    }

    private void GrantPassiveFallbackIncome(EntityUid sourceUid, WH40KCommandNodeComponent component)
    {
        if (string.IsNullOrWhiteSpace(component.TeamId))
            return;

        var teamXpAndInfluence = GetPassiveFrontPointGain(component);
        if (teamXpAndInfluence <= 0)
            return;

        _teamRule.TryAdjustTeamXp(component.TeamId, teamXpAndInfluence, out _, out _, out _, "command-node-passive");
        _teamRule.TryAdjustTeamInfluence(component.TeamId, teamXpAndInfluence, out _, out _, "command-node-passive");

        var funds = WH40KCommandEconomyCalculator.GetPassiveFallbackFundsReward(teamXpAndInfluence);
        if (funds > 0)
            TryAdjustTeamFunds(sourceUid, component.TeamId, funds, "command-node-passive");
    }

    private string GetDefaultLineRoleId(string teamId)
    {
        if (TryResolveReinforcementProfileForTeam(teamId, out var profile))
        {
            foreach (var option in profile.Options)
            {
                return option.Job.Id;
            }
        }

        return teamId;
    }

    private string GetMemberName(EntityUid attached)
    {
        if (_mind.TryGetMind(attached, out _, out var mind) &&
            !string.IsNullOrWhiteSpace(mind.CharacterName))
        {
            return mind.CharacterName;
        }

        var entityName = MetaData(attached).EntityName;
        if (!string.IsNullOrWhiteSpace(entityName))
            return entityName;

        return Loc.GetString("w40k-cmd-team-composition-role-unknown");
    }

    private WH40KCommandNodeBonusIntelState BuildBonusIntel(string teamId, WH40KCommandNodeComponent node)
    {
        var effectiveLevel = GetEffectiveTeamTierLevel(teamId);
        var treeBonuses = _treeBonuses.GetTeamBonuses(teamId);

        var hasEngineering = TryGetEngineeringIntel(
            teamId,
            effectiveLevel,
            out var engineeringTier,
            out var engineeringSpeedBonusPercent,
            out var engineeringMinProcessSeconds,
            out var engineeringMaterialStorageLimit,
            out var engineeringGlobalTimeMultiplier);

        var hasOreExtractor = TryGetOreExtractorIntel(
            teamId,
            effectiveLevel,
            out var oreExtractorTier,
            out var oreExtractorSpawnIntervalSeconds,
            out var oreExtractorSpawnCount,
            out var oreExtractorAllowedOreNames);

        var hasLogistics = TryGetLogisticsIntel(
            teamId,
            out var logisticsTier,
            out var logisticsTierMaxItemsBonus,
            out var logisticsTierDeliveryReductionMinutes,
            out var logisticsExternalDeliverySpeedBonusPercent,
            out var logisticsExternalMaxItemsBonusPercent,
            out var logisticsExternalPriceDiscountPercent);

        var hasSpecialLathe = TryGetSpecialLatheIntel(
            teamId,
            effectiveLevel,
            out var specialLatheTier,
            out var specialLatheSpeedBonusPercent,
            out var specialLatheProcessSeconds,
            out var specialLatheMaterialStorageLimit,
            out var specialLatheOutputMultiplier);

        var machineSpeedBonusPercent = Math.Max(0, treeBonuses.MachineSpeedBonusPercent);
        var machineStorageBonus = Math.Max(0, treeBonuses.MachineStorageBonus);

        if (hasEngineering)
        {
            engineeringSpeedBonusPercent += machineSpeedBonusPercent;
            engineeringMinProcessSeconds = ApplySpeedBonusToSeconds(engineeringMinProcessSeconds, machineSpeedBonusPercent);
            engineeringGlobalTimeMultiplier = ApplySpeedBonusToMultiplier(engineeringGlobalTimeMultiplier, machineSpeedBonusPercent);
            engineeringMaterialStorageLimit = Math.Max(0, engineeringMaterialStorageLimit + machineStorageBonus);
        }

        if (hasSpecialLathe)
        {
            specialLatheSpeedBonusPercent += machineSpeedBonusPercent;
            specialLatheProcessSeconds = ApplySpeedBonusToSeconds(specialLatheProcessSeconds, machineSpeedBonusPercent);
            specialLatheMaterialStorageLimit = Math.Max(0, specialLatheMaterialStorageLimit + machineStorageBonus);
        }

        return new WH40KCommandNodeBonusIntelState(
            hasEngineering,
            engineeringTier,
            engineeringSpeedBonusPercent,
            engineeringMinProcessSeconds,
            engineeringMaterialStorageLimit,
            engineeringGlobalTimeMultiplier,
            hasOreExtractor,
            oreExtractorTier,
            oreExtractorSpawnIntervalSeconds,
            oreExtractorSpawnCount,
            oreExtractorAllowedOreNames,
            hasLogistics,
            logisticsTier,
            logisticsTierMaxItemsBonus,
            logisticsTierDeliveryReductionMinutes,
            logisticsExternalDeliverySpeedBonusPercent,
            logisticsExternalMaxItemsBonusPercent,
            logisticsExternalPriceDiscountPercent,
            hasSpecialLathe,
            specialLatheTier,
            specialLatheSpeedBonusPercent,
            specialLatheProcessSeconds,
            specialLatheMaterialStorageLimit,
            specialLatheOutputMultiplier,
            GetPassiveFrontPointGain(node),
            GetPassiveIntervalSeconds(node));
    }

    private bool TryGetEngineeringIntel(
        string teamId,
        int effectiveLevel,
        out int tier,
        out int speedBonusPercent,
        out float minProcessSeconds,
        out int materialStorageLimit,
        out float globalTimeMultiplier)
    {
        tier = 0;
        speedBonusPercent = 0;
        minProcessSeconds = 0f;
        materialStorageLimit = 0;
        globalTimeMultiplier = 1f;

        var found = false;
        var bestTier = -1;
        var bestSpeedBonus = int.MinValue;
        var bestMinProcessSeconds = float.MaxValue;

        var query = EntityQueryEnumerator<WH40KTieredLatheProcessingComponent>();
        while (query.MoveNext(out _, out var processing))
        {
            if (!TracksTeam(processing.TeamIds, processing.TeamId, teamId))
                continue;

            // "Engineering" card tracks generic faction machine loop only.
            // Specialized lathes with tiered recipe packs are shown in their own card.
            if (HasTieredRecipePacks(processing))
                continue;

            var currentTier = SelectTier(
                effectiveLevel,
                processing.Tier1MinBaseLevel,
                processing.Tier2MinBaseLevel,
                processing.Tier3MinBaseLevel);

            var currentMinProcess = Math.Max(0f, currentTier switch
            {
                3 => processing.MinProcessSecondsTier3,
                2 => processing.MinProcessSecondsTier2,
                1 => processing.MinProcessSecondsTier1,
                _ => processing.MinProcessSecondsTier0
            });
            var currentGlobalMultiplier = Math.Max(0.01f, processing.GlobalTimeMultiplier);
            var currentSpeedBonus = CalculateSpeedBonusPercent(processing.MinProcessSecondsTier0, currentMinProcess);

            if (found)
            {
                if (currentTier < bestTier)
                    continue;

                if (currentTier == bestTier && currentSpeedBonus < bestSpeedBonus)
                    continue;

                if (currentTier == bestTier &&
                    currentSpeedBonus == bestSpeedBonus &&
                    currentMinProcess > bestMinProcessSeconds)
                {
                    continue;
                }
            }

            tier = currentTier;
            speedBonusPercent = currentSpeedBonus;
            minProcessSeconds = currentMinProcess;
            materialStorageLimit = 0;
            globalTimeMultiplier = currentGlobalMultiplier;
            bestTier = currentTier;
            bestSpeedBonus = currentSpeedBonus;
            bestMinProcessSeconds = currentMinProcess;
            found = true;
        }

        return found;
    }

    private bool TryGetOreExtractorIntel(
        string teamId,
        int effectiveLevel,
        out int tier,
        out float spawnIntervalSeconds,
        out int spawnCount,
        out string allowedOreNames)
    {
        var fallbackProfileAvailable = TryResolveOreExtractorIntelProfileForTeam(teamId, out var fallbackProfile);
        if (fallbackProfileAvailable)
        {
            // Team fallback profile is pre-applied, then replaced by live extractor data if found.
            var fallbackTier = SelectTier(
                effectiveLevel,
                fallbackProfile.Tier1MinBaseLevel,
                fallbackProfile.Tier2MinBaseLevel,
                fallbackProfile.Tier3MinBaseLevel);

            tier = fallbackTier;
            spawnIntervalSeconds = GetOreExtractorSpawnIntervalSeconds(fallbackTier, fallbackProfile);
            spawnCount = GetOreExtractorSpawnCount(fallbackTier, fallbackProfile);
            allowedOreNames = BuildAllowedExtractorOreNames(fallbackProfile, fallbackTier);
        }
        else
        {
            tier = 0;
            spawnIntervalSeconds = 0f;
            spawnCount = 0;
            allowedOreNames = Loc.GetString("w40k-cmd-tactical-bonuses-no-ores");
        }

        var found = false;
        var bestTier = -1;
        var bestAllowedCount = -1;
        var query = EntityQueryEnumerator<WH40KOreExtractorComponent>();
        while (query.MoveNext(out _, out var extractor))
        {
            if (!TracksTeam(extractor.TeamIds, extractor.TeamId, teamId))
                continue;

            var currentTier = SelectTier(
                effectiveLevel,
                extractor.Tier1MinBaseLevel,
                extractor.Tier2MinBaseLevel,
                extractor.Tier3MinBaseLevel);

            var currentInterval = MathF.Max(0.1f, currentTier switch
            {
                3 => extractor.SpawnIntervalTier3,
                2 => extractor.SpawnIntervalTier2,
                1 => extractor.SpawnIntervalTier1,
                _ => extractor.SpawnIntervalTier0
            });

            var currentSpawnCount = Math.Max(1, currentTier switch
            {
                3 => extractor.SpawnCountTier3,
                2 => extractor.SpawnCountTier2,
                1 => extractor.SpawnCountTier1,
                _ => extractor.SpawnCountTier0
            });

            var currentAllowedCount = GetAllowedExtractorOreCount(extractor, currentTier);
            if (found)
            {
                if (currentTier < bestTier)
                    continue;

                if (currentTier == bestTier && currentAllowedCount < bestAllowedCount)
                    continue;
            }

            tier = currentTier;
            spawnIntervalSeconds = currentInterval;
            spawnCount = currentSpawnCount;
            allowedOreNames = BuildAllowedExtractorOreNames(extractor, currentTier);
            bestTier = currentTier;
            bestAllowedCount = currentAllowedCount;
            found = true;
        }

        return found || fallbackProfileAvailable;
    }

    private bool TryGetLogisticsIntel(
        string teamId,
        out int tier,
        out int tierMaxItemsBonus,
        out int tierDeliveryReductionMinutes,
        out int externalDeliverySpeedBonusPercent,
        out int externalMaxItemsBonusPercent,
        out int externalPriceDiscountPercent)
    {
        tier = 0;
        tierMaxItemsBonus = 0;
        tierDeliveryReductionMinutes = 0;
        externalDeliverySpeedBonusPercent = 0;
        externalMaxItemsBonusPercent = 0;
        externalPriceDiscountPercent = 0;

        var found = false;
        var bestTier = -1;

        var query = EntityQueryEnumerator<CargoLogisticsTierComponent>();
        while (query.MoveNext(out _, out var logistics))
        {
            foreach (var (account, accountTeamId) in logistics.AccountTeams)
            {
                if (!string.Equals(accountTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var currentTier = logistics.GetTier(account);
                if (found && currentTier < bestTier)
                    continue;

                tier = currentTier;
                tierMaxItemsBonus = logistics.GetTierMaxItemsBonus(currentTier);
                tierDeliveryReductionMinutes = logistics.GetTierDeliveryReductionSeconds(currentTier) / 60;
                externalDeliverySpeedBonusPercent = RoundToInt(logistics.GetExternalDeliverySpeedBonusPercent(account));
                externalMaxItemsBonusPercent = RoundToInt(logistics.GetExternalMaxItemsBonusPercent(account));
                externalPriceDiscountPercent = RoundToInt(logistics.GetExternalPriceDiscountPercent(account));
                bestTier = currentTier;
                found = true;
            }
        }

        return found;
    }

    private bool TryGetSpecialLatheIntel(
        string teamId,
        int effectiveLevel,
        out int tier,
        out int speedBonusPercent,
        out float processSeconds,
        out int materialStorageLimit,
        out int outputMultiplier)
    {
        tier = 0;
        speedBonusPercent = 0;
        processSeconds = 0f;
        materialStorageLimit = 0;
        outputMultiplier = 1;

        var found = false;
        var bestTier = -1;
        var bestOutputMultiplier = int.MinValue;
        var bestSpeedBonus = int.MinValue;

        var query = EntityQueryEnumerator<WH40KTieredLatheProcessingComponent>();
        while (query.MoveNext(out _, out var processing))
        {
            if (!TracksTeam(processing.TeamIds, processing.TeamId, teamId))
                continue;

            if (!HasTieredRecipePacks(processing))
                continue;

            var currentTier = SelectTier(
                effectiveLevel,
                processing.Tier1MinBaseLevel,
                processing.Tier2MinBaseLevel,
                processing.Tier3MinBaseLevel);

            var currentProcessSeconds = GetSpecialLatheProcessSecondsForTier(processing, currentTier);
            var currentOutputMultiplier = GetSpecialLatheOutputMultiplierForTier(processing, currentTier);
            var tier0Seconds = GetSpecialLatheProcessSecondsForTier(processing, 0);
            var currentSpeedBonus = CalculateSpeedBonusPercent(tier0Seconds, currentProcessSeconds);

            if (found)
            {
                if (currentTier < bestTier)
                    continue;

                if (currentTier == bestTier && currentOutputMultiplier < bestOutputMultiplier)
                    continue;

                if (currentTier == bestTier &&
                    currentOutputMultiplier == bestOutputMultiplier &&
                    currentSpeedBonus < bestSpeedBonus)
                {
                    continue;
                }
            }

            tier = currentTier;
            speedBonusPercent = currentSpeedBonus;
            processSeconds = currentProcessSeconds;
            materialStorageLimit = 0;
            outputMultiplier = currentOutputMultiplier;
            bestTier = currentTier;
            bestOutputMultiplier = currentOutputMultiplier;
            bestSpeedBonus = currentSpeedBonus;
            found = true;
        }

        return found;
    }

    private int GetEffectiveTeamTierLevel(string teamId)
    {
        var baseLevel = 1;
        if (_teamRule.TryGetTeamProgress(teamId, out var currentLevel, out _, out _))
            baseLevel = Math.Max(1, currentLevel);

        return baseLevel + GetTeamNodeUpgrade(teamId);
    }

    private int GetTeamNodeUpgrade(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return 0;

        var best = 0;
        var query = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out _, out var node))
        {
            if (!string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            best = Math.Max(best, Math.Max(0, node.UpgradeLevel));
        }

        return best;
    }

    private static bool TracksTeam(IReadOnlyCollection<string> teamIds, string teamId, string targetTeamId)
    {
        if (teamIds.Count > 0)
            return teamIds.Any(id => string.Equals(id, targetTeamId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(teamId))
            return string.Equals(teamId, targetTeamId, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static int SelectTier(int level, int tier1MinBaseLevel, int tier2MinBaseLevel, int tier3MinBaseLevel)
    {
        if (level >= Math.Max(1, tier3MinBaseLevel))
            return 3;

        if (level >= Math.Max(1, tier2MinBaseLevel))
            return 2;

        if (level >= Math.Max(1, tier1MinBaseLevel))
            return 1;

        return 0;
    }

    private static int CalculateSpeedBonusPercent(float baselineSeconds, float currentSeconds)
    {
        if (baselineSeconds <= 0.001f || currentSeconds <= 0.001f)
            return 0;

        var percent = ((baselineSeconds / currentSeconds) - 1f) * 100f;
        if (percent < 0f)
            percent = 0f;

        return (int)Math.Round(percent, MidpointRounding.AwayFromZero);
    }

    private static float GetOreExtractorSpawnIntervalSeconds(
        int tier,
        WH40KCommandOreExtractorIntelProfilePrototype profile)
    {
        return MathF.Max(0.1f, tier switch
        {
            3 => profile.SpawnIntervalTier3,
            2 => profile.SpawnIntervalTier2,
            1 => profile.SpawnIntervalTier1,
            _ => profile.SpawnIntervalTier0
        });
    }

    private static int GetOreExtractorSpawnCount(
        int tier,
        WH40KCommandOreExtractorIntelProfilePrototype profile)
    {
        return Math.Max(1, tier switch
        {
            3 => profile.SpawnCountTier3,
            2 => profile.SpawnCountTier2,
            1 => profile.SpawnCountTier1,
            _ => profile.SpawnCountTier0
        });
    }

    private static int GetAllowedExtractorOreCount(WH40KOreExtractorComponent extractor, int tier)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddOreIds(ids, extractor.Tier0Ores);
        if (tier >= 1)
            AddOreIds(ids, extractor.Tier1Ores);
        if (tier >= 2)
            AddOreIds(ids, extractor.Tier2Ores);
        if (tier >= 3)
            AddOreIds(ids, extractor.Tier3Ores);
        return ids.Count;
    }

    private string BuildAllowedExtractorOreNames(WH40KOreExtractorComponent extractor, int tier)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddOreIds(ids, extractor.Tier0Ores);
        if (tier >= 1)
            AddOreIds(ids, extractor.Tier1Ores);
        if (tier >= 2)
            AddOreIds(ids, extractor.Tier2Ores);
        if (tier >= 3)
            AddOreIds(ids, extractor.Tier3Ores);

        if (ids.Count == 0)
            return Loc.GetString("w40k-cmd-tactical-bonuses-no-ores");

        var oreNames = ids
            .Select(GetOreDisplayNameForIntel)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (oreNames.Length == 0)
            return Loc.GetString("w40k-cmd-tactical-bonuses-no-ores");

        return string.Join(", ", oreNames);
    }

    private string BuildAllowedExtractorOreNames(WH40KCommandOreExtractorIntelProfilePrototype profile, int tier)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddOreIds(ids, profile.Tier0Ores);
        if (tier >= 1)
            AddOreIds(ids, profile.Tier1Ores);
        if (tier >= 2)
            AddOreIds(ids, profile.Tier2Ores);
        if (tier >= 3)
            AddOreIds(ids, profile.Tier3Ores);

        if (ids.Count == 0)
            return Loc.GetString("w40k-cmd-tactical-bonuses-no-ores");

        var oreNames = ids
            .Select(GetOreDisplayNameForIntel)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (oreNames.Length == 0)
            return Loc.GetString("w40k-cmd-tactical-bonuses-no-ores");

        return string.Join(", ", oreNames);
    }

    private string GetOreDisplayNameForIntel(string oreId)
    {
        if (!_proto.TryIndex<OrePrototype>(oreId, out var ore) || ore.OreEntity is not { } oreEntity)
            return oreId;

        if (!_proto.TryIndex<EntityPrototype>(oreEntity, out var oreEntityProto))
            return oreId;

        return oreEntityProto.Name;
    }

    private static bool HasTieredRecipePacks(WH40KTieredLatheProcessingComponent processing)
    {
        return processing.Tier0Pack is { } ||
               processing.Tier1Pack is { } ||
               processing.Tier2Pack is { } ||
               processing.Tier3Pack is { };
    }

    private float GetSpecialLatheProcessSecondsForTier(WH40KTieredLatheProcessingComponent processing, int tier)
    {
        var configured = Math.Max(0f, tier switch
        {
            3 => processing.MinProcessSecondsTier3,
            2 => processing.MinProcessSecondsTier2,
            1 => processing.MinProcessSecondsTier1,
            _ => processing.MinProcessSecondsTier0
        });

        if (configured > 0f)
            return configured;

        var packId = SelectSpecialLathePackForTier(processing, tier);
        if (!TryGetPrimaryRecipeFromPack(packId, out var recipeId))
            return 0f;

        if (!_proto.TryIndex(recipeId, out LatheRecipePrototype? recipe))
            return 0f;

        return Math.Max(0f, (float)recipe.CompleteTime.TotalSeconds);
    }

    private int GetSpecialLatheOutputMultiplierForTier(WH40KTieredLatheProcessingComponent processing, int tier)
    {
        var packId = SelectSpecialLathePackForTier(processing, tier);
        if (TryGetPrimaryRecipeFromPack(packId, out var recipeId) &&
            _proto.TryIndex(recipeId, out LatheRecipePrototype? recipe) &&
            recipe.Result is { } resultProto &&
            _proto.TryIndex<EntityPrototype>(resultProto, out var resultEntity) &&
            resultEntity.TryGetComponent<StackComponent>(out var stackComp, EntityManager.ComponentFactory))
        {
            return Math.Max(1, stackComp.Count);
        }

        return 1;
    }

    private static ProtoId<LatheRecipePackPrototype>? SelectSpecialLathePackForTier(
        WH40KTieredLatheProcessingComponent processing,
        int tier)
    {
        var direct = GetSpecialLathePack(processing, tier);
        if (direct is { })
            return direct;

        for (var fallback = tier - 1; fallback >= 0; fallback--)
        {
            var candidate = GetSpecialLathePack(processing, fallback);
            if (candidate is { })
                return candidate;
        }

        for (var fallback = tier + 1; fallback <= 3; fallback++)
        {
            var candidate = GetSpecialLathePack(processing, fallback);
            if (candidate is { })
                return candidate;
        }

        return null;
    }

    private static ProtoId<LatheRecipePackPrototype>? GetSpecialLathePack(
        WH40KTieredLatheProcessingComponent processing,
        int tier)
    {
        return tier switch
        {
            3 => processing.Tier3Pack,
            2 => processing.Tier2Pack,
            1 => processing.Tier1Pack,
            _ => processing.Tier0Pack
        };
    }

    private bool TryGetPrimaryRecipeFromPack(
        ProtoId<LatheRecipePackPrototype>? packId,
        out ProtoId<LatheRecipePrototype> recipeId)
    {
        recipeId = default;

        if (packId is not { } id || !_proto.TryIndex(id, out var pack))
            return false;

        foreach (var recipe in pack.Recipes)
        {
            recipeId = recipe;
            return true;
        }

        return false;
    }

    private static void AddOreIds(HashSet<string> target, IReadOnlyCollection<string> source)
    {
        foreach (var oreId in source)
        {
            if (!string.IsNullOrWhiteSpace(oreId))
                target.Add(oreId);
        }
    }

    private static int RoundToInt(float value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static float ApplySpeedBonusToSeconds(float seconds, int speedBonusPercent)
    {
        if (seconds <= 0.001f || speedBonusPercent <= 0)
            return seconds;

        var multiplier = Math.Max(0.05f, 1f - speedBonusPercent / 100f);
        return MathF.Max(0.1f, seconds * multiplier);
    }

    private static float ApplySpeedBonusToMultiplier(float multiplier, int speedBonusPercent)
    {
        if (multiplier <= 0.001f || speedBonusPercent <= 0)
            return Math.Max(0.01f, multiplier);

        var speedMultiplier = Math.Max(0.05f, 1f - speedBonusPercent / 100f);
        return MathF.Max(0.01f, multiplier * speedMultiplier);
    }
}
