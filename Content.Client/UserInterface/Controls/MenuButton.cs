using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Palette;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Controls;

public sealed class MenuButton : ContainerButton
{
    [Dependency] private readonly IInputManager _inputManager = default!;
    public const string StyleClassLabelTopButton = "topButtonLabel";
    // public const string StyleClassRedTopButton = "topButtonLabel";

    // TODO: KIIIIIILLLLLLLLLLLLLLLLLLLLLLLLLLL --kaylie.
    private static readonly Color ColorNormal = Color.FromHex("#E6E0CF");
    private static readonly Color ColorHovered = Color.FromHex("#F0E7CF");
    private static readonly Color ColorPressed = Color.FromHex("#D7C89B");
    private static readonly Color ColorDisabled = Color.FromHex("#6A6458");
    private static readonly Color ColorAlertNormal = Palettes.Red.Text;
    private static readonly Color ColorAlertHovered = Palettes.Red.Text;
    private static readonly Color ColorAlertPressed = Palettes.Red.TextDark;
    private static readonly Color ColorAlertDisabled = Palettes.Red.TextDark.WithAlpha(0.65f);

    private const float VertPad = 4f;

    private BoundKeyFunction? _function;
    private readonly BoxContainer _root;
    private readonly TextureRect? _buttonIcon;
    private readonly Label? _buttonLabel;

    public string AppendStyleClass { set => AddStyleClass(value); }
    public Texture? Icon { get => _buttonIcon!.Texture; set => _buttonIcon!.Texture = value; }

    public BoundKeyFunction? BoundKey
    {
        get => _function;
        set
        {
            _function = value;
            _buttonLabel!.Text = _function == null ? "" : BoundKeyHelper.ShortKeyName(_function.Value);
        }
    }

    public BoxContainer ButtonRoot => _root;

    public MenuButton()
    {
        IoCManager.InjectDependencies(this);
        _buttonIcon = new TextureRect()
        {
            TextureScale = new Vector2(0.5f, 0.5f),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            VerticalExpand = true,
            Margin = new Thickness(0, VertPad),
            ModulateSelfOverride = ColorNormal,
            Stretch = TextureRect.StretchMode.KeepCentered
        };
        _buttonLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HAlignment.Center,
            ModulateSelfOverride = ColorNormal,
            StyleClasses = {StyleClassLabelTopButton}
        };
        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children =
            {
                _buttonIcon,
                _buttonLabel
            }
        };
        AddChild(_root);
        ToggleMode = true;
    }

    protected override void EnteredTree()
    {
        _inputManager.OnKeyBindingAdded += OnKeyBindingChanged;
        _inputManager.OnKeyBindingRemoved += OnKeyBindingChanged;
        _inputManager.OnInputModeChanged += OnKeyBindingChanged;
    }

    protected override void ExitedTree()
    {
        _inputManager.OnKeyBindingAdded -= OnKeyBindingChanged;
        _inputManager.OnKeyBindingRemoved -= OnKeyBindingChanged;
        _inputManager.OnInputModeChanged -= OnKeyBindingChanged;
    }


    private void OnKeyBindingChanged(IKeyBinding obj)
    {
        _buttonLabel!.Text = _function == null ? "" : BoundKeyHelper.ShortKeyName(_function.Value);
    }

    private void OnKeyBindingChanged()
    {
        _buttonLabel!.Text = _function == null ? "" : BoundKeyHelper.ShortKeyName(_function.Value);
    }

    protected override void StylePropertiesChanged()
    {
        // colors of children depend on style, so ensure we update when style is changed
        base.StylePropertiesChanged();
        UpdateChildColors();
    }

    private void UpdateChildColors()
    {
        if (_buttonIcon == null || _buttonLabel == null) return;
        var useAlertPalette = HasStyleClass(StyleClass.Negative);

        switch (DrawMode)
        {
            case DrawModeEnum.Normal:
                _buttonIcon.ModulateSelfOverride = useAlertPalette ? ColorAlertNormal : ColorNormal;
                _buttonLabel.ModulateSelfOverride = useAlertPalette ? ColorAlertNormal : ColorNormal;
                break;

            case DrawModeEnum.Pressed:
                _buttonIcon.ModulateSelfOverride = useAlertPalette ? ColorAlertPressed : ColorPressed;
                _buttonLabel.ModulateSelfOverride = useAlertPalette ? ColorAlertPressed : ColorPressed;
                break;

            case DrawModeEnum.Hover:
                _buttonIcon.ModulateSelfOverride = useAlertPalette ? ColorAlertHovered : ColorHovered;
                _buttonLabel.ModulateSelfOverride = useAlertPalette ? ColorAlertHovered : ColorHovered;
                break;

            case DrawModeEnum.Disabled:
                _buttonIcon.ModulateSelfOverride = useAlertPalette ? ColorAlertDisabled : ColorDisabled;
                _buttonLabel.ModulateSelfOverride = useAlertPalette ? ColorAlertDisabled : ColorDisabled;
                break;
        }
    }


    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        UpdateChildColors();
    }
}
