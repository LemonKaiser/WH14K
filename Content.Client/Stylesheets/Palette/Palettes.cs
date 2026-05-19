namespace Content.Client.Stylesheets.Palette;

/// <summary>
///     Stores all style palettes in one accessible location
/// </summary>
/// <remarks>
///     Technically not limited to only colors, can store like, standard padding amounts, and font sizes, maybe?
/// </remarks>
public static class Palettes
{
    // dark utility tones
    public static readonly ColorPalette Navy = ColorPalette.FromHexBase(
        "#3b3226",
        lightnessShift: 0.05f,
        chromaShift: 0.004f,
        element: Color.FromHex("#4a4030"),
        background: Color.FromHex("#101218"),
        text: Color.FromHex("#e6dec7"));
    public static readonly ColorPalette Cyan = ColorPalette.FromHexBase(
        "#302b23",
        lightnessShift: 0.05f,
        chromaShift: 0.0035f,
        element: Color.FromHex("#3d372d"),
        background: Color.FromHex("#0e1015"),
        text: Color.FromHex("#e6dec7"));
    public static readonly ColorPalette Slate = ColorPalette.FromHexBase(
        "#2d2f36",
        lightnessShift: 0.05f,
        chromaShift: 0.0025f,
        element: Color.FromHex("#373a43"),
        background: Color.FromHex("#141721"),
        text: Color.FromHex("#cdb991"));
    public static readonly ColorPalette Neutral = ColorPalette.FromHexBase(
        "#31343b",
        lightnessShift: 0.05f,
        chromaShift: 0.002f,
        element: Color.FromHex("#3b3e46"),
        background: Color.FromHex("#101218"),
        text: Color.FromHex("#cdb991"));
    public static readonly ColorPalette Button = ColorPalette.FromHexBase(
        "#f2ead4",
        lightnessShift: 0.04f,
        chromaShift: 0.0f,
        element: Color.FromHex("#e1d3b1"),
        background: Color.FromHex("#101218"),
        text: Color.FromHex("#e6dec7")) with
    {
        HoveredElement = Color.FromHex("#fff6de"),
        PressedElement = Color.FromHex("#b8ae95"),
        DisabledElement = Color.FromHex("#686252"),
        Text = Color.FromHex("#d7b65a"),
        TextDark = Color.FromHex("#8d7440"),
    };

    // status tones
    public static readonly ColorPalette Red = ColorPalette.FromHexBase(
        "#b13c34",
        lightnessShift: 0.05f,
        chromaShift: 0.014f,
        element: Color.FromHex("#7b211d"),
        background: Color.FromHex("#14090a"),
        text: Color.FromHex("#d45147")) with
    {
        TextDark = Color.FromHex("#962f2a"),
        HoveredElement = Color.FromHex("#93302b"),
        PressedElement = Color.FromHex("#651916"),
        DisabledElement = Color.FromHex("#3d2322"),
    };
    public static readonly ColorPalette Amber = ColorPalette.FromHexBase(
        "#c9a94c",
        lightnessShift: 0.05f,
        chromaShift: 0.01f,
        element: Color.FromHex("#8d7440"),
        background: Color.FromHex("#18140a"),
        text: Color.FromHex("#d7b65a"));
    public static readonly ColorPalette Green = ColorPalette.FromHexBase(
        "#6b8a56",
        lightnessShift: 0.05f,
        chromaShift: 0.006f,
        element: Color.FromHex("#516944"),
        background: Color.FromHex("#11160f"),
        text: Color.FromHex("#aac48f"));
    public static readonly StatusPalette Status = new([Red.Base, Amber.Base, Green.Base]);

    // highlight tones
    public static readonly ColorPalette Gold = ColorPalette.FromHexBase(
        "#c9a94c",
        lightnessShift: 0.05f,
        chromaShift: 0.01f,
        element: Color.FromHex("#8d7440"),
        background: Color.FromHex("#18140a"),
        text: Color.FromHex("#d7b65a"));
    public static readonly ColorPalette Maroon = ColorPalette.FromHexBase(
        "#6a5530",
        lightnessShift: 0.05f,
        chromaShift: 0.006f,
        element: Color.FromHex("#554325"),
        background: Color.FromHex("#140f08"),
        text: Color.FromHex("#cdb991"));

    // Intended to be used with `ModulateSelf` to darken / lighten something
    public static readonly ColorPalette AlphaModulate = ColorPalette.FromHexBase("#ffffff");

}
