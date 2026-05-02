using Content.Server.Speech.Components;
using Content.Shared.Speech.EntitySystems;
using Robust.Shared.Random;

namespace Content.Server.Speech.EntitySystems;

public sealed class BarkAccentSystem : RelayAccentSystem<BarkAccentComponent>
{
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly IReadOnlyList<string> Barks = new List<string>
    {
        " Гав!",
        " ГАВ",
        " вуф-вуф"
    }.AsReadOnly();

    private static readonly IReadOnlyDictionary<string, string> SpecialWords = new Dictionary<string, string>
    {
        { "ah", "arf" },
        { "Ah", "Arf" },
        { "oh", "oof" },
        { "Oh", "Oof" },
        { "га", "гаф" },
        { "Га", "Гаф" },
        { "угу", "вуф" },
        { "Угу", "Вуф" },
    };

    protected override string AccentuateInternal(EntityUid uid, BarkAccentComponent comp, string message)
    {
        foreach (var (word, repl) in SpecialWords)
        {
            message = message.Replace(word, repl);
        }

        return message.Replace("!", _random.Pick(Barks))
            .Replace("l", "r").Replace("L", "R")
            .Replace("л", "р").Replace("Л", "Р");
    }
}
