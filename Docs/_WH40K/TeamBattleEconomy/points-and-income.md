# Очки режима, доход и точки захвата

Этот документ описывает базовую экономику Team Battle: какие очки существуют внутри режима, как они начисляются, что влияет на доход и как работают capture/influence points.

## Основные сущности

### TeamFrontPoints

`TeamFrontPoints` - накопленный прогресс фронта команды.

Используется для:

- расчета `TeamBaseLevel`;
- открытия условий `WH40KMinBaseLevelCondition` в магазинах;
- открытия подкреплений с `minBaseLevel`;
- выбора тиров машин, конвертеров, ore extractor и логистики;
- UI прогресса команды.

Фронтовые очки не тратятся командными действиями. Даже если команда покупает узел дерева или вызывает подкрепление, уровень базы остается прежним, потому что тратятся `TeamCommandPoints`.

### TeamCommandPoints

`TeamCommandPoints` - расходуемый командный бюджет.

Используется для:

- апгрейда командного узла;
- покупки узлов command tree;
- ручных и автоматических подкреплений.

В большинстве источников дохода командные очки начисляются вместе с фронтовыми. Исключения и детали описаны ниже.

### TeamBaseLevel

`TeamBaseLevel` вычисляется из `TeamFrontPoints` по порогам профиля режима.

В активном `WH40KTeamBattleConfig120m`:

| Уровень | Нужно `TeamFrontPoints` |
| --- | ---: |
| 1 | стартовый уровень |
| 2 | 60 |
| 3 | 120 |
| 4 | 180 |
| 5 | 340 |
| 6 | 1300 |
| 7 | 1800 |
| 8 | 2400 |
| 9 | 3200 |

Стартовое значение `TeamFrontPoints` и `TeamCommandPoints` - 20 на команду.

## Фазы и множители

Режим использует фазы:

| Фаза | Когда | Множитель экономики |
| --- | --- | ---: |
| `Preparation` | первые 600 секунд | x1 |
| `Assault` | после подготовки, до 3600 секунд assault duration | x2 |
| `Apocalypse` | поздняя фаза после Assault | x3 |

Множитель применяется только к доходам, которые проходят через `AddTeamFrontPoints`. Это важно:

- пассивный доход command node умножается фазой;
- доход от influence points умножается фазой;
- убийства не умножаются фазой;
- mission development rewards не умножаются фазой;
- team random event periodic rewards не умножаются фазой.

## Источники очков

### Старт раунда

При инициализации команды получают:

- `TeamFrontPoints = 20`;
- `TeamCommandPoints = 20`;
- `TeamBaseLevel = 1`.

### Убийства

За валидированное убийство команда получает `frontPointsPerKill`, в текущем профиле это 1.

Особенность: убийства идут через unscaled-путь. Поэтому награда за kill всегда +1 front и +1 command, независимо от Preparation/Assault/Apocalypse.

Если kill reward позже отзывается, система вычитает соответствующие очки обратно из front и command.

### Пассивный доход command node

Главные командные терминалы `StructureLogisticsConsoleImperiumCmd` и `StructureLogisticsConsoleHereticsCmd` имеют `WH40KCommandNode` с `passiveFrontPointsPerInterval: 2`.

Базовая формула:

- интервал = `75 - upgradeLevel * 5` секунд;
- минимум интервала = 36 секунд;
- gain = `passiveFrontPointsPerInterval + floor(upgradeLevel / 2)`;
- затем gain проходит через фазовый множитель экономики.

Для текущих главных терминалов:

| UpgradeLevel | Интервал | База за тик | Preparation | Assault | Apocalypse |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 75s | 2 | 2 | 4 | 6 |
| 1 | 70s | 2 | 2 | 4 | 6 |
| 2 | 65s | 3 | 3 | 6 | 9 |
| 3 | 60s | 3 | 3 | 6 | 9 |
| 4 | 55s | 4 | 4 | 8 | 12 |

Reinforcement, Upgrade Tree и Mission Board терминалы тоже имеют `WH40KCommandNode`, но в YAML у них `passiveFrontPointsPerInterval: 0`, поэтому сами по себе пассивный доход не дают.

### Точки захвата

Точки реализованы через `WH40KInfluencePoint`.

В текущих прототипах есть:

| Прототип | Радиус | Время захвата | Награда | Спавн чипов |
| --- | ---: | ---: | ---: | --- |
| `MachineChipProduser` | 2.67 | 20s | 1 front base / 120s | `DataChip1` каждые 60s |
| `MachineChipProduserCenter` | 3 | 30s | 2 front base / 120s | `DataChip4` каждые 120s |
| `MachineChipProduserImperiumOwned` | 2.67 | 20s | 1 front base / 120s | как базовая точка |
| `MachineChipProduserHereticsOwned` | 2.67 | 20s | 1 front base / 120s | как базовая точка |

Capture включается с `Assault`. До этой фазы прогресс захвата не идет. Spawner дата-чипов тоже включается с Assault через `WH40KPhaseTimedSpawner`.

Награда владельцу:

```text
baseReward = frontPointsPerInterval
blackFrontMultiplier = 2, если активен BlackFront, иначе 1
phaseMultiplier = 1 / 2 / 3 по текущей фазе
итог = baseReward * blackFrontMultiplier * phaseMultiplier
```

То есть обычная точка в Assault дает +2 front и +2 command каждые 120 секунд, центральная - +4/+4. В Apocalypse обычная точка дает +3/+3, центральная +6/+6. Во время BlackFront эти значения дополнительно удваиваются до применения фазового дохода внутри `AddTeamFrontPoints`.

### Как идет захват

Система считает живых или критических участников команд в радиусе точки.

Правила:

- если ни одна команда не контролирует радиус, текущий прогресс захвата откатывается со скоростью `captureDecayPerSecond`;
- если лидирующие команды равны по численности, точка contested и прогресс замораживается;
- если одна команда лидирует, прогресс растет в ее сторону;
- скорость зависит от превосходства: `topCount - secondCount`, минимум 1, максимум `maxCaptureSpeedMultiplier`;
- по умолчанию максимум множителя скорости равен 3;
- если команда уже владеет точкой, прогресс не копится заново.

Когда прогресс достигает `captureTimeSeconds`, `ownerTeamId` меняется на команду-захватчика, прогресс сбрасывается и таймер награды начинается заново.

### Динамические миссии

Динамические миссии дают development points. Они добавляются в front и command напрямую, без фазового множителя.

Базовые награды `WH40KCommandDynamicMissionConfig`:

| Outcome | Development points |
| --- | ---: |
| Major | 14 |
| Minor | 6 |
| Timeout | 1 |
| Failure | 0 |

Некоторые миссии задают `rewardTempoBonusPercent`; при major outcome к награде добавляется `ceil(reward * percent / 100)`.

Миссии также могут выдавать временные token-эффекты:

- `tactical_call_discount` - сдвигает вперед cooldown тактик/подкреплений;
- `intel_event_roll_haste` - ускоряет следующий roll team random event.

### Team random events

Team random events - отдельный слой командных событий. Некоторые из них влияют на экономику напрямую:

- `logistics_corridor` дает +1 development point каждые 20 секунд;
- `scrap_windfall` дает +1 development point каждые 18 секунд.

Эти points добавляются как mission development points: +front и +command, без фазового множителя.

Другие team events дают косвенные экономические эффекты:

- `relay_overclock` ускоряет cooldown тактик и подкреплений;
- `medicae_push` ускоряет лечение;
- `servitor_rush` ускоряет строительство;
- `fireline_surge`, `iron_discipline`, `suppression_grid`, `counter_battery_window` меняют боевые множители;
- `vox_jamming_pulse` замедляет прогресс миссий врага.

## Модификаторы дохода

### Фазовый множитель

Действует только на `AddTeamFrontPoints`:

- passive command node;
- influence point reward;
- любые будущие источники, если они вызовут `AddTeamFrontPoints`.

Не действует на:

- kills;
- mission development rewards;
- team event periodic development rewards;
- ручные корректировки через `TryAdjustTeamFrontPoints`.

### BlackFront

Round event `BlackFront` включает погодный фронт `WHBlackFront` и ставит `GetInfluenceRewardMultiplier()` в 2. Это влияет только на награды influence points.

### LogisticsSurge

Round event `LogisticsSurge` напрямую front/command очки не дает, но меняет экономику магазинов и действий:

- ammo listings в категориях `VoxAmmo` и `AltarAmmo` получают price multiplier 0.7;
- cooldown store/supply систем может использовать множитель 0.65 через `GetStoreCooldownMultiplier()`;
- construction do-after multiplier 0.65;
- medical do-after multiplier 0.7.

### Баффы за уровни базы

При повышении уровня базы команда получает случайный бафф из пула:

- `Pulling` - игнорирование штрафа скорости при pull;
- `Medical` - medical delay multiplier 0.8;
- `Construction` - construction delay multiplier 0.75.

Эти баффы не дают очки напрямую, но ускоряют действия команды и тем самым влияют на фактическую экономику фронта.

## Практические последствия для баланса

- Пассив command node резко усиливается фазой, поэтому поздняя экономика растет быстрее даже без захвата точек.
- Kill reward остается стабильным +1/+1 всю игру, поэтому на поздних фазах он становится менее важен относительно точек и пассива.
- Центральная точка вдвое сильнее обычной и одновременно спавнит `DataChip4`; это и front/command income, и источник фракционных денег/исследований.
- BlackFront делает удержание точек особенно ценным, потому что удваивает только influence economy.
- Команда может потратить все command points и не потерять base level. Это полезно для агрессивных камбэков через подкрепления и дерево.
