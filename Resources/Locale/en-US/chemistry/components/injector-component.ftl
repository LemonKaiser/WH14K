## UI

injector-draw-text = Draw
injector-inject-text = Inject
injector-invalid-injector-toggle-mode = Invalid
injector-volume-label = Volume: [color=white]{$currentVolume}/{$totalVolume}[/color]
    Mode: [color=white]{$modeString}[/color] ([color=white]{$transferVolume}u[/color])

## Entity


injector-component-inject-mode-name = inject
injector-component-draw-mode-name = draw
injector-component-dynamic-mode-name = dynamic
injector-component-mode-changed-text = Now {$mode}
injector-component-transfer-success-message = You transfer {$amount}u into {THE($target)}.
injector-component-transfer-success-message-self = You transfer {$amount}u into yourself.
injector-component-inject-success-message = You inject {$amount}u into {THE($target)}!
injector-component-inject-success-message-self = You inject {$amount}u into yourself!
injector-component-transfer-success-message = You transfer {$amount}u into {THE($target)}.
injector-component-draw-success-message = You draw {$amount}u from {THE($target)}.
injector-component-draw-success-message-self = You draw {$amount}u from youself.
injector-component-target-already-full-message = {CAPITALIZE(THE($target))} is already full!
injector-component-target-already-full-message-self = You are already full!
injector-component-target-is-empty-message = {CAPITALIZE(THE($target))} is empty!
injector-component-target-is-empty-message-self = You are empty!
injector-component-cannot-toggle-draw-message = Too full to draw!
injector-component-cannot-toggle-inject-message = Nothing to inject!

## mob-inject doafter messages

injector-component-drawing-user = You start drawing the needle.
injector-component-injecting-user = You start injecting the needle.
injector-component-drawing-target = {CAPITALIZE(THE($user))} is trying to use a needle to draw from you!
injector-component-injecting-target = {CAPITALIZE(THE($user))} is trying to inject a needle into you!
