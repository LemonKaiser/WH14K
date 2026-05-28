using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Vehicle.Visuals;
using Robust.Client.GameObjects;

namespace Content.Client._WH40K.Vehicle.Visuals;

public sealed partial class WH40KVehicleRiderSeatVisualSystem : EntitySystem
{
    [Dependency] private  SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, Vector2> _originalOffsets = new();

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var activeRiders = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WH40KVehicleRiderSeatComponent>();
        while (query.MoveNext(out _, out var seat))
        {
            var seatCount = Math.Min(seat.SeatOffsets.Count, seat.SeatOccupants.Count);

            for (var i = 0; i < seatCount; i++)
            {
                var rider = seat.SeatOccupants[i];
                if (!TryComp<SpriteComponent>(rider, out var sprite))
                    continue;

                if (!_originalOffsets.ContainsKey(rider))
                    _originalOffsets[rider] = sprite.Offset;

                _sprite.SetOffset((rider, sprite), _originalOffsets[rider] + seat.SeatOffsets[i]);
                activeRiders.Add(rider);
            }
        }

        foreach (var (rider, originalOffset) in _originalOffsets.ToArray())
        {
            if (activeRiders.Contains(rider))
                continue;

            if (TryComp<SpriteComponent>(rider, out var sprite))
                _sprite.SetOffset((rider, sprite), originalOffset);

            _originalOffsets.Remove(rider);
        }
    }
}
