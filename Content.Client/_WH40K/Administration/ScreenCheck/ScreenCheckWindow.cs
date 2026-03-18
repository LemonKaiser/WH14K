using System.IO;
using System.Numerics;
using Content.Shared._WH40K.Administration.ScreenCheck;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WH40K.Administration.ScreenCheck;

public sealed class ScreenCheckWindow : DefaultWindow
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly ILogManager _logs = default!;

    private readonly Label _statusLabel;
    private readonly TextureRect _imageRect;
    private ISawmill _sawmill = default!;
    private OwnedTexture? _texture;

    public ScreenCheckWindow()
    {
        IoCManager.InjectDependencies(this);

        _sawmill = _logs.GetSawmill("screencheck");

        MinSize = new Vector2(760, 520);
        SetSize = new Vector2(1024, 768);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            HorizontalExpand = true
        };
        Contents.AddChild(root);

        _statusLabel = new Label
        {
            HorizontalExpand = true
        };
        root.AddChild(_statusLabel);

        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        root.AddChild(panel);

        _imageRect = new TextureRect
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MinSize = new Vector2(640, 360),
            Visible = false
        };
        panel.AddChild(_imageRect);
    }

    public void UpdateState(ScreenCheckEuiState state)
    {
        Title = Loc.GetString("screen-check-window-title", ("player", state.TargetName));

        if (state.Status == ScreenCheckUiStatus.Success && TryLoadTexture(state.ImageData))
        {
            _statusLabel.Text = Loc.GetString("screen-check-status-success");
            _imageRect.Visible = true;
            return;
        }

        ClearTexture();
        _imageRect.Visible = false;
        _statusLabel.Text = state.Status switch
        {
            ScreenCheckUiStatus.Pending => Loc.GetString("screen-check-status-pending"),
            ScreenCheckUiStatus.TimedOut => Loc.GetString("screen-check-status-timeout"),
            ScreenCheckUiStatus.TargetDisconnected => Loc.GetString("screen-check-status-disconnected"),
            ScreenCheckUiStatus.CaptureFailed => Loc.GetString("screen-check-status-capture-failed"),
            ScreenCheckUiStatus.InvalidData => Loc.GetString("screen-check-status-invalid-data"),
            ScreenCheckUiStatus.Cancelled => Loc.GetString("screen-check-status-cancelled"),
            _ => Loc.GetString("screen-check-status-invalid-data")
        };
    }

    public void Cleanup()
    {
        ClearTexture();
    }

    private bool TryLoadTexture(byte[] imageData)
    {
        if (!ScreenCheckImageValidator.IsValidEncodedJpeg(imageData))
            return false;

        try
        {
            using var stream = new MemoryStream(imageData, writable: false);
            using var image = Image.Load<Rgba32>(stream);

            ClearTexture();
            _texture = _clyde.LoadTextureFromImage(image, "screencheck");
            _imageRect.Texture = _texture;
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Warning("Failed to decode screencheck image: {0}", e);
            return false;
        }
    }

    private void ClearTexture()
    {
        _imageRect.Texture = null;
        _texture?.Dispose();
        _texture = null;
    }
}
