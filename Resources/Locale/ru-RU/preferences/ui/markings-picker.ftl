markings-used = Используемые черты
markings-unused = Неиспользуемые черты
markings-add = Добавить черту
markings-remove = Убрать черту
markings-rank-up = Вверх
markings-rank-down = Вниз
markings-search = Поиск
marking-points-remaining = Черт осталось: { $points }
marking-used = { $marking-name }
marking-used-forced = { $marking-name } (Принудительно)
marking-slot-add = Добавить
marking-slot-remove = Удалить
marking-slot = Слот { $number }

-markings-selection = { $selectable ->
    [0] У вас не осталось доступных черт.
    [one] Вы можете выбрать ещё одну черту.
   *[other] Вы можете выбрать ещё { $selectable } черт.
}
markings-limits = { $required ->
    [true] { $count ->
        [-1] Выберите хотя бы одну черту.
        [0] Нельзя выбрать ни одной черты, но это обязательный слой. Это баг.
        [one] Выберите одну черту.
       *[other] Выберите минимум одну и максимум {$count} черт. { -markings-selection(selectable: $selectable) }
    }
   *[false] { $count ->
        [-1] Можно выбрать любое количество черт.
        [0] Нельзя выбрать ни одной черты.
        [one] Можно выбрать не более одной черты.
       *[other] Можно выбрать не более {$count} черт. { -markings-selection(selectable: $selectable) }
    }
}
markings-reorder = Порядок черт

humanoid-marking-modifier-force = Принудительно
humanoid-marking-modifier-ignore-species = Игнорировать вид
humanoid-marking-modifier-respect-limits = Учитывать лимиты
humanoid-marking-modifier-respect-group-sex = Учитывать ограничения вида и пола
humanoid-marking-modifier-base-layers = Базовый слой
humanoid-marking-modifier-enable = Включить
humanoid-marking-modifier-prototype-id = ID прототипа:

# Categories
markings-organ-Torso = Торс
markings-organ-Head = Голова
markings-organ-ArmLeft = Левая рука
markings-organ-ArmRight = Правая рука
markings-organ-HandRight = Правая кисть
markings-organ-HandLeft = Левая кисть
markings-organ-LegLeft = Левая нога
markings-organ-LegRight = Правая нога
markings-organ-FootLeft = Левая стопа
markings-organ-FootRight = Правая стопа
markings-organ-Eyes = Глаза

markings-layer-Special = Специальное
markings-layer-Tail = Хвост
markings-layer-Tail-Moth = Крылья
markings-layer-Hair = Волосы
markings-layer-FacialHair = Борода и усы
markings-layer-UndergarmentTop = Нижнее бельё (верх)
markings-layer-UndergarmentBottom = Нижнее бельё (низ)
markings-layer-Chest = Грудь
markings-layer-Head = Голова
markings-layer-Snout = Морда
markings-layer-SnoutCover = Морда (внешний слой)
markings-layer-HeadSide = Голова (бок)
markings-layer-HeadTop = Голова (верх)
markings-layer-Eyes = Глаза
markings-layer-RArm = Правая рука
markings-layer-LArm = Левая рука
markings-layer-RHand = Правая кисть
markings-layer-LHand = Левая кисть
markings-layer-RLeg = Правая нога
markings-layer-LLeg = Левая нога
markings-layer-RFoot = Правая стопа
markings-layer-LFoot = Левая стопа
markings-layer-Overlay = Наложение
