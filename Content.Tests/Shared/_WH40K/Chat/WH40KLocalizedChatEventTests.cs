using System.Collections.Generic;
using Content.Shared._WH40K.Chat;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._WH40K.Chat;

[TestFixture]
public sealed class WH40KLocalizedChatEventTests
{
    [Test]
    public void DefaultEventHasEmptyLocKey()
    {
        var ev = new WH40KLocalizedChatEvent();
        Assert.That(ev.LocKey, Is.EqualTo(string.Empty));
        Assert.That(ev.LocArgs, Is.Null);
        Assert.That(ev.ResolveArgValues, Is.False);
        Assert.That(ev.ColorOverride, Is.Null);
    }

    [Test]
    public void InitLocKeyOnly()
    {
        var ev = new WH40KLocalizedChatEvent { LocKey = "wh40k-phase-assault-announce" };
        Assert.That(ev.LocKey, Is.EqualTo("wh40k-phase-assault-announce"));
        Assert.That(ev.LocArgs, Is.Null);
    }

    [Test]
    public void InitWithArgs()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-team-service-message",
            LocArgs = new Dictionary<string, string>
            {
                ["team"] = "wh40k-team-imperium"
            },
            ResolveArgValues = true
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(1));
        Assert.That(ev.LocArgs!["team"], Is.EqualTo("wh40k-team-imperium"));
        Assert.That(ev.ResolveArgValues, Is.True);
    }

    [Test]
    public void InitWithColorOverride()
    {
        var color = Color.FromHex("#FF0000");
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-team-winner-announce",
            ColorOverride = color
        };

        Assert.That(ev.ColorOverride, Is.EqualTo(color));
    }

    [Test]
    public void EmptyArgsDoNotThrow()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "some-key",
            LocArgs = new Dictionary<string, string>()
        };

        Assert.That(ev.LocArgs, Is.Not.Null);
        Assert.That(ev.LocArgs, Is.Empty);
    }

    [Test]
    public void MixedResolveAndPlainArgs()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-weather-start-announce",
            LocArgs = new Dictionary<string, string>
            {
                ["weather"] = "wh40k-weather-name-WHAsh",
                ["danger"] = "wh40k-weather-danger-high",
                ["summary"] = "wh40k-weather-summary-WHAsh",
                ["protection"] = "wh40k-weather-protection-generic"
            },
            ResolveArgValues = true
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(4));
        Assert.That(ev.LocArgs!.Keys, Is.EquivalentTo(new[] { "weather", "danger", "summary", "protection" }));
    }

    [Test]
    public void NumberArgsKeptAsStrings()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-round-event-warning",
            LocArgs = new Dictionary<string, string>
            {
                ["seconds"] = "15",
                ["event"] = "wh40k-round-event-name-logistics"
            },
            ResolveArgValues = true
        };

        Assert.That(ev.LocArgs!["seconds"], Is.EqualTo("15"));
    }

    [Test]
    public void MultipleArgsPreserveAllPairs()
    {
        var args = new Dictionary<string, string>
        {
            ["mission"] = "Supply Raid",
            ["duration"] = "120",
            ["coords"] = "X:42 Y:17",
        };

        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-command-runtime-mission-global-started",
            LocArgs = args,
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(3));
        Assert.That(ev.LocArgs!["mission"], Is.EqualTo("Supply Raid"));
        Assert.That(ev.LocArgs["duration"], Is.EqualTo("120"));
        Assert.That(ev.LocArgs["coords"], Is.EqualTo("X:42 Y:17"));
    }

    [Test]
    public void ResolveArgValuesDefaultIsFalse()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-team-time-limit-announce",
            LocArgs = new Dictionary<string, string> { ["team"] = "wh40k-team-imperium" },
        };

        Assert.That(ev.ResolveArgValues, Is.False);
    }

    [Test]
    public void TeamBattleAnnounceNoArgs()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-round-event-logistics-start"
        };

        Assert.That(ev.LocKey, Is.EqualTo("wh40k-round-event-logistics-start"));
        Assert.That(ev.LocArgs, Is.Null);
        Assert.That(ev.ResolveArgValues, Is.False);
        Assert.That(ev.ColorOverride, Is.Null);
    }

    [Test]
    public void LevelUpAnnouncementArgs()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-team-level-up-announce",
            LocArgs = new Dictionary<string, string>
            {
                ["team"] = "wh40k-team-imperium",
                ["level"] = "3",
            },
            ResolveArgValues = true,
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(2));
        Assert.That(ev.LocArgs!["team"], Is.EqualTo("wh40k-team-imperium"));
        Assert.That(ev.LocArgs["level"], Is.EqualTo("3"));
    }

    [Test]
    public void LevelBuffAnnouncementArgs()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-team-level-buff-announce",
            LocArgs = new Dictionary<string, string>
            {
                ["team"] = "wh40k-team-heretics",
                ["buff"] = "wh40k-team-level-buff-name-medical",
                ["effect"] = "wh40k-team-level-buff-effect-medical",
            },
            ResolveArgValues = true,
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(3));
        Assert.That(ev.ResolveArgValues, Is.True);
    }

    [Test]
    public void MissionOutcomeTeamArgs()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-command-runtime-mission-outcome-team",
            LocArgs = new Dictionary<string, string>
            {
                ["mission"] = "Cargo Extraction",
                ["tier"] = "Major Success",
                ["points"] = "150",
            },
            ResolveArgValues = false,
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(3));
        Assert.That(ev.LocArgs!["points"], Is.EqualTo("150"));
        Assert.That(ev.ResolveArgValues, Is.False);
    }

    [Test]
    public void ColorOverridePreservesAlpha()
    {
        var color = new Color(1f, 0f, 0f, 0.5f);
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "some-key",
            ColorOverride = color,
        };

        Assert.That(ev.ColorOverride!.Value.A, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void EventInheritsFromEntityEventArgs()
    {
        var ev = new WH40KLocalizedChatEvent();
        Assert.That(ev, Is.InstanceOf<Robust.Shared.GameObjects.EntityEventArgs>());
    }

    [Test]
    public void ArgKeysAreCaseSensitive()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "test-key",
            LocArgs = new Dictionary<string, string>
            {
                ["Team"] = "upper",
                ["team"] = "lower",
            },
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(2));
        Assert.That(ev.LocArgs!["Team"], Is.EqualTo("upper"));
        Assert.That(ev.LocArgs["team"], Is.EqualTo("lower"));
    }

    [Test]
    public void WeatherWarningFullArgs()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-weather-warning-announce",
            LocArgs = new Dictionary<string, string>
            {
                ["weather"] = "wh40k-weather-name-WHAcidRain",
                ["seconds"] = "45",
                ["danger"] = "wh40k-weather-danger-extreme",
                ["summary"] = "wh40k-weather-summary-WHAcidRain",
                ["protection"] = "wh40k-weather-protection-hardsuit",
            },
            ResolveArgValues = true,
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(5));
        Assert.That(ev.LocArgs!.ContainsKey("weather"), Is.True);
        Assert.That(ev.LocArgs.ContainsKey("seconds"), Is.True);
        Assert.That(ev.LocArgs.ContainsKey("danger"), Is.True);
        Assert.That(ev.LocArgs.ContainsKey("summary"), Is.True);
        Assert.That(ev.LocArgs.ContainsKey("protection"), Is.True);
    }

    [Test]
    public void EnemyCounterMissionArgs()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-command-runtime-mission-enemy-counter-cargo",
            LocArgs = new Dictionary<string, string>
            {
                ["enemy"] = "wh40k-team-heretics",
                ["mission"] = "Supply Raid",
            },
            ResolveArgValues = true,
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(2));
        Assert.That(ev.LocArgs!["enemy"], Is.EqualTo("wh40k-team-heretics"));
    }

    [Test]
    public void IntelJamArgs()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-command-runtime-mission-intel-jam-applied",
            LocArgs = new Dictionary<string, string>
            {
                ["duration"] = "60",
                ["targets"] = "1",
            },
        };

        Assert.That(ev.LocArgs, Has.Count.EqualTo(2));
        Assert.That(ev.LocArgs!["duration"], Is.EqualTo("60"));
        Assert.That(ev.LocArgs["targets"], Is.EqualTo("1"));
    }

    [Test]
    public void NullColorOverrideIsDefault()
    {
        var ev = new WH40KLocalizedChatEvent
        {
            LocKey = "any-key",
            ColorOverride = null,
        };

        Assert.That(ev.ColorOverride.HasValue, Is.False);
    }
}
