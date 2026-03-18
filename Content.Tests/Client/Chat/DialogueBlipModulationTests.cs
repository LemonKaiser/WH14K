using Content.Client.Chat.UI;
using Content.Shared.Chat;
using Content.Shared.Speech;
using NUnit.Framework;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Tests.Client.Chat;

[TestFixture]
public sealed class DialogueBlipModulationTests
{
    private static readonly DialogueBlipProfile BaseProfile = new(
        new SoundPathSpecifier("/Audio/Voice/Talk/speak_2.ogg"),
        1f,
        0.05f,
        -11f,
        30f,
        2,
        0.04f,
        VoiceTone.Normal);

    [Test]
    public void QuestionEndingRaisesPitchAgainstPeriod()
    {
        var period = GetTrailingVoicedModulation("hello.");
        var question = GetTrailingVoicedModulation("hello?");

        Assert.That(question.PitchScale, Is.GreaterThan(period.PitchScale));
    }

    [Test]
    public void CapsAndExclamationIncreaseIntensity()
    {
        var neutral = GetTrailingVoicedModulation("hello.");
        var emphatic = GetTrailingVoicedModulation("HELLO!");

        Assert.Multiple(() =>
        {
            Assert.That(emphatic.PitchScale, Is.GreaterThan(neutral.PitchScale));
            Assert.That(emphatic.VolumeOffsetDb, Is.GreaterThan(neutral.VolumeOffsetDb));
            Assert.That(emphatic.CharactersPerBlip, Is.LessThanOrEqualTo(neutral.CharactersPerBlip));
        });
    }

    [Test]
    public void EllipsisLowersPitchAndSlowsCadence()
    {
        var period = GetTrailingVoicedModulation("hello.");
        var ellipsis = GetTrailingVoicedModulation("hello...");

        Assert.Multiple(() =>
        {
            Assert.That(ellipsis.PitchScale, Is.LessThan(period.PitchScale));
            Assert.That(ellipsis.DelayMultiplier, Is.GreaterThan(period.DelayMultiplier));
            Assert.That(ellipsis.CharactersPerBlip, Is.GreaterThanOrEqualTo(period.CharactersPerBlip));
        });
    }

    [Test]
    public void FasterRevealTightensCadence()
    {
        var slower = GetTrailingVoicedModulation("steady text.", speedScale: 0.6f);
        var faster = GetTrailingVoicedModulation("steady text.", speedScale: 1.8f);

        Assert.Multiple(() =>
        {
            Assert.That(faster.CharactersPerBlip, Is.LessThanOrEqualTo(slower.CharactersPerBlip));
            Assert.That(faster.DelayMultiplier, Is.LessThan(slower.DelayMultiplier));
        });
    }

    [Test]
    public void RadioTransportAppliesFilterAndVolumeCut()
    {
        var direct = GetTrailingVoicedModulation("check one two.");
        var radio = GetTrailingVoicedModulation("check one two.", transport: ChatSpeechTransport.Radio);

        Assert.Multiple(() =>
        {
            Assert.That((radio.AudioFlags & AudioFlags.NoOcclusion) != 0, Is.True);
            Assert.That(radio.Occlusion, Is.GreaterThan(0.2f));
            Assert.That(radio.VolumeOffsetDb, Is.LessThan(direct.VolumeOffsetDb));
        });
    }

    [Test]
    public void WhisperAndRadioContextsCombine()
    {
        var whisper = GetTrailingVoicedModulation("quiet check.", speechType: SpeechBubble.SpeechType.Whisper);
        var whisperRadio = GetTrailingVoicedModulation("quiet check.", SpeechBubble.SpeechType.Whisper, ChatSpeechTransport.Radio);

        Assert.Multiple(() =>
        {
            Assert.That((whisperRadio.AudioFlags & AudioFlags.NoOcclusion) != 0, Is.True);
            Assert.That(whisperRadio.Occlusion, Is.GreaterThan(0.2f));
            Assert.That(whisperRadio.VolumeOffsetDb, Is.LessThan(whisper.VolumeOffsetDb));
            Assert.That(whisperRadio.CharactersPerBlip, Is.GreaterThanOrEqualTo(whisper.CharactersPerBlip));
        });
    }

    [Test]
    public void VoiceToneChangesPitchAndCadence()
    {
        var low = GetTrailingVoicedModulation("steady text.", voiceTone: VoiceTone.Low);
        var high = GetTrailingVoicedModulation("steady text.", voiceTone: VoiceTone.High);

        Assert.Multiple(() =>
        {
            Assert.That(high.PitchScale, Is.GreaterThan(low.PitchScale));
            Assert.That(high.DelayMultiplier, Is.LessThan(low.DelayMultiplier));
            Assert.That(high.CharactersPerBlip, Is.LessThanOrEqualTo(low.CharactersPerBlip));
        });
    }

    private static DialogueBlipTextElementModulation GetTrailingVoicedModulation(
        string text,
        SpeechBubble.SpeechType speechType = SpeechBubble.SpeechType.Say,
        ChatSpeechTransport transport = ChatSpeechTransport.Direct,
        float speedScale = 1f,
        VoiceTone voiceTone = VoiceTone.Normal,
        int senderId = 42)
    {
        var message = FormattedMessage.FromMarkupOrThrow(FormattedMessage.EscapeText(text));
        var textElements = DialogueRevealTextElementHelper.GetTextElements(message);
        var context = DialogueBlipModulation.BuildContext(textElements, new EntityUid(senderId), speechType, transport);
        var voicedIndex = FindTrailingVoicedIndex(textElements);
        return DialogueBlipModulation.GetTextElementModulation(
            context,
            voicedIndex,
            speedScale,
            BaseProfile with { VoiceTone = voiceTone });
    }

    private static int FindTrailingVoicedIndex(string[] textElements)
    {
        for (var i = textElements.Length - 1; i >= 0; i--)
        {
            if (!DialogueRevealTextElementHelper.IsSilentTextElementForDialogueBlip(textElements[i]))
                return i;
        }

        Assert.Fail("Expected the message to contain at least one voiced text element.");
        return 0;
    }
}
