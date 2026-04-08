<?php
/**
 * Discord OAuth2 Callback Relay for WH40K
 *
 * Deploy this on the external web server (e.g. https://2612.koara.live/wh40k/discord-auth/callback).
 * It receives the Discord OAuth callback (GET with ?code=...&state=...) and forwards code+state
 * to the game server's relay endpoint via POST.
 *
 * Configuration (environment variables or edit the constants below):
 *   RELAY_TARGET  — game server relay URL, e.g. http://ebengrad.node-oheir.simplestation.org:25910/wh40k/discord-auth/relay
 *   RELAY_SECRET  — shared secret matching the game server's wh40k.discord_auth_relay_secret CVar
 */

// ── Configuration ──────────────────────────────────────────────────────────
$relayTarget = getenv('RELAY_TARGET') ?: 'http://ebengrad.node-oheir.simplestation.org:25910/wh40k/discord-auth/relay';
$relaySecret = getenv('RELAY_SECRET') ?: '';
// ────────────────────────────────────────────────────────────────────────────

header('Cache-Control: no-store');

if ($_SERVER['REQUEST_METHOD'] !== 'GET') {
    http_response_code(405);
    exit('Method Not Allowed');
}

if (empty($relaySecret)) {
    http_response_code(500);
    renderPage(false, 'Relay не настроен', 'Секрет relay не задан на сервере.');
    exit;
}

// Discord error (user denied access)
$error = $_GET['error'] ?? '';
if ($error !== '') {
    http_response_code(400);
    renderPage(false, 'Авторизация отклонена', 'Вы отклонили авторизацию Discord.');
    exit;
}

$code  = $_GET['code']  ?? '';
$state = $_GET['state'] ?? '';

if ($code === '' || $state === '') {
    http_response_code(400);
    renderPage(false, 'Неверный запрос', 'Отсутствует код или state параметр.');
    exit;
}

// Forward to game server
$payload = json_encode(['code' => $code, 'state' => $state]);

$ch = curl_init($relayTarget);
curl_setopt_array($ch, [
    CURLOPT_POST           => true,
    CURLOPT_POSTFIELDS     => $payload,
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_TIMEOUT        => 15,
    CURLOPT_CONNECTTIMEOUT => 5,
    CURLOPT_HTTPHEADER     => [
        'Content-Type: application/json',
        'X-WH40K-Relay-Secret: ' . $relaySecret,
    ],
]);

$body    = curl_exec($ch);
$httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
$curlErr  = curl_error($ch);
curl_close($ch);

if ($curlErr !== '') {
    http_response_code(502);
    renderPage(false, 'Ошибка соединения', 'Не удалось связаться с игровым сервером.');
    exit;
}

$json = @json_decode($body, true);
$ok      = $json['ok']      ?? false;
$message = $json['message'] ?? 'Неизвестный ответ от сервера.';

http_response_code($ok ? 200 : $httpCode);
renderPage($ok, $ok ? 'Discord привязан' : 'Ошибка привязки', $message);
exit;

// ── HTML renderer ──────────────────────────────────────────────────────────
function renderPage(bool $success, string $title, string $message): void
{
    $color     = $success ? '#6ab04c' : '#d35454';
    $badgeText = $success ? 'OK' : 'ERROR';
    $safeTitle   = htmlspecialchars($title,   ENT_QUOTES, 'UTF-8');
    $safeBadge   = htmlspecialchars($badgeText, ENT_QUOTES, 'UTF-8');
    $safeMessage = htmlspecialchars($message, ENT_QUOTES, 'UTF-8');

    echo <<<HTML
<!doctype html>
<html lang="ru">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{$safeTitle}</title>
    <style>
        body { background:#12151c; color:#f1f3f5; font-family:Segoe UI, sans-serif; margin:0; padding:32px; }
        .card { max-width:560px; margin:0 auto; background:#1b2230; border:1px solid #2f3a4d; border-radius:14px; padding:24px; }
        .badge { display:inline-block; padding:6px 10px; border-radius:999px; background:{$color}; color:#fff; font-weight:700; margin-bottom:16px; }
        h1 { margin:0 0 12px 0; font-size:24px; }
        p { margin:0; line-height:1.5; color:#d9dde5; }
    </style>
</head>
<body>
    <div class="card">
        <div class="badge">{$safeBadge}</div>
        <h1>{$safeTitle}</h1>
        <p>{$safeMessage}</p>
    </div>
</body>
</html>
HTML;
}
