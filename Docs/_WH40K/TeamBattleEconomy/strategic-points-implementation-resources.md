# Strategic Points Implementation Resources

Статус: технический навигатор для будущей реализации. Код и ассеты этим документом не меняются.

Этот файл дополняет [strategic-points-rework.md](strategic-points-rework.md): там описана механика, здесь - какие системы изучать, куда вносить изменения и какие официальные материалы держать под рукой.

## Короткий итог решений

- В фазе 1 делаем сразу все три типа точек: resource, research, influence.
- Фаза 1 - backend/MVP без полного UI точки; полноценный point UI уходит в фазу 2.
- T0 остается постоянным map anchor и никогда не удаляется как часть цикла build/destroy.
- Resource T1-T3 строятся прямо поверх resource T0.
- Influence T1-T3 скрывают sprite T0 и показывают флаг.
- Research T1-T3 строятся рядом с T0 и могут использовать дополнительные visual props/animations.
- Апгрейд в MVP доступен только Mechanicus/Enginseer-линии через отдельный компонент навыка.
- Research points копятся в team research bank даже без живого research server на карте.
- Старые data chips не спавнятся точками и не нужны новой экономике, но сами prototypes остаются как задел.
- Тесты пишутся после всех трех фаз, чтобы не закреплять временный MVP API.

## Официальные ссылки

- SS14 Construction: https://docs.spacestation14.com/en/space-station-14/core-tech/construction.html  
  Нужно для T1 через construction menu, construction graphs, node actions, edge conditions и ограничений initial construction.
- RobustToolbox ECS: https://docs.spacestation14.com/en/robust-toolbox/ecs.html  
  Нужно для правильной формы компонентов, систем, directed events, cancellable events и публичных API между системами.
- Destructible: https://docs.spacestation14.com/en/space-station-14/core-tech/destructible.html  
  Нужно для HP T1/T2/T3, Damageable/Destructible threshold и custom threshold behavior при сбросе точки в T0.
- UI and You: https://docs.spacestation14.com/en/ss14-by-example/ui-and-you.html  
  Нужно для фазы 2: Bound UI, shared UI state/messages, client BUI, XAML windows.
- Sprites and Icons: https://docs.spacestation14.com/en/robust-toolbox/rendering/sprites-and-icons.html  
  Нужно для SpriteComponent layers, runtime visibility, offsets и owner/type визуалов.
- RSI spec: https://docs.spacestation14.com/en/specifications/robust-station-image.html  
  Нужно для переноса `points/` в `.rsi` папки, `meta.json`, states, delays и licensing metadata.
- Entity/prototype example: https://docs.spacestation14.com/en/ss14-by-example/adding-a-simple-bikehorn.html  
  Быстрый пример, как entity prototypes собираются из компонентов в YAML.
- YAML Crash Course: https://docs.spacestation14.com/en/general-development/tips/yaml-crash-course.html  
  Полезно для новых prototype/profile файлов.

## Главные локальные системы

| Зона | Файлы | Что важно |
| --- | --- | --- |
| Team battle state | `Content.Server/_WH40K/GameTicking/Rules/WH40KTeamBattleRuleSystem.cs`, `.../Components/WH40KTeamBattleRuleComponent.cs`, `.../Prototypes/WH40KTeamBattleConfigPrototype.cs` | Сейчас здесь `TeamFrontPoints`, `TeamCommandPoints`, фазы, уровни базы, kill rewards. Нужны API для TeamXP, Influence, team research bank, integer income multipliers. |
| Старые точки | `Content.Server/_WH40K/Influence/WH40KInfluencePointSystem.cs`, `Content.Shared/_WH40K/Influence/WH40KInfluencePointComponent.cs`, `Resources/Prototypes/_WH40K/Entities/Structures/Machines/points.yml` | Это standing-in-radius capture и data-chip reward tick. Для новой системы использовать как источник идей по naming/notifications, но не сохранять capture loop. |
| Construction menu | `Content.Shared/Construction/Prototypes/ConstructionPrototype.cs`, `Content.Shared/Construction/Conditions/IConstructionCondition.cs`, `Content.Server/Construction/ConstructionSystem.Initial.cs`, `Resources/Prototypes/_WH40K/Recipes/Construction/*` | Уже есть `WH40KAllowedTeams`. Для T1 лучше добавить WH40K construction prototypes, shared condition matching T0 и custom completed `IGraphAction` для owner/link. |
| DoAfter | `Content.Shared/DoAfter/DoAfterArgs.cs`, `Content.Shared/DoAfter/DoAfterEvent.cs` | Фаза 2 upgrades: `BreakOnMove = true`, `BreakOnDamage = true`, target point damage не должен отменять do-after. Duplicate guard должен быть по target/event. |
| HP/repair/destruction | `Content.Shared/Repairable/*`, `Content.Shared/Damage/*`, `Content.Server/Destructible/*` | Для built point использовать `Damageable`, `Destructible`, `Repairable`. Через `RepairAttemptEvent` отменять ремонт врагом. Через custom threshold behavior сбрасывать built point в T0. |
| Materials/stacks | `Content.Shared/Stacks/*`, `Content.Server/Materials/MaterialStorageSystem.cs`, `Content.Shared/Construction/Steps/MaterialConstructionGraphStep.cs` | T1 можно делать через construction material steps. Upgrade material insert в фазе 2 лучше делать через stack/material APIs, с частичным списанием нужного количества. |
| Research | `Content.Server/_WH40K/Research/WH40KResearchTeamSystem.cs`, `WH40KResearchPointChipSystem.cs`, `Content.Server/Research/Systems/ResearchSystem*.cs` | Сейчас RP живут на `ResearchServerComponent`. Нужен явный team bank и bridge, где servers становятся клиентами/отображением team bank. |
| Cargo/funds | `Content.Server/_WH40K/SupplyDrop/WH40KSupplyDropSystem.cs`, `Content.Server/_WH40K/Vehicle/Fabrication/WH40KVehicleFabricationSystem.cs`, `Resources/Prototypes/_WH40K/Catalog/Cargo/accounts.yml` | Resource point income начисляет деньги в `WH40KImperium`/`WH40KHeretics` через cargo bank. Нужно вынести team->cargo account helper из admin logic в общий helper. |
| Command node/tree | `Content.Server/_WH40K/Command/WH40KCommandNodeSystem.cs`, `WH40KCommandNodeSystem.Reinforcement.cs`, `Content.Shared/_WH40K/Command/*`, `Resources/Prototypes/_WH40K/Command/*` | Phase 3: command tree price = funds + research, reinforcements = funds + influence, time restriction убрать, base level restriction оставить. |
| Tactical map | `Content.Server/_WH40K/TacticalMap/WH40KTacticalMapSystem.cs`, `Content.Shared/_WH40K/TacticalMap/WH40KTacticalMapUi.cs`, `Content.Client/_WH40K/TacticalMap/UI/*` | Сейчас карта строит markers из `WH40KInfluencePointComponent`. Нужно заменить/расширить на strategic points: owner + type + tier, без HP. |
| Notifications | `Content.Server/_WH40K/Notifications/*`, `Content.Shared/_WH40K/Notifications/WH40KNotificationEvents.cs` | Использовать существующую систему для build/upgrade/destroy notifications. Категория `Point` уже подходит. |
| Admin commands | `Content.Server/_WH40K/GameTicking/Commands/WH40KBattleAdminCommand.cs` | Нужны алиасы/новые команды: `teamxp`, `influence`, `funds`, `researchbank`, `strategicpoint list/set-tier/reset/set-owner`. |

## Где создавать новую систему

Рекомендуемое размещение без изменения движка:

```text
Content.Shared/_WH40K/StrategicPoints
Content.Server/_WH40K/StrategicPoints
Content.Client/_WH40K/StrategicPoints          # фаза 2 UI/visuals
Resources/Prototypes/_WH40K/StrategicPoints
Resources/Prototypes/_WH40K/Entities/Structures/StrategicPoints
Resources/Textures/_WH40K/StrategicPoints
```

Минимальные изменения вне `_WH40K` допустимы только как bridge к уже существующим content-системам SS14, например construction conditions/actions или research-server integration. RobustToolbox/engine не менять.

## Data-driven профиль

Нужен prototype/profile слой, чтобы не хардкодить баланс в C#:

```text
WH40KStrategicPointProfile
- pointType: Resource/Research/Influence
- tiers:
  - tier: 1/2/3
    maxHp
    incomeFunds
    incomeResearch
    incomeInfluence
    incomeTeamXp
    upgradeSeconds
    upgradeMaterials
    buildPrototype
    spriteProfile
```

Отдельно полезны:

```text
WH40KStrategicPointPhaseIncomeProfile
- preparationNumerator: 1
- preparationDenominator: 2
- assaultNumerator: 1
- assaultDenominator: 1
- apocalypseNumerator: 3
- apocalypseDenominator: 1
```

Так Preparation x0.5 остается целочисленной: `grant = (base * numerator + remainder) / denominator`, `remainder = ... % denominator`. Остатки хранить по team/source/currency, а UI показывает только целые значения.

## Фаза 1: путь реализации

1. Добавить shared enums и компоненты:
   - `WH40KStrategicPointType`
   - `WH40KStrategicPointTier`
   - `WH40KStrategicPointAnchorComponent`
   - `WH40KStrategicPointComponent`
   - `WH40KStrategicPointProfilePrototype`

2. Добавить team bank API:
   - `TryAdjustTeamXp`
   - `TryAdjustTeamInfluence`
   - `TrySpendTeamInfluence`
   - `TryAdjustTeamResearchPoints`
   - `TrySpendTeamResearchPoints`
   - `TryGetTeamEconomySnapshot`

3. Сохранить старые поля на время миграции, но публично считать:
   - `TeamFrontPoints` = legacy storage/alias для TeamXP;
   - `TeamCommandPoints` = legacy storage/alias для Influence.

4. Сделать T0 anchor prototypes трех типов и T1 built prototypes трех типов.

5. T1 construction:
   - WH40K construction prototype на каждый тип;
   - `WH40KAllowedTeams` использовать для командного доступа;
   - shared `IConstructionCondition` проверяет nearby/same-tile T0 нужного типа и что anchor свободен;
   - custom `IGraphAction` после construction completion получает `userUid`, определяет team, линкует built point с T0, выставляет owner и запускает income timer.

6. Не использовать construction graph replacement для самого T0. В официальной construction системе node с entity prototype может заменить/удалить сущность, а T0 у нас должен переживать весь матч.

7. Income scheduler:
   - built point хранит `nextIncomeAt`;
   - system начисляет income раз в 10 секунд;
   - phase multiplier применяется rational-методом;
   - base fallback income идет отдельно и не зависит от phase.

8. Выключить старый point income:
   - data-chip spawning с точек отключить;
   - old `WH40KInfluencePointSystem` не должен начислять новую экономику;
   - старые prototypes не удалять в фазе 1, если карты еще на них ссылаются.

9. Минимальная проверка доступа:
   - враг не открывает/не использует чужую точку;
   - чужой terminal/point UI не открывается;
   - point owner определяется только командой строителя.

10. Обновить kill reward:
   - line x1, special x2, CMD x3;
   - выдавать TeamXP + Influence;
   - использовать текущую validated kill логику.

## Фаза 2: что добавится поверх backend

- Upgrade material insertion по клику material stack на точку.
- Upgrade UI/verb с показом type, tier, owner, income, HP, materials.
- Mechanicus-only `WH40KStrategicPointUpgradeSkillComponent`.
- DoAfter T1->T2 30s и T2->T3 60s.
- Repairable welding owner-only через `RepairAttemptEvent`.
- Destruction reset built point -> T0, 50% material refund, team reward.
- Notifications build/upgrade/destroy.
- Tactical map: owner/type/tier, без HP.
- Звуки и sprite visualizer states.

## Фаза 3: миграция экономики

- Command tree spending: funds + research, без XP/influence и без time restriction.
- Reinforcement spending: funds + influence, role unlocks by base level остаются.
- Mission rewards: packages TeamXP/influence/funds/research/tokens.
- Command node UI: TeamXP, Influence, funds, research bank.
- Admin commands: новые названия и point debug commands.
- Deprecated stores/data-chip infrastructure: удалить только после проверки карт, ролей и starting gear.

Phase 3 implementation note:

- shared pricing lives in `Content.Shared/_WH40K/Command/WH40KCommandEconomyCalculator.cs`;
- command tree and command node upgrades use `cost * 35` funds and `cost * 10` team research;
- reinforcements keep the old base influence cost and add `influenceCost * 20` funds;
- mission reward units grant TeamXP/influence directly, plus `points * 35` funds and `points * 10` team research;
- UI strings should talk about influence/funds/research, not command/development points;
- regression coverage starts with `Content.Tests/Shared/_WH40K/Command/WH40KCommandEconomyCalculatorTests.cs`.

## Ассеты

Исходная папка:

```text
points/
```

Наблюдение по ассетам:

- `points/recourses/t0_pit.png` - resource T0, маленькая яма/основание;
- `points/recourses/<team>/t1..t3.png` - resource built visuals, широкие объекты, читаются поверх T0;
- `points/flag/t0.png` - маленькая платформа influence T0;
- `points/flag/<team>/t1..t3.png` - флаги, должны скрывать/замещать T0;
- `points/research/t1..t3/<team>.png` - исследовательские блоки рядом с anchor;
- `points/research/noktolit*.png` и animation files - дополнительные visuals для research point, лучше подключать в фазе 2 polish.

При переносе в `Resources/Textures/_WH40K/StrategicPoints` сделать `.rsi` папки и `meta.json`; проверить размеры через RSI spec. Если у research нет отдельного T0, сделать временный neutral anchor prototype или попросить отдельный sprite.

## Риски

- Нельзя случайно удалить T0 через construction graph node replacement.
- Нельзя оставить старый `AddTeamFrontPoints` как единую выдачу XP+Influence: новые валюты разделены.
- Research bank не должен зависеть от существования research server.
- Enemy access должен проверяться на server side, UI-only блокировки недостаточно.
- Tactical map не должна раскрывать HP.
- Fractional phase income нельзя хранить/показывать дробями; нужен integer remainder.
- Удаление Vox/Altar/store слоев отложить до phase 3 и делать только после поиска по maps/roles/starting gear.
