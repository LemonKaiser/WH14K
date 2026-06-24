# WH40K Murder Mystery

## Цель

Добавить новый мини-режим `Murder Mystery` для кастомного форка WH40K/SS14 с отдельным preset-ом, отдельным map pool, своей логикой ролей, оружия, способностей, паузы раунда, таймера и наград.

## Что уже реализовано

- [x] Добавлен отдельный `gamePreset` `WH40KMurderMystery`.
- [x] Добавлен отдельный `gameRule` `WH40KMurderMystery`.
- [x] Добавлен отдельный пул карт `WH40KMurderMysteryMapPool`.
- [x] `Prop Hunt` переведен на свой отдельный пул `WH40KPropHuntMapPool`.
- [x] Для мини-игр сохранена возможность позже менять карты только через прототипы.
- [x] Раунд ставится на паузу, пока не наберется минимум `2` активных игрока.
- [x] После набора игроков раунд продолжается автоматически.
- [x] Через `30` секунд после старта активной фазы раздаются роли.
- [x] Реализованы роли `Murder`, `Sheriff`, `Civilian`.
- [x] Убийцы видят друг друга по личной иконке.
- [x] Убийцы не могут наносить урон друг другу.
- [x] Для всех участников отключены раздевание и обыск.
- [x] Одежда, PDA, ID и рюкзак блокируются от снятия через режимные ограничения.
- [x] Убийца получает личный нож.
- [x] Нож можно бросать и выбрасывать.
- [x] Поднимать и использовать нож может только его владелец-убийца.
- [x] Шериф получает специальный револьвер.
- [x] Револьвер имеет до `3` патронов и дозаряжается по `1` патрону каждые `30` секунд.
- [x] Гражданский, поднявший револьвер, становится новым шерифом.
- [x] При смерти шерифа револьвер выпадает на пол.
- [x] Выстрел шерифа по мирному убивает и цель, и шерифа.
- [x] Выстрел шерифа по убийце убивает только убийцу.
- [x] Нож убийцы удаляется после смерти владельца.
- [x] Трупы остаются в мире.
- [x] Кровь периодически очищается.
- [x] Используется только таймерный HUD без счетчиков ролей и без kill feed.
- [x] Длительность раунда установлена на `15` минут.
- [x] Убийца получил способности `Smoke` и `Flash`.
- [x] Каждая из способностей убийцы имеет `3` использования за раунд и `60` секунд КД.
- [x] Карта режима защищена от разрушения.
- [x] На карте режима принудительно включаются гравитация и стандартная атмосфера.
- [x] Добавлены русские и английские локали режима, ролей, оружия и действий.
- [x] Добавлены тесты на scaling ролей и на prototype-конфиг режима.
- [x] `Release`-сборка проходит успешно.

## Важные правила режима

- Масштаб ролей сейчас считается как `ceil(players / 10)` для убийц и столько же для шерифов, с ограничением по общему числу игроков.
- Таймер раунда начинает идти только после фактической раздачи ролей.
- Если время вышло, побеждает команда мирных.
- Победившая сторона получает `500 XP`.

## Файлы режима

- `Content.Server/_WH40K/MurderMystery/WH40KMurderMysteryRuleSystem.cs`
- `Content.Server/GameTicking/Rules/Components/WH40KMurderMysteryRuleComponent.cs`
- `Content.Server/_WH40K/MurderMystery/WH40KMurderMysteryPlayerComponent.cs`
- `Content.Server/_WH40K/MurderMystery/WH40KMurderMysteryWeaponComponents.cs`
- `Content.Shared/_WH40K/MurderMystery/`
- `Content.Client/_WH40K/Overlays/WH40KMurderMysteryStatusIconSystem.cs`
- `Resources/Prototypes/_WH40K/GameRules/murder_mystery.yml`
- `Resources/Prototypes/_WH40K/Actions/murder_mystery.yml`
- `Resources/Prototypes/_WH40K/Entities/Objects/Weapons/murder_mystery.yml`
- `Resources/Prototypes/_WH40K/Maps/Pools/wh40k_minigames.yml`
- `Resources/Locale/ru-RU/_wh40k/murder_mystery.ftl`
- `Resources/Locale/en-US/_wh40k/murder_mystery.ftl`

## Проверка

- `dotnet build SpaceStation14.slnx -c Release --no-restore`
- `dotnet test Content.Tests/Content.Tests.csproj -c Release --no-build --filter WH40KMurderMystery`
