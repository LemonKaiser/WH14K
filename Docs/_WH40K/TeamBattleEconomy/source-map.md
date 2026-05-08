# Карта исходников экономики Team Battle

Этот файл перечисляет основные места, где реализована экономика WH40K Team Battle. Его удобно использовать как стартовую точку перед изменением баланса.

## Режим и конфиги

- `Content.Server/_WH40K/GameTicking/Rules/WH40KTeamBattleRuleSystem.cs` - центральная система режима: фазы, очки, уровни базы, rewards, phase multipliers, dynamic reinforcement cost, round events.
- `Content.Server/_WH40K/GameTicking/Rules/Components/WH40KTeamBattleRuleComponent.cs` - runtime state режима: команды, front/command points, thresholds, активные события.
- `Content.Server/_WH40K/GameTicking/Rules/Prototypes/WH40KTeamBattleConfigPrototype.cs` - prototypes для economy/points/weather/events/logistics/black front/orbital profiles.
- `Content.Shared/_WH40K/GameMode/WH40KBattlePhase.cs` - enum фаз `Preparation`, `Assault`, `Apocalypse`.
- `Resources/Prototypes/_WH40K/GameRules/wh40k_team_battle.yml` - активный rule prototype `WH40KTeamBattle`.
- `Resources/Prototypes/_WH40K/GameRules/wh40k_team_battle_configs.yml` - профиль `WH40KTeamBattleConfig120m`.

## Точки захвата

- `Content.Shared/_WH40K/Influence/WH40KInfluencePointComponent.cs` - параметры capture point.
- `Content.Server/_WH40K/Influence/WH40KInfluencePointSystem.cs` - логика capture, contested state, decay и periodic reward.
- `Resources/Prototypes/_WH40K/Entities/Structures/Machines/points.yml` - `MachineChipProduser`, `MachineChipProduserCenter`, owned variants, `ImpPointLathe`, `ChPointLathe`.

## Командные терминалы

- `Content.Server/_WH40K/Command/Components/WH40KCommandNodeComponent.cs` - состояние command node: upgrade, passive income, doctrine, tactic, reinforcement settings.
- `Content.Server/_WH40K/Command/WH40KCommandNodeSystem.cs` - UI state, command upgrades, tree purchases, doctrine/tactic/mission board, cargo/research unlock application.
- `Content.Server/_WH40K/Command/WH40KCommandNodeSystem.Reinforcement.cs` - новый reinforcement UI/runtime.
- `Resources/Prototypes/_WH40K/Entities/Structures/specific/logistics_consoles.yml` - все фракционные терминалы логистики, command, sell, reinforcement, upgrade tree и mission board.

## Command tree

- `Content.Shared/_WH40K/Command/WH40KCommandTreePrototype.cs` - структура профиля command tree.
- `Content.Shared/_WH40K/Command/WH40KCommandTreeCostPrototype.cs` - cost profile.
- `Content.Shared/_WH40K/Command/WH40KCommandTreeCostCalculator.cs` - effective price formula.
- `Content.Server/_WH40K/Command/WH40KCommandTreeBonusSystem.cs` - агрегатор постоянных бонусов купленных узлов.
- `Resources/Prototypes/_WH40K/Command/node_tree.yml` - default tree nodes/domains/unlocks/bonuses.
- `Resources/Prototypes/_WH40K/Command/node_tree_cost_profiles.yml` - surcharge/catchup настройки цены.
- `Resources/Prototypes/_WH40K/Command/doctrines.yml` - доктрины.
- `Resources/Prototypes/_WH40K/Command/tactical_presets.yml` - battle tactic presets.

## Подкрепления

- `Content.Shared/_WH40K/Command/WH40KCommandReinforcementPrototype.cs` - reinforcement profiles/options/team map.
- `Resources/Prototypes/_WH40K/Command/reinforcement_profiles.yml` - Imperium/Heretics reinforcement options.
- `Content.Server/_WH40K/Reinforcement/Components/WH40KReinforcementSpawnPointComponent.cs` - spawn points для подкреплений.

## Миссии и team events

- `Content.Shared/_WH40K/Command/WH40KCommandDynamicMissionPrototype.cs` - dynamic mission prototypes.
- `Content.Server/_WH40K/Command/WH40KCommandEventMissionRuntimeSystem.cs` - dynamic mission runtime, development rewards, mission tokens, team random events.
- `Content.Shared/_WH40K/Command/WH40KTeamEventEffectComponent.cs` - временные эффекты team random events на участников.
- `Resources/Prototypes/_WH40K/Command/dynamic_missions.yml` - mission configs и rewards.
- `Resources/Prototypes/_WH40K/Command/mission_board.yml` - mission board display/selectable task config.
- `Resources/Prototypes/_WH40K/Command/team_random_events.yml` - team random event profile.

## Карго, деньги и магазины

- `Resources/Prototypes/_WH40K/Entities/Stations/wh40k_station.yml` - station bank accounts, cargo order DB, initial unlocks, logistics tier mapping.
- `Resources/Prototypes/_WH40K/Catalog/Cargo/accounts.yml` - cargo account prototypes.
- `Resources/Prototypes/_WH40K/Catalog/Cargo/markets.yml` - markets.
- `Resources/Prototypes/_WH40K/Catalog/Cargo/products.yml` - WH40K cargo products.
- `Resources/Prototypes/_WH40K/Catalog/Cargo/vehicle_products.yml` - vehicle cargo products.
- `Resources/Prototypes/_WH40K/Store/currency.yml` - `Intelligence`, `intelligence_imp`, `intelligence_ch`, `WH40KFactionFunds`.
- `Resources/Prototypes/_WH40K/Catalog/vox_catalog.yml` - Imperium intelligence store listings.
- `Resources/Prototypes/_WH40K/Catalog/altar_catalog.yml` - Heretics intelligence store listings.
- `Content.Server/_WH40K/Store/WH40KStoreAccessSystem.cs` - team access for stores.
- `Content.Server/_WH40K/Store/Conditions/WH40KMinBaseLevelCondition.cs` - listing gate by base level.
- `Content.Server/_WH40K/Store/Conditions/WH40KMinPhaseCondition.cs` - listing gate by battle phase.

## Дата-чипы и исследования

- `Resources/Prototypes/_WH40K/Entities/Specific/intelligence.yml` - `DataChip`, `DataChipImp`, stack price/currency/research point chip.
- `Resources/Prototypes/_WH40K/Entities/Specific/intelligence_ch.yml` - `DataChipChaos`.
- `Content.Server/_WH40K/Research/WH40KResearchPointChipSystem.cs` - upload chips into team research.
- `Content.Server/_WH40K/Research/WH40KResearchTeamSystem.cs` - team-bound research access/server ownership.
- `Content.Server/_WH40K/Research/Components/WH40KResearchPointChipComponent.cs` - points per chip.
- `Content.Server/_WH40K/Research/Components/WH40KResearchTeamComponent.cs` - research team id.

## Машины, тиры и extractor

- `Resources/Prototypes/_WH40K/tier_profiles.yml` - standard thresholds/machine/logistics profiles.
- `Content.Server/_WH40K/Store/WH40KTieredLatheProcessingSystem.cs` - tiered lathe runtime.
- `Content.Server/_WH40K/Store/Components/WH40KTieredLatheProcessingComponent.cs` - tiered lathe fields.
- `Content.Server/_WH40K/Store/WH40KChipConverterSystem.cs` - chip converter tier sync, если используется отдельными конвертерами.
- `Content.Server/_WH40K/OreExtractor/WH40KOreExtractorSystem.cs` - ore extractor tiering/spawn runtime.
- `Content.Server/_WH40K/OreExtractor/Components/WH40KOreExtractorComponent.cs` - ore extractor fields.
- `Resources/Prototypes/_WH40K/Entities/Structures/Machines/ore_extractor.yml` - ore extractor entities.
- `Resources/Prototypes/_WH40K/Entities/Structures/Machines/lathes.yml` - WH40K lathes/research clients.

## Supply drop и транспорт

- `Content.Server/_WH40K/SupplyDrop/WH40KSupplyDropSystem.cs` - pads и Vox supply drop store; списание bank funds.
- `Content.Shared/_WH40K/SupplyDrop/WH40KSupplyDropPadComponent.cs` - supply drop pad fields.
- `Content.Shared/_WH40K/SupplyDrop/WH40KVoxSupplyDropStoreComponent.cs` - backpack/store fields.
- `Resources/Prototypes/_WH40K/Catalog/vox_supplydrop_catalog.yml` - `WH40KFactionFunds` supply-drop listings.
- `Resources/Prototypes/_WH40K/Entities/Clothing/Back/backpacks.yml` - Vox/Chaos backpack supply-drop stores.
- `Content.Server/_WH40K/Vehicle/Fabrication/WH40KVehicleFabricationSystem.cs` - vehicle fabrication, bank cost, material/part checks, queue/refund.
- `Content.Shared/_WH40K/Vehicle/Fabrication/WH40KVehicleFabricationComponents.cs` - vehicle fabrication component state.
- `Resources/Prototypes/_WH40K/Entities/Structures/Machines/Computers/vehicle_fabrication.yml` - vehicle fabrication consoles.
- `Resources/Prototypes/_WH40K/Vehicle/vehicle_recipes.yml` - vehicle recipes.
