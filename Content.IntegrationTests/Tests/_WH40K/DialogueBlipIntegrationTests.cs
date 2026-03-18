#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client.Chat.Managers;
using Content.Client.Chat.UI;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server.Chat.Systems;
using Content.Shared.CCVar;
using Content.Server.GameTicking;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using ClientPlayerManager = Robust.Client.Player.IPlayerManager;
using ServerPlayerManager = Robust.Server.Player.IPlayerManager;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class DialogueBlipIntegrationTests
{
    private static readonly FieldInfo ActiveSpeechBubblesField = typeof(ChatUIController)
        .GetField("_activeSpeechBubbles", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo QueuedSpeechBubblesField = typeof(ChatUIController)
        .GetField("_queuedSpeechBubbles", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo SpeechBubbleQueueDataMessageQueueProperty = typeof(ChatUIController)
        .GetNestedType("SpeechBubbleQueueData", BindingFlags.NonPublic)!
        .GetProperty("MessageQueue", BindingFlags.Instance | BindingFlags.Public)!;

    [Test]
    public async Task LocalSpeechBubbleHandlesMixedLanguageMessage()
    {
        await using var pair = await StartWh40KRoundAsync();
        var (chatController, player) = await GetClientChatControllerAsync(pair);

        await pair.Client.WaitPost(() => chatController.History.Clear());
        var baseline = await GetActiveBubbleCountAsync(pair, chatController, player);

        var token = $"dlg-local-{Guid.NewGuid():N}";
        var text = $"Привет 世界 {token}.";

        await pair.Client.WaitPost(() =>
        {
            var chat = pair.Client.ResolveDependency<IChatManager>();
            chat.SendMessage(text, ChatSelectChannel.Local);
        });

        var message = await WaitForMessageAsync(
            pair,
            chatController,
            msg => msg.Channel == ChatChannel.Local && msg.Message.Contains(token, StringComparison.Ordinal));
        var bubbleCount = await WaitForBubbleCountAsync(pair, chatController, player, baseline + 1);

        Assert.Multiple(() =>
        {
            Assert.That(message.Message, Does.Contain("Привет"));
            Assert.That(message.Message, Does.Contain("世界"));
            Assert.That(message.Message, Does.Contain(token));
            Assert.That(message.SpeechTransport, Is.EqualTo(ChatSpeechTransport.Direct));
            Assert.That(bubbleCount, Is.EqualTo(baseline + 1));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RadioSpeechCreatesOneLocalWhisperBubbleAndOneRadioChatLine()
    {
        await using var pair = await StartWh40KRoundAsync();
        var (chatController, player) = await GetClientChatControllerAsync(pair);
        await EnableIntrinsicRadioAsync(pair);

        await pair.Client.WaitPost(() => chatController.History.Clear());
        var baseline = await GetActiveBubbleCountAsync(pair, chatController, player);

        var token = $"dlg-radio-{Guid.NewGuid():N}";
        var text = $";Радио 世界 {token}.";

        await pair.Client.WaitPost(() =>
        {
            var chat = pair.Client.ResolveDependency<IChatManager>();
            chat.SendMessage(text, ChatSelectChannel.Local);
        });

        var whisper = await WaitForMessageAsync(
            pair,
            chatController,
            msg => msg.Channel == ChatChannel.Whisper && msg.Message.Contains(token, StringComparison.Ordinal));
        var radio = await WaitForMessageAsync(
            pair,
            chatController,
            msg => msg.Channel == ChatChannel.Radio && msg.Message.Contains(token, StringComparison.Ordinal));
        var bubbleCount = await WaitForBubbleCountAsync(pair, chatController, player, baseline + 1);

        Assert.Multiple(() =>
        {
            Assert.That(whisper.Message, Does.Contain(token));
            Assert.That(radio.Message, Does.Contain(token));
            Assert.That(whisper.SpeechTransport, Is.EqualTo(ChatSpeechTransport.Radio));
            Assert.That(radio.SpeechTransport, Is.EqualTo(ChatSpeechTransport.Radio));
            Assert.That(bubbleCount, Is.EqualTo(baseline + 1), "Radio traffic must not create a second bubble on top of the local whisper bubble.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StationAnnouncementAddsRadioMessageButNoSpeechBubble()
    {
        await using var pair = await StartWh40KRoundAsync();
        var (chatController, player) = await GetClientChatControllerAsync(pair);

        await pair.Client.WaitPost(() => chatController.History.Clear());
        var baseline = await GetActiveBubbleCountAsync(pair, chatController, player);

        var token = $"dlg-announce-{Guid.NewGuid():N}";

        await pair.Server.WaitPost(() =>
        {
            var playerMan = pair.Server.ResolveDependency<ServerPlayerManager>();
            var actor = playerMan.Sessions.Single().AttachedEntity!.Value;
            pair.Server.System<ChatSystem>().DispatchStationAnnouncement(actor, $"Station test {token}.", "QA");
        });

        var announcement = await WaitForMessageAsync(
            pair,
            chatController,
            msg => msg.Channel == ChatChannel.Radio && msg.Message.Contains(token, StringComparison.Ordinal));

        await pair.RunTicksSync(20);
        var bubbleCount = await GetActiveBubbleCountAsync(pair, chatController, player);

        Assert.Multiple(() =>
        {
            Assert.That(announcement.Message, Does.Contain(token));
            Assert.That(bubbleCount, Is.EqualTo(baseline), "Announcements must stay chat-line-only and not spawn a speech bubble.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpeechBubbleQueueBacklogStaysBoundedDuringSpamBurst()
    {
        await using var pair = await StartWh40KRoundAsync();
        var (chatController, player) = await GetClientChatControllerAsync(pair);

        await pair.Client.WaitPost(() => chatController.History.Clear());

        var tokenPrefix = $"dlg-spam-{Guid.NewGuid():N}";
        var messages = Enumerable.Range(0, 6)
            .Select(i => $"{tokenPrefix}-{i} {new string('a', 90)}")
            .ToArray();

        await pair.Client.WaitPost(() =>
        {
            var entMan = pair.Client.ResolveDependency<IEntityManager>();
            var netEntity = entMan.GetNetEntity(player);

            foreach (var text in messages)
            {
                var wrapped = $"[BubbleHeader][Name]QA[/Name][/BubbleHeader] says, \"[BubbleContent]{FormattedMessage.EscapeText(text)}[/BubbleContent]\"";
                var msg = new ChatMessage(ChatChannel.Local, text, wrapped, netEntity, null);
                chatController.ProcessChatMessage(msg);
            }
        });

        var matchingMessages = await GetMatchingMessageCountAsync(
            pair,
            chatController,
            msg => msg.Channel == ChatChannel.Local && msg.Message.Contains(tokenPrefix, StringComparison.Ordinal));
        var queuedCount = await GetQueuedBubbleCountAsync(pair, chatController, player);

        Assert.Multiple(() =>
        {
            Assert.That(matchingMessages, Is.EqualTo(messages.Length));
            Assert.That(
                queuedCount,
                Is.EqualTo(4),
                "Speech bubble backlog must stay capped per sender even if chat history continues to accept messages.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LocalSpeechBubbleCreatesDialogueBlipAudioStreamWithExpectedParams()
    {
        await using var pair = await StartWh40KRoundAsync();
        var (chatController, player) = await GetClientChatControllerAsync(pair);

        DialogueBlipExpectedProfile expectedProfile = default;
        HashSet<EntityUid>? baselineAudioEntities = null;
        const string text = "b";

        await pair.Client.WaitPost(() =>
        {
            chatController.History.Clear();
            expectedProfile = PrepareDialogueBlipAudioTestState(
                pair,
                player,
                1f,
                text,
                speechType: SpeechBubble.SpeechType.Say,
                speechTransport: ChatSpeechTransport.Direct);
            baselineAudioEntities = CaptureAudioEntitySet(pair.Client.ResolveDependency<IEntityManager>());
        });

        var baseline = await GetActiveBubbleCountAsync(pair, chatController, player);

        await pair.Client.WaitPost(() =>
        {
            chatController.ProcessChatMessage(BuildSpeechBubbleChatMessage(
                pair,
                player,
                ChatChannel.Local,
                text,
                ChatSpeechTransport.Direct));
        });

        await WaitForMessageAsync(
            pair,
            chatController,
            msg => msg.Channel == ChatChannel.Local && string.Equals(msg.Message, text, StringComparison.Ordinal));
        await WaitForBubbleCountAsync(pair, chatController, player, baseline + 1);

        var sample = await WaitForDialogueBlipAudioSampleAsync(
            pair,
            baselineAudioEntities!,
            player,
            expectedProfile.ExpectedSoundPaths);

        Assert.Multiple(() =>
        {
            Assert.That(
                ContainsExpectedSoundPath(sample.EntityName, expectedProfile.ExpectedSoundPaths),
                Is.True,
                $"Expected dialogue blip sound to come from one of: {string.Join(", ", expectedProfile.ExpectedSoundPaths)}. Actual entity name: {sample.EntityName}");
            Assert.That(sample.Parent, Is.EqualTo(player));
            Assert.That(sample.State, Is.EqualTo(AudioState.Playing));
            Assert.That(sample.Params.Volume, Is.EqualTo(expectedProfile.Volume).Within(0.001f));
            Assert.That(sample.Params.Variation.HasValue, Is.True);
            Assert.That(sample.Params.Variation!.Value, Is.EqualTo(expectedProfile.Variation).Within(0.0001f));
            Assert.That(sample.Params.MaxDistance, Is.EqualTo(10f).Within(0.001f));
            Assert.That(sample.Params.ReferenceDistance, Is.EqualTo(1f).Within(0.001f));
            Assert.That(sample.Params.RolloffFactor, Is.EqualTo(3f).Within(0.001f));
            Assert.That(sample.Params.Loop, Is.False);
            Assert.That(sample.Flags, Is.EqualTo(expectedProfile.Flags));
            Assert.That(sample.Occlusion, Is.EqualTo(expectedProfile.Occlusion).Within(0.0001f));
            Assert.That(sample.Params.Pitch, Is.GreaterThan(0f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ZeroDialogueBlipVolumeDoesNotCreateAudioStream()
    {
        await using var pair = await StartWh40KRoundAsync();
        var (chatController, player) = await GetClientChatControllerAsync(pair);

        DialogueBlipExpectedProfile expectedProfile = default;
        HashSet<EntityUid>? baselineAudioEntities = null;
        const string text = "b";
        await pair.Client.WaitPost(() =>
        {
            chatController.History.Clear();
            expectedProfile = PrepareDialogueBlipAudioTestState(
                pair,
                player,
                0f,
                text,
                speechType: SpeechBubble.SpeechType.Say,
                speechTransport: ChatSpeechTransport.Direct);
            baselineAudioEntities = CaptureAudioEntitySet(pair.Client.ResolveDependency<IEntityManager>());
        });

        var baseline = await GetActiveBubbleCountAsync(pair, chatController, player);

        await pair.Client.WaitPost(() =>
        {
            chatController.ProcessChatMessage(BuildSpeechBubbleChatMessage(
                pair,
                player,
                ChatChannel.Local,
                text,
                ChatSpeechTransport.Direct));
        });

        await WaitForMessageAsync(
            pair,
            chatController,
            msg => msg.Channel == ChatChannel.Local && string.Equals(msg.Message, text, StringComparison.Ordinal));
        await WaitForBubbleCountAsync(pair, chatController, player, baseline + 1);
        await pair.RunTicksSync(20);

        var sample = await GetDialogueBlipAudioSampleAsync(
            pair,
            baselineAudioEntities!,
            player,
            expectedProfile.ExpectedSoundPaths);
        Assert.That(sample, Is.Null, "Dialogue blip audio stream should not be created when the user volume slider is set to zero.");

        await pair.CleanReturnAsync();
    }

    private static async Task<TestPair> StartWh40KRoundAsync()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
            InLobby = true,
            DummyTicker = false,
            Fresh = true
        });

        await pair.WaitCommand("forcemap Battlefield40k");
        await pair.WaitCommand("setgamepreset WH40KTeamBattle 9999");
        await pair.WaitClientCommand("toggleready True");
        await pair.WaitCommand("startround");
        await pair.RunTicksSync(80);

        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var playerMan = pair.Server.ResolveDependency<ServerPlayerManager>();

            Assert.Multiple(() =>
            {
                Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
                Assert.That(playerMan.Sessions.Single().AttachedEntity, Is.Not.Null);
            });
        });

        return pair;
    }

    private static async Task<(ChatUIController Controller, EntityUid Player)> GetClientChatControllerAsync(TestPair pair)
    {
        ChatUIController? chatController = null;
        EntityUid player = default;

        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var playerMan = pair.Client.ResolveDependency<ClientPlayerManager>();

            chatController = ui.GetUIController<ChatUIController>();
            Assert.That(playerMan.LocalEntity, Is.Not.Null);
            player = playerMan.LocalEntity!.Value;
        });

        return (chatController!, player);
    }

    private static async Task EnableIntrinsicRadioAsync(TestPair pair)
    {
        await pair.Server.WaitPost(() =>
        {
            var playerMan = pair.Server.ResolveDependency<ServerPlayerManager>();
            var actor = playerMan.Sessions.Single().AttachedEntity!.Value;
            var entMan = pair.Server.ResolveDependency<IEntityManager>();

            var tx = entMan.EnsureComponent<IntrinsicRadioTransmitterComponent>(actor);
            tx.Channels = new() { SharedChatSystem.CommonChannel };
            entMan.Dirty(actor, tx);

            var rx = entMan.EnsureComponent<IntrinsicRadioReceiverComponent>(actor);
            entMan.Dirty(actor, rx);

            var active = entMan.EnsureComponent<ActiveRadioComponent>(actor);
            active.Channels = new() { SharedChatSystem.CommonChannel };
            entMan.Dirty(actor, active);

            var exempt = entMan.EnsureComponent<TelecomExemptComponent>(actor);
            entMan.Dirty(actor, exempt);
        });

        await pair.RunTicksSync(10);
    }

    private static async Task<int> GetActiveBubbleCountAsync(TestPair pair, ChatUIController controller, EntityUid entity)
    {
        var count = 0;
        await pair.Client.WaitPost(() => count = GetActiveBubbleCount(controller, entity));
        return count;
    }

    private static async Task<int> GetQueuedBubbleCountAsync(TestPair pair, ChatUIController controller, EntityUid entity)
    {
        var count = 0;
        await pair.Client.WaitPost(() => count = GetQueuedBubbleCount(controller, entity));
        return count;
    }

    private static int GetActiveBubbleCount(ChatUIController controller, EntityUid entity)
    {
        var bubbles = (Dictionary<EntityUid, List<SpeechBubble>>) ActiveSpeechBubblesField.GetValue(controller)!;
        return bubbles.TryGetValue(entity, out var list)
            ? list.Count
            : 0;
    }

    private static int GetQueuedBubbleCount(ChatUIController controller, EntityUid entity)
    {
        var queues = (IDictionary) QueuedSpeechBubblesField.GetValue(controller)!;
        if (!queues.Contains(entity))
            return 0;

        var queueData = queues[entity];
        if (queueData == null)
            return 0;

        var queue = (ICollection) SpeechBubbleQueueDataMessageQueueProperty.GetValue(queueData)!;
        return queue.Count;
    }

    private static DialogueBlipExpectedProfile PrepareDialogueBlipAudioTestState(
        TestPair pair,
        EntityUid player,
        float userVolume,
        string text,
        SpeechBubble.SpeechType speechType,
        ChatSpeechTransport speechTransport)
    {
        var cfg = pair.Client.ResolveDependency<IConfigurationManager>();
        cfg.SetCVar(CCVars.SpeechBubbleDialogueBlipsEnabled, true);
        cfg.SetCVar(CCVars.SpeechBubbleDialogueBlipVolume, userVolume);
        cfg.SetCVar(CCVars.SpeechBubbleDialogueBlipSpeed, 1f);

        var entMan = pair.Client.ResolveDependency<IEntityManager>();
        var protoMan = pair.Client.ResolveDependency<IPrototypeManager>();

        Assert.That(entMan.TryGetComponent(player, out SpeechComponent? speech), Is.True);
        Assert.That(speech!.SpeechSounds, Is.Not.Null);

        var speechPrototype = protoMan.Index<SpeechSoundsPrototype>(speech.SpeechSounds);
        Assert.That(speechPrototype.DialogueBlipSound, Is.Not.Null);

        var modulation = ResolveExpectedModulation(text, player, speechPrototype, speech!.VoiceTone, speechType, speechTransport);

        return new DialogueBlipExpectedProfile(
            modulation.VolumeOffsetDb + VolumeScaleToDb(userVolume),
            modulation.Variation,
            modulation.Flags,
            modulation.Occlusion,
            ResolveExpectedSoundPaths(protoMan, speechPrototype.DialogueBlipSound!));
    }

    private static HashSet<EntityUid> CaptureAudioEntitySet(IEntityManager entMan)
    {
        return entMan.GetEntities()
            .Where(entity => entMan.HasComponent<AudioComponent>(entity))
            .ToHashSet();
    }

    private static async Task<DialogueBlipAudioSample?> GetDialogueBlipAudioSampleAsync(
        TestPair pair,
        IReadOnlySet<EntityUid> baselineAudioEntities,
        EntityUid expectedParent,
        IReadOnlyList<string>? expectedSoundPaths = null)
    {
        DialogueBlipAudioSample? sample = null;
        await pair.Client.WaitPost(() =>
        {
            var entMan = pair.Client.ResolveDependency<IEntityManager>();
            sample = FindDialogueBlipAudioSample(entMan, baselineAudioEntities, expectedParent, expectedSoundPaths);
        });

        return sample;
    }

    private static async Task<DialogueBlipAudioSample> WaitForDialogueBlipAudioSampleAsync(
        TestPair pair,
        IReadOnlySet<EntityUid> baselineAudioEntities,
        EntityUid expectedParent,
        IReadOnlyList<string>? expectedSoundPaths = null,
        int maxTicks = 120)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var sample = await GetDialogueBlipAudioSampleAsync(pair, baselineAudioEntities, expectedParent, expectedSoundPaths);
            if (sample != null)
                return sample.Value;

            await pair.RunTicksSync(1);
        }

        Assert.Fail("Timed out waiting for a dialogue blip audio stream to be created on the client.");
        return default;
    }

    private static DialogueBlipAudioSample? FindDialogueBlipAudioSample(
        IEntityManager entMan,
        IReadOnlySet<EntityUid> baselineAudioEntities,
        EntityUid expectedParent,
        IReadOnlyList<string>? expectedSoundPaths = null)
    {
        foreach (var entity in entMan.GetEntities())
        {
            if (baselineAudioEntities.Contains(entity) ||
                !entMan.TryGetComponent(entity, out AudioComponent? audio) ||
                !entMan.TryGetComponent(entity, out MetaDataComponent? metaData) ||
                string.IsNullOrWhiteSpace(metaData.EntityName) ||
                !ContainsExpectedSoundPath(metaData.EntityName, expectedSoundPaths) ||
                !MatchesDialogueBlipAudioParams(audio.Params) ||
                !entMan.TryGetComponent(entity, out TransformComponent? xform) ||
                xform.Coordinates.EntityId != expectedParent)
            {
                continue;
            }

            return new DialogueBlipAudioSample(
                entity,
                metaData.EntityName,
                audio.Params,
                audio.State,
                xform.Coordinates.EntityId,
                audio.Flags,
                audio.Occlusion);
        }

        return null;
    }

    private static async Task<int> WaitForBubbleCountAsync(
        TestPair pair,
        ChatUIController controller,
        EntityUid entity,
        int expectedCount,
        int maxTicks = 180)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var count = await GetActiveBubbleCountAsync(pair, controller, entity);
            if (count >= expectedCount)
                return count;

            await pair.RunTicksSync(1);
        }

        Assert.Fail($"Timed out waiting for speech bubble count {expectedCount}.");
        return 0;
    }

    private static async Task<ChatMessage> WaitForMessageAsync(
        TestPair pair,
        ChatUIController controller,
        Func<ChatMessage, bool> predicate,
        int maxTicks = 180)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            ChatMessage? match = null;
            await pair.Client.WaitPost(() =>
            {
                match = controller.History
                    .Select(entry => entry.Msg)
                    .LastOrDefault(predicate);
            });

            if (match != null)
                return match;

            await pair.RunTicksSync(1);
        }

        Assert.Fail("Timed out waiting for expected chat message.");
        return null!;
    }

    private static async Task<int> GetMatchingMessageCountAsync(
        TestPair pair,
        ChatUIController controller,
        Func<ChatMessage, bool> predicate)
    {
        var count = 0;
        await pair.Client.WaitPost(() =>
        {
            count = controller.History
                .Select(entry => entry.Msg)
                .Count(predicate);
        });

        return count;
    }

    private static float VolumeScaleToDb(float scale)
    {
        return scale <= 0f ? float.NegativeInfinity : 20f * MathF.Log10(scale);
    }

    private static ChatMessage BuildSpeechBubbleChatMessage(
        TestPair pair,
        EntityUid player,
        ChatChannel channel,
        string text,
        ChatSpeechTransport speechTransport)
    {
        var entMan = pair.Client.ResolveDependency<IEntityManager>();
        var netEntity = entMan.GetNetEntity(player);
        var wrapped =
            $"[BubbleHeader][Name]QA[/Name][/BubbleHeader] says, \"[BubbleContent]{FormattedMessage.EscapeText(text)}[/BubbleContent]\"";

        return new ChatMessage(channel, text, wrapped, netEntity, null, speechTransport: speechTransport);
    }

    private static string[] ResolveExpectedSoundPaths(IPrototypeManager protoMan, SoundSpecifier sound)
    {
        return sound switch
        {
            SoundPathSpecifier path => [path.Path.ToString()],
            SoundCollectionSpecifier { Collection: not null } collection => protoMan
                .Index<SoundCollectionPrototype>(collection.Collection)
                .PickFiles
                .Select(path => path.ToString())
                .ToArray(),
            _ => []
        };
    }

    private static bool ContainsExpectedSoundPath(string entityName, IReadOnlyList<string>? expectedSoundPaths)
    {
        if (string.IsNullOrWhiteSpace(entityName))
            return false;

        if (expectedSoundPaths == null || expectedSoundPaths.Count == 0)
            return true;

        return expectedSoundPaths.Any(path => entityName.Contains(path, StringComparison.Ordinal));
    }

    private static bool MatchesDialogueBlipAudioParams(AudioParams audioParams)
    {
        return MathF.Abs(audioParams.MaxDistance - 10f) <= 0.001f &&
               MathF.Abs(audioParams.ReferenceDistance - 1f) <= 0.001f &&
               MathF.Abs(audioParams.RolloffFactor - 3f) <= 0.001f;
    }

    private static DialogueBlipExpectedModulation ResolveExpectedModulation(
        string text,
        EntityUid player,
        SpeechSoundsPrototype speechPrototype,
        VoiceTone voiceTone,
        SpeechBubble.SpeechType speechType,
        ChatSpeechTransport speechTransport)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DialogueBlipExpectedModulation(
                speechPrototype.DialogueBlipVolume,
                speechPrototype.DialogueBlipVariation,
                AudioFlags.None,
                0f);
        }

        var textElements = DialogueRevealTextElementHelper.GetTextElements(
            FormattedMessage.FromMarkupOrThrow(FormattedMessage.EscapeText(text)));
        var context = DialogueBlipModulation.BuildContext(textElements, player, speechType, speechTransport);
        var modulation = DialogueBlipModulation.GetTextElementModulation(
            context,
            FindLeadingVoicedTextElementIndex(textElements),
            1f,
            new DialogueBlipProfile(
                speechPrototype.DialogueBlipSound!,
                speechPrototype.DialogueBlipPitch,
                speechPrototype.DialogueBlipVariation,
                speechPrototype.DialogueBlipVolume,
                speechPrototype.DialogueCharsPerSecond,
                speechPrototype.DialogueCharsPerBlip,
                speechPrototype.DialoguePunctuationPause,
                voiceTone));

        return new DialogueBlipExpectedModulation(
            modulation.VolumeOffsetDb,
            modulation.Variation,
            modulation.AudioFlags,
            modulation.Occlusion);
    }

    private static int FindLeadingVoicedTextElementIndex(string[] textElements)
    {
        for (var i = 0; i < textElements.Length; i++)
        {
            if (!DialogueRevealTextElementHelper.IsSilentTextElementForDialogueBlip(textElements[i]))
                return i;
        }

        Assert.Fail("Expected at least one voiced text element.");
        return 0;
    }

    private readonly record struct DialogueBlipExpectedProfile(
        float Volume,
        float Variation,
        AudioFlags Flags,
        float Occlusion,
        string[] ExpectedSoundPaths);

    private readonly record struct DialogueBlipExpectedModulation(
        float VolumeOffsetDb,
        float Variation,
        AudioFlags Flags,
        float Occlusion);

    private readonly record struct DialogueBlipAudioSample(
        EntityUid Entity,
        string EntityName,
        AudioParams Params,
        AudioState State,
        EntityUid Parent,
        AudioFlags Flags,
        float Occlusion);
}
