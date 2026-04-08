## Strings for the battery (SMES/substation) menu

battery-menu-window-title = Энергоузел
battery-menu-header-subtitle = Освящённый энергоузел сектора.\nСанкционируйте приём, отдачу и резерв.
battery-menu-header-subtitle-smes = Сверхпроводящий резерв сектора.\nСанкционируйте приём, отдачу и запас.
battery-menu-header-subtitle-substation = Распределительный узел среднего контура.\nУдерживайте поток и резерв сектора.
battery-menu-footer-left = Магистраль стабильна. Резерв удерживает сектор.
battery-menu-footer-right = Банк энергии WH14K
battery-menu-footer-right-smes = СМЭС WH14K
battery-menu-footer-right-substation = Подстанция WH14K
battery-menu-footer-charging = Банк принимает подпитку из магистрали.
battery-menu-footer-discharging = Банк ведёт отдачу в сеть сектора.
battery-menu-footer-critical = Резерв на исходе. Сектор близок к просадке.
battery-menu-footer-locked = Приём и отдача вручную отсечены.

battery-menu-device-panel-title = Узел передачи
battery-menu-device-note-balanced = Баланс подпитки\nи отдачи удержан.
battery-menu-device-note-intake = Узел готов принять\nмощность магистрали.
battery-menu-device-note-output = Узел питает сеть\nсектора из резерва.
battery-menu-device-note-locked = Оба контура отсечены оператором.

battery-menu-button-off = Выкл
battery-menu-button-on = Вкл

battery-menu-out = Отдача
battery-menu-in = Приём
battery-menu-charge-header = Контур приёма
battery-menu-discharge-header = Контур отдачи
battery-menu-storage-header = Банк ячеек
battery-menu-passthrough = Транзит
battery-menu-max = Предел
battery-menu-current = Поток
battery-menu-stored = Резерв
battery-menu-energy = Энергия
battery-menu-eta-full = Пополнение
battery-menu-eta-empty = Истощение

battery-menu-input-state-off = ОТСЕЧЁН
battery-menu-input-state-standby = ОЖИДАНИЕ
battery-menu-input-state-ready = ГОТОВ
battery-menu-input-state-active = НАПИТКА

battery-menu-input-note-off = Приём из магистрали\nотсечён рубильником.
battery-menu-input-note-standby = Магистраль молчит.\nУзел ждёт подпитку.
battery-menu-input-note-ready = Контур открыт\nк приёму энергии.
battery-menu-input-note-active = Ячейки насыщаются\nвходящим потоком.

battery-menu-output-state-off = ОТСЕЧЁН
battery-menu-output-state-standby = ПУСТО
battery-menu-output-state-ready = РЕЗЕРВ
battery-menu-output-state-active = ОТДАЧА

battery-menu-output-note-off = Отдача в сектор\nотсечена рубильником.
battery-menu-output-note-empty = Запас для поддержки\nсети исчерпан.
battery-menu-output-note-ready = Резерв удерживается\nдо новой санкции.
battery-menu-output-note-active = Банк подпитывает сеть\nнакопленной мощностью.

battery-menu-storage-note-full = Ячейки насыщены\nи готовы к полной отдаче.
battery-menu-storage-note-high = Резерв достаточен\nдля штатной работы.
battery-menu-storage-note-medium = Резерв снижается.\nЖелательна подпитка.
battery-menu-storage-note-low = Резерв критичен.\nПотеря сети близка.

battery-menu-eta-value = ~{ $minutes } мин
battery-menu-eta-value-max = >{ $minutes } мин
battery-menu-eta-value-na = Н/Д
battery-menu-power-value = { POWERWATTS($value) }
battery-menu-stored-percent-value = { TOSTRING($value, "P0") }
battery-menu-stored-energy-value = { ENERGYWATTHOURS($value) }
