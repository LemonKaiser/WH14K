using Content.Server._WH40K.Localizations;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Examine;

public sealed partial class WH40KPlayerLanguageExamineSystem : EntitySystem
{
    [Dependency] private  WH40KPlayerCultureTracker _cultureTracker = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<ActorComponent> ent, ref ExaminedEvent args)
    {
        if (!HasComp<HumanoidProfileComponent>(ent))
            return;

        var languageCode = _cultureTracker.ResolveLanguageCode(ent.Comp.PlayerSession);
        if (languageCode == null)
            return;

        args.PushMarkup(Loc.GetString("wh40k-player-language-status-examine", ("language", languageCode)), -10);
    }
}
