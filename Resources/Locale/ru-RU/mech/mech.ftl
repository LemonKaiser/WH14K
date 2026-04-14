# UI
mech-menu-title = панель управления мехом
mech-equipment-label = Снаряжение
mech-modules-label = Модули

# Verbs
mech-verb-enter = Войти
mech-verb-exit = Извлечь пилота
mech-ui-open-verb = Открыть панель управления

# Installation
mech-install-begin-popup = {$user} устанавливает {THE($item)}...
mech-cannot-modify-closed-popup = Нельзя изменять конфигурацию, пока кабина закрыта!
mech-duplicate-installed-popup = Такой же предмет уже установлен.
mech-cannot-insert-broken-popup = Нельзя что-либо вставить, пока мех сломан.

mech-equipment-slot-full-popup = Нет свободных слотов снаряжения.
mech-module-slot-full-popup = Нет свободных слотов модулей.
mech-equipment-whitelist-fail-popup = Это снаряжение несовместимо с данным мехом.
mech-module-whitelist-fail-popup = Этот модуль несовместим с данным мехом.

# Selection
mech-select-popup = Выбрано: {$item}
mech-select-none-popup = Ничего не выбрано

# Radial menu
mech-radial-no-equipment = Нет снаряжения

# Status displays
mech-integrity-display-label = Целостность
mech-integrity-display = {$amount} %
mech-integrity-display-broken = СЛОМАН
mech-energy-display-label = Энергия
mech-energy-display = {$amount} %
mech-energy-missing = ОТСУТСТВУЕТ
mech-energy-drain-label = Расход:
mech-energy-drain-display = {$amount} Вт

mech-equipment-slot-display-label = Снаряжение: занято {$used}/{$max}
mech-module-slot-display-label = Модули: занято {$used}/{$max}
mech-grabber-capacity = {$current}/{$max}
mech-no-data-status = Нет данных
mech-cabin-not-airtight-status = Кабина негерметична
mech-cabin-no-air-status = Нет воздуха

mech-generator-output-label = Выход: {$rate} Вт
mech-generator-fuel-label = Топливо: {$amount} ({$name})
mech-generator-tesla-hint-label = Заряд от ближайшего APC: {$status}
mech-generator-tesla-status-online = Да
mech-generator-tesla-status-offline = Нет
mech-weapon-recharge-toggle = Заряжать от меха
mech-weapon-recharge-label = Зарядка от меха:
mech-weapon-recharge-state-enabled = Вкл
mech-weapon-recharge-state-disabled = Выкл

# Atmospheric system
mech-cabin-pressure-label = Воздух в кабине:
mech-cabin-pressure-level-label = {$level} кПа
mech-cabin-temperature-label = Температура:
mech-cabin-temperature-level-label = {$tempC} °C
mech-airtight-unavailable-label = Кабина негерметична
mech-tank-controls-label = Баллон:
mech-tank-missing-label = Баллон отсутствует
mech-tank-toggle-tooltip = Подключает баллон к системе жизнеобеспечения меха.
mech-tank-mode-supply = Подача
mech-tank-mode-refill = Заправка
mech-tank-mode-tooltip = Подача наполняет кабину из баллона. Заправка позволяет вентилятору пополнять баллон внешним воздухом.
mech-tank-target-pressure-label = Подача:
mech-tank-target-pressure-tooltip = Давление, которое баллон пытается поддерживать в кабине в режиме подачи.

mech-tank-pressure-label = Воздух в баллоне:
mech-tank-pressure-level-label = { $state ->
    [ok] {$pressure} кПа
    *[na] Н/Д
}

# Fan system
mech-fan-label = Вентилятор:
mech-fan-status-label = Состояние вентилятора:
mech-fan-status-level-label = { $state ->
    [on] Вкл
    [idle] Нет работы
    [off] Выкл
    *[na] Н/Д
}
mech-fan-missing-label = Вентилятор отсутствует
mech-filter-enabled-checkbox = Фильтр
mech-filter-enabled-tooltip = Если включено, вентилятор очищает воздух кабины от CO2, плазмы и N2O, а при заборе воздуха не кладёт эти газы в баллон.
mech-compressor-enabled-checkbox = Компрессор
mech-compressor-enabled-tooltip = В режиме заправки позволяет вентилятору качать баллон выше внешнего давления, до лимита баллона.

# Access restriction
mech-no-enter-popup = Вы не можете пилотировать это.

# Alert
mech-eject-pilot-alert-popup = {$user} вытаскивает пилота из {THE($item)}!

# Lock system
mech-lock-dna-label = Блокировка по ДНК:
mech-lock-card-label = Блокировка по ID:

mech-lock-register-button = Записать
mech-lock-activate-button = Активировать
mech-lock-deactivate-button = Деактивировать
mech-lock-reset-tooltip = Сброс
mech-lock-not-set-label = Не задано

mech-lock-no-dna-popup = У вас нет ДНК для привязки замка!
mech-lock-no-card-popup = У вас нет ID-карты для привязки замка!
mech-lock-access-denied-popup = Доступ запрещён! Этот мех заперт.

mech-lock-dna-registered-popup = Блокировка по ДНК записана!
mech-lock-card-registered-popup = Блокировка по ID записана!

# Settings access banner
mech-settings-no-access-label = Доступ запрещён
mech-remove-disabled-tooltip = Нельзя снять, пока внутри есть пилот.

# Other
mech-construction-guide-string = Все детали меха должны быть прикреплены к каркасу.
