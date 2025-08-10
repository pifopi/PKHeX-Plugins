﻿using FluentAssertions;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using Xunit;

namespace AutoModTests;

public static class ShowdownSetGenningTests
{
    static ShowdownSetGenningTests() => TestUtil.InitializePKHeXEnvironment();

    [Theory]
    [InlineData(GameVersion.US, Meowstic)]
    [InlineData(GameVersion.US, Darkrai)]
    [InlineData(GameVersion.B2, Genesect)]
    public static void VerifyManually(GameVersion game, string txt)
    {
        var dev = APILegality.EnableDevMode;
        APILegality.EnableDevMode = true;

        var sav = BlankSaveFile.Get(game, "ALM");
        TrainerSettings.Register(sav);

        var trainer = TrainerSettings.GetSavedTrainerData(game);
        RecentTrainerCache.SetRecentTrainer(trainer);

        var set = new ShowdownSet(txt);
        var almres = sav.GetLegalFromSet(set);
        APILegality.EnableDevMode = dev;

        var la = new LegalityAnalysis(almres.Created);
        la.Valid.Should().BeTrue();
    }

    private const string Darkrai =
        @"Darkrai
IVs: 7 Atk
Ability: Bad Dreams
Shiny: Yes
Timid Nature
- Hypnosis
- Feint Attack
- Nightmare
- Double Team";

    private const string Genesect =
        @"Genesect
Ability: Download
Shiny: Yes
Hasty Nature
- Extreme Speed
- Techno Blast
- Blaze Kick
- Shift Gear";

    private const string Meowstic =
        @"Meowstic-F @ Life Orb
Ability: Competitive
EVs: 4 Def / 252 SpA / 252 Spe
Timid Nature
- Psyshock
- Signal Beam
- Hidden Power Ground
- Calm Mind";
}
