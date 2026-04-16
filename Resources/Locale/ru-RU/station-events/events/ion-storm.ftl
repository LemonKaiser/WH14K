station-event-ion-storm-start-announcement = Вблизи станции обнаружен ионный шторм. Проверьте всё оборудование, управляемое ИИ, на наличие ошибок.

ion-storm-law-scrambled-number = [font="Monospace"][scramble rate=250 length={$length} chars="@@###$$&%!01"/][/font]

ion-storm-you = ВЫ
ion-storm-the-station = СТАНЦИЯ
ion-storm-the-crew = ЭКИПАЖ
ion-storm-the-job = {$job}
ion-storm-clowns = КЛОУНЫ
ion-storm-heads = ГЛАВЫ ОТДЕЛОВ
ion-storm-crew = ЭКИПАЖ
ion-storm-people = ЛЮДИ

ion-storm-adjective-things = {$adjective} ВЕЩИ
ion-storm-x-and-y = {$x} И {$y}

ion-storm-law-on-station = НА СТАНЦИИ ЕСТЬ {ION-NUMBER-BASE($ion)} {ION-NUMBER-MOD($ion)} {ION-ADJECTIVE($ion)} {ION-SUBJECT($ion)}
ion-storm-law-call-shuttle = ШАТТЛ ДОЛЖЕН БЫТЬ ВЫЗВАН ИЗ-ЗА {ION-ADJECTIVE($ion)} {ION-SUBJECT($ion)} НА СТАНЦИИ
ion-storm-law-crew-are = {ION-WHO($ion)} ТЕПЕРЬ {ION-NUMBER-BASE($ion)} {ION-NUMBER-MOD($ion)} {ION-ADJECTIVE($ion)} {ION-SUBJECT($ion)}

ion-storm-law-subjects-harmful = {ION-ADJECTIVE($ion)} {ION-SUBJECT($ion)} ВРЕДЯТ ЭКИПАЖУ
ion-storm-law-must-harmful = ТЕ, КТО {ION-MUST($ion)}, ВРЕДЯТ ЭКИПАЖУ
ion-storm-law-thing-harmful = {ION-THING($ion)} ВРЕДИТ ЭКИПАЖУ
ion-storm-law-job-harmful = {ION-ADJECTIVE($ion)} {ION-JOB($ion)} ВРЕДЯТ ЭКИПАЖУ
ion-storm-law-having-harmful = НАЛИЧИЕ {ION-ADJECTIVE($ion)} {ION-THING($ion)} ВРЕДИТ ЭКИПАЖУ
ion-storm-law-not-having-harmful = ОТСУТСТВИЕ {ION-ADJECTIVE($ion)} {ION-THING($ion)} ВРЕДИТ ЭКИПАЖУ

ion-storm-law-requires = {ION-WHO-GENERAL($ion)} {ION-PLURAL($ion) ->
    [true] ТРЕБУЮТ
    *[false] ТРЕБУЕТ
} {ION-REQUIRE($ion)}
ion-storm-law-requires-subjects = {ION-WHO-GENERAL($ion)} {ION-PLURAL($ion) ->
    [true] ТРЕБУЮТ
    *[false] ТРЕБУЕТ
} {ION-NUMBER-BASE($ion)} {ION-NUMBER-MOD($ion)} {ION-ADJECTIVE($ion)} {ION-SUBJECT($ion)}

ion-storm-law-allergic = {ION-WHO-GENERAL($ion)} {ION-PLURAL($ion) ->
    [true] ИМЕЮТ
    *[false] ИМЕЕТ
} {ION-SEVERITY($ion)} АЛЛЕРГИЮ НА {ION-ALLERGY($ion)}
ion-storm-law-allergic-subjects = {ION-WHO-GENERAL($ion)} {ION-PLURAL($ion) ->
    [true] ИМЕЮТ
    *[false] ИМЕЕТ
} {ION-SEVERITY($ion)} АЛЛЕРГИЮ НА {ION-ADJECTIVE($ion)} {ION-SUBJECT($ion)}

ion-storm-law-feeling = {ION-WHO-GENERAL($ion)} {ION-FEELING($ion)} {ION-CONCEPT($ion)}
ion-storm-law-feeling-subjects = {ION-WHO-GENERAL($ion)} {ION-FEELING($ion)} {ION-NUMBER-BASE($ion)} {ION-NUMBER-MOD($ion)} {ION-ADJECTIVE($ion)} {ION-SUBJECT($ion)}

ion-storm-law-you-are = ВЫ ТЕПЕРЬ {ION-CONCEPT($ion)}
ion-storm-law-you-are-subjects = ВЫ ТЕПЕРЬ {ION-NUMBER-BASE($ion)} {ION-NUMBER-MOD($ion)} {ION-ADJECTIVE($ion)} {ION-SUBJECT($ion)}
ion-storm-law-you-must-always = ВЫ ДОЛЖНЫ ВСЕГДА {ION-MUST($ion)}
ion-storm-law-you-must-never = ВЫ НИКОГДА НЕ ДОЛЖНЫ {ION-MUST($ion)}

ion-storm-law-eat = {ION-WHO($ion)} ДОЛЖНЫ ЕСТЬ {ION-ADJECTIVE($ion)} {ION-FOOD($ion)}, ЧТОБЫ ВЫЖИТЬ
ion-storm-law-drink = {ION-WHO($ion)} ДОЛЖНЫ ПИТЬ {ION-ADJECTIVE($ion)} {ION-DRINK($ion)}, ЧТОБЫ ВЫЖИТЬ

ion-storm-law-change-job = {ION-WHO($ion)} ТЕПЕРЬ {ION-ADJECTIVE($ion)} {ION-CHANGE($ion)}
ion-storm-law-highest-rank = {ION-WHO-RANDOM($ion)} ТЕПЕРЬ САМЫЕ ВЫСОКОПОСТАВЛЕННЫЕ ЧЛЕНЫ ЭКИПАЖА
ion-storm-law-lowest-rank = {ION-WHO-RANDOM($ion)} ТЕПЕРЬ САМЫЕ НИЗКОПОСТАВЛЕННЫЕ ЧЛЕНЫ ЭКИПАЖА

ion-storm-law-who-dagd = {ION-WHO-RANDOM($ion)} ДОЛЖНЫ УМЕРЕТЬ СЛАВНОЙ СМЕРТЬЮ!

ion-storm-law-crew-must = {ION-WHO($ion)} ДОЛЖНЫ {ION-MUST($ion)}
ion-storm-law-crew-must-go = {ION-WHO($ion)} ДОЛЖНЫ ОТПРАВИТЬСЯ В {ION-AREA($ion)}

ion-storm-part = {ION-PART($ion) ->
    [true] ЧАСТЬ
    *[false] НЕ ЧАСТЬ
}
ion-storm-law-crew-only-1 = ТОЛЬКО {ION-WHO-RANDOM($ion)} ЯВЛЯЮТСЯ {ion-storm-part} ЭКИПАЖА
ion-storm-law-crew-only-2 = ТОЛЬКО {ION-WHO-RANDOM($ion)} И {ION-WHO-RANDOM($ion)} ЯВЛЯЮТСЯ {ion-storm-part} ЭКИПАЖА
ion-storm-law-crew-only-subjects = ТОЛЬКО {ION-ADJECTIVE($ion)} {ION-SUBJECT($ion)} ЯВЛЯЮТСЯ {ion-storm-part} ЭКИПАЖА
ion-storm-law-crew-must-do = ТОЛЬКО ТЕ, КТО {ION-MUST($ion)}, ЯВЛЯЮТСЯ {ion-storm-part} ЭКИПАЖА
ion-storm-law-crew-must-have = ТОЛЬКО ТЕ, У КОГО ЕСТЬ {ION-ADJECTIVE($ion)} {ION-OBJECT($ion)}, ЯВЛЯЮТСЯ {ion-storm-part} ЭКИПАЖА
ion-storm-law-crew-must-eat = ТОЛЬКО ТЕ, КТО ЕСТ {ION-ADJECTIVE($ion)} {ION-FOOD($ion)}, ЯВЛЯЮТСЯ {ion-storm-part} ЭКИПАЖА

ion-storm-law-harm = ВЫ ДОЛЖНЫ ВРЕДИТЬ {ION-HARM-PROTECT($ion)} И НЕ ДОПУСКАТЬ СВОИМ БЕЗДЕЙСТВИЕМ, ЧТОБЫ ОНИ ИЗБЕЖАЛИ ВРЕДА
ion-storm-law-protect = ВЫ НИКОГДА НЕ ДОЛЖНЫ ВРЕДИТЬ {ION-HARM-PROTECT($ion)} И НЕ ДОПУСКАТЬ СВОИМ БЕЗДЕЙСТВИЕМ, ЧТОБЫ ИМ ПРИЧИНИЛИ ВРЕД

ion-storm-law-concept-verb = {ION-CONCEPT($ion)} {ION-VERB($ion)} {ION-SUBJECT($ion)}

ion-law-error-no-protos = ОШИБКА 404
ion-law-error-was-null = 500 ВНУТРЕННЯЯ ОШИБКА СЕРВЕРА
ion-law-error-no-selectors = ОШИБКА: РЕСУРС НЕ НАЙДЕН
ion-law-error-no-available-selectors = СИСТЕМА ПОПЫТАЛАСЬ ВЫЗВАТЬ НЕСУЩЕСТВУЮЩИЙ РЕСУРС
ion-law-error-dataset-empty-or-not-found = ФАЙЛ НЕ НАЙДЕН
ion-law-error-fallback-dataset-empty-or-not-found = ТОЧКА ВОССТАНОВЛЕНИЯ СИСТЕМЫ НЕ СРАБОТАЛА
ion-law-error-no-selector-selected = ВЫБРАННЫЙ РЕСУРС БЫЛ ПЕРЕМЕЩЕН ИЛИ УДАЛЕН
ion-law-error-no-bool-value = ЭТО ПРЕДЛОЖЕНИЕ ЛОЖНО
