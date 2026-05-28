using JetBrains.Annotations;
using Content.Shared.Stray.SpriteOnOffState;
using Content.Shared.Popups;

namespace Content.Server.Stray.SpriteOnOffState;


[UsedImplicitly]
public sealed partial class SpriteOnOffStateSystem : SharedSpriteOnOffStateSystem
{
    [Dependency] private  SharedPopupSystem _popup = default!;
    public override void ShangeIsOn(EntityUid uid, SpriteOnOffStateComponent? comp = null){
        if (!Resolve(uid, ref comp))
            return;

        ChIsOn(uid, !comp.IsOn, comp);

        if(comp.Popup != ""){
            _popup.PopupEntity(comp.Popup, uid);
        }
    }
}
