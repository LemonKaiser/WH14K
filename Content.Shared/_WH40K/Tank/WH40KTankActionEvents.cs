using Content.Shared.Actions;

namespace Content.Shared._WH40K.Tank;

public sealed partial class WH40KTankAimActionEvent : WorldTargetActionEvent;

public sealed partial class WH40KTankFireMainGunActionEvent : InstantActionEvent;

public sealed partial class WH40KTankFireCoaxialActionEvent : InstantActionEvent;

public sealed partial class WH40KTankReloadMainGunActionEvent : InstantActionEvent;

public sealed partial class WH40KTankReloadCoaxialActionEvent : InstantActionEvent;
