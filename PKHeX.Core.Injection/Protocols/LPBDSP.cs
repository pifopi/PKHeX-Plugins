﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using static System.Buffers.Binary.BinaryPrimitives;
using static PKHeX.Core.Injection.LiveHeXVersion;

namespace PKHeX.Core.Injection;

public sealed class LPBDSP : InjectionBase
{
    public static ReadOnlySpan<LiveHeXVersion> SupportedVersions =>
    [
        BD_v100,
        BD_v110,
        BD_v111,
        BDSP_v112,
        BDSP_v113,
        BDSP_v120,
        BD_v130,
        SP_v100,
        SP_v110,
        SP_v111,
        BDSP_v112,
        BDSP_v113,
        BDSP_v120,
        SP_v130,
    ];

    private const int ITEM_BLOCK_SIZE = 0xBB80;
    private const int ITEM_BLOCK_SIZE_RAM = (0xBB80 / 0x10) * 0xC;

    private const int UG_ITEM_BLOCK_SIZE = 999 * 0xC;
    private const int UG_ITEM_BLOCK_SIZE_RAM = 999 * 0x8;

    private const int DAYCARE_BLOCK_SIZE = 0x2C0;
    private const int DAYCARE_BLOCK_SIZE_RAM = 0x8 * 4;

    private const int MYSTATUS_BLOCK_SIZE = 0x50;
    private const int MYSTATUS_BLOCK_SIZE_RAM = 0x34;

    public static readonly Dictionary<string, (Func<PokeSysBotMini, byte[]?>, Action<PokeSysBotMini, byte[]>)> FunctionMap = new()
    {
        { "Items", (GetItemBlock, SetItemBlock) },
        { "MyStatus", (GetMyStatusBlock, SetMyStatusBlock) },
        { "Underground", (GetUGItemBlock, SetUGItemBlock) },
        { "Daycare", (GetDaycareBlock, SetDaycareBlock) },
    };

    public override Dictionary<string, string> SpecialBlocks { get; } = new()
    {
        { "Items", "B_OpenItemPouch_Click" },
        { "Underground", "B_OpenUGSEditor_Click" },
    };

#pragma warning disable CS8602 // Dereference of a possibly null reference.
    public static readonly IEnumerable<Type> types = Assembly.GetAssembly(typeof(ICustomBlock)).GetTypes().Where(t => typeof(ICustomBlock).IsAssignableFrom(t) && !t.IsInterface);

    private static ulong[] GetPokemonPointers(PokeSysBotMini psb, int box)
    {
        var sb = (ICommunicatorNX)psb.com;
        var (ptr, count) = RamOffsets.BoxOffsets(psb.Version);
        var addr = sb.GetPointerAddress(ptr);
        if (addr == InjectionUtil.INVALID_PTR)
            throw new Exception("Invalid Pointer string.");

        var b = psb.com.ReadBytes(addr, count * 8);
        if (b[0] == 0)
            return [];
        var boxptr = ArrayUtil.EnumerateSplit(b.ToArray(), 8).Select(z => ReadUInt64LittleEndian(z)).ToArray()[box] + 0x20; // add 0x20 to remove vtable bytes
        b = sb.ReadBytesAbsolute(boxptr, psb.SlotCount * 8);
        var pkmptrs = ArrayUtil.EnumerateSplit(b.ToArray(), 8).Select(z => ReadUInt64LittleEndian(z)).ToArray();
        return pkmptrs;
    }

    // relative to: PlayerWork.SaveData_TypeInfo
    private static string? GetTrainerPointer(LiveHeXVersion lv) => lv switch
    {
        BD_v130   => "[[[main+4C64DC0]+B8]+10]+E0",
        SP_v130   => "[[[main+4E7BE98]+B8]+10]+E0",
        BDSP_v120 => "[[[main+4E36C58]+B8]+10]+E0",
        BDSP_v113 => "[[[main+4E59E60]+B8]+10]+E0",
        BDSP_v112 => "[[[main+4E34DD0]+B8]+10]+E0",
        BD_v111   => "[[[main+4C1DCF8]+B8]+10]+E0",
        SP_v111   => "[[[main+4E34DD0]+B8]+10]+E0",
        _ => null,
    };

    // relative to: PlayerWork.SaveData_TypeInfo
    private static string? GetItemPointers(LiveHeXVersion lv) => lv switch
    {
        BD_v130   => "[[[[main+4C64DC0]+B8]+10]+48]+20",
        SP_v130   => "[[[[main+4E7BE98]+B8]+10]+48]+20",
        BDSP_v120 => "[[[[main+4E36C58]+B8]+10]+48]+20",
        BDSP_v113 => "[[[[main+4E59E60]+B8]+10]+48]+20",
        BDSP_v112 => "[[[[main+4E34DD0]+B8]+10]+48]+20",
        BD_v111   => "[[[[main+4C1DCF8]+B8]+10]+48]+20",
        SP_v111   => "[[[[main+4E34DD0]+B8]+10]+48]+20",
        _ => null,
    };

    // relative to: PlayerWork.SaveData_TypeInfo
    private static string? GetUndergroundPointers(LiveHeXVersion lv) => lv switch
    {
        BD_v130   => "[[[[main+4C64DC0]+B8]+10]+50]+20",
        SP_v130   => "[[[[main+4E7BE98]+B8]+10]+50]+20",
        BDSP_v120 => "[[[[main+4E36C58]+B8]+10]+50]+20",
        BDSP_v113 => "[[[[main+4E59E60]+B8]+10]+50]+20",
        BDSP_v112 => "[[[[main+4E34DD0]+B8]+10]+50]+20",
        BD_v111   => "[[[[main+4C1DCF8]+B8]+10]+50]+20",
        SP_v111   => "[[[[main+4E34DD0]+B8]+10]+50]+20",
        _ => null,
    };

    // relative to: PlayerWork.SaveData_TypeInfo
    private static string? GetDaycarePointers(LiveHeXVersion lv) => lv switch
    {
        BD_v130   => "[[[main+4C64DC0]+B8]+10]+450",
        SP_v130   => "[[[main+4E7BE98]+B8]+10]+450",
        BDSP_v120 => "[[[main+4E36C58]+B8]+10]+450",
        BDSP_v113 => "[[[main+4E59E60]+B8]+10]+450",
        BDSP_v112 => "[[[main+4E34DD0]+B8]+10]+450",
        BD_v111   => "[[[main+4C1DCF8]+B8]+10]+450",
        SP_v111   => "[[[main+4E34DD0]+B8]+10]+450",
        _ => null,
    };

    public override Span<byte> ReadBox(PokeSysBotMini psb, int box, int _, List<byte[]> allpkm)
    {
        if (psb.com is not ICommunicatorNX sb)
            return ArrayUtil.ConcatAll(allpkm.ToArray());

        var pkmptrs = GetPokemonPointers(psb, box);
        if (pkmptrs.Length == 0)
            return ArrayUtil.ConcatAll(allpkm.ToArray());
        var offsets = pkmptrs.ToDictionary(p => p + 0x20, _ => psb.SlotSize);
        return sb.ReadBytesAbsoluteMulti(offsets);
    }

    public override Span<byte> ReadSlot(PokeSysBotMini psb, int box, int slot)
    {
        if (psb.com is not ICommunicatorNX sb)
            return new byte[psb.SlotSize];

        var pkmptrs = GetPokemonPointers(psb, box);
        if (pkmptrs.Length == 0)
            return [];
        var pkmptr = pkmptrs[slot];
        return sb.ReadBytesAbsolute(pkmptr + 0x20, psb.SlotSize);
    }

    public override void SendSlot(PokeSysBotMini psb, ReadOnlySpan<byte> data, int box, int slot)
    {
        if (psb.com is not ICommunicatorNX sb)
            return;

        var pkmptrs = GetPokemonPointers(psb, box);
        if (pkmptrs.Length == 0)
            return;
        var pkmptr = pkmptrs[slot];
        sb.WriteBytesAbsolute(data, pkmptr + 0x20);
    }

    public override void SendBox(PokeSysBotMini psb, ReadOnlySpan<byte> boxData, int box)
    {
        if (psb.com is not ICommunicatorNX sb)
            return;

        int size = psb.SlotSize;
        var pkmptrs = GetPokemonPointers(psb, box);
        if (pkmptrs.Length == 0)
            return;
        for (int i = 0; i < psb.SlotCount; i++)
            sb.WriteBytesAbsolute(boxData.Slice(i * size, size), pkmptrs[i] + 0x20);
    }

    public static readonly Func<PokeSysBotMini, byte[]?> GetTrainerData = psb =>
    {
        var lv = psb.Version;
        var ptr = GetTrainerPointer(lv);
        if (ptr is null || psb.com is not ICommunicatorNX sb)
            return null;

        var retval = new byte[MYSTATUS_BLOCK_SIZE];
        var ram_block = sb.GetPointerAddress(ptr);
        if (ram_block == InjectionUtil.INVALID_PTR)
            throw new Exception("Invalid Pointer string.");

        var trainer_name = ptr.ExtendPointer(0x14);
        var trainer_name_addr = sb.GetPointerAddress(trainer_name);
        if (trainer_name_addr == InjectionUtil.INVALID_PTR)
            throw new Exception("Invalid Pointer string.");

        psb.com.ReadBytes(trainer_name_addr, 0x1A).CopyTo(retval.AsSpan());

        var extra = psb.com.ReadBytes(ram_block, MYSTATUS_BLOCK_SIZE_RAM);
        // TID, SID, Money, Male
        extra.Slice(0x8, 0x9).CopyTo(retval.AsSpan(0x1C));
        // Region Code, Badge Count, TrainerView, ROMCode, GameClear
        extra.Slice(0x11, 0x5).CopyTo(retval.AsSpan(0x28));
        // BodyType, Fashion ID
        extra.Slice(0x16, 0x2).CopyTo(retval.AsSpan(0x30));
        // StarterType, DSPlayer, FollowIndex, X, Y, Height, Rotation
        extra[0x18..].ToArray().CopyTo(retval, 0x34);

        return retval;
    };

    private static byte[]? GetItemBlock(PokeSysBotMini psb)
    {
        var ptr = GetItemPointers(psb.Version);
        if (ptr is null)
            return null;

        var nx = (ICommunicatorNX)psb.com;
        var addr = nx.GetPointerAddress(ptr);
        if (addr == InjectionUtil.INVALID_PTR)
            throw new Exception("Invalid Pointer string.");

        var item_blk = psb.com.ReadBytes(addr, ITEM_BLOCK_SIZE_RAM);
        var items = ArrayUtil.EnumerateSplit(item_blk.ToArray(), 0xC).Select(z =>
        {
            var retval = new byte[0x10];
            var zSpan = z.AsSpan();
            var rSpan = retval.AsSpan();
            zSpan[..0x5].CopyTo(rSpan);
            zSpan[0x5..0x6].CopyTo(rSpan[0x8..]);
            zSpan[0xA..].CopyTo(rSpan[0xC..]);
            return retval;
        }).ToArray();
        return ArrayUtil.ConcatAll(items);
    }

    private static void SetItemBlock(PokeSysBotMini psb, byte[] data)
    {
        var ptr = GetItemPointers(psb.Version);
        if (ptr is null)
            return;

        var nx = (ICommunicatorNX)psb.com;
        var addr = nx.GetPointerAddress(ptr);
        if (addr == InjectionUtil.INVALID_PTR)
            throw new Exception("Invalid Pointer string.");

        data = data.AsSpan(0, ITEM_BLOCK_SIZE).ToArray();
        var items = ArrayUtil.EnumerateSplit(data, 0x10).Select(z =>
        {
            var retval = new byte[0xC];
            var zSpan = z.AsSpan();
            var rSpan = retval.AsSpan();
            zSpan[..0x5].CopyTo(rSpan);
            zSpan[0x8..0x9].CopyTo(rSpan[0x5..]);
            zSpan[0xC..0xE].CopyTo(rSpan[0xA..]);
            return retval;
        }).ToArray();
        var payload = ArrayUtil.ConcatAll(items);
        psb.com.WriteBytes(payload, addr);
    }

    private static byte[]? GetUGItemBlock(PokeSysBotMini psb)
    {
        var ptr = GetUndergroundPointers(psb.Version);
        if (ptr is null)
            return null;

        var nx = (ICommunicatorNX)psb.com;
        var addr = nx.GetPointerAddress(ptr);
        if (addr == InjectionUtil.INVALID_PTR)
            throw new Exception("Invalid Pointer string.");

        var item_blk = psb.com.ReadBytes(addr, UG_ITEM_BLOCK_SIZE_RAM);

        // 8 byte entries need to be expanded to 12 bytes
        var result = new byte[12 * (item_blk.Length / 8)];
        for (int i = 0, j = 0; i < item_blk.Length; i += 8, j += 12)
        {
            var src = item_blk.Slice(i, 8);
            var dest = result.AsSpan(j, 12);
            src.CopyTo(dest);
        }
        return result;
    }

    private static void SetUGItemBlock(PokeSysBotMini psb, byte[] data)
    {
        var ptr = GetUndergroundPointers(psb.Version);
        if (ptr is null)
            return;

        var nx = (ICommunicatorNX)psb.com;
        var addr = nx.GetPointerAddress(ptr);
        if (addr == InjectionUtil.INVALID_PTR)
            throw new Exception("Invalid Pointer string.");

        data = data.AsSpan(0, UG_ITEM_BLOCK_SIZE).ToArray();
        var items = ArrayUtil.EnumerateSplit(data, 0xC).Select(z => z.AsSpan(0, 0x8).ToArray()).ToArray();
        var payload = ArrayUtil.ConcatAll(items);
        psb.com.WriteBytes(payload, addr);
    }

    private static byte[]? GetMyStatusBlock(PokeSysBotMini psb) => GetTrainerData(psb);

    private static void SetMyStatusBlock(PokeSysBotMini psb, byte[] data)
    {
        var lv = psb.Version;
        var ptr = GetTrainerPointer(lv);
        if (ptr is null || psb.com is not ICommunicatorNX sb)
            return;

        data = data.AsSpan(0, MYSTATUS_BLOCK_SIZE).ToArray();
        var trainer_name = ptr.ExtendPointer(0x14);
        var trainer_name_addr = sb.GetPointerAddress(trainer_name);
        if (trainer_name_addr == InjectionUtil.INVALID_PTR)
            throw new Exception("Invalid Pointer string.");

        var retval = new byte[MYSTATUS_BLOCK_SIZE_RAM];
        // TID, SID, Money, Male
        data.AsSpan(0x1C, 0x9).CopyTo(retval.AsSpan(0x8));
        // Region Code, Badge Count, TrainerView, ROMCode, GameClear
        data.AsSpan(0x28, 0x5).CopyTo(retval.AsSpan(0x11));
        // BodyType, Fashion ID
        data.AsSpan(0x30, 0x2).CopyTo(retval.AsSpan(0x16));
        // StarterType, DSPlayer, FollowIndex, X, Y, Height, Rotation
        data.AsSpan(0x34).ToArray().CopyTo(retval, 0x18);

        psb.com.WriteBytes(data.AsSpan(0, 0x1A), trainer_name_addr);
        psb.com.WriteBytes(retval.AsSpan(0x8).ToArray(), sb.GetPointerAddress(ptr) + 0x8);
    }

    private static byte[]? GetDaycareBlock(PokeSysBotMini psb)
    {
        var ptr = GetDaycarePointers(psb.Version);
        if (ptr is null)
            return null;

        var nx = (ICommunicatorNX)psb.com;
        var addr = nx.GetPointerAddress(ptr);
        var p1ptr = nx.GetPointerAddress(ptr.ExtendPointer(0x20, 0x20));
        var p2ptr = nx.GetPointerAddress(ptr.ExtendPointer(0x28, 0x20));
        var parent_one = psb.com.ReadBytes(p1ptr, 0x158);
        var parent_two = psb.com.ReadBytes(p2ptr, 0x158);
        var extra = psb.com.ReadBytes(addr + 0x8, 0x18);
        var extra_arr = ArrayUtil.EnumerateSplit(extra.ToArray(), 0x8).ToArray();
        var block = new byte[DAYCARE_BLOCK_SIZE];
        parent_one.CopyTo(block);
        parent_two.CopyTo(block.AsSpan(0x158));
        extra_arr[0].AsSpan(0, 4).CopyTo(block.AsSpan(0x158 * 2));
        extra_arr[1].CopyTo(block, (0x158 * 2) + 0x4);
        extra_arr[2].AsSpan(0, 4).CopyTo(block.AsSpan((0x158 * 2) + 0x4 + 0x8));
        return block;
    }

    private static void SetDaycareBlock(PokeSysBotMini psb, byte[] data)
    {
        var ptr = GetDaycarePointers(psb.Version);
        if (ptr is null)
            return;

        var nx = (ICommunicatorNX)psb.com;
        var addr = nx.GetPointerAddress(ptr);
        var parent_one_addr = nx.GetPointerAddress(ptr.ExtendPointer(0x20, 0x20));
        var parent_two_addr = nx.GetPointerAddress(ptr.ExtendPointer(0x28, 0x20));

        data = data.AsSpan(0, DAYCARE_BLOCK_SIZE).ToArray();
        psb.com.WriteBytes(data.AsSpan(0, 0x158), parent_one_addr);
        psb.com.WriteBytes(data.AsSpan(0x158, 0x158), parent_two_addr);

        var payload = new byte[DAYCARE_BLOCK_SIZE_RAM - 0x8];
        data.AsSpan(0x158 * 2, 4).CopyTo(payload.AsSpan());
        data.AsSpan((0x158 * 2) + 0x4, 0x8).CopyTo(payload.AsSpan( 0x8));
        data.AsSpan((0x158 * 2) + 0x4 + 0x8, 0x4).CopyTo(payload.AsSpan(0x8 * 2));
        psb.com.WriteBytes(payload, addr + 0x8);
    }

    public override bool ReadBlockFromString(PokeSysBotMini psb, SaveFile sav, string block, out List<byte[]>? read)
    {
        read = null;
        if (!FunctionMap.TryGetValue(block, out (Func<PokeSysBotMini, byte[]?>, Action<PokeSysBotMini, byte[]>) value))
        {
            // Check for custom blocks
            foreach (Type t in types)
            {
                if (t.Name != block)
                    continue;

                var m = t.GetMethod("Getter", BindingFlags.Public | BindingFlags.Static);
                if (m is null)
                    return false;

                var funcout = (byte[]?)m.Invoke(null, [psb]);
                if (funcout is not null)
                    read = [funcout];

                return true;
            }
            return false;
        }
        try
        {
            var data = (sav.GetType().GetProperty(block) ?? throw new Exception("Invalid Block")).GetValue(sav);

            if (data is IDataIndirect sb)
            {
                var getter = value.Item1;
                var funcout = getter.Invoke(psb);
                if (funcout is null)
                    return false;

                funcout.CopyTo(sb.Data);
                read = [funcout];
            }
            else
            {
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.StackTrace);
            return false;
        }
    }

    public override void WriteBlockFromString(PokeSysBotMini psb, string block, ReadOnlySpan<byte> data, object sb)
    {
        if (!FunctionMap.TryGetValue(block, out (Func<PokeSysBotMini, byte[]?>, Action<PokeSysBotMini, byte[]>) value))
        {
            // Custom Blocks
            ((ICustomBlock)sb).Setter(psb, data);
            return;
        }
        var setter = value.Item2;
        setter.Invoke(psb, [.. data]);
    }
}
