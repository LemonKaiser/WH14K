using System;
using Robust.Shared.Serialization;

namespace Content.Shared.Speech;

[Serializable, NetSerializable]
public enum VoiceTone : byte
{
    Low,
    Normal,
    High,
}

public static class VoiceToneHelpers
{
    public static float GetPitchScale(this VoiceTone tone)
    {
        return tone switch
        {
            VoiceTone.Low => 0.88f,
            VoiceTone.High => 1.12f,
            _ => 1f,
        };
    }

    public static float GetVariationScale(this VoiceTone tone)
    {
        return tone switch
        {
            VoiceTone.Low => 0.92f,
            VoiceTone.High => 1.08f,
            _ => 1f,
        };
    }

    public static float GetCadenceScale(this VoiceTone tone)
    {
        return tone switch
        {
            VoiceTone.Low => 1.08f,
            VoiceTone.High => 0.94f,
            _ => 1f,
        };
    }

    public static int GetCharactersPerBlipDelta(this VoiceTone tone)
    {
        return tone switch
        {
            VoiceTone.Low => 1,
            VoiceTone.High => -1,
            _ => 0,
        };
    }

    public static float GetVolumeOffsetDb(this VoiceTone tone)
    {
        return tone switch
        {
            VoiceTone.Low => 0.25f,
            VoiceTone.High => -0.1f,
            _ => 0f,
        };
    }
}
