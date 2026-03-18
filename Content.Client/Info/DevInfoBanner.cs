using Content.Client.Credits;
using Content.Client.Localization;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Client.Info
{
    public sealed class DevInfoBanner : BoxContainer, ILocalizedControl
    {
        private Button? _reportButton;
        private Button? _creditsButton;

        public DevInfoBanner()
        {
            var buttons = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal
            };
            AddChild(buttons);

            var uriOpener = IoCManager.Resolve<IUriOpener>();
            var cfg = IoCManager.Resolve<IConfigurationManager>();

            var bugReport = cfg.GetCVar(CCVars.InfoLinksBugReport);
            if (bugReport != "")
            {
                _reportButton = new Button();
                _reportButton.OnPressed += args => uriOpener.OpenUri(bugReport);
                buttons.AddChild(_reportButton);
            }

            _creditsButton = new Button();
            _creditsButton.OnPressed += args => new CreditsWindow().Open();
            buttons.AddChild(_creditsButton);
            Relocalize();
        }

        public void Relocalize()
        {
            if (_reportButton != null)
                _reportButton.Text = Loc.GetString("server-info-report-button");

            if (_creditsButton != null)
                _creditsButton.Text = Loc.GetString("server-info-credits-button");
        }
    }
}
