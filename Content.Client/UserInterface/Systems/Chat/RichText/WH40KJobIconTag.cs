using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.StatusIcon;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

[UsedImplicitly]
public sealed partial class WH40KJobIconTag : IMarkupTagHandler
{
    [Dependency] private  IPrototypeManager _prototypes = default!;
    [Dependency] private  IEntityManager _entMan = default!;

    private const float IconScale = 3.0f;
    private const float IconRightMargin = 6f;

    public string Name => "wh40kjobicon";

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!node.Attributes.TryGetValue("icon", out var iconParam) ||
            !iconParam.TryGetString(out var iconId) ||
            !_prototypes.TryIndex<JobIconPrototype>(iconId, out var jobIcon))
        {
            return false;
        }

        var sprite = _entMan.System<SpriteSystem>();

        control = new TextureRect
        {
            Texture = sprite.Frame0(jobIcon.Icon),
            TextureScale = new Vector2(IconScale, IconScale),
            Stretch = TextureRect.StretchMode.KeepCentered,
            VerticalAlignment = Control.VAlignment.Center,
            Margin = new Thickness(0f, 0f, IconRightMargin, 0f)
        };
        return true;
    }
}
