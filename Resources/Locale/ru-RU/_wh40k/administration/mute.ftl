wh40k-mute-panel-title = Панель мутов
wh40k-mute-panel-player = Игрок
wh40k-mute-panel-chat = Чат
wh40k-mute-panel-ahelp = АХелп
wh40k-mute-panel-erase = Стереть сообщения игрока из чата
wh40k-mute-panel-submit = Выдать мут
wh40k-mute-panel-tabs-basic = Основная инфа
wh40k-mute-panel-tabs-players = Список игроков
wh40k-mute-panel-reason = Причина
wh40k-mute-panel-no-type = Выберите хотя бы одну область мута.
wh40k-mute-panel-no-player = Выберите игрока для мута.
wh40k-mute-panel-no-reason = Укажите причину мута.

wh40k-mute-scope-chat = Чат
wh40k-mute-scope-ahelp = АХелп
wh40k-mute-scope-all = Чат + АХелп

wh40k-mute-command-invalid-type = Неизвестный тип мута: {$type}
wh40k-mute-command-invalid-erase = Неизвестное значение erase: {$value}
wh40k-mute-command-hint-scope = <тип>
wh40k-mute-command-hint-erase = [стереть сообщения]
wh40k-mute-command-hint-erase-no = Оставить сообщения
wh40k-mute-command-hint-erase-yes = Стереть сообщения

wh40k-unmute-command-none-active = У {$player} нет активных мутов для этой области.
wh40k-unmute-command-success = Снято активных мутов: {$count}. Игрок: {$player}.

wh40k-admin-hierarchy-action-mute = выдать мут
wh40k-admin-hierarchy-action-unmute = снять мут с

wh40k-chat-mute-placeholder-temporary = У вас мут чата на {$time}. Наведитесь, чтобы увидеть причину.
wh40k-chat-mute-placeholder-duration = У вас мут чата на {$time}. Наведитесь, чтобы увидеть причину.
wh40k-chat-mute-placeholder-until = У вас мут чата до {$time}. Наведитесь, чтобы увидеть причину.
wh40k-chat-mute-placeholder-permanent = У вас мут чата. Наведитесь, чтобы увидеть причину.
wh40k-ahelp-mute-placeholder-temporary = У вас мут АХелпа на {$time}. Наведитесь, чтобы увидеть причину.
wh40k-ahelp-mute-placeholder-duration = У вас мут АХелпа на {$time}. Наведитесь, чтобы увидеть причину.
wh40k-ahelp-mute-placeholder-until = У вас мут АХелпа до {$time}. Наведитесь, чтобы увидеть причину.
wh40k-ahelp-mute-placeholder-permanent = У вас мут АХелпа. Наведитесь, чтобы увидеть причину.
wh40k-mute-tooltip-temporary =
    Причина: {$reason}
    Истекает: {$time}
wh40k-mute-tooltip-permanent =
    Причина: {$reason}
    Истекает: никогда
wh40k-mute-time-seconds = {$count} сек.
wh40k-mute-time-minutes = {$count} мин.
wh40k-mute-time-hours =
    {$count ->
        [one] {$count} час
        [few] {$count} часа
       *[other] {$count} часов
    }
wh40k-mute-time-hours-minutes = {$hours} ч. {$minutes} мин.

wh40k-mute-list-title = Муты
wh40k-mute-list-header-type = Область
wh40k-mute-list-header-admin = Выдал
wh40k-mute-list-unmuted = Снят: {$date}
wh40k-mute-list-unmuted-by = Снял {$unmuter}

player-panel-mute = Мут
admin-player-actions-mute = Мут
admin-player-actions-window-mute = Панель мутов
wh40k-mute-unmute-denied-protected = Вы не можете снять мут, выданный администратором выше вас по иерархии.
cmd-mutepanel-desc = Открывает панель мутов для игрока.
cmd-mutepanel-help = Использование: {$command} [игрок]
cmd-mute-desc = Выдаёт аккаунту мут чата, мут ахелпа или оба сразу.
cmd-mute-help = Использование: {$command} <игрок> <chat|ahelp|all> <причина> [минуты] [erase]
cmd-unmute-desc = Снимает активные муты с аккаунта игрока.
cmd-unmute-help = Использование: {$command} <игрок> [chat|ahelp|all]
wh40k-kick-host-protected = Нельзя кикнуть игрока {$player}: HOST защищён от кика.
