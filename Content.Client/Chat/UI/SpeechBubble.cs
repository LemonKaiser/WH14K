using System.Numerics;
using Content.Client.Chat.Managers;
using Content.Client.UserInterface.Systems.Chat.RichText;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Speech;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI
{
    public abstract class SpeechBubble : Control
    {
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] protected readonly IConfigurationManager ConfigManager = default!;
        private readonly SharedTransformSystem _transformSystem;

        public enum SpeechType : byte
        {
            Emote,
            Say,
            Whisper,
            Looc
        }

        /// <summary>
        ///     The total time a speech bubble stays on screen.
        /// </summary>
        private static readonly TimeSpan TotalTime = TimeSpan.FromSeconds(4);

        /// <summary>
        ///     The amount of time at the end of the bubble's life at which it starts fading.
        /// </summary>
        private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(0.25f);

        /// <summary>
        ///     The distance in world space to offset the speech bubble from the center of the entity.
        ///     i.e. greater -> higher above the mob's head.
        /// </summary>
        private const float EntityVerticalOffset = 0.5f;

        /// <summary>
        ///     The default maximum width for speech bubbles.
        /// </summary>
        public const float SpeechMaxWidth = 256;

        protected readonly EntityUid SenderEntity;

        public SpeechType BubbleType { get; }

        public uint? ServerMessageId { get; }

        /// <summary>
        /// The time at which this bubble will die.
        /// </summary>
        private TimeSpan _deathTime;
        private bool _deathQueued;
        private bool _died;

        public float VerticalOffset { get; set; }
        private float _verticalOffsetAchieved;

        public Vector2 ContentSize { get; private set; }

        // man down
        public event Action<EntityUid, SpeechBubble>? OnDied;

        public static SpeechBubble CreateSpeechBubble(SpeechType type, ChatMessage message, EntityUid senderEntity)
        {
            switch (type)
            {
                case SpeechType.Emote:
                    return new TextSpeechBubble(type, message, senderEntity, "emoteBox");

                case SpeechType.Say:
                    return new FancyTextSpeechBubble(type, message, senderEntity, "sayBox");

                case SpeechType.Whisper:
                    return new FancyTextSpeechBubble(type, message, senderEntity, "whisperBox");

                case SpeechType.Looc:
                    return new TextSpeechBubble(type, message, senderEntity, "emoteBox", Color.FromHex("#48d1cc"));

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public SpeechBubble(SpeechType type, ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
        {
            IoCManager.InjectDependencies(this);
            SenderEntity = senderEntity;
            BubbleType = type;
            ServerMessageId = message.ServerMessageId;
            _transformSystem = _entityManager.System<SharedTransformSystem>();

            // Use text clipping so new messages don't overlap old ones being pushed up.
            RectClipContent = true;

            var bubble = BuildBubble(type, message, speechStyleClass, fontColor);

            AddChild(bubble);

            ForceRunStyleUpdate();

            bubble.Measure(Vector2Helpers.Infinity);
            ContentSize = bubble.DesiredSize;
            _verticalOffsetAchieved = -ContentSize.Y;
            _deathTime = _timing.RealTime + TotalTime;
        }

        public void SyncLifetimeFrom(SpeechBubble other)
        {
            _deathTime = other._deathTime;
        }

        protected abstract Control BuildBubble(SpeechType type, ChatMessage message, string speechStyleClass, Color? fontColor = null);

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            var timeLeft = (float)(_deathTime - _timing.RealTime).TotalSeconds;
            if (_entityManager.Deleted(SenderEntity) || timeLeft <= 0)
            {
                if (!_deathQueued)
                {
                    _deathQueued = true;
                    // Timer spawn to prevent concurrent modification exception.
                    Timer.Spawn(0, Die);
                }
                return;
            }

            // Lerp to our new vertical offset if it's been modified.
            if (MathHelper.CloseToPercent(_verticalOffsetAchieved - VerticalOffset, 0, 0.1))
            {
                _verticalOffsetAchieved = VerticalOffset;
            }
            else
            {
                _verticalOffsetAchieved = MathHelper.Lerp(_verticalOffsetAchieved, VerticalOffset, 10 * args.DeltaSeconds);
            }

            if (!_entityManager.TryGetComponent<TransformComponent>(SenderEntity, out var xform) || xform.MapID != _eyeManager.CurrentEye.Position.MapId)
            {
                Modulate = Color.White.WithAlpha(0);
                return;
            }

            if (timeLeft <= FadeTime.TotalSeconds)
            {
                // Update alpha if we're fading.
                Modulate = Color.White.WithAlpha(timeLeft / (float)FadeTime.TotalSeconds);
            }
            else
            {
                // Make opaque otherwise, because it might have been hidden before
                Modulate = Color.White;
            }

            var baseOffset = 0f;

            if (_entityManager.TryGetComponent<SpeechComponent>(SenderEntity, out var speech))
                baseOffset = speech.SpeechBubbleOffset;

            var offset = (-_eyeManager.CurrentEye.Rotation).ToWorldVec() * -(EntityVerticalOffset + baseOffset);
            var worldPos = _transformSystem.GetWorldPosition(xform) + offset;

            var lowerCenter = _eyeManager.WorldToScreen(worldPos) / UIScale;
            var screenPos = lowerCenter - new Vector2(ContentSize.X / 2, ContentSize.Y + _verticalOffsetAchieved);
            // Round to nearest 0.5
            screenPos = (screenPos * 2).Rounded() / 2;
            LayoutContainer.SetPosition(this, screenPos);

            var height = MathF.Ceiling(MathHelper.Clamp(lowerCenter.Y - screenPos.Y, 0, ContentSize.Y));
            SetHeight = height;
        }

        private void Die()
        {
            if (Disposed || _died)
                return;

            _died = true;
            _deathQueued = true;
            OnDied?.Invoke(SenderEntity, this);
        }

        /// <summary>
        ///     Causes the speech bubble to start fading IMMEDIATELY.
        /// </summary>
        public void FadeNow()
        {
            if (_deathTime > _timing.RealTime)
            {
                _deathTime = _timing.RealTime + FadeTime;
            }
        }

        protected void ExtendLifetime(TimeSpan extension)
        {
            if (extension > TimeSpan.Zero)
                _deathTime += extension;
        }

        protected FormattedMessage FormatSpeech(string message, Color? fontColor = null)
        {
            var msg = new FormattedMessage();
            if (fontColor != null)
                msg.PushColor(fontColor.Value);
            msg.AddMarkupOrThrow(message);
            return msg;
        }

        protected FormattedMessage ExtractAndFormatSpeechSubstring(ChatMessage message, string tag, Color? fontColor = null)
        {
            return FormatSpeech(SharedChatSystem.GetStringInsideTag(message, tag), fontColor);
        }

        protected bool ShouldRenderEmojiAliases(ChatMessage message)
        {
            var allowedChannels = ChatEmoji.ParseAllowedChannels(ConfigManager.GetCVar(CCVars.ChatEmojiAllowedChannels));
            return ChatEmoji.IsAllowed(allowedChannels, message.Channel);
        }

        protected IPrototypeManager EmojiPrototypeManager => _prototypeManager;

    }

    public sealed class TextSpeechBubble : SpeechBubble
    {
        public TextSpeechBubble(SpeechType speechType, ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
            : base(speechType, message, senderEntity, speechStyleClass, fontColor)
        {
        }

        protected override Control BuildBubble(SpeechType type, ChatMessage message, string speechStyleClass, Color? fontColor = null)
        {
            var displayMessage = ChatEmojiRichText.ReplaceEmojiText(
                SpeechBubbleDisplayPolicy.LimitMessage(FormatSpeech(message.WrappedMessage, fontColor)),
                ShouldRenderEmojiAliases(message),
                EmojiPrototypeManager);
            var label = new RichTextLabel
            {
                MaxWidth = SpeechMaxWidth,
            };

            label.SetMessage(displayMessage, tagsAllowed: null);

            var panel = new PanelContainer
            {
                StyleClasses = { "speechBox", speechStyleClass },
                Children = { label },
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity))
            };

            return panel;
        }
    }

    public sealed class FancyTextSpeechBubble : SpeechBubble
    {
        private DialogueRevealRichTextLabel? _dialogueContent;

        public FancyTextSpeechBubble(SpeechType speechType, ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
            : base(speechType, message, senderEntity, speechStyleClass, fontColor)
        {
            if (_dialogueContent?.ShouldAnimate == true)
                ExtendLifetime(_dialogueContent.EstimatedRevealDuration);
        }

        protected override Control BuildBubble(SpeechType type, ChatMessage message, string speechStyleClass, Color? fontColor = null)
        {
            var dialogueMessage = ChatEmojiRichText.ReplaceEmojiText(
                SpeechBubbleDisplayPolicy.LimitMessage(
                    ExtractAndFormatSpeechSubstring(message, "BubbleContent", fontColor)),
                ShouldRenderEmojiAliases(message),
                EmojiPrototypeManager);

            if (!ConfigManager.GetCVar(CCVars.ChatEnableFancyBubbles))
            {
                _dialogueContent = new DialogueRevealRichTextLabel(
                    dialogueMessage,
                    SenderEntity,
                    type,
                    message.SpeechTransport)
                {
                    MaxWidth = SpeechMaxWidth
                };

                var unfanciedPanel = new PanelContainer
                {
                    StyleClasses = { "speechBox", speechStyleClass },
                    Children = { _dialogueContent },
                    ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity)),
                };

                PrepareDialogueReveal(unfanciedPanel, _dialogueContent);
                return unfanciedPanel;
            }

            var bubbleHeader = new RichTextLabel
            {
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleSpeakerOpacity)),
                Margin = new Thickness(1, 1, 1, 1),
            };

            _dialogueContent = new DialogueRevealRichTextLabel(
                dialogueMessage,
                SenderEntity,
                type,
                message.SpeechTransport)
            {
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleTextOpacity)),
                MaxWidth = SpeechMaxWidth,
                Margin = new Thickness(2, 6, 2, 2),
                StyleClasses = { "bubbleContent" },
            };

            //We'll be honest. *Yes* this is hacky. Doing this in a cleaner way would require a bottom-up refactor of how saycode handles sending chat messages. -Myr
            bubbleHeader.SetMessage(
                ChatEmojiRichText.ReplaceEmojiText(
                    ExtractAndFormatSpeechSubstring(message, "BubbleHeader", fontColor),
                    ShouldRenderEmojiAliases(message),
                    EmojiPrototypeManager),
                tagsAllowed: null);

            //As for below: Some day this could probably be converted to xaml. But that is not today. -Myr
            var mainPanel = new PanelContainer
            {
                StyleClasses = { "speechBox", speechStyleClass },
                Children = { _dialogueContent },
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity)),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Bottom,
                Margin = new Thickness(4, 14, 4, 2)
            };

            var headerPanel = new PanelContainer
            {
                StyleClasses = { "speechBox", speechStyleClass },
                Children = { bubbleHeader },
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.ChatFancyNameBackground) ? ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity) : 0f),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Top
            };

            var panel = new PanelContainer
            {
                Children = { mainPanel, headerPanel }
            };

            PrepareDialogueReveal(panel, _dialogueContent);
            return panel;
        }

        private static void PrepareDialogueReveal(Control bubbleControl, DialogueRevealRichTextLabel? dialogueContent)
        {
            if (dialogueContent == null)
                return;

            bubbleControl.ForceRunStyleUpdate();
            bubbleControl.Measure(Vector2Helpers.Infinity);
            bubbleControl.MinSize = bubbleControl.DesiredSize;
            dialogueContent.StartRevealIfEnabled();
        }
    }
}
