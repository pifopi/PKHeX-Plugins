﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using PKHeX.Core;
using PKHeX.Core.AutoMod;

namespace AutoModPlugins;

public class PluginSettings
{
    private const string Trainer = nameof(Trainer);
    private const string Connection = nameof(Connection);
    private const string Customization = nameof(Customization);
    private const string Legality = nameof(Legality);
    private const string LivingDex = nameof(LivingDex);
    private const string TransferDex = nameof(TransferDex);
    private const string Miscellaneous = nameof(Miscellaneous);
    private const string Development = nameof(Development);

    private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new() { WriteIndented = true };

    [Browsable(false)]
    public string? ConfigPath { get; init; }

    // Trainer
    [Category(Trainer)]
    [Description("Allows overriding trainer data with \"OT\", \"TID\", \"SID\", and \"OTGender\" as part of a Showdown set.")]
    public bool AllowTrainerOverride { get; set; } = true;

    [Category(Trainer)]
    [Description("Enables use of custom trainer data based on the \"trainers\" folder.")]
    public bool UseTrainerFolder { get; set; } = true;

    [Category(Trainer)]
    [Description("Default OT Name to use while generating Pokémon.")]
    public string DefaultOT { get; set; } = "ALM";

    [Category(Trainer)]
    [Description("Default TID to use while generating Pokémon. (TID16)")]
    public ushort DefaultTID16 { get; set; } = 54321;

    [Category(Trainer)]
    [Description("Default SID to use while generating Pokémon. (SID16)")]
    public ushort DefaultSID16 { get; set; } = 12345;

    // Connection
    [Category(Connection)]
    [Description("Stores the last IP used by LiveHeX.")]
    public string LatestIP { get; set; } = "192.168.1.65";

    [Category(Connection)]
    [Description("Stores the last port used by LiveHeX.")]
    public string LatestPort { get; set; } = "6000";

    [Category(Connection)]
    [Description("Allows LiveHeX to use USB-Botbase instead of sys-botbase.")]
    public bool USBBotBasePreferred { get; set; }


    // Customization
    [Category(Customization)]
    [Description("Allows overriding Poké Ball with \"Ball\" in a Showdown set.")]
    public bool ForceSpecifiedBall { get; set; } = true;

    [Category(Customization)]
    [Description(
        @"The priority order for what games ALM will try.
            NewestFirst - will use descending game order latest first.
            NativeOnly - will use the GamePair for the current save file only.
            PriorityOrder - will use the Priority Order setting"
    )]
    public GameVersionPriorityType GameVersionPriority { get; set; } = GameVersionPriorityType.NewestFirst;

    [Category(Customization)]
    [Description("The order of GameVersions ALM will attempt to legalize from.")]
    public List<GameVersion> PriorityOrder { get; set; } = [.. Enum.GetValues<GameVersion>().Where(GameUtil.IsValidSavedVersion).Reverse()];

    [Category(Customization)]
    [Description("Adds all ribbons that are legal according to PKHeX legality.")]
    public bool SetAllLegalRibbons { get; set; } = true;

    [Category(Customization)]
    [Description("Sets all past-generation Pokémon as Battle Ready for games that support it.")]
    public bool SetBattleVersion { get; set; } = true;

    [Category(Customization)]
    [Description("Attempts to choose a matching Poké Ball based on Pokémon color.")]
    public bool SetMatchingBalls { get; set; } = true;

    [Category(Customization)]
    [Description("Force Showdown sets with level 50 to level 100")]
    public bool ForceLevel100for50 { get; set; } = true;

    [Category(Customization)]
    [Description("Export format for ALM Showdown Template")]
    public BattleTemplateDisplayStyle ExportFormat { get; set; } = BattleTemplateDisplayStyle.Showdown;

    // Legality
    [Category(Legality)]
    [Description("Global timeout per Pokémon being generated (in seconds)")]
    public int Timeout { get; set; } = 15;

    [Category(Legality)]
    [Description("Defines the order in which Pokémon encounters are prioritized")]
    public List<EncounterTypeGroup> PrioritizeEncounters { get; set; } =
    [
        EncounterTypeGroup.Egg,
        EncounterTypeGroup.Static,
        EncounterTypeGroup.Trade,
        EncounterTypeGroup.Slot,
        EncounterTypeGroup.Mystery,
    ];

    [Category(Legality)]
    [Description("Produces an Easter Egg Pokémon if the provided set is illegal.")]
    public bool EnableEasterEggs { get; set; }

    // Living Dex
    [Category(LivingDex)]
    [Description("Generate all forms of the Pokémon. Note that some generations may not have enough box space for all forms.")]
    public bool IncludeForms { get; set; }

    [Category(LivingDex)]
    [Description("Try to generate the shiny version of the Pokémon if possible.")]
    public bool SetShiny { get; set; }

    [Category(LivingDex)]
    [Description("Try to generate the alpha version of the Pokémon if possible.")]
    public bool SetAlpha { get; set; }

    [Category(TransferDex)]
    [Description("Generate Transfer Living Dex destination game")]
    public GameVersion TransferVersion { get; set; } = Latest.Version;

    // Miscellaneous
    [Category(Miscellaneous)]
    [Description("Used for \"Generate Smogon Sets\". If set to true, ALM will ask for approval for each set before attempting to generate it.")]
    public bool PromptForSmogonImport { get; set; }

    [Category(Miscellaneous)]
    [Description("Sets markings on the Pokémon based on IVs.")]
    public bool UseMarkings { get; set; } = true;

    [Category(Miscellaneous)]
    [Description("Sets IVs of 31 to blue and 30 to red if enabled. Otherwise, sets IVs of 31 to blue and 0 to red.")]
    public bool UseCompetitiveMarkings { get; set; } = true;

    [Category(Miscellaneous)]
    [Description("Sets the types to use when generating a random team of Pokémon.")]
    public MoveType[] RandomTypes { get; set; } = [];

    // Development
    [Category(Development)]
    [Description("If enabled, ignores version mismatch warnings until the next PKHeX.Core release. Also bypasses Switch connection checks.")]
    public bool EnableDevMode { get; set; }

    [Browsable(false)]
    public string LatestAllowedVersion { get; set; } = "0.0.0.0";

    public void Save()
    {
        string output = JsonSerializer.Serialize(this, CachedJsonSerializerOptions);
        using StreamWriter sw = new(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "almconfig.json"));
        sw.WriteLine(output);
    }
}
