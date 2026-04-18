reagent-name-wh40k-promethium = топливо техники (прометий)
reagent-desc-wh40k-promethium = Переработанное тяжёлое топливо, которым питают лёгкую боевую технику на линии фронта.

wh40k-vehicle-engine-action-name = Двигатель
wh40k-vehicle-engine-action-description = Завести или заглушить двигатель техники.

ent-ActionWH40KToggleVehicleEngine = Двигатель
    .desc = Завести или заглушить двигатель техники.

wh40k-vehicle-engine-state-off = Выключен
wh40k-vehicle-engine-state-starting = Запуск
wh40k-vehicle-engine-state-running = Работает
wh40k-vehicle-engine-state-stalled = Заглох
wh40k-vehicle-engine-state-disabled = Неисправен

wh40k-vehicle-service-state-nominal = Исправен
wh40k-vehicle-service-state-worn = Изношен
wh40k-vehicle-service-state-critical = Критичен
wh40k-vehicle-service-state-disabled = Выведен из строя

wh40k-vehicle-toggle-on = Вкл
wh40k-vehicle-toggle-off = Выкл

wh40k-vehicle-engine-popup-starting = Двигатель начинает раскручиваться.
wh40k-vehicle-engine-popup-running = Двигатель схватывает и выходит на ровный гул.
wh40k-vehicle-engine-popup-off = Двигатель глохнет.
wh40k-vehicle-engine-popup-no-key = В замке нет подходящего ключа.
wh40k-vehicle-engine-popup-no-fuel = Бак пуст.
wh40k-vehicle-engine-popup-disabled = Техника слишком повреждена, чтобы завестись.
wh40k-vehicle-engine-popup-stalled-no-fuel = Двигатель кашляет и глохнет: подача прометия иссякла.
wh40k-vehicle-engine-popup-disabled-while-running = Двигатель глохнет: повреждённая трансмиссия заклинивает.

wh40k-vehicle-examine-fuel = Топливо: [color=orange]{ $percent }%[/color] ({ $current } / { $capacity })
wh40k-vehicle-examine-runtime = Время работы на холостом ходу: [color=lightblue]{ $remaining }[/color]
wh40k-vehicle-examine-engine = Состояние двигателя: [color=white]{ $state }[/color]
wh40k-vehicle-examine-service = Техническое состояние: [color=white]{ $state }[/color] ([color=lightblue]{ $integrity }%[/color] целостности)

wh40k-vehicle-terminal-examine-buffer = Буфер терминала: [color=orange]{ $current } / { $capacity }[/color]
wh40k-vehicle-terminal-examine-modes = Автозакачка: [color=white]{ $intake }[/color], автозаправка: [color=white]{ $refuel }[/color]

wh40k-vehicle-fuel-ui-window-title = Топливный терминал техники
wh40k-vehicle-fuel-ui-subtitle = Управляйте закачкой прометия, запасом терминала и заправкой техники рядом.
wh40k-vehicle-fuel-ui-footer = Держите терминал под питанием и оставляйте канистры или технику в радиусе трёх тайлов.

wh40k-vehicle-fuel-ui-card-buffer = Буфер терминала
wh40k-vehicle-fuel-ui-card-source = Ближайший источник
wh40k-vehicle-fuel-ui-card-vehicle = Ближайшая техника

wh40k-vehicle-fuel-ui-buffer-hint = Уровень резерва: { $percent }%
wh40k-vehicle-fuel-ui-source-hint = Запас источника: { $current } / { $capacity }
wh40k-vehicle-fuel-ui-source-hint-none = Поблизости нет источника прометия.
wh40k-vehicle-fuel-ui-vehicle-hint = Уровень бака: { $percent }% ({ $current } / { $capacity })
wh40k-vehicle-fuel-ui-vehicle-hint-none = Поблизости нет техники для заправки.

wh40k-vehicle-fuel-ui-button-enable-intake = Автозакачка Вкл
wh40k-vehicle-fuel-ui-button-disable-intake = Автозакачка Выкл
wh40k-vehicle-fuel-ui-button-enable-refuel = Автозаправка Вкл
wh40k-vehicle-fuel-ui-button-disable-refuel = Автозаправка Выкл

wh40k-vehicle-fuel-ui-diagnostics-title = Диагностика техники
wh40k-vehicle-fuel-ui-power-label = Питание терминала
wh40k-vehicle-fuel-ui-engine-label = Состояние двигателя
wh40k-vehicle-fuel-ui-service-label = Техсостояние
wh40k-vehicle-fuel-ui-runtime-label = Максимум холостого хода
wh40k-vehicle-fuel-ui-service-value = { $state } ({ $integrity }%)

wh40k-vehicle-fuel-ui-no-source = Источник не найден
wh40k-vehicle-fuel-ui-no-vehicle = Техника не найдена
