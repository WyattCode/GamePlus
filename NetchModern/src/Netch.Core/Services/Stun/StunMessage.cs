using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace Netch.Core.Services.Stun;

/// <summary>
///     Minimal STUN (RFC 5389 / RFC 3489) message encoder/decoder — just enough for NAT discovery:
///     Binding requests with an optional CHANGE-REQUEST, and parsing of (XOR-)MAPPED-ADDRESS and
///     OTHER/CHANGED-ADDRESS.
/// </summary>
public static class StunMessage
{
    public const uint MagicCookie = 0x2112A442;

    private const ushort BindingRequest = 0x0001;
    private const ushort BindingResponse = 0x0101;

    private const ushort AttrMappedAddress = 0x0001;
    private const ushort AttrChangeRequest = 0x0003;
    private const ushort AttrChangedAddress = 0x0005;
    private const ushort AttrXorMappedAddress = 0x0020;
    private const ushort AttrOtherAddress = 0x802C;

    public static byte[] BuildBindingRequest(byte[] transactionId, bool changeIp, bool changePort)
    {
        var attrLen = (changeIp || changePort) ? 8 : 0; // CHANGE-REQUEST is 4-byte value + 4-byte header
        var buffer = new byte[20 + attrLen];

        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0), BindingRequest);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), (ushort)attrLen);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4), MagicCookie);
        transactionId.CopyTo(buffer, 8);

        if (attrLen > 0)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(20), AttrChangeRequest);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(22), 4);
            var flags = 0u;
            if (changeIp) flags |= 0x4;
            if (changePort) flags |= 0x2;
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(24), flags);
        }

        return buffer;
    }

    public static byte[] NewTransactionId()
    {
        var id = new byte[12];
        RandomNumberGenerator.Fill(id);
        return id;
    }

    public sealed record Response(IPEndPoint? MappedAddress, IPEndPoint? OtherAddress);

    public static Response? Parse(ReadOnlySpan<byte> data, byte[] transactionId)
    {
        if (data.Length < 20)
            return null;

        var type = BinaryPrimitives.ReadUInt16BigEndian(data);
        if (type != BindingResponse)
            return null;

        var length = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        if (!data.Slice(8, 12).SequenceEqual(transactionId))
            return null;

        IPEndPoint? mapped = null;
        IPEndPoint? other = null;

        var offset = 20;
        var end = 20 + length;
        while (offset + 4 <= data.Length && offset + 4 <= end)
        {
            var attrType = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            var attrLen = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            var valueStart = offset + 4;
            if (valueStart + attrLen > data.Length)
                break;

            var value = data.Slice(valueStart, attrLen);
            switch (attrType)
            {
                case AttrXorMappedAddress:
                    mapped = ParseAddress(value, xor: true, transactionId);
                    break;
                case AttrMappedAddress:
                    mapped ??= ParseAddress(value, xor: false, transactionId);
                    break;
                case AttrOtherAddress:
                case AttrChangedAddress:
                    other = ParseAddress(value, xor: false, transactionId);
                    break;
            }

            offset = valueStart + attrLen + (4 - attrLen % 4) % 4; // 4-byte alignment
        }

        return new Response(mapped, other);
    }

    private static IPEndPoint? ParseAddress(ReadOnlySpan<byte> value, bool xor, byte[] transactionId)
    {
        if (value.Length < 8)
            return null;

        var family = value[1];
        if (family != 0x01) // IPv4 only
            return null;

        var port = BinaryPrimitives.ReadUInt16BigEndian(value[2..]);
        Span<byte> addr = stackalloc byte[4];
        value.Slice(4, 4).CopyTo(addr);

        if (xor)
        {
            port ^= (ushort)(MagicCookie >> 16);
            Span<byte> cookie = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(cookie, MagicCookie);
            for (var i = 0; i < 4; i++)
                addr[i] ^= cookie[i];
        }

        return new IPEndPoint(new IPAddress(addr), port);
    }
}
