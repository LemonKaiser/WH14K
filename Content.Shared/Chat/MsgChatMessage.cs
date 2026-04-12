using System.IO;
using JetBrains.Annotations;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Chat
{
    [Serializable, NetSerializable]
    public sealed class ChatMessage
    {
        public ChatChannel Channel;

        /// <summary>
        /// This is the text spoken by the entity, after accents and such were applied.
        /// This should have <see cref="FormattedMessage.EscapeText"/> applied before using it in any rich text box.
        /// </summary>
        public string Message;

        /// <summary>
        /// This is the <see cref="Message"/> but with special characters escaped and wrapped in some rich text
        /// formatting tags.
        /// </summary>
        public string WrappedMessage;

        public NetEntity SenderEntity;

        /// <summary>
        ///     Identifier sent when <see cref="SenderEntity"/> is <see cref="NetEntity.Invalid"/>
        ///     if this was sent by a player to assign a key to the sender of this message.
        ///     This is unique per sender.
        /// </summary>
        public int? SenderKey;

        public bool HideChat;
        public Color? MessageColorOverride;
        public string? AudioPath;
        public float AudioVolume;

        /// <summary>
        /// Optional sender role icon id resolved server-side.
        /// Used by clients to render role icon even when sender entity data is not yet replicated.
        /// </summary>
        public string? SenderJobIconId;

        /// <summary>
        /// Optional sender grammar proper-noun flag resolved server-side.
        /// Used by clients for deterministic name-coloring of early round messages.
        /// </summary>
        public bool? SenderNameIsProperNoun;

        /// <summary>
        /// How this spoken line reached the local listener.
        /// Used by client-side speech bubble systems to apply context-aware playback.
        /// </summary>
        public ChatSpeechTransport SpeechTransport;

        /// <summary>
        /// Optional transient id assigned by the server for late in-place updates.
        /// Used by delayed translation to replace the original line after fallback send.
        /// </summary>
        public uint? ServerMessageId;

        [NonSerialized]
        public bool Read;

        public ChatMessage(
            ChatChannel channel,
            string message,
            string wrappedMessage,
            NetEntity source,
            int? senderKey,
            bool hideChat = false,
            Color? colorOverride = null,
            string? audioPath = null,
            float audioVolume = 0,
            string? senderJobIconId = null,
            bool? senderNameIsProperNoun = null,
            ChatSpeechTransport speechTransport = ChatSpeechTransport.Direct,
            uint? serverMessageId = null)
        {
            Channel = channel;
            Message = message;
            WrappedMessage = wrappedMessage;
            SenderEntity = source;
            SenderKey = senderKey;
            HideChat = hideChat;
            MessageColorOverride = colorOverride;
            AudioPath = audioPath;
            AudioVolume = audioVolume;
            SenderJobIconId = senderJobIconId;
            SenderNameIsProperNoun = senderNameIsProperNoun;
            SpeechTransport = speechTransport;
            ServerMessageId = serverMessageId;
        }
    }

    /// <summary>
    ///     Sent from server to client to notify the client about a new chat message.
    /// </summary>
    [UsedImplicitly]
    public sealed class MsgChatMessage : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.Command;

        public ChatMessage Message = default!;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var length = buffer.ReadVariableInt32();
            using var stream = new MemoryStream(length);
            buffer.ReadAlignedMemory(stream, length);
            serializer.DeserializeDirect(stream, out Message);
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            var stream = new MemoryStream();
            serializer.SerializeDirect(stream, Message);
            buffer.WriteVariableInt32((int) stream.Length);
            buffer.Write(stream.AsSpan());
        }
    }
}
