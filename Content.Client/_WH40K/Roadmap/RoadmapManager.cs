using Content.Shared._WH40K.Roadmap;
using Content.Shared.CCVar;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Roadmap;

public sealed class RoadmapManager : IPostInjectInit
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly IResourceManager _resourceManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private const string SawmillName = "roadmap";
    private const int FallbackRoadmapRevision = 1;
    private const int PrototypeRefreshRetryIntervalMs = 1000;
    private const int PrototypeRefreshRetryAttempts = 20;
    public const string RoadmapPrototypeId = "wh40k-roadmap-main";

    private ISawmill _sawmill = default!;
    private int _remainingPrototypeRefreshAttempts = PrototypeRefreshRetryAttempts;
    private bool _prototypeRefreshRetryScheduled;

    public bool NewRoadmapEntries { get; private set; }
    public int LastReadId { get; private set; }
    public int MaxId { get; private set; } = FallbackRoadmapRevision;

    public event Action? NewRoadmapEntriesChanged;

    public void Initialize()
    {
        var hasPrototype = TryRefreshRevisionFromPrototype(raiseEventOnChange: false);
        CheckLastSeenEntry();

        if (!hasPrototype)
            SchedulePrototypeRefreshRetry();

        _configManager.OnValueChanged(CCVars.ServerId, OnServerIdChanged);
        _prototypeManager.PrototypesReloaded += OnPrototypesReloaded;
    }

    public void SaveNewReadId()
    {
        TryRefreshRevisionFromPrototype(raiseEventOnChange: false);
        NewRoadmapEntries = false;
        NewRoadmapEntriesChanged?.Invoke();

        using var sw = _resourceManager.UserData.OpenWriteText(new ResPath($"/roadmap_last_seen_{_configManager.GetCVar(CCVars.ServerId)}"));
        sw.Write(MaxId.ToString());
        LastReadId = MaxId;
    }

    private void OnServerIdChanged(string _)
    {
        CheckLastSeenEntry();

        if (!_prototypeManager.HasIndex<WH40KRoadmapPrototype>(RoadmapPrototypeId))
            SchedulePrototypeRefreshRetry();
    }

    public void SetCurrentRevision(int revision)
    {
        _remainingPrototypeRefreshAttempts = 0;
        ApplyRevision(Math.Max(1, revision), raiseEventOnChange: true);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<WH40KRoadmapPrototype>())
            return;

        TryRefreshRevisionFromPrototype(raiseEventOnChange: true);
    }

    private bool TryRefreshRevisionFromPrototype(bool raiseEventOnChange)
    {
        try
        {
            if (_prototypeManager.HasIndex<WH40KRoadmapPrototype>(RoadmapPrototypeId))
            {
                var roadmap = _prototypeManager.Index<WH40KRoadmapPrototype>(RoadmapPrototypeId);
                ApplyRevision(Math.Max(1, roadmap.Revision), raiseEventOnChange);
                return true;
            }
        }
        catch (UnknownPrototypeException)
        {
            // Prototypes can still be mid-reload during client startup.
        }

        ApplyRevision(FallbackRoadmapRevision, raiseEventOnChange);
        return false;
    }

    private void ApplyRevision(int revision, bool raiseEventOnChange)
    {
        var maxChanged = MaxId != revision;
        MaxId = revision;

        var newEntries = LastReadId < MaxId;
        var entriesChanged = NewRoadmapEntries != newEntries;
        NewRoadmapEntries = newEntries;

        if (raiseEventOnChange && (maxChanged || entriesChanged))
            NewRoadmapEntriesChanged?.Invoke();
    }

    private void SchedulePrototypeRefreshRetry()
    {
        if (_prototypeRefreshRetryScheduled || _remainingPrototypeRefreshAttempts <= 0)
            return;

        _prototypeRefreshRetryScheduled = true;
        Timer.Spawn(PrototypeRefreshRetryIntervalMs, () =>
        {
            _prototypeRefreshRetryScheduled = false;

            if (TryRefreshRevisionFromPrototype(raiseEventOnChange: true))
                return;

            _remainingPrototypeRefreshAttempts--;
            SchedulePrototypeRefreshRetry();
        });
    }

    private void CheckLastSeenEntry()
    {
        LastReadId = 0;

        var path = new ResPath($"/roadmap_last_seen_{_configManager.GetCVar(CCVars.ServerId)}");
        if (_resourceManager.UserData.TryReadAllText(path, out var lastReadIdText))
        {
            if (!int.TryParse(lastReadIdText, out var parsed))
            {
                _sawmill.Warning($"Failed to parse roadmap marker '{lastReadIdText}', resetting to 0.");
                parsed = 0;
            }

            LastReadId = parsed;
        }

        ApplyRevision(MaxId, raiseEventOnChange: true);
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = _logManager.GetSawmill(SawmillName);
    }
}
