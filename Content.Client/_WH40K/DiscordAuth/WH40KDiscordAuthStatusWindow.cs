using System;
using System.Numerics;
using Content.Client.Localization;
using Content.Client.Stylesheets;
using Content.Shared._WH40K.DiscordAuth;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.DiscordAuth;

public sealed class WH40KDiscordAuthStatusWindow : DefaultWindow, ILocalizedControl
{
    private readonly IGameTiming _timing;
    private readonly Label _nameLabel;
    private readonly Label _idLabel;
    private readonly Label _guildLabel;
    private readonly Label _roleLabel;
    private WH40KDiscordAuthSnapshot? _snapshot;
    private TimeSpan _refreshCooldownEndsAt;
    private bool _refreshPending;
    private bool _refreshShouldWarn;

    public Button RefreshButton { get; }
    public Button UnlinkButton { get; }

    public WH40KDiscordAuthStatusWindow()
    {
        _timing = IoCManager.Resolve<IGameTiming>();
        MinSize = SetSize = new Vector2(520f, 280f);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _nameLabel = new Label();
        _nameLabel.HorizontalExpand = true;
        _nameLabel.ClipText = true;
        _idLabel = new Label();
        _guildLabel = new Label();
        _roleLabel = new Label();

        RefreshButton = new Button
        {
            Text = Loc.GetString("wh40k-discord-auth-window-refresh"),
            HorizontalExpand = true,
            ClipText = true,
            TextAlign = Label.AlignMode.Center,
        };
        RefreshButton.MinWidth = 472f;

        UnlinkButton = new Button
        {
            Text = Loc.GetString("wh40k-discord-auth-window-unlink"),
            HorizontalExpand = true,
            ClipText = true,
            TextAlign = Label.AlignMode.Center,
        };
        UnlinkButton.MinWidth = 472f;
        UnlinkButton.StyleClasses.Add(StyleNano.ButtonCaution);
        UnlinkButton.Label.FontColorOverride = Color.White;

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
        };

        buttons.AddChild(RefreshButton);
        buttons.AddChild(UnlinkButton);

        root.AddChild(_nameLabel);
        root.AddChild(_idLabel);
        root.AddChild(_guildLabel);
        root.AddChild(_roleLabel);
        root.AddChild(new Control { VerticalExpand = true });
        root.AddChild(buttons);

        ContentsContainer.AddChild(root);
        Relocalize();
    }

    public void ApplySnapshot(WH40KDiscordAuthSnapshot snapshot)
    {
        _snapshot = snapshot;
        _refreshPending = false;
        _refreshShouldWarn = snapshot.CacheStale && snapshot.GuildCheckConfigured;
        _refreshCooldownEndsAt = snapshot.RefreshCooldownRemaining > TimeSpan.Zero
            ? _timing.CurTime + snapshot.RefreshCooldownRemaining
            : TimeSpan.Zero;

        RefreshSnapshotLabels();
        UpdateRefreshButtonState();
    }

    public void Relocalize()
    {
        Title = Loc.GetString("wh40k-discord-auth-window-title");
        UnlinkButton.Text = Loc.GetString("wh40k-discord-auth-window-unlink");

        if (_snapshot != null)
            RefreshSnapshotLabels();
        else
            RefreshButton.Text = Loc.GetString("wh40k-discord-auth-window-refresh");
    }

    private void RefreshSnapshotLabels()
    {
        if (_snapshot == null)
            return;

        var snapshot = _snapshot;

        var primaryDisplayName = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.Username : snapshot.DisplayName;
        var fullDisplayName = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(primaryDisplayName);
        if (string.IsNullOrWhiteSpace(fullDisplayName))
            fullDisplayName = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(snapshot.Username);
        if (string.IsNullOrWhiteSpace(fullDisplayName))
            fullDisplayName = string.IsNullOrWhiteSpace(snapshot.DiscordUserId) ? "-" : snapshot.DiscordUserId;

        _nameLabel.Text = Loc.GetString(
            "wh40k-discord-auth-window-name",
            ("name", WH40KDiscordAuthDisplayNameSanitizer.Ellipsize(fullDisplayName, 44)));
        _nameLabel.ToolTip = fullDisplayName;
        _idLabel.Text = Loc.GetString(
            "wh40k-discord-auth-window-id",
            ("id", string.IsNullOrWhiteSpace(snapshot.DiscordUserId) ? "-" : snapshot.DiscordUserId));

        var guildStatus = snapshot.GuildCheckConfigured
            ? snapshot.GuildMemberKnown
                ? Loc.GetString(snapshot.IsGuildMember
                    ? "wh40k-discord-auth-window-status-yes"
                    : "wh40k-discord-auth-window-status-no")
                : Loc.GetString("wh40k-discord-auth-window-status-unknown")
            : Loc.GetString("wh40k-discord-auth-window-status-not-configured");

        if (_refreshShouldWarn)
        {
            guildStatus = Loc.GetString("wh40k-discord-auth-window-status-stale", ("status", guildStatus));
        }

        _guildLabel.Text = Loc.GetString("wh40k-discord-auth-window-guild", ("status", guildStatus));

        _roleLabel.Visible = snapshot.RoleGateConfigured;
        if (snapshot.RoleGateConfigured)
        {
            var roleStatus = Loc.GetString(snapshot.RoleGatePassed
                ? "wh40k-discord-auth-window-status-passed"
                : "wh40k-discord-auth-window-status-failed");

            if (_refreshShouldWarn)
                roleStatus = Loc.GetString("wh40k-discord-auth-window-status-stale", ("status", roleStatus));

            _roleLabel.Text = Loc.GetString(
                "wh40k-discord-auth-window-role-gate",
                ("status", roleStatus));
        }

        UpdateRefreshButtonState();
    }

    public void MarkRefreshPending()
    {
        _refreshPending = true;
        UpdateRefreshButtonState();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateRefreshButtonState();
    }

    private void UpdateRefreshButtonState()
    {
        var cooldownRemaining = _refreshCooldownEndsAt > _timing.CurTime
            ? _refreshCooldownEndsAt - _timing.CurTime
            : TimeSpan.Zero;

        if (_refreshShouldWarn)
        {
            if (!RefreshButton.StyleClasses.Contains(StyleNano.ButtonCaution))
                RefreshButton.StyleClasses.Add(StyleNano.ButtonCaution);
        }
        else
        {
            RefreshButton.StyleClasses.Remove(StyleNano.ButtonCaution);
        }

        if (cooldownRemaining > TimeSpan.Zero)
        {
            var seconds = Math.Max(1, (int) Math.Ceiling(cooldownRemaining.TotalSeconds));
            RefreshButton.Disabled = true;
            RefreshButton.Text = Loc.GetString("wh40k-discord-auth-window-refresh-cooldown", ("seconds", seconds));
            return;
        }

        if (_refreshPending)
        {
            RefreshButton.Disabled = true;
            RefreshButton.Text = Loc.GetString("wh40k-discord-auth-window-refresh-pending");
            return;
        }

        RefreshButton.Disabled = false;
        RefreshButton.Text = Loc.GetString("wh40k-discord-auth-window-refresh");
    }
}
