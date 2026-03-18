cmd-screencheck-desc = Request a screenshot from an online player's client.
cmd-screencheck-help = Usage: screencheck <ckey/userId>
cmd-screencheck-hint = <ckey>

screen-check-player-not-found = Could not find player '{ $player }'.
screen-check-player-offline = Player '{ $player }' is not online.
screen-check-request-sent = Screencheck request sent to { $player }.
screen-check-request-active-admin = You already have an active screencheck request.
screen-check-request-active-target = Player '{ $player }' already has an active screencheck request.
screen-check-request-limit-reached = Too many active screencheck requests. Try again in a moment.

screen-check-window-title = Screencheck: { $player }
screen-check-status-pending = Waiting for the player's client screenshot...
screen-check-status-success = Screenshot received.
screen-check-status-timeout = The player's client did not answer in time.
screen-check-status-disconnected = The player disconnected before the screenshot was received.
screen-check-status-capture-failed = The player's client failed to capture the screenshot.
screen-check-status-invalid-data = The screenshot data could not be decoded.
screen-check-status-cancelled = The screencheck was cancelled.

screen-check-admin-announcement-start = { $admin } requested a screencheck from { $player }.
screen-check-admin-announcement-finish = Screencheck for { $player } by { $admin } finished with status: { $status }.

player-panel-screen-check-active = Active screencheck: by { $admin } since { $time }
player-panel-screen-check-active-none = Active screencheck: none
player-panel-screen-check-last = Last screencheck: { $status }, by { $admin } at { $time }
player-panel-screen-check-last-none = Last screencheck: none
