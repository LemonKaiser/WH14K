using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Log;

namespace Content.Server._WH40K;

public sealed partial class WH40KTipsConfigSystem : EntitySystem
{
    private const string Wh40kTipsDataset = "WH40KTips";

    [Dependency] private IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        var sawmill = Logger.GetSawmill("wh40k.tips");
        var changed = false;

        if (_cfg.GetCVar(CCVars.TipsDataset) == "Tips")
        {
            _cfg.SetCVar(CCVars.TipsDataset, Wh40kTipsDataset);
            changed = true;
        }

        if (_cfg.GetCVar(CCVars.LoginTipsDataset) == "Tips")
        {
            _cfg.SetCVar(CCVars.LoginTipsDataset, Wh40kTipsDataset);
            changed = true;
        }

        if (changed)
            sawmill.Info($"Tips dataset defaulted to {Wh40kTipsDataset} (was vanilla \"Tips\").");
        else
            sawmill.Info($"Tips dataset already overridden by config; leaving as \"{_cfg.GetCVar(CCVars.TipsDataset)}\".");
    }
}

