using Content.Client._WH40K.DiscordAuth;
using Content.Client._WH40K.Roadmap;
using Content.Client.Changelog;
using Content.Client.Localization;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared.CCVar;
using Content.Shared._WH40K.DiscordAuth;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client.Info
{
    public sealed class LinkBanner : BoxContainer, ILocalizedControl
    {
        private const float DiscordAuthIdleButtonMinWidth = 0f;
        private const float DiscordAuthLinkButtonMinWidth = 96f;
        private const float DiscordAuthLinkedButtonMinWidth = 0f;
        private const float DiscordAuthButtonMaxWidth = 144f;
        private const int DiscordAuthButtonMaxTextElements = 20;

        private readonly IConfigurationManager _cfg;
        private readonly IEntityManager _entityManager;
        private readonly IUriOpener _uriOpener;
        private readonly BoxContainer _buttonsRow;
        private readonly Button _discordAuthButton;
        private readonly Button _rulesButton;
        private readonly Button _guidebookButton;
        private WH40KDiscordAuthSystem? _discordAuth;

        private ValueList<(CVarDef<string> cVar, string locKey, Button button)> _infoLinks;

        public LinkBanner()
        {
            _buttonsRow = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal
            };
            AddChild(_buttonsRow);

            _entityManager = IoCManager.Resolve<IEntityManager>();
            _uriOpener = IoCManager.Resolve<IUriOpener>();
            _cfg = IoCManager.Resolve<IConfigurationManager>();

            _rulesButton = new Button();
            _rulesButton.OnPressed += _ => new RulesAndInfoWindow().Open();
            _buttonsRow.AddChild(_rulesButton);

            var guidebookController = UserInterfaceManager.GetUIController<GuidebookUIController>();
            _guidebookButton = new Button();
            _guidebookButton.OnPressed += _ =>
            {
                guidebookController.ToggleGuidebook();
            };
            _buttonsRow.AddChild(_guidebookButton);

            var changelogButton = new ChangelogButton();
            changelogButton.OnPressed += _ => UserInterfaceManager.GetUIController<ChangelogUIController>().ToggleWindow();
            _buttonsRow.AddChild(changelogButton);

            var roadmapButton = new RoadmapButton();
            roadmapButton.OnPressed += _ => UserInterfaceManager.GetUIController<RoadmapUIController>().ToggleRoadmap();
            _buttonsRow.AddChild(roadmapButton);

            AddInfoButton("server-info-discord-button", CCVars.InfoLinksDiscord);
            AddInfoButton("server-info-website-button", CCVars.InfoLinksWebsite);
            AddInfoButton("server-info-wiki-button", CCVars.InfoLinksWiki);
            AddInfoButton("server-info-forum-button", CCVars.InfoLinksForum);
            AddInfoButton("server-info-telegram-button", CCVars.InfoLinksTelegram);

            void AddInfoButton(string loc, CVarDef<string> cVar)
            {
                var button = new Button();
                button.OnPressed += _ => _uriOpener.OpenUri(_cfg.GetCVar(cVar));
                _buttonsRow.AddChild(button);
                _infoLinks.Add((cVar, loc, button));
            }

            _discordAuthButton = new Button();
            _discordAuthButton.MinWidth = DiscordAuthLinkButtonMinWidth;
            _discordAuthButton.MaxWidth = DiscordAuthButtonMaxWidth;
            _discordAuthButton.TextAlign = Label.AlignMode.Center;
            _discordAuthButton.OnPressed += _ => OnDiscordAuthPressed();
            _buttonsRow.AddChild(_discordAuthButton);

            Relocalize();
        }

        protected override void EnteredTree()
        {
            // LinkBanner is constructed before the client even connects to the server due to UI refactor stuff.
            // We need to update these buttons when the UI is shown.

            base.EnteredTree();

            TryActivateDiscordAuth();

            UpdateInfoLinkButtons();
            UpdateDiscordButton();
        }

        protected override void ExitedTree()
        {
            base.ExitedTree();

            DeactivateDiscordAuth();
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            if (_discordAuth == null && TryActivateDiscordAuth())
                UpdateDiscordButton();
        }

        public void Relocalize()
        {
            _rulesButton.Text = Loc.GetString("server-info-rules-button");
            _guidebookButton.Text = Loc.GetString("server-info-guidebook-button");

            foreach (var (_, locKey, button) in _infoLinks)
            {
                button.Text = Loc.GetString(locKey);
            }

            UpdateDiscordButton();
        }

        private void OnDiscordSnapshotUpdated(WH40KDiscordAuthSnapshot snapshot)
        {
            UpdateDiscordButton();
        }

        private void OnDiscordAuthPressed()
        {
            var discordAuthUi = UserInterfaceManager.GetUIController<WH40KDiscordAuthUIController>();

            if (TryActivateDiscordAuth()
                && _discordAuth!.TryGetCachedSnapshot(out var snapshot)
                && snapshot.IsLinked)
            {
                discordAuthUi.OpenWindowForSnapshot(snapshot);
                return;
            }

            if (TryActivateDiscordAuth()
                && _discordAuth!.TryGetCachedSnapshot(out snapshot)
                && !snapshot.Enabled)
            {
                var discordUrl = _cfg.GetCVar(CCVars.InfoLinksDiscord);
                if (!string.IsNullOrWhiteSpace(discordUrl))
                {
                    _uriOpener.OpenUri(discordUrl);
                    return;
                }
            }

            discordAuthUi.OpenWindowOrStartLink();
        }

        private void UpdateDiscordButton()
        {
            if (IsInsideTree && TryActivateDiscordAuth() && _discordAuth!.TryGetCachedSnapshot(out var snapshot))
            {
                string? tooltip = null;
                UpdateDiscordButtonWidth(snapshot.IsLinked, snapshot.Enabled);
                _discordAuthButton.Visible = true;
                UpdateInfoLinkButtons();

                if (snapshot.IsLinked)
                {
                    _discordAuthButton.Text = GetLobbyDisplayName(snapshot, out tooltip);
                    _discordAuthButton.ToolTip = tooltip;
                }
                else
                {
                    _discordAuthButton.Text = Loc.GetString("wh40k-discord-auth-button");
                    _discordAuthButton.ToolTip = snapshot.Enabled
                        ? Loc.GetString("wh40k-discord-auth-button-link")
                        : Loc.GetString("wh40k-discord-auth-popup-disabled");
                }

                return;
            }

            UpdateDiscordButtonWidth(isLinked: false, authEnabled: false);
            _discordAuthButton.Visible = true;
            UpdateInfoLinkButtons();
            _discordAuthButton.Text = Loc.GetString("wh40k-discord-auth-button");
            _discordAuthButton.ToolTip = null;
        }

        private void UpdateDiscordButtonWidth(bool isLinked, bool authEnabled)
        {
            _discordAuthButton.MinWidth = isLinked
                ? DiscordAuthLinkedButtonMinWidth
                : authEnabled
                    ? DiscordAuthLinkButtonMinWidth
                    : DiscordAuthIdleButtonMinWidth;
            _discordAuthButton.MaxWidth = DiscordAuthButtonMaxWidth;
        }

        private void UpdateInfoLinkButtons()
        {
            foreach (var (cVar, _, link) in _infoLinks)
            {
                var visible = _cfg.GetCVar(cVar) != "";
                if (cVar == CCVars.InfoLinksDiscord)
                    visible = false;

                link.Visible = visible;
            }
        }

        private static string GetLobbyDisplayName(WH40KDiscordAuthSnapshot snapshot, out string? tooltip)
        {
            var primary = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.Username : snapshot.DisplayName;
            tooltip = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(primary);

            if (string.IsNullOrWhiteSpace(tooltip))
                tooltip = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(snapshot.Username);

            if (string.IsNullOrWhiteSpace(tooltip))
                tooltip = string.IsNullOrWhiteSpace(snapshot.DiscordUserId) ? "-" : snapshot.DiscordUserId;

            return WH40KDiscordAuthDisplayNameSanitizer.Ellipsize(tooltip, DiscordAuthButtonMaxTextElements);
        }

        private bool TryActivateDiscordAuth()
        {
            if (_discordAuth != null)
                return true;

            if (!_entityManager.TrySystem(out WH40KDiscordAuthSystem? discordAuth))
                return false;

            _discordAuth = discordAuth;
            _discordAuth.SnapshotUpdated += OnDiscordSnapshotUpdated;
            _discordAuth.EnsureSnapshot();
            return true;
        }

        private void DeactivateDiscordAuth()
        {
            if (_discordAuth == null)
                return;

            _discordAuth.SnapshotUpdated -= OnDiscordSnapshotUpdated;
            _discordAuth = null;
        }
    }
}
