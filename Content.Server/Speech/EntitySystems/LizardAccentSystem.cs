using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Robust.Shared.Random;
using Content.Shared.Speech;

namespace Content.Server.Speech.EntitySystems;

public sealed class LizardAccentSystem : EntitySystem
{
    private static readonly Regex RegexLowerS = new("s+");
    private static readonly Regex RegexUpperS = new("S+");
    private static readonly Regex RegexInternalX = new(@"(\w)x");
    private static readonly Regex RegexLowerEndX = new(@"\bx([\-|r|R]|\b)");
    private static readonly Regex RegexUpperEndX = new(@"\bX([\-|r|R]|\b)");
    private static readonly Regex RegexLowerEs = new(@"\u0441+");
    private static readonly Regex RegexUpperEs = new(@"\u0421+");
    private static readonly Regex RegexLowerZe = new(@"\u0437+");
    private static readonly Regex RegexUpperZe = new(@"\u0417+");
    private static readonly Regex RegexLowerSha = new(@"\u0448+");
    private static readonly Regex RegexUpperSha = new(@"\u0428+");
    private static readonly Regex RegexLowerChe = new(@"\u0447+");
    private static readonly Regex RegexUpperChe = new(@"\u0427+");

    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LizardAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, LizardAccentComponent component, AccentGetEvent args)
    {
        var message = args.Message;

        // hissss
        message = RegexLowerS.Replace(message, "sss");
        // hiSSS
        message = RegexUpperS.Replace(message, "SSS");
        // ekssit
        message = RegexInternalX.Replace(message, "$1kss");
        // ecks
        message = RegexLowerEndX.Replace(message, "ecks$1");
        // eckS
        message = RegexUpperEndX.Replace(message, "ECKS$1");

        // \u0441 => \u0441\u0441\u0441
        message = RegexLowerEs.Replace(
            message,
            _random.Pick(new List<string>() { "\u0441\u0441", "\u0441\u0441\u0441" })
        );
        // \u0421 => \u0421\u0421\u0421
        message = RegexUpperEs.Replace(
            message,
            _random.Pick(new List<string>() { "\u0421\u0421", "\u0421\u0421\u0421" })
        );
        // \u0437 => \u0441\u0441\u0441
        message = RegexLowerZe.Replace(
            message,
            _random.Pick(new List<string>() { "\u0441\u0441", "\u0441\u0441\u0441" })
        );
        // \u0417 => \u0421\u0421\u0421
        message = RegexUpperZe.Replace(
            message,
            _random.Pick(new List<string>() { "\u0421\u0421", "\u0421\u0421\u0421" })
        );
        // \u0448 => \u0448\u0448\u0448
        message = RegexLowerSha.Replace(
            message,
            _random.Pick(new List<string>() { "\u0448\u0448", "\u0448\u0448\u0448" })
        );
        // \u0428 => \u0428\u0428\u0428
        message = RegexUpperSha.Replace(
            message,
            _random.Pick(new List<string>() { "\u0428\u0428", "\u0428\u0428\u0428" })
        );
        // \u0447 => \u0449\u0449\u0449
        message = RegexLowerChe.Replace(
            message,
            _random.Pick(new List<string>() { "\u0449\u0449", "\u0449\u0449\u0449" })
        );
        // \u0427 => \u0429\u0429\u0429
        message = RegexUpperChe.Replace(
            message,
            _random.Pick(new List<string>() { "\u0429\u0429", "\u0429\u0429\u0429" })
        );
        args.Message = message;
    }
}
