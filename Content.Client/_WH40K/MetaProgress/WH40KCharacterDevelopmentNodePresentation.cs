using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.MetaProgress;

public sealed record WH40KCharacterDevelopmentNodePresentation(string BranchTitleKey, string BranchSubtitleKey, string NodeTitleKey, string DescriptionKey, string StateKey, int Cost, bool Planned, bool Available, WH40KCharacterDevelopmentOrganType Organ, Color Accent);
