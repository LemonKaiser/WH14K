# Discord Auth Relay

Скрипт-прокси для перенаправления Discord OAuth callback на игровой сервер.

## Проблема

Discord перенаправляет пользователя на `https://2612.koara.live/wh40k/discord-auth/callback`,
но игровой сервер доступен на `ebengrad.node-oheir.simplestation.org:25910` — панелька
не даёт прямого доступа к вебхукам на этом адресе.

## Решение

1. **callback.php** принимает GET-запрос от Discord (с `code` и `state`).
2. Делает POST на игровой сервер `/wh40k/discord-auth/relay`, передавая `code` + `state` + общий секрет.
3. Игровой сервер обменивает код на токен и привязывает Discord-аккаунт.

## Настройка

### На игровом сервере (CVars)

```
wh40k.discord_auth_relay_secret = ВСТАВИТЬ_СЕКРЕТ_СЮДА
wh40k.discord_auth_redirect_uri = https://2612.koara.live/wh40k/discord-auth/callback
```

### На внешнем сервере (2612.koara.live)

1. Залить `callback.php` в `/wh40k/discord-auth/callback` (так чтобы URL `https://2612.koara.live/wh40k/discord-auth/callback` вызывал этот скрипт).
2. Задать переменные окружения (или отредактировать константы в самом файле):
   - `RELAY_TARGET` = `http://ebengrad.node-oheir.simplestation.org:25910/wh40k/discord-auth/relay`
   - `RELAY_SECRET` = тот же секрет, что и в CVar

Если используется `.htaccess` / nginx, настроить rewrite:

**nginx:**
```nginx
location = /wh40k/discord-auth/callback {
    fastcgi_pass unix:/run/php/php-fpm.sock;
    include fastcgi_params;
    fastcgi_param SCRIPT_FILENAME /path/to/callback.php;
    fastcgi_param RELAY_TARGET http://ebengrad.node-oheir.simplestation.org:25910/wh40k/discord-auth/relay;
    fastcgi_param RELAY_SECRET ВСТАВИТЬ_СЕКРЕТ_СЮДА;
}
```

**Apache (.htaccess):**
```apache
RewriteEngine On
RewriteRule ^wh40k/discord-auth/callback$ callback.php [L]

SetEnv RELAY_TARGET http://ebengrad.node-oheir.simplestation.org:25910/wh40k/discord-auth/relay
SetEnv RELAY_SECRET ВСТАВИТЬ_СЕКРЕТ_СЮДА
```

### В Discord Developer Portal

Redirect URI: `https://2612.koara.live/wh40k/discord-auth/callback`

## Генерация секрета

```bash
openssl rand -hex 32
```

Скопировать в обе конфигурации (CVar на игровом сервере и переменная окружения на 2612.koara.live).
