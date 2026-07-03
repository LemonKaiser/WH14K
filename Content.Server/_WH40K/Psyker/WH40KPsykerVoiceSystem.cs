using Content.Shared._WH40K.Psyker;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Plays WH40K psyker voice lines for level-ups and dangerously unstable casts.
/// </summary>
public sealed partial class WH40KPsykerVoiceSystem : EntitySystem
{
    private const float OverheatInstabilityThreshold = 0.70f;

    private static readonly SoundSpecifier PsykerLevelUpVoice =
        new SoundCollectionSpecifier("WH40KPsykerLevelUpVoice", AudioParams.Default.WithVolume(-5f));

    private static readonly SoundSpecifier PsykerOverheatCastVoice =
        new SoundCollectionSpecifier("WH40KPsykerOverheatCastVoice", AudioParams.Default.WithVolume(-6f));

    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerProgressionComponent, WH40KPsykerLevelChangedEvent>(OnPsykerLevelChanged);
        SubscribeLocalEvent<WH40KWarpInstabilityComponent, WH40KWarpActionCastEvent>(OnWarpActionCast);
    }

    private void OnPsykerLevelChanged(Entity<WH40KPsykerProgressionComponent> ent, ref WH40KPsykerLevelChangedEvent args)
    {
        if (!HasComp<WH40KPsykerRoleComponent>(ent.Owner))
            return;

        if (HasComp<WH40KChaosGiftRoleComponent>(ent.Owner))
            return;

        if (args.CurrentLevel <= args.PreviousLevel)
            return;

        _audio.PlayPvs(PsykerLevelUpVoice, ent.Owner);
    }

    private void OnWarpActionCast(Entity<WH40KWarpInstabilityComponent> ent, ref WH40KWarpActionCastEvent args)
    {
        if (!HasComp<WH40KPsykerRoleComponent>(ent.Owner))
            return;

        if (HasComp<WH40KChaosGiftRoleComponent>(ent.Owner))
            return;

        if (ent.Comp.MaxInstability <= 0f)
        {
            return;
        }

        var instabilityRatio = ent.Comp.CurrentInstability / ent.Comp.MaxInstability;
        if (instabilityRatio < OverheatInstabilityThreshold)
            return;

        _audio.PlayPvs(PsykerOverheatCastVoice, ent.Owner);
    }
}
