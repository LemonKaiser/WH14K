using System.IO;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Administration.ScreenCheck;

public sealed class MsgScreenCheckRequest : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public uint RequestId;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        RequestId = buffer.ReadUInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(RequestId);
    }
}

public sealed class MsgScreenCheckResponse : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public uint RequestId;
    public bool Success;
    public byte[]? ImageData;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        RequestId = buffer.ReadUInt32();
        Success = buffer.ReadBoolean();

        if (!buffer.ReadBoolean())
        {
            ImageData = null;
            return;
        }

        var length = buffer.ReadVariableInt32();
        if (length <= 0 || length > ScreenCheckImageValidator.MaxImageBytes)
            throw new InvalidDataException($"Invalid screencheck image length: {length}.");

        ImageData = new byte[length];
        buffer.ReadBytes(ImageData, 0, length);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(RequestId);
        buffer.Write(Success);
        buffer.Write(ImageData != null);

        if (ImageData == null)
            return;

        if (ImageData.Length <= 0 || ImageData.Length > ScreenCheckImageValidator.MaxImageBytes)
            throw new InvalidDataException($"Invalid screencheck image length: {ImageData.Length}.");

        buffer.WriteVariableInt32(ImageData.Length);
        buffer.Write(ImageData);
    }
}
