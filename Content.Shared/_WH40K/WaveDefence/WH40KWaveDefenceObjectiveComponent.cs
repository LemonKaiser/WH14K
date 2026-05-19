using Content.Shared.FixedPoint;

namespace Content.Shared._WH40K.WaveDefence;

[RegisterComponent]
public sealed partial class WH40KWaveDefenceObjectiveComponent : Component
{
    [DataField("teamId")]
    public string TeamId = "Imperium";

    [DataField("name")]
    public string NameLoc = "wh40k-wave-defence-command-node-name";

    [DataField("maxHealth")]
    public FixedPoint2 MaxHealth = FixedPoint2.New(2500);

    [DataField("warnAtPercent")]
    public float WarnAtPercent = 0.5f;

    [DataField("destructionDelaySeconds")]
    public float DestructionDelaySeconds;

    [DataField("isPrimaryObjective")]
    public bool IsPrimaryObjective = true;

    [ViewVariables]
    public bool LowHealthAnnounced;

    [ViewVariables]
    public bool Destroyed;
}
