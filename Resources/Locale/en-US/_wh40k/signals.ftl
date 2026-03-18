ent-WH40KSignalFlareBase = tactical signal flare
    .desc = A tactical flare that marks a temporary coordinate beacon once lit and grounded.
ent-WH40KSignalFlareImperium = imperium signal flare
    .desc = A tactical signal flare calibrated to Imperium command channels.
ent-WH40KSignalFlareHeretics = heretics signal flare
    .desc = A tactical signal flare calibrated to Heretics command channels.
ent-WH40KSignalFlareMarker = signal flare marker
    .desc = A temporary tactical beacon created by a lit and grounded signal flare.

wh40k-signal-flare-examine-policy = Signal cadence: { $seconds }s personal cooldown; anti-spam window { $window }s / { $count } signals; team active cap { $active }.
wh40k-signal-flare-examine-arming = Marker arming in progress: #{ $id }.

wh40k-signal-flare-popup-pickup-blocked = You cannot pick up a lit signal flare.
wh40k-signal-flare-popup-stow-blocked = You cannot store a lit signal flare.
wh40k-signal-flare-popup-no-team = Your team identity is not resolved for this signal flare.
wh40k-signal-flare-popup-wrong-team = This signal flare belongs to another faction.
wh40k-signal-flare-popup-user-cooldown = You need to wait { $seconds }s before arming another signal flare.
wh40k-signal-flare-popup-rate-limit = Signal flare rate limit reached ({ $count } in window). Try again in { $seconds }s.
wh40k-signal-flare-popup-armed = Signal flare #{ $id } armed. Keep it grounded.
wh40k-signal-flare-popup-marker-unavailable = Signal flare marker prototype is unavailable.
wh40k-signal-flare-popup-team-cap = Team signal flare marker cap reached ({ $count } active).
wh40k-signal-flare-popup-activated = Signal flare marker #{ $id } active at { $x } / { $y }.

wh40k-signal-flare-team-message = [Signal flare] { $user }: marker #{ $id } active at { $x } / { $y }.
wh40k-signal-flare-user-unknown = unknown operator
wh40k-signal-flare-marker-label = Signal flare
