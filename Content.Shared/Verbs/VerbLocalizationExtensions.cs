namespace Content.Shared.Verbs;

public static class VerbLocalizationExtensions
{
    public static TVerb WithTextLoc<TVerb>(this TVerb verb, string locId, bool forceLowercase = false)
        where TVerb : Verb
    {
        verb.TextLocId = locId;
        verb.TextForceLowercase = forceLowercase;
        return verb;
    }

    public static TVerb WithMessageLoc<TVerb>(this TVerb verb, string locId)
        where TVerb : Verb
    {
        verb.MessageLocId = locId;
        verb.MessageUsesTextPrefix = false;
        return verb;
    }

    public static TVerb WithPrefixedMessageLoc<TVerb>(this TVerb verb, string locId)
        where TVerb : Verb
    {
        verb.MessageLocId = locId;
        verb.MessageUsesTextPrefix = true;
        return verb;
    }
}
