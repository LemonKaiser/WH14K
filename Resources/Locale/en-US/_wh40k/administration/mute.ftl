wh40k-mute-panel-title = Mute Panel
wh40k-mute-panel-player = Player
wh40k-mute-panel-chat = Chat
wh40k-mute-panel-ahelp = AHelp
wh40k-mute-panel-erase = Erase the player's chat messages
wh40k-mute-panel-submit = Apply Mute
wh40k-mute-panel-tabs-basic = Basic info
wh40k-mute-panel-tabs-players = Player List
wh40k-mute-panel-reason = Reason
wh40k-mute-panel-no-type = Select at least one mute scope.
wh40k-mute-panel-no-player = Select a player to mute.
wh40k-mute-panel-no-reason = Specify a mute reason.

wh40k-mute-scope-chat = Chat
wh40k-mute-scope-ahelp = AHelp
wh40k-mute-scope-all = Chat + AHelp

wh40k-mute-command-invalid-type = Unknown mute scope: {$type}
wh40k-mute-command-invalid-erase = Unknown erase flag: {$value}
wh40k-mute-command-hint-scope = <scope>
wh40k-mute-command-hint-erase = [erase messages]
wh40k-mute-command-hint-erase-no = Keep existing messages
wh40k-mute-command-hint-erase-yes = Erase existing messages

wh40k-unmute-command-none-active = {$player} has no active mutes for that scope.
wh40k-unmute-command-success = Removed {$count} active mute(s) from {$player}.
wh40k-mute-unmute-denied-protected = You cannot remove a mute that was applied by a higher-ranked admin.

wh40k-admin-hierarchy-action-mute = apply a mute to
wh40k-admin-hierarchy-action-unmute = remove a mute from

cmd-mutepanel-desc = Opens the mute panel for a player.
cmd-mutepanel-help = Usage: {$command} [player]
cmd-mute-desc = Applies a chat mute, an ahelp mute, or both to a player account.
cmd-mute-help = Usage: {$command} <player> <chat|ahelp|all> <reason> [minutes] [erase]
cmd-unmute-desc = Removes active mutes from a player account.
cmd-unmute-help = Usage: {$command} <player> [chat|ahelp|all]

wh40k-chat-mute-placeholder-temporary = You are chat-muted for {$time}. Hover to see the reason.
wh40k-chat-mute-placeholder-duration = You are chat-muted for {$time}. Hover to see the reason.
wh40k-chat-mute-placeholder-until = You are chat-muted until {$time}. Hover to see the reason.
wh40k-chat-mute-placeholder-permanent = You are chat-muted. Hover to see the reason.
wh40k-ahelp-mute-placeholder-temporary = You are ahelp-muted for {$time}. Hover to see the reason.
wh40k-ahelp-mute-placeholder-duration = You are ahelp-muted for {$time}. Hover to see the reason.
wh40k-ahelp-mute-placeholder-until = You are ahelp-muted until {$time}. Hover to see the reason.
wh40k-ahelp-mute-placeholder-permanent = You are ahelp-muted. Hover to see the reason.
wh40k-mute-tooltip-temporary =
    Reason: {$reason}
    Expires: {$time}
wh40k-mute-tooltip-permanent =
    Reason: {$reason}
    Expires: Never
wh40k-mute-time-seconds =
    {$count ->
        [one] {$count} sec.
       *[other] {$count} sec.
    }
wh40k-mute-time-minutes =
    {$count ->
        [one] {$count} min.
       *[other] {$count} min.
    }
wh40k-mute-time-hours =
    {$count ->
        [one] {$count} hour
       *[other] {$count} hours
    }
wh40k-mute-time-hours-minutes = {$hours} hr. {$minutes} min.

wh40k-mute-list-title = Mutes
wh40k-mute-list-header-type = Scope
wh40k-mute-list-header-admin = Muted by
wh40k-mute-list-unmuted = Unmuted: {$date}
wh40k-mute-list-unmuted-by = By {$unmuter}

player-panel-mute = Mute
admin-player-actions-mute = Mute
admin-player-actions-window-mute = Mute Panel
wh40k-kick-host-protected = Cannot kick HOST-protected player {$player}.
