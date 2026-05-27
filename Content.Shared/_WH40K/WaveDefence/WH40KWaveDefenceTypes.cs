namespace Content.Shared._WH40K.WaveDefence;

public enum WH40KWaveDefencePhase : byte
{
    Preparation = 0,
    WaveActive = 1,
    Intermission = 2,
    Victory = 3,
    Defeat = 4,
}

public enum WH40KWaveDefenceMode : byte
{
    Fixed = 0,
    Endless = 1,
}

public enum WH40KWaveSpawnPointType : byte
{
    Attacker = 0,
    DefenderStart = 1,
    DefenderReinforcement = 2,
}

public enum WH40KWaveSquadRole : byte
{
    Soldier = 0,
    Support = 1,
    Leader = 2,
    Breacher = 3,
    Reserve = 4,
}

public enum WH40KWaveCompletionPolicy : byte
{
    EliminateAttackers = 0,
}

public enum WH40KWaveAiProfile : byte
{
    SimpleSwarm = 0,
    AdvancedHumanoidConcept = 1,
}
