using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Overlays;

/// <summary>
/// Grants the wearer a local night-vision post-process overlay.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KNightVisionComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Strength = 0.82f;

    [DataField, AutoNetworkedField]
    public float BrightnessBoost = 2.45f;

    [DataField, AutoNetworkedField]
    public float Contrast = 1.18f;

    [DataField, AutoNetworkedField]
    public float Vignette = 0.32f;

    [DataField, AutoNetworkedField]
    public float Scanline = 0.14f;

    [DataField, AutoNetworkedField]
    public float Noise = 0.018f;

    /// <summary>
    /// Local minimum brightness pushed into the lighting buffer while the optics are enabled.
    /// This keeps night vision from becoming a normal flashlight or global fullbright.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float LightFloor = 0.34f;
}
