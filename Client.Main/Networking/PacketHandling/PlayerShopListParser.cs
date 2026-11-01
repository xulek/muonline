#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Client.Main.Core.Utilities;

namespace Client.Main.Networking.PacketHandling
{
    internal sealed record PlayerShopList(ushort OwnerId, string OwnerName, string ShopName,
        bool IsUpdate, IReadOnlyList<(byte Slot, byte[] ItemData, uint Price)> Items);

    internal sealed record PlayerShopBuyResultData(ushort SellerId, byte Result, byte InventorySlot, byte[] ItemData)
    {
        public bool Success => Result == 1;
    }

    internal static class PlayerShopBuyResultParser
    {
        public static bool TryParse(ReadOnlySpan<byte> data, bool extended, out PlayerShopBuyResultData? result)
        {
            result = null;
            if (data.Length < (extended ? 8 : 21) || data[0] != 0xC1 || data[1] != data.Length || data[2] != 0x3F || data[3] != 0x06)
                return false;
            ushort seller = extended ? BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) : BinaryPrimitives.ReadUInt16BigEndian(data[5..]);
            byte status = data[extended ? 6 : 4];
            if (status != 1)
            {
                result = new PlayerShopBuyResultData(seller, status, 0, Array.Empty<byte>());
                return true;
            }
            if (extended)
            {
                if (!ItemDataParser.TryGetExtendedItemLength(data[8..], out int size) ||
                    (size != data.Length - 8 && (data.Length - 8 != 15 || data[(8 + size)..].IndexOfAnyExcept((byte)0) >= 0)))
                    return false;
                result = new PlayerShopBuyResultData(seller, status, data[7], data.Slice(8, size).ToArray());
            }
            else
            {
                if (data.Length != 21) return false;
                result = new PlayerShopBuyResultData(seller, status, data[20], data.Slice(7, 12).ToArray());
            }
            return true;
        }
    }

    internal static class PlayerShopListParser
    {
        public static bool TryParse(ReadOnlySpan<byte> data, bool extended, out PlayerShopList? shop)
        {
            shop = null;
            if (data.Length < 55 || data[0] != 0xC2 || BinaryPrimitives.ReadUInt16BigEndian(data[1..]) != data.Length ||
                data[3] != 0x3F || (data[4] != 0x05 && data[4] != 0x13) || data[5] == 0)
                return false;

            if (data[54] > 32) return false;
            var items = new List<(byte Slot, byte[] ItemData, uint Price)>(data[54]);
            var slots = new HashSet<byte>();
            int offset = 55;
            for (int i = 0; i < data[54]; i++)
            {
                if (extended)
                {
                    if (data.Length - offset < 14 || BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 4)..]) != 0 ||
                        BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 6)..]) != 0 ||
                        !ItemDataParser.TryGetExtendedItemLength(data[(offset + 9)..], out int size) ||
                        data.Length - offset < 9 + size)
                        return false;
                    if (!slots.Add(data[offset + 8])) return false;
                    items.Add((data[offset + 8], data.Slice(offset + 9, size).ToArray(),
                        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..])));
                    offset += 9 + size;
                }
                else
                {
                    if (data.Length - offset < 20) return false;
                    if (!slots.Add(data[offset])) return false;
                    items.Add((data[offset], data.Slice(offset + 1, 12).ToArray(),
                        BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 16)..])));
                    offset += 20;
                }
            }
            if (offset != data.Length) return false;

            shop = new PlayerShopList(BinaryPrimitives.ReadUInt16BigEndian(data[6..]),
                ReadString(data.Slice(8, 10)), ReadString(data.Slice(18, 36)), data[4] == 0x13, items);
            return true;
        }

        private static string ReadString(ReadOnlySpan<byte> data)
        {
            int terminator = data.IndexOf((byte)0);
            return Encoding.UTF8.GetString(terminator < 0 ? data : data[..terminator]);
        }
    }
}
