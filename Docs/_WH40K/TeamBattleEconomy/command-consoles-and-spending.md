# Командные консоли, траты и разблокировки

Этот документ описывает командные терминалы Team Battle, куда тратятся `TeamCommandPoints`, что открывают апгрейды и как работают подкрепления, доктрины, тактики и mission board.

## Типы командных и логистических терминалов

Фракционные терминалы описаны в `Resources/Prototypes/_WH40K/Entities/Structures/specific/logistics_consoles.yml`.

| Прототип | Фракция | UI | Экономическая роль |
| --- | --- | --- | --- |
| `StructureLogisticsConsoleImperiumBuy` | Imperium | `CargoConsoleUiKey.Orders` | Покупка cargo products за `WH40KImperium` bank funds |
| `StructureLogisticsConsoleHereticsBuy` | Heretics | `CargoConsoleUiKey.Orders` | Покупка cargo products за `WH40KHeretics` bank funds |
| `StructureLogisticsConsoleImperiumSell` | Imperium | `CargoPalletConsoleUiKey.Sale` | Продажа `DataChipImp` в банк фракции |
| `StructureLogisticsConsoleHereticsSell` | Heretics | `CargoPalletConsoleUiKey.Sale` | Продажа `DataChipChaos` в банк фракции |
| `StructureLogisticsConsoleImperiumCmd` | Imperium | `WH40KCommandNodeUiKey.Key` | Главный command node, пассивный доход, тактики, доктрины, состояние команды |
| `StructureLogisticsConsoleHereticsCmd` | Heretics | `WH40KCommandNodeUiKey.Key` | То же для Heretics |
| `StructureLogisticsConsoleImperiumReinforcement` | Imperium | `WH40KCommandNodeUiKey.Reinforcement` | Запрос подкреплений |
| `StructureLogisticsConsoleHereticsReinforcement` | Heretics | `WH40KCommandNodeUiKey.Reinforcement` | Запрос подкреплений |
| `StructureLogisticsConsoleImperiumUpgradeTree` | Imperium | `WH40KCommandNodeUiKey.UpgradeTree` | Покупка узлов command tree |
| `StructureLogisticsConsoleHereticsUpgradeTree` | Heretics | `WH40KCommandNodeUiKey.UpgradeTree` | Покупка узлов command tree |
| `StructureLogisticsConsoleImperiumMissionBoard` | Imperium | `WH40KCommandNodeUiKey.MissionBoard` | Выбор faction mission task |
| `StructureLogisticsConsoleHereticsMissionBoard` | Heretics | `WH40KCommandNodeUiKey.MissionBoard` | Выбор faction mission task |

Все эти терминалы проверяют командный доступ через `teamId`. Ghost с `CanGhostInteract` может обходить это ограничение.

## Command node upgrade

`WH40KCommandNodeComponent` имеет собственный `UpgradeLevel` от 0 до 4.

Стоимость апгрейда:

```text
cost = UpgradeBaseCost + UpgradeCostStep * currentUpgradeLevel
```

При текущих значениях:

| UpgradeLevel до покупки | Цена | UpgradeLevel после покупки |
| ---: | ---: | ---: |
| 0 | 12 | 1 |
| 1 | 20 | 2 |
| 2 | 28 | 3 |
| 3 | 36 | 4 |

Оплата идет только из `TeamCommandPoints`, source `command-upgrade`.

Что дает upgrade:

- ускоряет пассивный тик command node;
- увеличивает пассивный gain на уровнях 2 и 4;
- добавляется к `TeamBaseLevel` при выборе тира некоторых машин (`effectiveLevel = teamBaseLevel + bestCommandNodeUpgrade`);
- влияет на `WH40KTieredLatheProcessing`, `WH40KOreExtractor` и chip converter-like машины.

Важно: upgrade command node не увеличивает `TeamFrontPoints` напрямую и не считается отдельным base level. Это машинный/командный модификатор поверх уровня базы.

## Command tree

Command tree покупается за `TeamCommandPoints`. Профиль по умолчанию - `WH40KCommandTreeProfileDefault`.

Домены:

- `engineering`;
- `logistics`;
- `research`;
- `weaponry`;
- `equipment`.

Каждый узел может иметь:

- `cost` - базовая цена;
- `parents` - обязательные предыдущие узлы;
- `minBaseLevel` - минимальный `TeamBaseLevel`;
- `minRoundTimeSeconds` - минимальное время раунда;
- `technologyUnlocks` - открытие технологий на research server команды;
- `latheRecipeUnlocks` - открытие lathe recipes;
- `cargoProductUnlocks` / `teamCargoProductUnlocks` - открытие cargo products;
- `researchPointGrant` - прямой grant research points;
- постоянные бонусы: machine speed/storage, cargo speed/capacity/discount, research speed/point bonus.

### Динамическая цена узла

Фактическая цена не всегда равна `cost`. Она считается через `WH40KCommandTreeCostCalculator` и профиль `WH40KCommandTreeCostDefault`.

Профиль:

| Поле | Значение |
| --- | ---: |
| `reserveBasePoints` | 24 |
| `reservePerBaseLevel` | 12 |
| `reserveOverflowStepPoints` | 15 |
| `reserveSurchargePerStep` | 3 |
| `preparationSurchargeCap` | 18 |
| `assaultSurchargeCap` | 15 |
| `apocalypseSurchargeCap` | 9 |
| `preparationCatchupTargetLevel` | 1 |
| `assaultCatchupTargetLevel` | 3 |
| `apocalypseCatchupTargetLevel` | 5 |
| `catchupDiscountPerMissingLevel` | 2 |
| `preparationCatchupDiscountCap` | 0 |
| `assaultCatchupDiscountCap` | 4 |
| `apocalypseCatchupDiscountCap` | 10 |

Смысл:

- если у команды слишком много command points относительно резерва, узлы дорожают;
- если команда отстает по уровню базы от целевого уровня фазы, узлы дешевеют;
- итоговая цена минимум 1.

### Что открывают домены

`engineering`:

- machine speed bonus;
- machine storage bonus;
- ранний unlock faction ammo phosphor.

`logistics`:

- cargo max items bonus percent;
- cargo delivery speed bonus percent;
- capstone также дает cargo price discount percent.

`research`:

- research point grants;
- research point bonus percent;
- research time speed bonus percent.

`weaponry`:

- технологии weapon authorization;
- большие пакеты cargo unlocks для оружия, боеприпасов, турелей и тяжелого вооружения.

`equipment`:

- технологии equipment authorization;
- cargo unlocks для медицины, инженерного снаряжения, мин, гранат, pinpointer/fulton и похожих предметов.

### Доктрина

Доктрина назначается через command node, когда достигнут нужный уровень доктрины. По умолчанию doctrine unlock level - 3.

Назначение доктрины:

- не тратит command points;
- фиксирует выбранную доктрину;
- может заблокировать домен дерева через `LockedDomain`.

### Battle tactic

Battle tactic выбирается через command node.

Особенности:

- прямой цены в command points нет;
- есть cooldown смены 300 секунд;
- cooldown может ускоряться team events или mission tokens.

## Подкрепления

Подкрепления используют отдельный `Reinforcement` UI и профили `WH40KCommandReinforcementProfileImperium` / `WH40KCommandReinforcementProfileHeretics`.

### Ограничения фазы

Ручные и автоматические подкрепления разрешены только в `Assault`.

- В `Preparation` запрос блокируется.
- В `Apocalypse` запрос блокируется.

### Общие ограничения

- ручная заявка приходит через 60 секунд;
- автоматическая заявка приходит через 300 секунд;
- общий cooldown после заявки - 600 секунд;
- максимум 10 единиц в одной заявке;
- у каждой роли есть `maxCount`;
- некоторые роли требуют `minBaseLevel`;
- auto mode можно запретить на уровне опции через `allowAuto`.

### Динамическая цена подкреплений

Цена единицы считается от `baseCost` через кривую режима:

```text
normalized = elapsedRoundTime / curveDuration
multiplier = baseMultiplier + scale * normalized^exponent
unitCost = round(baseCost * multiplier)
```

В текущем профиле:

- `baseMultiplier = 1`;
- `scale = 1.25`;
- `exponent = 2`;
- duration берется из `roundTimeLimitSeconds` и ограничивается 3600..10800 секунд.

При 120m конфиге цена примерно растет от 1.0x в начале до 2.25x к концу раунда.

### Imperium reinforcement profile

| ID | Job | Base cost | Max | Min level |
| --- | --- | ---: | ---: | ---: |
| `guardsman` | `Guardsman` | 18 | 10 | 1 |
| `voxscout` | `VoxScout` | 24 | 3 | 1 |
| `medic` | `Medic` | 26 | 3 | 1 |
| `tithesupplier` | `TitheSupplier` | 20 | 2 | 1 |
| `munitorum-officer` | `MunitorumOfficer` | 28 | 1 | 1 |
| `lexmechanic` | `Lexmechanic` | 28 | 2 | 1 |
| `sergeant` | `Sergeant` | 32 | 3 | 1 |
| `chaplain` | `WH40KChaplain` | 36 | 1 | 1 |
| `psyker` | `Psyker` | 40 | 1 | 1 |
| `kasrkin` | `Kasrkin` | 40 | 5 | 4 |
| `lieutenant` | `Lieutenant` | 42 | 1 | 1 |
| `commissar` | `Commissar` | 46 | 1 | 1 |

### Heretics reinforcement profile

| ID | Job | Base cost | Max | Min level |
| --- | --- | ---: | ---: | ---: |
| `hguardsman` | `HGuardsman` | 18 | 10 | 1 |
| `hbandsupplier` | `HBandSupplier` | 20 | 2 | 1 |
| `hlexmechanic` | `HLexmechanic` | 28 | 2 | 1 |
| `hsergeant` | `HSergeant` | 32 | 3 | 1 |
| `hlieutenant` | `HLieutenant` | 40 | 1 | 1 |
| `HellishForeman` | `HellishForeman` | 40 | 5 | 4 |

Если заявка оплачена, но спавн не удался, command points возвращаются source `reinforcement-refund`.

## Mission Board

Mission Board позволяет выбрать faction mission task из предложенных задач. Назначение задачи само по себе не тратит command points.

Динамические миссии дальше награждают команду через outcome:

- major/minor/timeout/failure development points;
- возможный tempo bonus;
- возможный token reward.

В `mission_board.yml` также есть поля активной награды (`activeRewardBasePoints`, `activeRewardPerBaseLevel`, `activeRewardMinPoints`). По текущему коду они определены в прототипе/данных, но в runtime начисления mission board rewards не используются как отдельный источник очков. Реальный экономический эффект сейчас идет через dynamic mission runtime.

## Team random events в command UI

Team random events живут рядом с command node runtime.

Профиль `WH40KCommandTeamRandomEventProfileDefault`:

- roll interval 480..720 секунд;
- максимум 1 active event на команду;
- anti-repeat включен;
- у событий есть duration/cooldown/allowed phases/tags.

Экономически важные события:

- `logistics_corridor` - mission progress x1.28 и +1 development point каждые 20s;
- `scrap_windfall` - +1 development point каждые 18s;
- `relay_overclock` - ускоряет cooldown подкреплений и battle tactics на 1 секунду в секунду;
- `vox_jamming_pulse` - enemy mission progress x0.82.

## Сводка трат TeamCommandPoints

| Трата | Source | Валюта | Что меняет |
| --- | --- | --- | --- |
| Command node upgrade | `command-upgrade` | `TeamCommandPoints` | UpgradeLevel command node, passive income и effective machine tier |
| Command tree node | `tree-node` | `TeamCommandPoints` | Unlocks, bonuses, cargo/research/lathe products |
| Manual reinforcement | `reinforcement-manual` | `TeamCommandPoints` | Создает pending заявку на подкрепление |
| Auto reinforcement | `reinforcement-auto` | `TeamCommandPoints` | Автоматическая заявка при низком alive percent |

Не тратят command points:

- выбор doctrine;
- выбор battle tactic;
- выбор mission board task;
- получение mission rewards;
- открытие cargo products через уже купленный tree node.
