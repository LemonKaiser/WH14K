using System;
using System.Numerics;
using Robust.Shared.Random;

namespace Content.Shared._WH40K.Combat;

public static class WH40KDirectionalBarricadeHelpers
{
    public static bool ShouldPassFromOrigin(
        Vector2 passDirection,
        Vector2 shotDirection,
        Vector2 originDirection,
        float passSideMaxDistance,
        float blockedSidePassChance,
        float blockedSidePointBlankPassDistance,
        IRobustRandom random)
    {
        const float sideDotThreshold = 0.05f;
        const float shotDotThreshold = 0.2f;
        if (passDirection.LengthSquared() <= 0.0001f || shotDirection.LengthSquared() <= 0.0001f)
            return false;

        var passDir = passDirection.Normalized();
        var shotDir = shotDirection.Normalized();
        var originLenSq = originDirection.LengthSquared();

        var dotShot = Vector2.Dot(passDir, shotDir);
        if (MathF.Abs(dotShot) <= shotDotThreshold)
            return false;

        var originDot = originLenSq <= 0.0001f ? 0f : Vector2.Dot(passDir, originDirection.Normalized());
        if (originLenSq > 0.0001f && MathF.Abs(originDot) <= sideDotThreshold)
            return false;

        var fromPassSide = originLenSq <= 0.0001f
            ? dotShot < 0f
            : originDot > 0f;

        if (fromPassSide)
        {
            if (dotShot >= -shotDotThreshold)
                return false;

            return MathF.Sqrt(originLenSq) <= passSideMaxDistance + 0.001f;
        }

        if (dotShot <= shotDotThreshold)
            return false;

        var pointBlankDistance = MathF.Max(0f, blockedSidePointBlankPassDistance);
        if (pointBlankDistance > 0f)
        {
            var maxDistance = pointBlankDistance + 0.001f;
            if (originLenSq <= maxDistance * maxDistance)
                return true;
        }

        var chance = Math.Clamp(blockedSidePassChance, 0f, 1f);
        return random.Prob(chance);
    }
}
