﻿using FluentAssertions;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using Xunit;
using static PKHeX.Core.Species;

namespace AutoModTests;

public static class LogicTests
{
    [Theory]
    [InlineData(Pikachu, 1)]
    public static void TestShinyLockInfo(Species species, byte form, bool locked = true)
    {
        var result = SimpleEdits.IsShinyLockedSpeciesForm((ushort)species, form);
        result.Should().Be(locked);
    }
}
