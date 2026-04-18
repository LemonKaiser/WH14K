reagent-name-wh40k-promethium = vehicle fuel (promethium)
reagent-desc-wh40k-promethium = A refined hydrocarbon fuel used to keep light battlefield vehicles alive between deployments.

wh40k-vehicle-engine-action-name = Toggle Engine
wh40k-vehicle-engine-action-description = Start or stop the vehicle's engine.

ent-ActionWH40KToggleVehicleEngine = Toggle Engine
    .desc = Start or stop the vehicle's engine.

wh40k-vehicle-engine-state-off = Off
wh40k-vehicle-engine-state-starting = Starting
wh40k-vehicle-engine-state-running = Running
wh40k-vehicle-engine-state-stalled = Stalled
wh40k-vehicle-engine-state-disabled = Disabled

wh40k-vehicle-service-state-nominal = Nominal
wh40k-vehicle-service-state-worn = Worn
wh40k-vehicle-service-state-critical = Critical
wh40k-vehicle-service-state-disabled = Disabled

wh40k-vehicle-toggle-on = On
wh40k-vehicle-toggle-off = Off

wh40k-vehicle-engine-popup-starting = The engine starts to turn over.
wh40k-vehicle-engine-popup-running = The engine catches and settles into a steady growl.
wh40k-vehicle-engine-popup-off = The engine winds down.
wh40k-vehicle-engine-popup-no-key = The ignition does not have the correct key.
wh40k-vehicle-engine-popup-no-fuel = The tank is dry.
wh40k-vehicle-engine-popup-disabled = The drivetrain is too damaged to start.
wh40k-vehicle-engine-popup-stalled-no-fuel = The engine coughs and stalls as the promethium feed runs dry.
wh40k-vehicle-engine-popup-disabled-while-running = The engine dies as damaged drivetrain systems seize up.

wh40k-vehicle-examine-fuel = Fuel: [color=orange]{ $percent }%[/color] ({ $current } / { $capacity })
wh40k-vehicle-examine-runtime = Idle runtime remaining: [color=lightblue]{ $remaining }[/color]
wh40k-vehicle-examine-engine = Engine state: [color=white]{ $state }[/color]
wh40k-vehicle-examine-service = Service state: [color=white]{ $state }[/color] ([color=lightblue]{ $integrity }%[/color] integrity)

wh40k-vehicle-terminal-examine-buffer = Terminal buffer: [color=orange]{ $current } / { $capacity }[/color]
wh40k-vehicle-terminal-examine-modes = Auto intake: [color=white]{ $intake }[/color], auto refuel: [color=white]{ $refuel }[/color]

wh40k-vehicle-fuel-ui-window-title = Vehicle Fuel Terminal
wh40k-vehicle-fuel-ui-subtitle = Manage promethium intake, terminal reserves and nearby vehicle refueling.
wh40k-vehicle-fuel-ui-footer = Keep the terminal powered and leave fuel containers or motorized vehicles within three tiles.

wh40k-vehicle-fuel-ui-card-buffer = Terminal Buffer
wh40k-vehicle-fuel-ui-card-source = Nearby Source
wh40k-vehicle-fuel-ui-card-vehicle = Nearby Vehicle

wh40k-vehicle-fuel-ui-buffer-hint = Reserve level: { $percent }%
wh40k-vehicle-fuel-ui-source-hint = Source reserve: { $current } / { $capacity }
wh40k-vehicle-fuel-ui-source-hint-none = No promethium source in range.
wh40k-vehicle-fuel-ui-vehicle-hint = Tank level: { $percent }% ({ $current } / { $capacity })
wh40k-vehicle-fuel-ui-vehicle-hint-none = No refuel target in range.

wh40k-vehicle-fuel-ui-button-enable-intake = Auto Intake On
wh40k-vehicle-fuel-ui-button-disable-intake = Auto Intake Off
wh40k-vehicle-fuel-ui-button-enable-refuel = Auto Refuel On
wh40k-vehicle-fuel-ui-button-disable-refuel = Auto Refuel Off

wh40k-vehicle-fuel-ui-diagnostics-title = Vehicle Diagnostics
wh40k-vehicle-fuel-ui-power-label = Terminal Power
wh40k-vehicle-fuel-ui-engine-label = Engine State
wh40k-vehicle-fuel-ui-service-label = Service State
wh40k-vehicle-fuel-ui-runtime-label = Max Idle Runtime
wh40k-vehicle-fuel-ui-service-value = { $state } ({ $integrity }%)

wh40k-vehicle-fuel-ui-no-source = No source
wh40k-vehicle-fuel-ui-no-vehicle = No vehicle
