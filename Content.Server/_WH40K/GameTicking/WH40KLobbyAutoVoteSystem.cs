using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Voting;
using Content.Server.Voting.Managers;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Voting;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.GameTicking;

public sealed class WH40KLobbyAutoVoteSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IVoteManager _voteManager = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

    private const float AutoVoteSafetyBufferSeconds = 1f;

    private ISawmill _sawmill = default!;
    private AutoVoteStage _stage;
    private TimeSpan _nextActionAt;
    private bool _lobbyTimeEnsured;
    private bool _lobbySequenceClaimed;
    private readonly HashSet<int> _managedVoteIds = new();

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("wh40k.lobby_auto_vote");

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        if (_gameTicker.RunLevel == GameRunLevel.PreRoundLobby)
            QueueLobbyVotes("startup");
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ResetState();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_stage == AutoVoteStage.Idle)
            return;

        if (!ShouldKeepRunning())
        {
            CancelManagedVotes("sequence stopped");
            ResetState();
            return;
        }

        if (_playerManager.PlayerCount == 0)
            return;

        if (!_lobbyTimeEnsured)
            EnsureLobbyTimeIfNeeded();

        if (_timing.CurTime < _nextActionAt || _voteManager.ActiveVotes.Any())
            return;

        switch (_stage)
        {
            case AutoVoteStage.WaitingForDelay:
                StartNextVote();
                break;
            case AutoVoteStage.WaitingForPresetVote:
                StartMapVoteOrComplete();
                break;
            case AutoVoteStage.WaitingForMapVote:
                CompleteSequence();
                break;
        }
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New != GameRunLevel.PreRoundLobby)
        {
            CancelManagedVotes($"run level changed to {ev.New}");
            ResetLobbyState();
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        QueueLobbyVotes("round cleanup");
    }

    private void QueueLobbyVotes(string reason)
    {
        if (!ShouldFeatureRun())
        {
            ResetState();
            return;
        }

        if (_lobbySequenceClaimed)
        {
            _sawmill.Info($"Ignoring duplicate WH40K lobby auto-vote request ({reason}) while current lobby sequence is already claimed.");
            return;
        }

        _stage = AutoVoteStage.WaitingForDelay;
        _nextActionAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(CCVars.WH40KLobbyAutoVoteDelaySeconds)));
        _lobbyTimeEnsured = false;
        _lobbySequenceClaimed = true;

        _sawmill.Info($"Queued automatic WH40K lobby votes ({reason}).");
    }

    private void StartNextVote()
    {
        if (_cfg.GetCVar(CCVars.WH40KLobbyAutoVotePresetEnabled))
        {
            BeginManagedStandardVote(StandardVoteType.Preset);
            _stage = AutoVoteStage.WaitingForPresetVote;
            _nextActionAt = _timing.CurTime + TimeSpan.FromSeconds(0.1f);
            _sawmill.Info("Started automatic WH40K preset vote.");
            return;
        }

        StartMapVoteOrComplete();
    }

    private void StartMapVoteOrComplete()
    {
        if (_cfg.GetCVar(CCVars.WH40KLobbyAutoVoteMapEnabled))
        {
            BeginManagedStandardVote(StandardVoteType.Map);
            _stage = AutoVoteStage.WaitingForMapVote;
            _nextActionAt = _timing.CurTime + TimeSpan.FromSeconds(0.1f);
            _sawmill.Info("Started automatic WH40K map vote.");
            return;
        }

        CompleteSequence();
    }

    private void CompleteSequence()
    {
        _sawmill.Info("Finished automatic WH40K lobby vote sequence.");
        ResetState();
    }

    private void BeginManagedStandardVote(StandardVoteType voteType)
    {
        var existingVotes = _voteManager.ActiveVotes
            .Select(vote => vote.Id)
            .ToHashSet();

        _voteManager.CreateStandardVote(null, voteType);

        var newVote = _voteManager.ActiveVotes
            .FirstOrDefault(vote => !existingVotes.Contains(vote.Id));

        if (newVote == null)
        {
            _sawmill.Warning($"Automatic WH40K {voteType} vote did not create a trackable active vote.");
            return;
        }

        TrackManagedVote(newVote);
    }

    private void TrackManagedVote(IVoteHandle vote)
    {
        if (!_managedVoteIds.Add(vote.Id))
            return;

        vote.OnFinished += OnManagedVoteFinished;
        vote.OnCancelled += OnManagedVoteCancelled;
    }

    private void OnManagedVoteFinished(IVoteHandle sender, VoteFinishedEventArgs args)
    {
        _managedVoteIds.Remove(sender.Id);
    }

    private void OnManagedVoteCancelled(IVoteHandle sender)
    {
        _managedVoteIds.Remove(sender.Id);
    }

    private void CancelManagedVotes(string reason)
    {
        if (_managedVoteIds.Count == 0)
            return;

        var cancelled = 0;

        foreach (var vote in _voteManager.ActiveVotes
                     .Where(vote => _managedVoteIds.Contains(vote.Id) && !vote.Finished)
                     .ToArray())
        {
            vote.Cancel();
            cancelled++;
        }

        _managedVoteIds.Clear();

        if (cancelled > 0)
            _sawmill.Info($"Cancelled {cancelled} automatic WH40K lobby vote(s) because {reason}.");
    }

    private void EnsureLobbyTimeIfNeeded()
    {
        _lobbyTimeEnsured = true;

        if (!_cfg.GetCVar(CCVars.WH40KLobbyAutoVoteEnsureLobbyTime))
            return;

        var availableSeconds = Math.Max(0f, (float) (_gameTicker.LobbyDuration - _gameTicker.RoundPreloadTime).TotalSeconds);
        if (availableSeconds <= 0f)
            return;

        var requiredSeconds = Math.Max(0f, _cfg.GetCVar(CCVars.WH40KLobbyAutoVoteDelaySeconds));
        if (_cfg.GetCVar(CCVars.WH40KLobbyAutoVotePresetEnabled))
            requiredSeconds += _cfg.GetCVar(CCVars.VoteTimerPreset);

        if (_cfg.GetCVar(CCVars.WH40KLobbyAutoVoteMapEnabled))
            requiredSeconds += _cfg.GetCVar(CCVars.VoteTimerMap);

        requiredSeconds += AutoVoteSafetyBufferSeconds;

        if (requiredSeconds <= availableSeconds)
            return;

        var extraSeconds = requiredSeconds - availableSeconds;
        if (_gameTicker.DelayStart(TimeSpan.FromSeconds(extraSeconds)))
        {
            _sawmill.Info(
                $"Extended lobby countdown by {extraSeconds:0.##}s for automatic WH40K voting.");
        }
    }

    private bool ShouldFeatureRun()
    {
        return _cfg.GetCVar(CCVars.WH40KLobbyAutoVoteEnabled) &&
               _gameTicker.LobbyEnabled &&
               !_gameTicker.DummyTicker &&
               (_cfg.GetCVar(CCVars.WH40KLobbyAutoVotePresetEnabled) ||
                _cfg.GetCVar(CCVars.WH40KLobbyAutoVoteMapEnabled));
    }

    private bool ShouldKeepRunning()
    {
        return ShouldFeatureRun() &&
               _gameTicker.RunLevel == GameRunLevel.PreRoundLobby;
    }

    private void ResetState()
    {
        _stage = AutoVoteStage.Idle;
        _nextActionAt = TimeSpan.Zero;
        _lobbyTimeEnsured = false;
    }

    private void ResetLobbyState()
    {
        ResetState();
        _lobbySequenceClaimed = false;
    }

    private enum AutoVoteStage : byte
    {
        Idle,
        WaitingForDelay,
        WaitingForPresetVote,
        WaitingForMapVote
    }
}
