﻿using System;
using System.Collections.Generic;

namespace PKHeX.Core.Injection;

public sealed class PokeSysBotMini(LiveHeXVersion lv, ICommunicator communicator) : InjectionBase
{
    public readonly long BoxStart = RamOffsets.GetB1S1Offset(lv);
    public readonly int SlotSize = RamOffsets.GetSlotSize(lv);
    public readonly int SlotCount = RamOffsets.GetSlotCount(lv);
    public readonly int GapSize = RamOffsets.GetGapSize(lv);
    public readonly LiveHeXVersion Version = lv;
    public readonly ICommunicator com = communicator;
    public readonly InjectionBase Injector = GetInjector(lv);
    public bool Connected => com.Connected;

    public ulong GetSlotOffset(int box, int slot) => GetBoxOffset(box) + (ulong)((SlotSize + GapSize) * slot);

    public ulong GetBoxOffset(int box) => (ulong)BoxStart + (ulong)((SlotSize + GapSize) * SlotCount * box);

    public Span<byte> ReadSlot(int box, int slot) => Injector.ReadSlot(this, box, slot);

    public Span<byte> ReadBox(int box, int len)
    {
        var allpkm = new List<byte[]>();
        return Injector.ReadBox(this, box, len, allpkm);
    }

    public void SendSlot(ReadOnlySpan<byte> data, int box, int slot) => Injector.SendSlot(this, data, box, slot);

    public void SendBox(ReadOnlySpan<byte> boxData, int box)
    {
        for (int i = 0; i < SlotCount; i++)
            SendSlot(boxData.Slice(i * SlotSize, SlotSize), box, i);
        Injector.SendBox(this, boxData, box);
    }

    public Span<byte> ReadOffset(ulong offset) => com.ReadBytes(offset, SlotSize);
}
