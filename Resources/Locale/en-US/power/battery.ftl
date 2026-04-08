## Strings for the battery (SMES/substation) menu

battery-menu-window-title = Power Node
battery-menu-header-subtitle = Sanctified sector power node.\nSanction intake, discharge and reserve.
battery-menu-header-subtitle-smes = Superconductive reserve bank.\nSanction intake, discharge and cell stock.
battery-menu-header-subtitle-substation = Medium-grid relay node.\nHold the flow and preserve sector reserve.
battery-menu-footer-left = Trunk feed stable. Reserve is holding the sector.
battery-menu-footer-right = Energy Bank WH14K
battery-menu-footer-right-smes = SMES WH14K
battery-menu-footer-right-substation = Substation WH14K
battery-menu-footer-charging = The bank is drawing blessed feed from the trunk.
battery-menu-footer-discharging = The bank is driving reserve power into the sector.
battery-menu-footer-critical = Reserve nearly exhausted. Sector brownout is imminent.
battery-menu-footer-locked = Intake and discharge are both manually cut off.

battery-menu-device-panel-title = Transfer Node
battery-menu-device-note-balanced = Intake and discharge\nremain in balance.
battery-menu-device-note-intake = The node stands ready\nto absorb trunk power.
battery-menu-device-note-output = The node feeds the\nsector from reserve.
battery-menu-device-note-locked = Both contours are sealed by operator sanction.

battery-menu-button-off = Off
battery-menu-button-on = On

battery-menu-out = Discharge
battery-menu-in = Intake
battery-menu-charge-header = Intake Contour
battery-menu-discharge-header = Discharge Contour
battery-menu-storage-header = Storage Bank
battery-menu-passthrough = Throughput
battery-menu-max = Limit
battery-menu-current = Flow
battery-menu-stored = Reserve
battery-menu-energy = Energy
battery-menu-eta-full = Replenish
battery-menu-eta-empty = Deplete

battery-menu-input-state-off = SEALED
battery-menu-input-state-standby = STANDBY
battery-menu-input-state-ready = READY
battery-menu-input-state-active = FEEDING

battery-menu-input-note-off = Intake from the trunk\nis sealed by breaker sanction.
battery-menu-input-note-standby = The trunk is silent.\nThe node awaits fresh feed.
battery-menu-input-note-ready = The contour is open\nto receive power.
battery-menu-input-note-active = The cells are saturating\nunder inbound flow.

battery-menu-output-state-off = SEALED
battery-menu-output-state-standby = DRY
battery-menu-output-state-ready = RESERVE
battery-menu-output-state-active = FEEDING

battery-menu-output-note-off = Discharge into the sector\nis sealed by breaker sanction.
battery-menu-output-note-empty = The bank holds no reserve\nfor sector support.
battery-menu-output-note-ready = Reserve is being held\nuntil further sanction.
battery-menu-output-note-active = The bank is feeding the grid\nfrom stored power.

battery-menu-storage-note-full = Cells saturated\nand ready for reserve duty.
battery-menu-storage-note-high = Reserve sufficient\nfor stable sector duty.
battery-menu-storage-note-medium = Reserve declining.\nReplenishment advised.
battery-menu-storage-note-low = Reserve critical.\nGrid loss is near.

battery-menu-eta-value = ~{ $minutes } min
battery-menu-eta-value-max = >{ $minutes } min
battery-menu-eta-value-na = N/A
battery-menu-power-value = { POWERWATTS($value) }
battery-menu-stored-percent-value = { TOSTRING($value, "P0") }
battery-menu-stored-energy-value = { ENERGYWATTHOURS($value) }
