using System;
using System.Text;
using Content.Shared.Chat;
using Content.Shared.CCVar;
using Content.Shared.Speech;
using Robust.Shared.Audio.Components;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI
{
    internal sealed class DialogueRevealRichTextLabel : RichTextLabel
    {
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IEntityManager _entManager = default!;
        [Dependency] private readonly IPrototypeManager _protoManager = default!;
        [Dependency] private readonly IGameTiming _timing = default!;

        private static TimeSpan _nextGlobalBlipTime;
        private readonly SharedAudioSystem? _audio;
        private readonly EntityUid _senderEntity;
        private readonly FormattedMessage _fullMessage;
        private readonly DialogueBlipProfile? _profile;
        private readonly string[] _plainTextElements;
        private readonly DialogueBlipMessageContext _modulationContext;
        private readonly int _maxAnimatedTextElementCount;

        private int _visibleTextElementCount;
        private int _charsUntilNextBlip = 1;
        private int _playedBlipCount;
        private float _revealAccumulator;
        private float _nextTextElementDelay;
        private bool _animating;

        public bool CanAnimate => _profile != null &&
                                  _plainTextElements.Length > 0 &&
                                  _maxAnimatedTextElementCount > 0;
        public bool ShouldAnimate => CanAnimate && IsDialogueBlipFeatureActive();

        public TimeSpan EstimatedRevealDuration
        {
            get
            {
                if (_profile == null)
                    return TimeSpan.Zero;

                var speedScale = GetSpeedScale();
                return speedScale <= 0f
                    ? TimeSpan.Zero
                    : TimeSpan.FromSeconds(EstimateRevealDurationSeconds(
                        _plainTextElements,
                        _profile.Value,
                        _maxAnimatedTextElementCount,
                        speedScale,
                        _modulationContext));
            }
        }

        public DialogueRevealRichTextLabel(
            FormattedMessage message,
            EntityUid senderEntity,
            SpeechBubble.SpeechType speechType,
            ChatSpeechTransport speechTransport)
        {
            IoCManager.InjectDependencies(this);

            _fullMessage = SpeechBubbleDisplayPolicy.LimitMessage(message);
            _senderEntity = senderEntity;
            _audio = _entManager.SystemOrNull<SharedAudioSystem>();
            _profile = ResolveDialogueBlipProfile();
            _plainTextElements = DialogueRevealTextElementHelper.GetTextElements(_fullMessage);
            _modulationContext = DialogueBlipModulation.BuildContext(_plainTextElements, senderEntity, speechType, speechTransport);
            _maxAnimatedTextElementCount = _profile == null
                ? 0
                : SpeechBubbleDisplayPolicy.GetAnimatedTextElementCount(
                    _plainTextElements.Length,
                    _profile.Value.CharactersPerSecond);

            SetMessage(_fullMessage, tagsAllowed: null);
        }

        public void StartRevealIfEnabled()
        {
            if (!CanAnimate || !IsDialogueBlipFeatureActive())
                return;

            _visibleTextElementCount = 0;
            _charsUntilNextBlip = 1;
            _playedBlipCount = 0;
            _revealAccumulator = 0f;
            _nextTextElementDelay = 0f;
            _animating = true;
            SetMessage(FormattedMessage.Empty, tagsAllowed: null);
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            if (!_animating)
                return;

            if (!IsDialogueBlipFeatureActive())
            {
                CompleteReveal();
                return;
            }

            _revealAccumulator += args.DeltaSeconds;
            var speedScale = GetSpeedScale();

            while (_visibleTextElementCount < _maxAnimatedTextElementCount &&
                   _revealAccumulator >= _nextTextElementDelay)
            {
                _revealAccumulator -= _nextTextElementDelay;
                RevealNextTextElement(speedScale);

                if (!_animating)
                    break;
            }
        }

        private void RevealNextTextElement(float speedScale)
        {
            var textElementIndex = _visibleTextElementCount;
            var textElement = _plainTextElements[textElementIndex];
            _visibleTextElementCount++;
            var modulation = _profile == null
                ? default
                : DialogueBlipModulation.GetTextElementModulation(
                    _modulationContext,
                    textElementIndex,
                    speedScale,
                    _profile.Value);

            SetMessage(DialogueRevealTextElementHelper.BuildVisibleMessage(_fullMessage, _visibleTextElementCount), tagsAllowed: null);
            TryPlayDialogueBlip(textElement, modulation);

            if (_visibleTextElementCount >= _plainTextElements.Length)
            {
                _animating = false;
                return;
            }

            if (_visibleTextElementCount >= _maxAnimatedTextElementCount)
            {
                CompleteReveal();
                return;
            }

            _nextTextElementDelay = GetTextElementDelaySeconds(textElement, speedScale, modulation);
        }

        private void CompleteReveal()
        {
            _animating = false;
            SetMessage(_fullMessage, tagsAllowed: null);
        }

        private void TryPlayDialogueBlip(string textElement, DialogueBlipTextElementModulation modulation)
        {
            if (_profile == null ||
                DialogueRevealTextElementHelper.IsSilentTextElementForDialogueBlip(textElement) ||
                _playedBlipCount >= SpeechBubbleDisplayPolicy.MaxBlipsPerBubble)
                return;

            _charsUntilNextBlip = Math.Max(0, _charsUntilNextBlip - 1);
            if (_charsUntilNextBlip > 0)
                return;

            if (_audio == null)
                return;

            var volumeScale = Math.Clamp(_cfg.GetCVar(CCVars.SpeechBubbleDialogueBlipVolume), 0f, 1f);
            if (volumeScale <= 0.001f || !_entManager.EntityExists(_senderEntity))
                return;

            var now = _timing.RealTime;
            if (now < _nextGlobalBlipTime)
                return;

            var profile = _profile.Value;
            var audioParams = profile.Sound.Params
                .AddVolume(modulation.VolumeOffsetDb)
                .AddVolume(VolumeScaleToDb(volumeScale))
                .WithPitchScale(modulation.PitchScale)
                .WithVariation(modulation.Variation)
                .WithReferenceDistance(1f)
                .WithMaxDistance(10f)
                .WithRolloffFactor(3f);

            var played = _audio.PlayEntity(profile.Sound, Filter.Local(), _senderEntity, false, audioParams);
            if (played == null)
                return;

            if (modulation.AudioFlags != AudioFlags.None)
                played.Value.Component.Flags |= modulation.AudioFlags;

            played.Value.Component.Occlusion = modulation.Occlusion;
            _charsUntilNextBlip = Math.Max(1, modulation.CharactersPerBlip);
            _playedBlipCount++;
            _nextGlobalBlipTime = now + TimeSpan.FromSeconds(SpeechBubbleDisplayPolicy.MinSecondsBetweenBlips);
        }

        private bool IsDialogueBlipFeatureActive()
        {
            return _cfg.GetCVar(CCVars.SpeechBubbleDialogueBlipsEnabled) &&
                   !_cfg.GetCVar(CCVars.ReducedMotion);
        }

        private float GetSpeedScale()
        {
            return MathF.Max(0.05f, _cfg.GetCVar(CCVars.SpeechBubbleDialogueBlipSpeed));
        }

        private DialogueBlipProfile? ResolveDialogueBlipProfile()
        {
            if (!_entManager.TryGetComponent(_senderEntity, out SpeechComponent? speech) ||
                speech.SpeechSounds == null)
            {
                return null;
            }

            var prototype = _protoManager.Index<SpeechSoundsPrototype>(speech.SpeechSounds);
            if (prototype.DialogueBlipSound == null ||
                prototype.DialogueCharsPerSecond <= 0f ||
                prototype.DialogueCharsPerBlip <= 0)
            {
                return null;
            }

            return new DialogueBlipProfile(
                prototype.DialogueBlipSound,
                prototype.DialogueBlipPitch,
                prototype.DialogueBlipVariation,
                prototype.DialogueBlipVolume,
                prototype.DialogueCharsPerSecond,
                prototype.DialogueCharsPerBlip,
                prototype.DialoguePunctuationPause,
                speech.VoiceTone);
        }

        private static float EstimateRevealDurationSeconds(
            IReadOnlyList<string> textElements,
            DialogueBlipProfile profile,
            int animatedTextElementCount,
            float speedScale,
            DialogueBlipMessageContext modulationContext)
        {
            var total = 0f;
            for (var i = 0; i < animatedTextElementCount && i < textElements.Count; i++)
            {
                var textElement = textElements[i];
                var modulation = DialogueBlipModulation.GetTextElementModulation(
                    modulationContext,
                    i,
                    speedScale,
                    profile);
                total += GetTextElementDelaySeconds(textElement, speedScale, modulation, profile);
            }

            return total;
        }

        private float GetTextElementDelaySeconds(string textElement, float speedScale, DialogueBlipTextElementModulation modulation)
        {
            return GetTextElementDelaySeconds(textElement, speedScale, modulation, _profile!.Value);
        }

        private static float GetTextElementDelaySeconds(
            string textElement,
            float speedScale,
            DialogueBlipTextElementModulation modulation,
            DialogueBlipProfile profile)
        {
            return GetBaseTextElementDelaySeconds(textElement, profile) * modulation.DelayMultiplier / MathF.Max(0.05f, speedScale);
        }

        private static float GetBaseTextElementDelaySeconds(string textElement, DialogueBlipProfile profile)
        {
            var baseDelay = 1f / MathF.Max(1f, profile.CharactersPerSecond);

            if (DialogueRevealTextElementHelper.IsWhitespace(textElement))
                return baseDelay * 0.35f;

            return DialogueRevealTextElementHelper.GetLeadingRune(textElement).Value switch
            {
                '.' or '!' or '?' => baseDelay + profile.PunctuationPauseSeconds,
                ',' or ':' or ';' => baseDelay + profile.PunctuationPauseSeconds * 0.6f,
                _ => baseDelay
            };
        }

        private static float VolumeScaleToDb(float scale)
        {
            return scale <= 0f ? float.NegativeInfinity : 20f * MathF.Log10(scale);
        }
    }

    internal readonly record struct DialogueBlipProfile(
        SoundSpecifier Sound,
        float Pitch,
        float Variation,
        float VolumeOffset,
        float CharactersPerSecond,
        int CharactersPerBlip,
        float PunctuationPauseSeconds,
        VoiceTone VoiceTone);
}
