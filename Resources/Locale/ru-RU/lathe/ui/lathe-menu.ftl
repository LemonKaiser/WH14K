lathe-menu-title = Меню станка
lathe-menu-queue = Очередь
lathe-menu-server-list = Список серверов
lathe-menu-sync = Синхр.
lathe-menu-search-designs = Поиск проектов
lathe-menu-category-all = Всё
lathe-menu-search-filter = Фильтр
lathe-menu-amount = Кол-во:
lathe-menu-recipe-count =
    { $count ->
        [1] { $count } Рецепт
        [few] { $count } Рецепта
       *[other] { $count } Рецептов
    }
lathe-menu-reagent-slot-examine = Сбоку имеется отверстие для мензурки.
lathe-reagent-dispense-no-container = Жидкость выливается из { $name } на пол!
lathe-menu-result-reagent-display = { $reagent } ({ $amount } ед.)
lathe-menu-material-display = { $material } { $amount }
lathe-menu-tooltip-display = { $amount } { $material }
lathe-menu-description-display = [italic]{ $description }[/italic]
lathe-menu-material-amount =
    { $amount ->
        [1] { NATURALFIXED($amount, 2) } ({ $unit })
       *[other] { NATURALFIXED($amount, 2) } ({ $unit })
    }
lathe-menu-material-amount-missing =
    { $amount ->
        [1] { NATURALFIXED($amount, 2) } { $unit } { $material } ([color=red]{ NATURALFIXED($missingAmount, 2) } { $unit } не хватает[/color])
       *[other] { NATURALFIXED($amount, 2) } { $unit } { $material } ([color=red]{ NATURALFIXED($missingAmount, 2) } { $unit } не хватает[/color])
    }
lathe-menu-no-materials-message = Материалы не загружены
lathe-menu-silo-linked-message = Хранилище связано
lathe-menu-fabricating-message = Производится...
lathe-menu-materials-title = Материалы
lathe-menu-materials-title-with-limit = Материалы (макс. { $max })
lathe-menu-queue-title = Очередь производства
lathe-menu-delete-fabricating-tooltip = Отменить производство текущего объекта.
lathe-menu-delete-item-tooltip = Отменить производство этой партии.
lathe-menu-move-up-tooltip = Перенести эту партию вперёд в очереди.
lathe-menu-move-down-tooltip = Перенести эту партию назад в очереди.
lathe-menu-infinite-queue-tooltip = Добавить бесконечную задачу в очередь.
lathe-menu-item-single = { $index }. { $name }
lathe-menu-item-batch = { $index }. { $name } ({ $printed }/{ $total })
lathe-menu-item-infinite = { $index }. { $name } ({ $printed }/{ $infinity })
lathe-menu-item-progress-infinite = { $name } ({ $printed }/{ $infinity })
lathe-popup-material-storage-full = Нет места для материалов.
lathe-menu-header-subtitle-fallback = Печатайте изделия, управляйте очередью и следите за расходом материалов из одного терминала.
lathe-menu-server-title = Рецептурный uplink
lathe-menu-server-status-linked = Доступна синхронизация с исследовательскими серверами. Потенциальных пакетов: { $packs }.
lathe-menu-server-status-static = Станок работает по локальной производственной колоде.
lathe-menu-summary-profile-label = Профиль
lathe-menu-summary-designs-label = Проекты
lathe-menu-summary-queue-label = Очередь
lathe-menu-summary-materials-label = Материалы
lathe-menu-summary-designs-value = { $visible }/{ $total }
lathe-menu-summary-queue-value-idle = { $batches } парт.
lathe-menu-summary-queue-value-active = Активно • { $batches }
lathe-menu-summary-materials-value = { $current } ед.
lathe-menu-summary-materials-value-limited = { $current }/{ $max }
lathe-menu-designs-title = Производственные шаблоны
lathe-menu-designs-subtitle = { $categories } категорий в текущей колоде
lathe-menu-designs-subtitle-filtered = Фильтр: { $category }
lathe-menu-designs-empty = По текущему фильтру ничего не найдено.
lathe-menu-queue-subtitle = Контролируйте активную печать и порядок партий.
lathe-menu-queue-empty = Очередь пуста. Добавьте шаблон из левой панели.
lathe-menu-queue-state-label = Операция
lathe-menu-queue-state-idle = Ожидание
lathe-menu-queue-state-printing = Печать: { $name }
lathe-menu-queue-depth-label = Глубина
lathe-menu-queue-depth-value = { $batches } партий / { $items } ед.
lathe-menu-queue-depth-value-infinite = { $batches } партий / { $items } ед. / { $infinite } беск.
lathe-menu-queue-request-label = Запрос
lathe-menu-queue-request-value = x{ $amount }
lathe-menu-materials-subtitle = Следите за запасом, каналами подачи и стоимостью расхода.
lathe-menu-materials-storage-label = Хранилище
lathe-menu-materials-storage-value = { $current } ед. • { $types } типов
lathe-menu-materials-storage-value-limited = { $current }/{ $max } ед. • { $types } типов
lathe-menu-materials-storage-value-typed = { $current } ед. • { $types }/{ $limit } типов
lathe-menu-materials-source-label = Подача
lathe-menu-materials-source-silo = Силос связан
lathe-menu-materials-source-internal = Внутренние запасы
lathe-menu-materials-efficiency-label = Расход
lathe-menu-materials-efficiency-value = x{ $multiplier }
lathe-menu-footer = Используйте поиск и фильтр слева, а `Inf` ставит бесконечную партию до ручной отмены.
lathe-menu-footer-imperium = Машинный архив Механикус требует контроля очереди и лимитов сырья перед следующим апгрейдом.
lathe-menu-footer-heretics = Кузнечные узлы варбанды лучше работают при короткой очереди и стабильной подаче сырья.
lathe-menu-profile-general = Универсальный производственный узел
lathe-menu-profile-industrial = Промышленная сборка
lathe-menu-profile-research = Исследовательская ковка
lathe-menu-profile-circuit = Печатный контур
lathe-menu-profile-armory = Оружейный техфаб
lathe-menu-profile-biotic = Биофабрика
lathe-menu-profile-robotics = Робофабрика
lathe-menu-profile-ore = Переработка руды
lathe-menu-recipe-action = В очередь
lathe-menu-recipe-meta = { $materials } мат. | { $seconds } с
lathe-menu-recipe-meta-category = { $category } | { $materials } мат. | { $seconds } с
lathe-menu-recipe-status-ready = Готово
lathe-menu-recipe-status-blocked = Недостаточно сырья
lathe-menu-abort-button = Отмена
lathe-menu-queue-row-single = { $name }
lathe-menu-queue-row-batch = { $name } ({ $printed }/{ $total })
lathe-menu-queue-row-infinite = { $name } ({ $printed }/{ $infinity })
