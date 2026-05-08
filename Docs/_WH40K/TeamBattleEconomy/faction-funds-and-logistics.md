# Фракционные деньги, дата-чипы и логистика

Этот документ описывает производные экономики Team Battle: карго-аккаунты фракций, дата-чипы, магазины Vox/Altar, research points, supply drop, логистические тиры, машины и транспорт.

## Карго-аккаунты фракций

Станция `WH40KStation` заводит два банковских аккаунта:

| Account | Стартовый баланс |
| --- | ---: |
| `WH40KImperium` | 12000 |
| `WH40KHeretics` | 12000 |

`increasePerSecond: 0`, то есть пассивного банковского дохода станции нет.

Эти деньги тратятся через:

- cargo buy consoles;
- Vox supply-drop store/backpack, где `WH40KFactionFunds` отображает баланс account;
- vehicle fabrication consoles.

Пополняются они в основном через продажу дата-чипов в cargo sell console.

## Cargo buy consoles

`StructureLogisticsConsoleImperiumBuy` и `StructureLogisticsConsoleHereticsBuy` используют стандартный cargo order UI.

Особенности:

- Imperium покупает из `WH40KImperiumMarket` за account `WH40KImperium`;
- Heretics покупают из `WH40KHereticsMarket` за account `WH40KHeretics`;
- batch delivery delay задан как 300 секунд;
- доступ ограничен `WH40KStoreTeam`.

Ассортимент не полностью открыт сразу. `WH40KCargoProductUnlocks` на станции содержит initial unlocked products, а command tree может открывать дополнительные cargo products.

## Cargo sell consoles

Sell consoles используют `CargoPalletConsole`.

| Console | Принимает stack type | Account |
| --- | --- | --- |
| `StructureLogisticsConsoleImperiumSell` | `DataChipImp` | `WH40KImperium` |
| `StructureLogisticsConsoleHereticsSell` | `DataChipChaos` | `WH40KHeretics` |

`DataChipImp` и `DataChipChaos` имеют `StackPrice.price: 1000`, поэтому один чип оценивается как 1000 банковских денег при продаже.

## DataChip и intelligence валюты

Есть три связанных, но разных понятия:

| Объект/валюта | Где используется | Назначение |
| --- | --- | --- |
| `DataChip` | нейтральный material/stack | Спавнится точками, используется как сырье для конвертации |
| `DataChipImp` | stack/currency/research chip | Imperial sell, Vox store currency, research upload |
| `DataChipChaos` | stack/currency/research chip | Heretic sell, Altar store currency, research upload |
| `intelligence_imp` | store currency | Цена listings в Vox catalog |
| `intelligence_ch` | store currency | Цена listings в Altar catalog |

`DataChipImp` имеет currency price `intelligence_imp: 1`. `DataChipChaos` имеет currency price `intelligence_ch: 1`. Поэтому физические чипы можно вставлять в соответствующие store-интерфейсы как валюту.

Один фракционный дата-чип также является `WH40KResearchPointChip` и при загрузке в research server дает 1000 research points до бонусов.

## Как появляются дата-чипы

### Точки захвата

`MachineChipProduser` спавнит нейтральный `DataChip1` каждые 60 секунд с Assault.

`MachineChipProduserCenter` спавнит `DataChip4` каждые 120 секунд с Assault.

Эти нейтральные чипы сами по себе не являются `DataChipImp`/`DataChipChaos`; для фракционной экономики нужны конвертеры.

### Point lathes / chip converters

`ImpPointLathe` и `ChPointLathe` - фракционные lathe-машины для производства фракционных дата-чипов из доступных рецептов.

Они используют `WH40KTieredLatheProcessing`:

- effective level = `TeamBaseLevel + best CommandNode UpgradeLevel`;
- тиры берутся из `WH40KTierMachineStandard`;
- tier thresholds: 2, 3, 4;
- tier machine timings: tier0 min 10s, tier1 8s, tier2 5s, tier3 3s;
- выбранный recipe pack меняется по тиру.

Практически это значит: рост фронтовых очков и апгрейд command node повышают качество/скорость производства дата-чипов.

## Research points

`DataChipImp` и `DataChipChaos` можно использовать на research server/client своей команды.

Правила:

- проверяется access reader цели;
- проверяется team access через `WH40KResearchTeam`;
- количество research points = `stack count * pointsPerUnit`;
- `pointsPerUnit` сейчас 1000;
- command tree research bonuses могут увеличить итог через `ResearchPointBonusPercent`.

Command tree также может давать прямой `researchPointGrant` при покупке узлов research-домена.

Research points не являются `TeamFrontPoints` или `TeamCommandPoints`. Они живут в обычной research-системе и тратятся на технологии/рецепты.

## Vox/Altar stores

Vox catalog использует `intelligence_imp`, Altar catalog - `intelligence_ch`.

В этих каталогах многие listings gated по base level через `WH40KMinBaseLevelCondition`.

Примеры gated уровней:

- level 2 - улучшенное оружие, advanced armor, combat shotgun, bolter ammo;
- level 4 - plasma/disposable RPG/high-tier medical;
- level 5 - reusable rocket launcher/AP ammo;
- level 6+ - sentinel-style reinforcements в store listings.

`WH40KMinBaseLevelCondition` берет team id из store entity (`WH40KStoreTeam`) или buyer и смотрит `TeamBaseLevel`. Поэтому покупки в этих магазинах зависят от front progression, а не от command point balance.

Во время `LogisticsSurge` ammo listings в категориях `VoxAmmo` и `AltarAmmo` получают cost modifier с multiplier 0.7.

## WH40KFactionFunds и supply drop

`WH40KFactionFunds` - currency prototype для UI, но баланс берется из банковского account фракции.

### WH40KSupplyDropPad

Pad component имеет:

- account;
- teamId;
- cost;
- cooldown;
- drop delay.

При запуске:

- проверяется team access;
- проверяется баланс account;
- деньги списываются через cargo bank;
- crate падает на позицию актора после delay.

### Vox supply-drop backpack/store

`ClothingBackpackVox` и `ClothingBackpackVoxChaos` используют `WH40KVoxSupplyDropStore`.

Для Imperium:

- account `WH40KImperium`;
- teamId `Imperium`;
- fundsCurrency `WH40KFactionFunds`.

Для Heretics:

- account `WH40KHeretics`;
- teamId `Heretics`;
- fundsCurrency `WH40KFactionFunds`.

В текущем backpack-конфиге:

- dropDelaySeconds 30;
- cooldownSeconds 180;
- marker `WH40KSupplyDropParachuteCrateVisual`;
- crate `WH40KVoxSupplyDropCrate`.

Каталог `vox_supplydrop_catalog.yml`:

| Listing | Cost `WH40KFactionFunds` |
| --- | ---: |
| `VoxSupplyDropNutribrick` | 150 |
| `VoxSupplyDropBasicMedkit` | 800 |
| `VoxSupplyDropLasgunPowerCell` | 650 |
| `VoxSupplyDropMagazineStubRifle` | 300 |
| `VoxSupplyDropWHSoup` | 200 |
| `VoxSupplyDropAutogunAmmoBox` | 700 |
| `VoxSupplyDropMortarShellHE` | 1200 |

Некоторые listings имеют `listingDropAmounts`, например nutribrick x4, rifle magazines x4, mortar HE x2.

## Cargo logistics tier

`CargoLogisticsTier` синхронизируется от `TeamBaseLevel` через `WH40KCargoLogisticsTierSyncSystem`.

Профиль `WH40KTierLogisticsStandard`:

| Tier | Требуемый level | Max items bonus | Delivery reduction |
| ---: | ---: | ---: | ---: |
| 0 | ниже 2 | 0 | 0 |
| 1 | 2 | +2 | -1 minute |
| 2 | 3 | +5 | -2 minutes |
| 3 | 4 | +10 | -5 minutes |

На это накладываются external bonuses из command tree:

- `CargoDeliverySpeedBonusPercent`;
- `CargoMaxItemsBonusPercent`;
- `CargoPriceDiscountPercent`.

Итог:

- base cargo capacity 10 растет от tier bonus и percent bonus;
- batch delay 300 секунд уменьшается tier reduction и затем percent speed bonus;
- unit price cargo order может снижаться через cargo price discount percent.

## Tiered machines

`WH40KTieredLatheProcessing` используется не только для point lathes. Общая логика:

```text
effectiveLevel = max(teamBaseLevel + bestCommandNodeUpgrade among tracked teams)
tier = SelectTier(effectiveLevel, thresholds 2/3/4)
```

Затем система:

- меняет `Lathe.TimeMultiplier`;
- применяет minimum production time по тиру;
- выбирает recipe pack tier0..tier3;
- применяет machine speed bonus из command tree.

Таким образом, front progression влияет на производственную экономику даже без прямой выдачи денег.

## Ore extractor

`WH40KOreExtractor` тоже использует effective level:

```text
effectiveLevel = TeamBaseLevel + bestCommandNodeUpgrade
```

Тиры по умолчанию:

- tier0: `OreSteel`, `OreCoal`;
- tier1: +`OreSpaceQuartz`;
- tier2: +`OreGold`, `OreSilver`;
- tier3: +`OrePlasma`, `OreUranium`.

Скорость и количество:

| Tier | Interval | Count |
| ---: | ---: | ---: |
| 0 | 4s | 1 |
| 1 | 3s | 2 |
| 2 | 2s | 3 |
| 3 | 1s | 4 |

Это производственная экономика материалов: она не дает front/command points напрямую, но ускоряет снабжение, vehicle fabrication и производство.

## Vehicle fabrication

Vehicle fabrication consoles:

- `WH40KVehicleFabricationConsoleImperium`;
- `WH40KVehicleFabricationConsoleChaos`.

Они используют:

- cargo account фракции;
- material storage;
- stored vehicle parts container;
- очередь заказов;
- assembly pad в радиусе 8 тайлов.

Текущие рецепты:

| Recipe | Product | Bank cost | Build time | Materials |
| --- | --- | ---: | ---: | --- |
| `WH40KImperiumVehicleMotorbikeRecipe` | `WH40KImperiumVehicleMotorbike` | 10000 | 45s | Steel 10, Glass 2 |
| `WH40KHereticsVehicleMotorbikeRecipe` | `WH40KHereticsVehicleMotorbike` | 10000 | 45s | Steel 10, Glass 2 |

При постановке в очередь:

- проверяется bank balance;
- проверяются материалы;
- проверяются stored parts;
- деньги списываются из account;
- при удалении заказа из очереди материалы, parts и деньги возвращаются.

## Экономическая цепочка в раунде

Типичный путь ресурсов выглядит так:

1. Команда получает front/command points от точек, пассива, kills и миссий.
2. Front points повышают base level.
3. Base level открывает stores, подкрепления, machine tiers и logistics tier.
4. Command points тратятся на command node upgrades, command tree и подкрепления.
5. Command tree открывает cargo products, technologies, recipes и постоянные бонусы.
6. Capture points спавнят нейтральные chips.
7. Фракционные converters превращают экономику точек в `DataChipImp`/`DataChipChaos`.
8. Чипы либо продаются в bank funds, либо тратятся как intelligence currency, либо загружаются в research.
9. Bank funds уходят в cargo orders, supply drops и vehicle fabrication.

Главное: эти слои связаны прогрессией, но не являются одной валютой. Изменяя баланс одного слоя, нужно проверять, не появился ли обходной путь через другой слой.
