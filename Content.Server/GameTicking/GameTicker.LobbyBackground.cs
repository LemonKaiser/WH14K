using Content.Shared.GameTicking.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using System.Linq;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    private const string WH40KPreferredLobbyBackgroundId = "WH40KPlushDevLineup";

    [ViewVariables]
    public ProtoId<LobbyBackgroundPrototype>? LobbyBackground { get; private set; }

    [ViewVariables]
    private List<ProtoId<LobbyBackgroundPrototype>>? _lobbyBackgrounds;

    private static readonly string[] WhitelistedBackgroundExtensions = new[] { "png", "jpg", "jpeg", "webp" };

    private void InitializeLobbyBackground()
    {
        var allprotos = _prototypeManager.EnumeratePrototypes<LobbyBackgroundPrototype>().ToList();
        _lobbyBackgrounds ??= new List<ProtoId<LobbyBackgroundPrototype>>();

        //create protoids from them
        foreach (var proto in allprotos)
        {
            var hasValidStaticBackground = proto.Background is { } staticPath &&
                                           WhitelistedBackgroundExtensions.Contains(staticPath.Extension, StringComparer.OrdinalIgnoreCase);

            var hasValidGifBackground = proto.BackgroundGif is { } gifPath &&
                                        string.Equals(gifPath.Extension, "gif", StringComparison.OrdinalIgnoreCase);

            if (!hasValidStaticBackground && !hasValidGifBackground)
                continue;

            //create a protoid and add it to the list
            _lobbyBackgrounds.Add(new ProtoId<LobbyBackgroundPrototype>(proto.ID));
        }

        RandomizeLobbyBackground();
    }

    private void RandomizeLobbyBackground()
    {
        // Prefer the curated WH40K static lineup when the content pack provides it.
        if (_prototypeManager.TryIndex<LobbyBackgroundPrototype>(WH40KPreferredLobbyBackgroundId, out var preferred)
            && preferred.Background is { } staticPath
            && WhitelistedBackgroundExtensions.Contains(staticPath.Extension, StringComparer.OrdinalIgnoreCase))
        {
            LobbyBackground = new ProtoId<LobbyBackgroundPrototype>(preferred.ID);
            return;
        }

        if (_lobbyBackgrounds != null && _lobbyBackgrounds.Count != 0)
            LobbyBackground = _robustRandom.Pick(_lobbyBackgrounds);
        else
            LobbyBackground = null;
    }
}
