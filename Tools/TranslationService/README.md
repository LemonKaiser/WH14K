# WH40K Translation Service

`WH40K Translation Service` это отдельный HTTP-сервис для автоматического перевода игрового чата `RU <-> EN` через `CTranslate2 + OPUS-MT`.

Сервис можно запустить:

- локально на той же машине, где работает ваш сервер;
- на отдельной VPS или домашнем сервере;
- в Docker-контейнере.

Серверная часть `WH14K` уже умеет работать с этим сервисом напрямую. Также в коде заранее подготовлен альтернативный режим с `DeepL API`, если вы позже захотите отказаться от локальных моделей.

## Что входит в систему

Система состоит из двух частей:

1. `Tools/TranslationService`.
   Это отдельный Python/FastAPI сервис, который принимает HTTP-запросы на перевод.

2. Серверные настройки `WH14K`.
   Игра отправляет сообщения в локальный сервис, получает перевод и подставляет его в чат.

На практике это выглядит так:

- игрок пишет сообщение в игре;
- сервер определяет язык сообщения;
- сервер отправляет текст в Translation Service;
- сервис возвращает перевод;
- игроки получают текст на своём выбранном языке перевода.

## Что умеет сервис

- переводит только `RU <-> EN`;
- работает на CPU, без обязательной GPU;
- хранит локальный cache переводов с LRU-вытеснением и ленивой периодической очисткой по TTL (O(1) на чтение и запись в среднем случае);
- использует лёгкий language-id через `py3langid` как fallback для смешанных `RU/EN` реплик;
- переводит смешанные сообщения не только целиком, но и по предложениям и языковым фрагментам;
- чистит шумный чат-текст перед переводом: повторы букв/пунктуации, типографские кавычки, явные ошибки раскладки;
- поддерживает glossary для терминов, имён, сленга и служебных токенов: case-insensitive сопоставление, регистр target-слова подстраивается под исходный (`Псайкер`, `псайкер`, `ПСАЙКЕР` — всё из одной записи);
- умеет бережно обходиться со ссылками, кодами, числами и похожими строками;
- отдаёт простой API `POST /translate`;
- принимает и `snake_case`, и `PascalCase` JSON-поля;
- ограничивает нагрузку по IP: 100 запросов / 10 секунд, автоматически вычищает устаревшие записи каждые 60 секунд;
- поддерживает горячую перезагрузку глоссария через `POST /glossary/reload`;
- валидирует конфигурацию при старте и выдаёт понятные ошибки на неверные параметры;
- все регуляные выражения скомпилированы при загрузке, а не на каждый запрос.

## Что потребуется заранее

Рекомендуется подготовить:

- `Python 3.11+` (поддерживается `3.11`, `3.12`, `3.13`, `3.14`);
- `git`;
- свободные несколько гигабайт на модели и tokenizer-файлы;
- открытый порт `8090`, если сервис будет доступен по сети;
- `Docker Desktop` или `docker + docker compose`, если вы хотите запускать сервис в контейнере.

Важно:

- репозиторий не содержит готовых папок `models/` и `tokenizers/`;
- их нужно скачать отдельно перед первым запуском;
- для удобства есть два скрипта: `download_models.sh` для bash-среды и `download_models.ps1` для PowerShell;
- если вы оставляете `WH40K_TRANSLATION_API_KEY=change-me`, тот же ключ нужно прописать и в конфиге игрового сервера.

## Настройка на Windows (PowerShell)

### 1. Перейдите в каталог сервиса

```powershell
Set-Location "c:\path\to\WH14K\Tools\TranslationService"
```

### 2. Подготовьте `.env` и glossary

```powershell
Copy-Item .env.example .env
Copy-Item .\config\glossary.example.json .\config\glossary.json
```

### 3. Создайте виртуальное окружение и установите зависимости

```powershell
py -3 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --upgrade pip
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

Если у вас установлено несколько версий Python, при необходимости замените `py -3` на более явный вариант, например `py -3.11` или `py -3.12`.

Если вы предпочитаете активировать окружение вручную, можно использовать и такой вариант:

```powershell
.\.venv\Scripts\Activate.ps1
```

Но для простоты в примерах ниже используются прямые пути к `python.exe` и `uvicorn.exe`, чтобы не зависеть от политики выполнения PowerShell.

### 4. Скачайте модели и tokenizer-файлы

Для Windows используйте `download_models.ps1`.

Если политика выполнения разрешает запуск локальных скриптов, достаточно выполнить:

```powershell
.\download_models.ps1
```

Если PowerShell блокирует запуск `.ps1`, используйте такой вариант:

```powershell
powershell -ExecutionPolicy Bypass -File .\download_models.ps1
```

Скрипт:

- создаёт каталоги `models/` и `tokenizers/`;
- устанавливает Python-зависимости из `requirements.txt`;
- проверяет наличие `torch`;
- конвертирует `Helsinki-NLP/opus-mt-en-ru` и `Helsinki-NLP/opus-mt-ru-en` в формат `CTranslate2`;
- сохраняет tokenizer-файлы в `tokenizers/`.

Если вы не хотите запускать `.ps1`, ниже остаются эквивалентные команды вручную.

Создайте нужные каталоги:

```powershell
New-Item -ItemType Directory -Force .\models\opus-mt-en-ru | Out-Null
New-Item -ItemType Directory -Force .\models\opus-mt-ru-en | Out-Null
New-Item -ItemType Directory -Force .\tokenizers\opus-mt-en-ru | Out-Null
New-Item -ItemType Directory -Force .\tokenizers\opus-mt-ru-en | Out-Null
```

Установите зависимости:

```powershell
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

Сконвертируйте модели:

```powershell
.\.venv\Scripts\ct2-transformers-converter.exe --force --model Helsinki-NLP/opus-mt-en-ru --quantization int8 --output_dir .\models\opus-mt-en-ru
.\.venv\Scripts\ct2-transformers-converter.exe --force --model Helsinki-NLP/opus-mt-ru-en --quantization int8 --output_dir .\models\opus-mt-ru-en
```

Скачайте tokenizer-файлы:

```powershell
@'
from pathlib import Path
from transformers import AutoTokenizer

models = {
    "Helsinki-NLP/opus-mt-en-ru": Path("tokenizers") / "opus-mt-en-ru",
    "Helsinki-NLP/opus-mt-ru-en": Path("tokenizers") / "opus-mt-ru-en",
}

for source, target_dir in models.items():
    tokenizer = AutoTokenizer.from_pretrained(source)
    tokenizer.save_pretrained(target_dir)
    print(f"saved tokenizer for {source} -> {target_dir}")
'@ | .\.venv\Scripts\python.exe -
```

### 5. Запустите сервис

```powershell
.\.venv\Scripts\uvicorn.exe app.main:app --host 0.0.0.0 --port 8090
```

Если вы хотите оставить окно открытым и смотреть логи, достаточно держать этот процесс запущенным.

## Настройка на Linux или macOS

### 1. Перейдите в каталог сервиса

```bash
cd /path/to/WH14K/Tools/TranslationService
```

### 2. Подготовьте `.env` и glossary

```bash
cp .env.example .env
cp config/glossary.example.json config/glossary.json
```

### 3. Создайте виртуальное окружение и установите зависимости

```bash
python3 -m venv .venv
source .venv/bin/activate
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

### 4. Скачайте модели и tokenizer-файлы

```bash
chmod +x download_models.sh
sed -i 's/\r$//' download_models.sh
./download_models.sh
```

Скрипт:

- устанавливает Python-зависимости;
- конвертирует `Helsinki-NLP/opus-mt-en-ru` и `Helsinki-NLP/opus-mt-ru-en` в формат `CTranslate2`;
- сохраняет tokenizer-файлы в `tokenizers/`.

### 5. Запустите сервис

```bash
./.venv/bin/uvicorn app.main:app --host 0.0.0.0 --port 8090
```

### Дополнительная заметка для Linux

Если сервис не стартует и ругается на OpenMP или системные библиотеки, на Ubuntu/Debian обычно помогает:

```bash
sudo apt-get update
sudo apt-get install -y libgomp1
```

На macOS чаще всего проще либо использовать обычный Python из Homebrew, либо сразу идти через Docker, если вы не хотите разбираться с нативными зависимостями.

## Запуск через Docker

Docker-режим удобен, если вы хотите изолировать сервис от основной системы.

Важно: Docker Compose не скачивает модели автоматически. Папки `models/` и `tokenizers/` всё равно нужно подготовить заранее.

### 1. Подготовьте `.env` и glossary

```bash
cp .env.example .env
cp config/glossary.example.json config/glossary.json
```

На Windows можно сделать так:

```powershell
Copy-Item .env.example .env
Copy-Item .\config\glossary.example.json .\config\glossary.json
```

### 2. Скачайте модели

Сделайте это любым способом из предыдущих разделов.

### 3. Соберите и поднимите контейнер

```bash
docker compose up -d --build
```

### 4. Посмотрите логи Docker

```bash
docker compose logs -f
```

## Что настраивается в `.env`

Файл `.env` управляет локальным сервисом.

Самые важные параметры:

- `WH40K_TRANSLATION_HOST`.
  На каком адресе слушать сервис. Обычно оставляют `0.0.0.0`.

- `WH40K_TRANSLATION_PORT`.
  Порт сервиса. По умолчанию `8090`.

- `WH40K_TRANSLATION_API_KEY`.
  API-ключ. Если вы не хотите открывать сервис всем подряд, оставьте здесь непустое значение и укажите тот же ключ в конфиге сервера.

- `WH40K_TRANSLATION_MODEL_ROOT`.
  Где лежат конвертированные модели.

- `WH40K_TRANSLATION_TOKENIZER_ROOT`.
  Где лежат tokenizer-файлы.

- `WH40K_TRANSLATION_GLOSSARY_PATH`.
  Путь до `glossary.json`.

- `WH40K_TRANSLATION_CACHE_TTL_SECONDS` и `WH40K_TRANSLATION_CACHE_MAX_ITEMS`.
  Размер и время жизни локального cache.

- `WH40K_TRANSLATION_INTER_THREADS` и `WH40K_TRANSLATION_INTRA_THREADS`.
  Настройки потоков CPU. Для обычной VPS часто хватает `1` и `2`.

- `WH40K_TRANSLATION_BEAM_SIZE` и `WH40K_TRANSLATION_MAX_DECODING_LENGTH`.
  Настройки декодирования. Для слабой CPU-машины хороший стартовый вариант это `beam_size=1` и `max_decoding_length=128`.

## Проверка, что сервис действительно работает

### Вариант для Linux/macOS

```bash
curl http://127.0.0.1:8090/health
```

Ожидаемый ответ примерно такой:

```json
{
  "status": "ok",
  "directions": ["EN->RU", "RU->EN"],
  "glossary_terms": 123,
  "cache_size": 0
}
```

### Вариант для Windows PowerShell

```powershell
Invoke-RestMethod http://127.0.0.1:8090/health
```

## Быстрый smoke-test через `probe_service.py`

Этот скрипт прогоняет набор типичных игровых фраз и показывает:

- что сервис отвечает;
- что перевод вообще работает в обе стороны;
- сколько примерно занимает каждый запрос.

### Windows PowerShell

```powershell
$env:WH40K_TRANSLATION_SERVICE_URL = "http://127.0.0.1:8090"
$env:WH40K_TRANSLATION_API_KEY = "change-me"
.\.venv\Scripts\python.exe .\probe_service.py
```

### Linux/macOS

```bash
WH40K_TRANSLATION_SERVICE_URL=http://127.0.0.1:8090 \
WH40K_TRANSLATION_API_KEY=change-me \
./.venv/bin/python ./probe_service.py
```

## Подключение к серверу

Когда сервис уже работает, пропишите его адрес в config.toml

Пример для локального Translation Service:

```toml
[wh40k.chat_translation]
enabled = true
provider = "service"
service_url = "http://127.0.0.1:8090"
api_key = "change-me"
timeout_ms = 450
soft_hold_ms = 75
failure_backoff_seconds = 5
max_message_length = 240
cache_ttl_seconds = 1800
cache_max_entries = 4096
```

Если сервис стоит на другой машине, замените `127.0.0.1` на внешний IP или доменное имя.

Лучше не добавлять завершающий `/`, чтобы не плодить путаницу в примерах и проверках.

После изменения конфига перезапустите игровой сервер.

## Если вы хотите использовать DeepL вместо локальной модели

Серверная часть уже умеет работать и без локального Python-сервиса.

Пример конфигурации:

```toml
[wh40k.chat_translation]
enabled = true
provider = "deepl"
timeout_ms = 450
soft_hold_ms = 75
failure_backoff_seconds = 5
max_message_length = 240
cache_ttl_seconds = 1800
cache_max_entries = 4096

[wh40k.chat_translation.deepl]
auth_key = "YOUR_DEEPL_KEY"
base_url = ""
model_type = "latency_optimized"
preserve_formatting = true
split_sentences = "0"
context = ""

[wh40k.chat_translation.deepl.glossary_id]
ru_en = ""
en_ru = ""
```

Практические замечания:

- если `base_url` пустой и ключ заканчивается на `:fx`, автоматически используется `https://api-free.deepl.com`;
- для остальных ключей автоматически используется `https://api.deepl.com`;
- `RU` и `EN` уже совпадают с используемыми кодами языка;
- `context` можно использовать для улучшения коротких реплик;
- `glossary_id` задаётся отдельно для `RU -> EN` и `EN -> RU`.

## Как работает glossary

Файл `config/glossary.json` нужен для случаев, когда вы не хотите полагаться только на модель.

Поддерживаются три раздела:

- `preserve`.
  Термин вообще не переводится.

- `ru_en`.
  Замена для направления `RU -> EN`.

- `en_ru`.
  Замена для направления `EN -> RU`.

Glossary особенно полезен для:

- названий ролей;
- фракций и имён собственных;
- игрового сленга;
- кодов и технических обозначений вроде `SS14`, `WH40K`, `A12`, `ENG-3`.

После изменения `glossary.json` можно перезагрузить глоссарий без перезапуска сервиса.

Glossary поддерживает case-insensitive сопоставление, поэтому для одного термина достаточно одной записи — регистр подстраивается автоматически. Например, запись `"Psyker" -> "Псайкер"` будет корректно работать и для `"psyker"`, и для `"PSYKER"`.

### Горячая перезагрузка glossary

Вместо перезапуска сервиса можно вызвать:

```bash
curl -X POST http://127.0.0.1:8090/glossary/reload \
  -H "X-Api-Key: change-me"
```

Windows PowerShell:

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8090/glossary/reload -Headers @{"X-Api-Key"="change-me"}
```

Сервис перечитает `glossary.json` (и `glossary.example.json`) и применит обновления к следующим запросам.

## Практические настройки для небольшой VPS

Если вы запускаете сервис на скромной CPU-машине, хороший базовый набор такой:

- `WH40K_TRANSLATION_INTER_THREADS=1`
- `WH40K_TRANSLATION_INTRA_THREADS=2`
- `WH40K_TRANSLATION_BEAM_SIZE=1`
- `WH40K_TRANSLATION_MAX_DECODING_LENGTH=128`
- `py3langid`, который ставится вместе с `requirements.txt`
- int8-модели, которые уже используются в скрипте скачивания

Обычно этого хватает, чтобы не раздувать расход CPU и при этом держать нормальную задержку для игрового чата. Если позже понадобится чуть проверить качество на коротких репликах, `beam_size=2` можно попробовать вручную, но для слабой машины это уже компромисс в сторону latency.

## Частые проблемы

### Сервис не стартует и пишет про отсутствующие модели

Проверьте, что у вас действительно существуют папки:

- `models/opus-mt-en-ru`
- `models/opus-mt-ru-en`
- `tokenizers/opus-mt-en-ru`
- `tokenizers/opus-mt-ru-en`

### `401 invalid_api_key`

Ключ в `.env` и ключ в конфиге `WH14K` не совпадают.

### `404` или нет соединения с сервисом

Проверьте:

- правильный `service_url`;
- что сервис действительно слушает порт `8090`;
- что firewall или security group не блокируют соединение.

## Что лучше выбрать на практике

Если не хочется тратить время на разбор зависимостей:

- на Windows чаще всего самый простой путь это `download_models.ps1` или Docker;
- на Linux/VPS обычно удобнее обычное `venv` + `download_models.sh`;
- если вам не нужен локальный сервис вообще, можно сразу использовать `DeepL`.
