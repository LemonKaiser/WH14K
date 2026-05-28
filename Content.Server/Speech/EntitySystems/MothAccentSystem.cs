using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Robust.Shared.Random;
using Content.Shared.Speech;

namespace Content.Server.Speech.EntitySystems;

public sealed partial class MothAccentSystem : EntitySystem
{
    [Dependency] private  IRobustRandom _random = default!;

    private static readonly Regex RegexLowerBuzz = new Regex("z{1,3}");
    private static readonly Regex RegexUpperBuzz = new Regex("Z{1,3}");
    private static readonly Regex RegexLowerZh = new(@"\u0436+");
    private static readonly Regex RegexUpperZh = new(@"\u0416+");
    private static readonly Regex RegexLowerZe = new(@"\u0437+");
    private static readonly Regex RegexUpperZe = new(@"\u0417+");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MothAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, MothAccentComponent component, AccentGetEvent args)
    {
        var message = args.Message;

        // buzzz
        message = RegexLowerBuzz.Replace(message, "zzz");
        // buZZZ
        message = RegexUpperBuzz.Replace(message, "ZZZ");

        // \u0436 => \u0436\u0436\u0436
        message = RegexLowerZh.Replace(
            message,
            _random.Pick(new List<string>() { "\u0436\u0436", "\u0436\u0436\u0436" })
        );
        // \u0416 => \u0416\u0416\u0416
        message = RegexUpperZh.Replace(
            message,
            _random.Pick(new List<string>() { "\u0416\u0416", "\u0416\u0416\u0416" })
        );
        // \u0437 => \u0437\u0437\u0437
        message = RegexLowerZe.Replace(
            message,
            _random.Pick(new List<string>() { "\u0437\u0437", "\u0437\u0437\u0437" })
        );
        // \u0417 => \u0417\u0417\u0417
        message = RegexUpperZe.Replace(
            message,
            _random.Pick(new List<string>() { "\u0417\u0417", "\u0417\u0417\u0417" })
        );

        args.Message = message;
    }
}
