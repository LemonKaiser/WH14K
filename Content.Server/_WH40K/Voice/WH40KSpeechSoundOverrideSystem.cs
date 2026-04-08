using Content.Shared.Clothing;
using Content.Shared.Speech;
using Content.Shared._WH40K.Voice;

namespace Content.Server._WH40K.Voice;

public sealed class WH40KSpeechSoundOverrideSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KSpeechSoundOverrideComponent, ClothingGotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<WH40KSpeechSoundOverrideComponent, ClothingGotUnequippedEvent>(OnGotUnequipped);
    }

    private void OnGotEquipped(Entity<WH40KSpeechSoundOverrideComponent> ent, ref ClothingGotEquippedEvent args)
    {
        if (!TryComp<SpeechComponent>(args.Wearer, out var speech))
            return;

        var state = EnsureComp<WH40KSpeechSoundOverrideStateComponent>(args.Wearer);

        if (state.Source == null)
            state.OriginalSpeechSounds = speech.SpeechSounds;

        state.Source = ent.Owner;
        speech.SpeechSounds = ent.Comp.SpeechSounds;
        Dirty(args.Wearer, speech);
    }

    private void OnGotUnequipped(Entity<WH40KSpeechSoundOverrideComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        if (!TryComp<SpeechComponent>(args.Wearer, out var speech) ||
            !TryComp<WH40KSpeechSoundOverrideStateComponent>(args.Wearer, out var state) ||
            state.Source != ent.Owner)
        {
            return;
        }

        speech.SpeechSounds = state.OriginalSpeechSounds;
        Dirty(args.Wearer, speech);
        RemComp<WH40KSpeechSoundOverrideStateComponent>(args.Wearer);
    }
}
