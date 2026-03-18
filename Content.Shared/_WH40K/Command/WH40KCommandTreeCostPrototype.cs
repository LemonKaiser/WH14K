using System.Collections.Generic;
using Content.Shared._WH40K.GameMode;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kCommandTreeCostProfile")]
public sealed partial class WH40KCommandTreeCostProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("reserveBasePoints")]
    public int ReserveBasePoints = 24;

    [DataField("reservePerBaseLevel")]
    public int ReservePerBaseLevel = 12;

    [DataField("reserveOverflowStepPoints")]
    public int ReserveOverflowStepPoints = 15;

    [DataField("reserveSurchargePerStep")]
    public int ReserveSurchargePerStep = 3;

    [DataField("reserveSurchargeCapPreparation")]
    public int ReserveSurchargeCapPreparation = 18;

    [DataField("reserveSurchargeCapAssault")]
    public int ReserveSurchargeCapAssault = 15;

    [DataField("reserveSurchargeCapApocalypse")]
    public int ReserveSurchargeCapApocalypse = 9;

    [DataField("catchupTargetLevelPreparation")]
    public int CatchupTargetLevelPreparation = 1;

    [DataField("catchupTargetLevelAssault")]
    public int CatchupTargetLevelAssault = 3;

    [DataField("catchupTargetLevelApocalypse")]
    public int CatchupTargetLevelApocalypse = 5;

    [DataField("catchupDiscountPerMissingLevel")]
    public int CatchupDiscountPerMissingLevel = 2;

    [DataField("catchupDiscountCapPreparation")]
    public int CatchupDiscountCapPreparation = 0;

    [DataField("catchupDiscountCapAssault")]
    public int CatchupDiscountCapAssault = 4;

    [DataField("catchupDiscountCapApocalypse")]
    public int CatchupDiscountCapApocalypse = 10;
}

[Prototype("wh40kCommandTreeCostTeamMap")]
public sealed partial class WH40KCommandTreeCostTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandTreeCostProfilePrototype> DefaultProfile = "WH40KCommandTreeCostProfileDefault";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandTreeCostProfilePrototype>> TeamProfiles = new();
}
