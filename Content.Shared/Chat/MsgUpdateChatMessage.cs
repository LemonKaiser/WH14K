using System.IO;
using JetBrains.Annotations;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Chat;

/// <summary>
///     Sent from server to client to replace an earlier chat line with the same <see cref="ChatMessage.ServerMessageId"/>.
/// </summary>
[UsedImplicitly]
public sealed class MsgUpdateChatMessage : NetMessage
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
        var length = (int) stream.Length;
        buffer.WriteVariableInt32(length);
        stream.TryGetBuffer(out var segment);
        buffer.Write(segment);
    }
}
