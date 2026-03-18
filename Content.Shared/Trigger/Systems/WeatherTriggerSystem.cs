using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Weather;

namespace Content.Shared.Trigger.Systems;

public sealed class WeatherTriggerSystem : XOnTriggerSystem<WeatherOnTriggerComponent>
{
    [Dependency] private readonly SharedWeatherSystem _weather = default!;

    protected override void OnTrigger(Entity<WeatherOnTriggerComponent> ent, EntityUid target, ref TriggerEvent args)
    {
        var xform = Transform(target);

        if (ent.Comp.Weather == null) //Clear weather if nothing is set
        {
            _weather.TrySetWeather(xform.MapID, null, out _);
            return;
        }

        _weather.TrySetWeather(xform.MapID, ent.Comp.Weather.Value, out _, ent.Comp.Duration);
    }
}
