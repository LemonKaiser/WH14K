using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Content.Shared.Speech;
using Robust.Shared.Random;

namespace Content.Server.Speech.EntitySystems;

public sealed class FrontalLispSystem : EntitySystem
{
    // @formatter:off
    private static readonly Regex RegexUpperTh = new(@"[T]+[Ss]+|[S]+[Cc]+(?=[IiEeYy]+)|[C]+(?=[IiEeYy]+)|[P][Ss]+|([S]+[Tt]+|[T]+)(?=[Ii]+[Oo]+[Uu]*[Nn]*)|[C]+[Hh]+(?=[Ii]*[Ee]*)|[Z]+|[S]+|[X]+(?=[Ee]+)");
    private static readonly Regex RegexLowerTh = new(@"[t]+[s]+|[s]+[c]+(?=[iey]+)|[c]+(?=[iey]+)|[p][s]+|([s]+[t]+|[t]+)(?=[i]+[o]+[u]*[n]*)|[c]+[h]+(?=[i]*[e]*)|[z]+|[s]+|[x]+(?=[e]+)");
    private static readonly Regex RegexUpperEcks = new(@"[E]+[Xx]+[Cc]*|[X]+");
    private static readonly Regex RegexLowerEcks = new(@"[e]+[x]+[c]*|[x]+");
    private static readonly Regex RegexLowerEs = new(@"\u0441");
    private static readonly Regex RegexUpperEs = new(@"\u0421");
    private static readonly Regex RegexLowerChe = new(@"\u0447");
    private static readonly Regex RegexUpperChe = new(@"\u0427");
    private static readonly Regex RegexLowerTse = new(@"\u0446");
    private static readonly Regex RegexUpperTse = new(@"\u0426");
    private static readonly Regex RegexLowerTeConsonant = new(@"\B[\u0442](?![\u0410\u0415\u0401\u0418\u041E\u0423\u042B\u042D\u042E\u042F\u0430\u0435\u0451\u0438\u043E\u0443\u044B\u044D\u044E\u044F])");
    private static readonly Regex RegexUpperTeConsonant = new(@"\B[\u0422](?![\u0410\u0415\u0401\u0418\u041E\u0423\u042B\u042D\u042E\u042F\u0430\u0435\u0451\u0438\u043E\u0443\u044B\u044D\u044E\u044F])");
    private static readonly Regex RegexLowerZe = new(@"\u0437");
    private static readonly Regex RegexUpperZe = new(@"\u0417");
    // @formatter:on

    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FrontalLispComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, FrontalLispComponent component, AccentGetEvent args)
    {
        var message = args.Message;

        // handles ts, sc(i|e|y), c(i|e|y), ps, st(io(u|n)), ch(i|e), z, s
        message = RegexUpperTh.Replace(message, "TH");
        message = RegexLowerTh.Replace(message, "th");
        // handles ex(c), x
        message = RegexUpperEcks.Replace(message, "EKTH");
        message = RegexLowerEcks.Replace(message, "ekth");

        // \u0441 - \u0448
        message = RegexLowerEs.Replace(message, _random.Prob(0.90f) ? "\u0448" : "\u0441");
        message = RegexUpperEs.Replace(message, _random.Prob(0.90f) ? "\u0428" : "\u0421");
        // \u0447 - \u0448
        message = RegexLowerChe.Replace(message, _random.Prob(0.90f) ? "\u0448" : "\u0447");
        message = RegexUpperChe.Replace(message, _random.Prob(0.90f) ? "\u0428" : "\u0427");
        // \u0446 - \u0447
        message = RegexLowerTse.Replace(message, _random.Prob(0.90f) ? "\u0447" : "\u0446");
        message = RegexUpperTse.Replace(message, _random.Prob(0.90f) ? "\u0427" : "\u0426");
        // \u0442 - \u0447
        message = RegexLowerTeConsonant.Replace(message, _random.Prob(0.90f) ? "\u0447" : "\u0442");
        message = RegexUpperTeConsonant.Replace(message, _random.Prob(0.90f) ? "\u0427" : "\u0422");
        // \u0437 - \u0436
        message = RegexLowerZe.Replace(message, _random.Prob(0.90f) ? "\u0436" : "\u0437");
        message = RegexUpperZe.Replace(message, _random.Prob(0.90f) ? "\u0416" : "\u0417");

        args.Message = message;
    }
}
