﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PKHeX.Core.AutoMod;

/// <summary>
/// Leverages <see cref="Core"/>'s <see cref="EncounterMovesetGenerator"/> to create a <see cref="PKM"/> from a <see cref="IBattleTemplate"/>.
/// </summary>
public static class APILegality
{
    /// <summary>
    /// Settings
    /// </summary>
    public static bool UseTrainerData { get; set; } = true;
    public static bool SetMatchingBalls { get; set; } = true;
    public static bool ForceSpecifiedBall { get; set; }
    public static bool SetAllLegalRibbons { get; set; } = true;
    public static bool UseCompetitiveMarkings { get; set; }
    public static bool UseMarkings { get; set; } = true;
    public static bool EnableDevMode { get; set; }
    public static string LatestAllowedVersion { get; set; } = "0.0.0.0";
    public static bool PrioritizeGame { get; set; } = true;
    public static GameVersion PrioritizeGameVersion { get; set; }
    public static bool SetBattleVersion { get; set; }
    public static bool AllowTrainerOverride { get; set; }
    public static bool AllowBatchCommands { get; set; } = true;
    public static bool ForceLevel100for50 { get; set; } = true;
    public static bool AllowHOMETransferGeneration { get; set; } = true;
    public static MoveType[] RandTypes { get; set; } = [];
    public static int Timeout { get; set; } = 15;

    private static bool AllowHOME => ParseSettings.Settings.HOMETransfer.HOMETransferTrackerNotPresent != Severity.Invalid;

    /// <summary>
    /// Main function that auto legalizes based on the legality
    /// </summary>
    /// <remarks>Leverages <see cref="Core"/>'s <see cref="EncounterMovesetGenerator"/> to create a <see cref="PKM"/> from a <see cref="IBattleTemplate"/>.</remarks>
    /// <param name="dest">Destination for the generated pkm</param>
    /// <param name="template">rough pkm that has all the <see cref="set"/> values entered</param>
    /// <param name="set">Showdown set object</param>
    /// <param name="satisfied">If the final result is legal or not</param>
    /// <param name="nativeOnly"></param>
    public static PKM GetLegalFromTemplate(this ITrainerInfo dest, PKM template, IBattleTemplate set, out LegalizationResult satisfied, bool nativeOnly = false)
    {
        RegenSet regen;
        if (set is RegenTemplate t)
        {
            t.FixGender(template.PersonalInfo);
            regen = t.Regen;
        }
        else
        {
            regen = RegenSet.Default;
        }

        if (template.Version == GameVersion.Any)
            template.Version = dest.Version;

        template.ApplySetDetails(set);
        template.SetRecordFlags([]); // Validate TR/MS moves for the encounter

        if (template.Species == (ushort)Species.Unown) // Force Unown form on template
            template.Form = set.Form;

        var abilityreq = GetRequestedAbility(template, set);
        var batchedit = AllowBatchCommands && regen.HasBatchSettings;
        var native = ModLogic.Config.NativeOnly && nativeOnly;
        var destType = template.GetType();
        var destVer = dest.Version;
        if (destVer <= 0 && dest is SaveFile s)
            destVer = s.Version;
        if (dest.Generation <= 2)
            template.EXP = 0; // no relearn moves in gen 1/2 so pass level 1 to generator

        var gamelist = FilteredGameList(template, destVer, AllowBatchCommands, set, native);
        if (gamelist is [GameVersion.DP])
            gamelist = [GameVersion.D, GameVersion.P];
        if (gamelist is [GameVersion.RS])
            gamelist = [GameVersion.R, GameVersion.S];

        var mutations = EncounterMutationUtil.GetSuggested(dest.Context, set.Level);
        var encounters = GetAllEncounters(pk: template, moves: new ReadOnlyMemory<ushort>(set.Moves), gamelist);
        var criteria = EncounterCriteria.GetCriteria(set, template.PersonalInfo, mutations);
        if (regen.EncounterFilters.Any())
            encounters = encounters.Where(enc => BatchEditing.IsFilterMatch(regen.EncounterFilters, enc));
        if (regen.SeedFilters.Any())
            encounters = encounters.Where(enc => enc is (IGenerateSeed32 or IGenerateSeed64)); // Only allow seed generation for seed encounters

        PKM? last = null;
        var timer = Stopwatch.StartNew();
        foreach (var enc in encounters)
        {
            // Return out if set times out
            if (timer.Elapsed.TotalSeconds >= Timeout)
            {
                timer.Stop();
                satisfied = LegalizationResult.Timeout;
                return template;
            }

            // Look before we leap -- don't waste time generating invalid / incompatible junk.
            if (!IsEncounterValid(set, enc, abilityreq, destVer))
                continue;

            criteria = SetSpecialCriteria(criteria, enc, set);

            // Create the PKM from the template.
            var tr = SimpleEdits.IsUntradeableEncounter(enc) ? dest : GetTrainer(regen, enc, set, dest);
            var raw = enc.GetPokemonFromEncounter(tr, criteria, set);
            if (raw.OriginalTrainerName.Length == 0)
            {
                raw.Language = tr.Language;
                tr.ApplyTo(raw);
            }
            raw = raw.SanityCheckLocation(enc);
            if (raw.IsEgg) // PGF events are sometimes eggs. Force hatch them before proceeding
                raw.HandleEggEncounters(enc, tr);
            if (enc is (IGenerateSeed32 or IGenerateSeed64) && regen.SeedFilters.Any())
            {
                switch (enc)
                {
                    case IGenerateSeed32 GS32:
                        var converted = Convert.ToUInt32(regen.SeedFilters[0], 16);
                        GS32.GenerateSeed32(raw, converted);
                        if (enc is ITeraRaid9 tr9)
                        {
                            var type = Tera9RNG.GetTeraType(converted, tr9.TeraType, enc.Species, enc.Form);
                            ((PK9)raw).TeraTypeOriginal = (MoveType)type;
                            if (set.TeraType != MoveType.Any && (MoveType)type != set.TeraType && TeraTypeUtil.CanChangeTeraType(enc.Species))
                                ((PK9)raw).SetTeraType(set.TeraType);
                        }
                        break;
                    case IGenerateSeed64 GS64:
                        var converted64 = Convert.ToUInt64(regen.SeedFilters[0], 16);
                        GS64.GenerateSeed64(raw, converted64); break;
                }
            }
            else
            {
                raw.PreSetPIDIV(enc, set, criteria);
            }
            // Transfer any VC1 via VC2, as there may be GSC exclusive moves requested.
            if (dest.Generation >= 7 && raw is PK1 basepk1)
                raw = basepk1.ConvertToPK2();

            // Bring to the target generation and filter
            var pk = EntityConverter.ConvertToType(raw, destType, out _);
            if (pk == null)
                continue;

            if (!EntityConverter.IsCompatibleGB(pk, template.Japanese, pk.Japanese))
                continue;

            if (dest.Generation >= 8 && HomeTrackerUtil.IsRequired(enc, pk) && !AllowHOME)
                continue;

            // Apply final details
            ApplySetDetails(pk, set, dest, enc, regen, criteria);
            // Apply final tweaks to the data.
            if (pk is IGigantamax gmax && gmax.CanGigantamax != set.CanGigantamax)
            {
                if (!Gigantamax.CanToggle(pk.Species, pk.Form, enc.Species, enc.Form))
                    continue;

                gmax.CanGigantamax = set.CanGigantamax; // soup hax
            }

            // Try applying batch editor values.
            if (batchedit)
            {
                pk.RefreshChecksum();
                var b = regen.Batch;
                BatchEditing.ScreenStrings(b.Filters);
                BatchEditing.ScreenStrings(b.Instructions);
                var modified = BatchEditing.TryModify(pk, b.Filters, b.Instructions);
                if (!modified && b.Filters.Count > 0)
                    continue;

                pk.ApplyPostBatchFixes();
            }

            if (pk is PK1 pk1 && pk1.TradebackValid())
            {
                satisfied = LegalizationResult.Regenerated;
                return pk;
            }
            // Verify the Legality of what we generated, and exit if it is valid.
            var la = new LegalityAnalysis(pk);
            if (la.Valid && pk.Species == set.Species) // Encounter Trades that evolve may cause higher than expected species
            {
                satisfied = LegalizationResult.Regenerated;
                return pk;
            }

            last = pk;
            Debug.WriteLine($"{(Species)pk.Species}\n{la.Report()}\n");
        }
        satisfied = LegalizationResult.Failed;
        return last ?? template;
    }

    private static PKM GetPokemonFromEncounter(this IEncounterable enc, ITrainerInfo tr, EncounterCriteria criteria, IBattleTemplate set)
    {
        if (enc is EncounterGift3 { Species: (ushort)Species.Jirachi })
        {
            if (tr.Language == (byte)LanguageID.Japanese)
                tr = tr.MutateLanguage(LanguageID.English, tr.Version);
        }
        var basepkm = enc.ConvertToPKM(tr, criteria);

        // If the encounter is a Wurmple, we need to make sure the evolution is correct.
        if (enc.Species == (int)Species.Wurmple && set.Species != (int)Species.Wurmple)
        {
            var wdest = WurmpleUtil.GetWurmpleEvoGroup(set.Species);
            while (WurmpleUtil.GetWurmpleEvoVal(basepkm.PID) != wdest)
                basepkm = enc.ConvertToPKM(tr, criteria);
        }

        return basepkm;
    }

    private static IEnumerable<IEncounterable> GetAllEncounters(PKM pk, ReadOnlyMemory<ushort> moves, IReadOnlyList<GameVersion> vers)
    {
        var orig_encs = EncounterMovesetGenerator.GenerateEncounters(pk, moves, vers);
        foreach (var enc in orig_encs)
            yield return enc;

        var pi = pk.PersonalInfo;
        var orig_form = pk.Form;
        var fc = pi.FormCount;
        if (fc == 0) // not present in game
        {
            // try again using past-gen table
            pi = PersonalTable.USUM.GetFormEntry(pk.Species, 0);
            fc = pi.FormCount;
        }
        for (byte f = 0; f < fc; f++)
        {
            if (f == orig_form)
                continue;

            if (FormInfo.IsBattleOnlyForm(pk.Species, f, pk.Format))
                continue;

            pk.Form = f;
            pk.SetGender(pk.GetSaneGender());
            var encs = EncounterMovesetGenerator.GenerateEncounters(pk, moves, vers);
            foreach (var enc in encs)
                yield return enc;
        }
    }

    public static AbilityRequest GetRequestedAbility(PKM template, IBattleTemplate set)
    {
        if (template.AbilityNumber == 4)
            return AbilityRequest.Hidden;

        var pi = template.PersonalInfo;
        var abils_count = pi.AbilityCount;
        if (abils_count <= 2 || pi is not IPersonalAbility12H h)
            return AbilityRequest.NotHidden;

        if (h.AbilityH == template.Ability)
            return AbilityRequest.PossiblyHidden;

        // if no set ability is specified, it is assumed as the first ability which can be the same as the HA
        if (set.Ability == -1 && h.Ability1 == h.AbilityH)
            return AbilityRequest.PossiblyHidden;

        return set.Ability == -1 ? AbilityRequest.Any : AbilityRequest.NotHidden; // Will allow any ability if ability is unspecified
    }

    private static bool TradebackValid(this PK1 pk1)
    {
        var valid = new LegalityAnalysis(pk1).Valid;
        if (!valid)
            pk1.CatchRate = pk1.Gen2Item;

        return valid;
    }

    /// <summary>
    /// Filter down the game list to search based on requested sets
    /// </summary>
    /// <param name="template">Template Pokémon with basic details set</param>
    /// <param name="destVer">Version in which the Pokémon needs to be imported</param>
    /// <param name="batchEdit">Whether settings currently allow batch commands</param>
    /// <param name="set">Set information to be used to filter the game list</param>
    /// <param nativeOnly="set">Whether to only return encounters from the current version</param>
    /// <param name="nativeOnly"></param>
    /// <returns>List of filtered games to check encounters for</returns>
    internal static GameVersion[] FilteredGameList(PKM template, GameVersion destVer, bool batchEdit, IBattleTemplate set, bool nativeOnly = false)
    {
        if (batchEdit && set is RegenTemplate { Regen.VersionFilters: { Count: not 0 } x } && TryGetSingleVersion(x, out var single))
            return single;

        var versionlist = GameUtil.GetVersionsWithinRange(template, template.Format);
        var gamelist = !nativeOnly ? [.. versionlist.OrderByDescending(c => c.GetGeneration())] : GetPairedVersions(destVer, versionlist);
        if (PrioritizeGame)
            gamelist = PrioritizeGameVersion == GameVersion.Any ? PrioritizeVersion(gamelist, destVer.GetIsland()) : PrioritizeVersion(gamelist, PrioritizeGameVersion);

        if (template.AbilityNumber == 4 && destVer.GetGeneration() < 8)
            gamelist = gamelist.Where(z => z.GetGeneration() is not 3 and not 4).ToArray();
        if (gamelist.Contains(GameVersion.HGSS))
        {
            gamelist = gamelist.Where(z => z != GameVersion.HGSS).ToArray();
            gamelist = [.. gamelist, GameVersion.HG];
            gamelist = [.. gamelist, GameVersion.SS];
        }
        if (gamelist.Contains(GameVersion.FRLG))
        {
            gamelist = gamelist.Where(z => z != GameVersion.FRLG).ToArray();
            gamelist = [.. gamelist, GameVersion.FR];
            gamelist = [.. gamelist, GameVersion.LG];
        }
        return gamelist;
    }

    private static bool TryGetSingleVersion(IReadOnlyList<StringInstruction> filters, [NotNullWhen(true)] out GameVersion[]? gamelist)
    {
        gamelist = null;
        foreach (var filter in filters)
        {
            if (filter.PropertyName != nameof(IVersion.Version))
                continue;

            GameVersion value;
            if (int.TryParse(filter.PropertyValue, out var i))
            {
                value = (GameVersion)i;
            }
            else if (Enum.TryParse<GameVersion>(filter.PropertyValue, out var g))
            {
                value = g;
            }
            else
            {
                return false;
            }

            GameVersion[] result;
            if (value.IsValidSavedVersion())
            {
                result = [value];
            }
            else
            {
                result = GameUtil.GameVersions.Where(z => value.Contains(z)).ToArray();
            }

            gamelist = filter.Comparer switch
            {
                InstructionComparer.IsEqual => result,
                InstructionComparer.IsNotEqual  => GameUtil.GameVersions.Where(z => !result.Contains(z)).ToArray(),
                InstructionComparer.IsGreaterThan => GameUtil.GameVersions.Where(z => result.Any(g => z > g)).ToArray(),
                InstructionComparer.IsGreaterThanOrEqual => GameUtil.GameVersions.Where(z => result.Any(g => z >= g)).ToArray(),
                InstructionComparer.IsLessThan => GameUtil.GameVersions.Where(z => result.Any(g => z < g)).ToArray(),
                InstructionComparer.IsLessThanOrEqual  => GameUtil.GameVersions.Where(z => result.Any(g => z <= g)).ToArray(),
                _ => result,
            };
            return gamelist.Length != 0;
        }
        return false;
    }

    private static readonly string[] MeisterNicknames =
    [
        "",
        "ポッちゃん",
        "Foppa",
        "Bloupi",
        "Mossy",
        "Pador",
        "",
        "",
        "",
        "",
        "",
    ];

    /// <summary>
    /// Grab a trainer from trainer database with mutated language
    /// </summary>
    /// <returns>ITrainerInfo of the trainer details</returns>
    private static ITrainerInfo GetTrainer(RegenSet regen, IEncounterTemplate enc, IBattleTemplate set, ITrainerInfo dest)
    {
        var ver = enc.Version;
        var gen = enc.Generation;
        var mutate = regen.Extra.Language;

        // Edge case override for Meister Magikarp
        var idx = Array.IndexOf(MeisterNicknames, set.Nickname);
        if (idx > 0)
            mutate = (LanguageID)idx;

        if (AllowTrainerOverride && regen is { HasTrainerSettings: true, Trainer: not null })
            return regen.Trainer.MutateLanguage(mutate, ver);

        return UseTrainerData ? TrainerSettings.GetSavedTrainerData(ver, gen).MutateLanguage(mutate, ver) : TrainerSettings.DefaultFallback(ver, regen.Extra.Language??(LanguageID)dest.Language);
    }

    /// <summary>
    /// Gives the currently loaded save priority over other saves in the same generation. Otherwise, generational order is preserved
    /// </summary>
    /// <param name="gamelist">Array of GameVersion which needs to be prioritized</param>
    /// <param name="game">GameVersion to prioritize</param>
    /// <returns>A prioritized GameVersion list</returns>
    private static GameVersion[] PrioritizeVersion(ReadOnlySpan<GameVersion> gamelist, GameVersion game)
    {
        var matched = 0;
        var retval = new List<GameVersion>();
        foreach (GameVersion poss in gamelist)
        {
            if (poss == game || game.Contains(poss))
            {
                retval.Insert(matched, poss);
                matched++;
            }
            else
            {
                retval.Add(poss);
            }
        }
        return [.. retval];
    }

    /// <summary>
    /// Checks if the encounter is even valid before processing it
    /// </summary>
    /// <param name="set">showdown set</param>
    /// <param name="enc">encounter object</param>
    /// <param name="abilityreq">is HA requested</param>
    /// <param name="destVer">version to generate in</param>
    /// <returns>if the encounter is valid or not</returns>
    private static bool IsEncounterValid(IBattleTemplate set, IEncounterTemplate enc, AbilityRequest abilityreq, GameVersion destVer)
    {
        if (enc is EncounterSlot3 && enc.Species == (ushort)Species.Unown && enc.Form != set.Form)
            return false;
        // Don't process if encounter min level is higher than requested level
        if (!IsRequestedLevelValid(set, enc))
            return false;

        // Don't process if the ball requested is invalid
        if (!IsRequestedBallValid(set, enc))
            return false;

        // Don't process if encounter and set shinies don't match
        if (!IsRequestedShinyValid(set, enc))
            return false;

        // Don't process if the requested set is Alpha and the Encounter is not
        if (!IsRequestedAlphaValid(set, enc))
            return false;

        // Don't process if the gender does not match the set
        if (set.Gender is not null && enc is IFixedGender { IsFixedGender: true } fg && fg.Gender != set.Gender)
            return false;

        // Don't process if PKM is definitely Hidden Ability and the PKM is from Gen 3 or Gen 4 and Hidden Capsule doesn't exist
        var gen = enc.Generation;
        if (abilityreq == AbilityRequest.Hidden && gen is 3 or 4 && destVer.GetGeneration() < 8)
            return false;

        if (set.Species == (ushort)Species.Pikachu)
        {
            switch (enc.Generation)
            {
                case 6 when set.Form != (enc is EncounterStatic6 ? enc.Form : 0):
                case >= 7 when set.Form != (enc is EncounterInvalid or EncounterEgg ? 0 : enc.Form):
                    return false;
            }
        }
        if (enc.Generation > 2 && set.EVs.Sum() > 510 && destVer is not GameVersion.GP and not GameVersion.GE)
            return false;

        return destVer.ExistsInGame(set.Species, set.Form);
    }

    public static bool IsRequestedLevelValid(IBattleTemplate set, IEncounterTemplate enc)
    {
        if (enc.LevelMin <= set.Level)
            return true;

        if (enc is IEncounterDownlevel dl && dl.GetDownleveledMin() <= set.Level)
            return true;

        return false;
    }

    public static bool IsRequestedBallValid(IBattleTemplate set, IEncounterTemplate enc)
    {
        if (set is RegenTemplate rt && enc.FixedBall != Ball.None && ForceSpecifiedBall)
        {
            var reqball = rt.Regen.Extra.Ball;
            if (reqball != enc.FixedBall && reqball != Ball.None)
                return false;
        }
        return true;
    }

    public static bool IsRequestedAlphaValid(IBattleTemplate set, IEncounterTemplate enc)
    {
        // No Alpha setting in base showdown
        if (set is not RegenTemplate rt)
            return true;

        // Check alpha request
        var requested = false;
        if (rt.Regen.HasExtraSettings)
            requested = rt.Regen.Extra.Alpha;

        // Requested alpha but encounter isn't an alpha
        return enc is not IAlphaReadOnly a ? !requested : a.IsAlpha == requested;
    }

    public static bool IsRequestedShinyValid(IBattleTemplate set, IEncounterTemplate enc)
    {
        if (enc is MysteryGift { CardID: >= 9000 })
            return true;

        // Don't process if shiny value doesn't match
        if (set.Shiny && enc.Shiny == Shiny.Never)
            return false;

        if (!set.Shiny && enc.Shiny.IsShiny())
            return false;

        // Further shiny filtering if set is regen template
        if (set is RegenTemplate { Regen: { HasExtraSettings: true } regen} && enc.Generation != 9)
        {
            var shinytype = regen.Extra.ShinyType;
            if (shinytype == Shiny.AlwaysStar && enc.Shiny == Shiny.AlwaysSquare)
                return false;
            if (shinytype == Shiny.AlwaysSquare && enc.Shiny == Shiny.AlwaysStar)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Method to check if PID/IV has already set
    /// </summary>
    /// <param name="pk">pkm to check</param>
    /// <param name="enc">enc to check</param>
    public static bool IsPIDIVSet(PKM pk, IEncounterTemplate enc) => enc switch
    {
        // If PID and IV is handled in PreSetPIDIV, don't set it here again and return out
        ITeraRaid9 or EncounterStatic8N or EncounterStatic8NC or EncounterStatic8ND or EncounterStatic8U => true,
        IOverworldCorrelation8 o when o.GetRequirement(pk) == OverworldCorrelation8Requirement.MustHave => true,
        IStaticCorrelation8b s when s.GetRequirement(pk) == StaticCorrelation8bRequirement.MustHave => true,
        EncounterSlot3 when pk.Species == (ushort)Species.Unown => true,
        EncounterEgg when GameVersion.BDSP.Contains(enc.Version) => true,
        EncounterGift3 when pk.Species == (ushort)Species.Jirachi => true, //PKHeX handles this now for both Wishmkr and CHANNEL
        _ => false,
    };

    /// <summary>
    /// Sanity checking locations before passing them into ApplySetDetails.
    /// Some encounters may have an empty met location leading to an encounter mismatch. Use this function for all encounter pre-processing!
    /// </summary>
    /// <param name="pk">Entity to fix</param>
    /// <param name="enc">Matched encounter</param>
    private static PKM SanityCheckLocation(this PKM pk, IEncounterTemplate enc)
    {
        const int SharedNest = 162; // Shared Nest for online encounter
        const int MaxLair = 244; // Dynamax Adventures
        pk.MetLocation = enc switch
        {
            EncounterStatic8N or EncounterStatic8ND or EncounterStatic8NC => SharedNest,
            EncounterStatic8U => MaxLair,
            _ => pk.MetLocation,
        };
        return pk;
    }

    /// <summary>
    /// Modifies the provided <see cref="pk"/> to the specifications required by <see cref="set"/>.
    /// </summary>
    /// <param name="pk">Converted final pkm to apply details to</param>
    /// <param name="set">Set details required</param>
    /// <param name="handler">Trainer to handle the Pokémon</param>
    /// <param name="enc">Encounter details matched to the Pokémon</param>
    /// <param name="regen">Regeneration information</param>
    /// <param name="criteria"></param>
    private static void ApplySetDetails(PKM pk, IBattleTemplate set, ITrainerInfo handler, IEncounterTemplate enc, RegenSet regen, EncounterCriteria criteria)
    {
        var language = regen.Extra.Language;
        var pidiv = MethodFinder.Analyze(pk);

        pk.SetPINGA(set, pidiv.Type, set.HiddenPowerType, enc, criteria);
        pk.SetSpeciesLevel(set, set.Form, enc, language);
        pk.SetDateLocks(enc);
        pk.SetHeldItem(set);

        // Actions that do not affect set legality
        pk.SetHandlerAndMemory(handler, enc);
        pk.SetFriendship(enc);
        pk.SetRecordFlags(set.Moves);

        // Legality Fixing
        pk.SetMovesEVs(set, enc);
        pk.SetCorrectMetLevel(enc);
        pk.SetGVs();
        pk.SetHyperTrainingFlags(set, enc);
        pk.SetEncryptionConstant(enc);
        pk.SetShinyBoolean(set.Shiny, enc, regen.Extra.ShinyType);
        pk.FixGender(set);

        // Final tweaks
        pk.SetGimmicks(set);
        pk.SetGigantamaxFactor(set, enc);
        pk.SetSuggestedRibbons(set, enc, SetAllLegalRibbons);
        pk.SetBelugaValues();
        pk.SetSuggestedContestStats(enc);
        pk.FixEdgeCases(enc);

        // Aesthetics
        pk.ApplyHeightWeight(enc);
        pk.SetSuggestedBall(enc, SetMatchingBalls, ForceSpecifiedBall, regen.Extra.Ball);
        pk.ApplyMarkings(UseMarkings);
        pk.ApplyBattleVersion(handler);
    }

    /// <summary>
    /// Validate and Set the gender if needed
    /// </summary>
    /// <param name="pk">PKM to modify</param>
    /// <param name="enc"></param>
    private static void ValidateGender(PKM pk, IEncounterTemplate enc)
    {
        bool genderValid = pk.IsGenderValid();
        if (!genderValid)
        {
            if (pk is { Format: 4, Species: (ushort)Species.Shedinja }) // Shedinja glitch
            {
                // should match original gender
                var gender = EntityGender.GetFromPIDAndRatio(pk.PID, 0x7F); // 50-50
                if (gender == pk.Gender)
                    genderValid = true;
            }
            else if (pk is { Format: > 5, Species: (ushort)Species.Marill or (ushort)Species.Azumarill })
            {
                var gv = pk.PID & 0xFF;
                if (gv > 63 && pk.Gender == 1) // evolved from Azurill after transferring to keep gender
                    genderValid = true;
            }
        }
        else
        {
            // check for mixed->fixed gender incompatibility by checking the gender of the original species
            if (SpeciesCategory.IsFixedGenderFromDual(pk.Species) && pk.Gender != 2) // Shedinja
                pk.Gender = EntityGender.GetFromPID(enc.Species, pk.EncryptionConstant);
            // genderValid = true; already true if we reach here
        }
        if (genderValid)
            return;

        switch (pk.Gender)
        {
            case 0:
                pk.Gender = 1;
                break;
            case 1:
                pk.Gender = 0;
                break;
            default:
                pk.GetSaneGender();
                break;
        }
    }

    /// <summary>
    /// Competitive IVs or PKHeX default IVs implementation
    /// </summary>
    /// <param name="pk"></param>
    /// <param name="apply">boolean to apply or not to apply markings</param>
    private static void ApplyMarkings(this PKM pk, bool apply = true)
    {
        if (!apply || pk.Format <= 3) // No markings if pk.Format is less than or equal to 3
            return;

        pk.SetMarkings();
    }

    /// <summary>
    /// Custom Marking applicator method
    /// </summary>
    public static Func<int, int, int> CompetitiveMarking(PKM pk)
    {
        return pk.Format < 7 ? GetSimpleMarking : GetComplexMarking;

        static int GetSimpleMarking(int val, int _) => val == 31 ? 1 : 0;
        static int GetComplexMarking(int val, int _) => val switch
        {
            31 => 1,
            30 => 2,
            _ => 0,
        };
    }

    /// <summary>
    /// Proper method to hyper train based on Showdown Sets. Also handles edge cases like ultra beasts
    /// </summary>
    /// <param name="pk">passed pkm object</param>
    /// <param name="set">showdown set to base hyper training on</param>
    /// <param name="enc"></param>
    private static void SetHyperTrainingFlags(this PKM pk, IBattleTemplate set, IEncounterTemplate enc)
    {
        if (pk is not IHyperTrain t || pk.Species == (ushort)Species.Stakataka)
            return;

        // Game exceptions (IHyperTrain exists because of the field but game disallows hyper training)
        var history = EvolutionChain.GetEvolutionChainsAllGens(pk, enc);
        if (!t.IsHyperTrainingAvailable())
            return;
        if (t.GetHyperTrainMinLevel(history, pk.Context) > pk.CurrentLevel)
            return;

        t.HyperTrain(pk, set.IVs);

        // Handle special cases here for ultrabeasts
        switch (pk.Species)
        {
            case (int)Species.Kartana when pk.Nature == Nature.Timid && set.IVs[1] <= 21: // Speed boosting Timid Kartana ATK IVs <= 19
                t.HT_ATK = false;
                break;
            case (int)Species.Stakataka when pk.StatNature == Nature.Lonely && set.IVs[2] <= 17: // Atk boosting Lonely Stakataka DEF IVs <= 15
                t.HT_DEF = false;
                break;
            case (int)Species.Pyukumuku when set.IVs[2] == 0 && set.IVs[5] == 0 && pk.Ability == (int)Ability.InnardsOut: // 0 Def / 0 Spd Pyukumuku with innards out
                t.HT_DEF = false;
                t.HT_SPD = false;
                break;
        }
    }

    /// <summary>
    /// Sets past-generation Pokémon as Battle Ready for games that support it
    /// </summary>
    /// <param name="pk">Return PKM</param>
    /// <param name="trainer">Trainer to handle the <see cref="pk"/></param>
    private static void ApplyBattleVersion(this PKM pk, ITrainerInfo trainer)
    {
        if (!SetBattleVersion)
            return;

        if (pk is PK8 { SWSH: false } pk8)
        {
            Span<ushort> relearn = stackalloc ushort[4];
            pk8.GetRelearnMoves(relearn);

            pk8.ClearRelearnMoves();
            pk8.BattleVersion = trainer.Version == GameVersion.SWSH ? GameVersion.SW : trainer.Version;

            if (!new LegalityAnalysis(pk8).Valid)
            {
                pk8.BattleVersion = GameVersion.Any;
                pk8.SetRelearnMoves(relearn);
            }
        }
    }

    /// <summary>
    /// Set forms of specific species to form 0 since they cannot have a form while boxed
    /// </summary>
    /// <param name="pk">Pokémon passed to the method</param>
    public static void SetBoxForm(this PKM pk)
    {
        if (pk.Format > 6)
            return;

        switch (pk.Species)
        {
            case (int)Species.Shaymin when pk.Form != 0:
            case (int)Species.Hoopa when pk.Form != 0:
            case (int)Species.Furfrou when pk.Form != 0:
                pk.Form = 0;
                if (pk is IFormArgument f)
                    f.FormArgument = 0;

                break;
        }
    }

    /// <summary>
    /// Handle Egg encounters (for example PGF events that were distributed as eggs)
    /// </summary>
    /// <param name="pk">pkm distributed as an egg</param>
    /// <param name="enc">encounter detail</param>
    /// <param name="tr">save file</param>
    private static void HandleEggEncounters(this PKM pk, IEncounterTemplate enc, ITrainerInfo tr)
    {
        if (!pk.IsEgg)
            return; // should be checked before, but condition added for future usecases
        // Handle egg encounters. Set them as traded and force hatch them before proceeding
        pk.ForceHatchPKM();
        if (enc is MysteryGift { IsEgg: true })
        {
            if (enc is EncounterGift3)
                pk.MetLevel = 0; // hatched

            pk.Language = tr.Language;
            pk.SetTrainerData(tr);
        }
        pk.EggLocation = Locations.TradedEggLocation(enc.Generation, enc.Version);
    }

    /// <summary>
    /// Set IV Values for the Pokémon
    /// </summary>
    private static void SetPINGA(this PKM pk, IBattleTemplate set, PIDType method, int hpType, IEncounterTemplate enc, EncounterCriteria criteria)
    {
        if (enc is not EncounterStatic4Pokewalker && enc.Generation > 2)
            ShowdownEdits.SetNature(pk, set, enc);

        // If PID and IV is handled in PreSetPIDIV, don't set it here again and return out
        bool changeEC = false;
        if (AllowBatchCommands && set is RegenTemplate { Regen: { HasBatchSettings: true } regen })
        {
            if (regen.TryGetBatchValue(nameof(IRibbonSetMark8.RibbonMarkCurry), out var hasCurry))
            {
                bool setCurryMark = string.Equals(hasCurry, "true", StringComparison.OrdinalIgnoreCase);
                if (setCurryMark)
                {
                    changeEC = enc.Context == EntityContext.Gen8; // for Gen8 encounters, we need to break the correlation
                    // Eagerly set the mark so that verifiers can know it was curry.
                    if (pk is IRibbonSetMark8 r8)
                        r8.RibbonMarkCurry = true;
                }
            }
        }

        if (IsPIDIVSet(pk, enc) && !changeEC)
            return;

        if (changeEC)
            pk.SetRandomEC(); // break correlation

        if (enc is MysteryGift mg)
        {
            var ivs = pk.IVs;
            Span<int> mgIvs = [];
            mg.GetIVs(mgIvs);
            for (int i = 0; i < mgIvs.Length; i++)
                ivs[i] = mgIvs[i] > 31 ? set.IVs[i] : mgIvs[i];

            pk.SetIVs(ivs);
            if (enc.Generation is not (3 or 4))
                return;
        }

        if (enc is IFixedIVSet { IVs.IsSpecified: true })
            return;

        if (enc.Generation is not (3 or 4))
        {
            pk.SetIVs(set.IVs);
            if (pk is not IAwakened)
                return;

            pk.SetAwakenedValues(set);
            return;
        }

        switch (enc)
        {
            case EncounterSlot3XD:
            case PCD:
            case EncounterEgg:
                return;
            // EncounterTrade4 doesn't have fixed PIDs, so don't early return
            case EncounterTrade3:
            case EncounterTrade4PID:
            case EncounterTrade4RanchGift:
                pk.SetEncounterTradeIVs();
                return; // Fixed PID, no need to mutate
            default:
                FindPIDIV(pk, method, hpType, set.Shiny, enc, set);
                ValidateGender(pk, enc);
                break;
        }

        // Handle mismatching abilities due to a PID re-roll
        // Check against ability index because the Pokémon could be a pre-evo at this point
        if (pk.Ability != set.Ability)
            pk.RefreshAbility(pk is PK5 { HiddenAbility: true } ? 2 : pk.AbilityNumber >> 1);

        var ability_idx = GetRequiredAbilityIdx(pk, set);
        if (set.Ability == -1 || set.Ability == pk.Ability || pk.AbilityNumber >> 1 == ability_idx || ability_idx == -1)
            return;

        var abilitypref = enc.Ability;
        ShowdownEdits.SetAbility(pk, set, abilitypref);
    }

    /// <summary>
    /// Set Ganbaru Values after IVs are fully set
    /// </summary>
    /// <param name="pk">PKM to set GVs on</param>
    private static void SetGVs(this PKM pk)
    {
        if (pk is not IGanbaru g)
            return;

        g.SetSuggestedGanbaruValues(pk);
    }

    /// <summary>
    /// Set PIDIV for raid PKM via XOROSHIRO incase it is transferred to future generations to preserve the IVs
    /// </summary>
    /// <param name="pk">Pokémon to be edited</param>
    /// <param name="enc">Raid encounter encounterable</param>
    /// <param name="set">Set to pass in requested IVs</param>
    /// <param name="criteria"></param>
    private static void PreSetPIDIV(this PKM pk, IEncounterTemplate enc, IBattleTemplate set, EncounterCriteria criteria)
    {
        if (enc is ITeraRaid9)
        {
            var pk9 = (PK9)pk;
            switch (enc)
            {
                case EncounterTera9 e:
                    FindTeraPIDIV(pk9, e, set, criteria);
                    break;
                case EncounterDist9 e:
                    FindTeraPIDIV(pk9, e, set, criteria);
                    break;
                case EncounterMight9 e:
                    FindTeraPIDIV(pk9, e, set, criteria);
                    break;
            }
            if (set.TeraType != MoveType.Any && set.TeraType != pk9.TeraType)
                pk9.SetTeraType(set.TeraType);
        }
        else if (enc is EncounterStatic8U && set.Shiny)
        {
            // Dynamax Adventure shinies are always XOR 1 (thanks santacrab!)
            pk.PID = SimpleEdits.GetShinyPID(pk.TID16, pk.SID16, pk.PID, 1);
        }
        else if (enc is IOverworldCorrelation8 eo)
        {
            if (eo.GetRequirement(pk) != OverworldCorrelation8Requirement.MustHave)
                return;
            var flawless = 0;
            if (enc is EncounterStatic8 estatic8)
                flawless = estatic8.FlawlessIVCount;

            // Attempt to give them requested 0 ivs at the very least unless they specifically request for all random ivs
            Span<int> cloned = stackalloc int[set.IVs.Length];
            if (set.IVs.Contains(31) || set.IVs.Contains(0))
            {
                for (int i = 0; i < set.IVs.Length; i++)
                    cloned[i] = set.IVs[i] != 0 ? 31 : 0;
            }
            else
            {
                cloned = set.IVs;
            }

            var pk8 = (PK8)pk;
            var shiny = set is RegenTemplate r ? r.Regen.Extra.ShinyType : set.Shiny ? Shiny.Always : Shiny.Never;
            if (!SimpleEdits.TryApplyHardcodedSeedWild8(pk8, enc, cloned, shiny))
                FindWildPIDIV8(pk8, shiny, flawless);
        }
        else if (enc is EncounterEgg && GameVersion.BDSP.Contains(enc.Version))
        {
            pk.SetIVs(set.IVs);
            Shiny shiny = set is RegenTemplate r ? r.Regen.Extra.ShinyType : set.Shiny ? Shiny.Always : Shiny.Never;
            FindEggPIDIV8b(pk, shiny, criteria, enc);
        }
        else if (enc is EncounterSlot3 { Species: (ushort)Species.Unown } enc3)
        {
            enc3.SetFromIVsUnown((PK3)pk, criteria);
        }
    }

    private static void FindTeraPIDIV<T>(PK9 pk, T enc, IBattleTemplate set, EncounterCriteria criteria) where T : ITeraRaid9, IEncounterTemplate
    {
        if (IsMatchCriteria9(pk, set, criteria))
            return;

        uint count = 0;
        uint finalseed = 0;
        ulong seed = Util.Rand.Rand64();
        Span<int> ivs = stackalloc int[6];
        do
        {
            var pi = PersonalTable.SV.GetFormEntry(enc.Species, enc.Form);
            var rand = new Xoroshiro128Plus(seed);
            seed = rand.NextInt(uint.MaxValue);
            if (!enc.CanBeEncountered((uint)seed))
                continue;
            rand = new Xoroshiro128Plus(seed);
            pk.EncryptionConstant = (uint)rand.NextInt(uint.MaxValue);
            var fakeTID = (uint)rand.NextInt();
            uint pid = (uint)rand.NextInt();
            if (enc.Shiny == Shiny.Random) // let's decide if it's shiny or not!
            {
                int i = 1;
                bool isShiny;
                uint xor;
                while (true)
                {
                    xor = ShinyUtil.GetShinyXor(pid, fakeTID);
                    isShiny = xor < 16;
                    if (isShiny)
                    {
                        if (xor != 0)
                            xor = 1;
                        break;
                    }
                    if (i >= 1)
                        break;
                    pid = (uint)rand.NextInt();
                    i++;
                }
                ShinyUtil.ForceShinyState(isShiny, ref pid, pk.ID32, xor);
            }
            else if (enc.Shiny == Shiny.Always)
            {
                var tid = (ushort)fakeTID;
                var sid = (ushort)(fakeTID >> 16);
                if (!ShinyUtil.GetIsShiny(fakeTID, pid)) // battled
                    pid = ShinyUtil.GetShinyPID(tid, sid, pid, 0);
                if (!ShinyUtil.GetIsShiny(pk.ID32, pid)) // captured
                    pid = ShinyUtil.GetShinyPID(pk.TID16, pk.SID16, pid, ShinyUtil.GetShinyXor(pid, fakeTID) == 0 ? 0u : 1u);
            }
            else // Never
            {
                if (ShinyUtil.GetIsShiny(fakeTID, pid)) // battled
                    pid ^= 0x1000_0000;
                if (ShinyUtil.GetIsShiny(pk.ID32, pid)) // captured
                    pid ^= 0x1000_0000;
            }
            pk.PID = pid;
            if (pk.IsShiny != set.Shiny)
                continue;

            ivs.Fill(-1);
            for (int i = 0; i < ((IFlawlessIVCount)enc).FlawlessIVCount; i++)
            {
                int index;
                do { index = (int)rand.NextInt(6); }
                while (ivs[index] != -1);
                ivs[index] = 31;
            }

            for (int i = 0; i < 6; i++)
            {
                if (ivs[i] == -1)
                    ivs[i] = (int)rand.NextInt(32);
            }
            if (!criteria.IsIVsCompatibleSpeedLast(ivs))
                continue;
            pk.IV_HP = ivs[0];
            pk.IV_ATK = ivs[1];
            pk.IV_DEF = ivs[2];
            pk.IV_SPA = ivs[3];
            pk.IV_SPD = ivs[4];
            pk.IV_SPE = ivs[5];

            int abil = enc.Ability switch
            {
                AbilityPermission.Any12H => (int)rand.NextInt(3) << 1,
                AbilityPermission.Any12 => (int)rand.NextInt(2) << 1,
                _ => (int)enc.Ability,
            };
            pk.RefreshAbility(abil >> 1);

            var gr = pi.Gender;
            Gender gender = gr switch
            {
                PersonalInfo.RatioMagicGenderless => Gender.Genderless,
                PersonalInfo.RatioMagicFemale => Gender.Female,
                PersonalInfo.RatioMagicMale => Gender.Male,
                _ => (Gender)Encounter9RNG.GetGender(gr, rand.NextInt(100)),
            };
            if (criteria.IsSpecifiedGender() && !criteria.IsSatisfiedGender((byte)gender))
                continue;
            pk.Gender = (byte)gender;

            Nature nature = enc.Species == (int)Species.Toxtricity ? ToxtricityUtil.GetRandomNature(ref rand, pk.Form) : (Nature)rand.NextInt(25);
            if (criteria.Nature != Nature.Random && nature != criteria.Nature)
                continue;
            pk.Nature = pk.StatNature = nature;

            pk.HeightScalar = (byte)(rand.NextInt(0x81) + rand.NextInt(0x80));
            pk.WeightScalar = (byte)(rand.NextInt(0x81) + rand.NextInt(0x80));
            pk.Scale = enc switch
            {
                EncounterMight9 m => m.ScaleType.GetSizeValue(m.Scale, ref rand),
                EncounterDist9 d => d.ScaleType.GetSizeValue(d.Scale, ref rand),
                _ => SizeType9Extensions.GetSizeValue(0, 0, ref rand),
            };
            finalseed = (uint)seed;
            break;
        } while (++count < uint.MaxValue);

        var type = Tera9RNG.GetTeraType(finalseed, enc.TeraType, enc.Species, enc.Form);
        pk.TeraTypeOriginal = (MoveType)type;
        if (set.TeraType != MoveType.Any && (MoveType)type != set.TeraType && TeraTypeUtil.CanChangeTeraType(enc.Species))
            pk.SetTeraType(set.TeraType);
    }

    /// <summary>
    /// Wild PID IVs being set through XOROSHIRO128
    /// </summary>
    /// <param name="pk">Pokémon to edit</param>
    /// <param name="shiny">Shinytype requested</param>
    /// <param name="flawless">number of flawless ivs</param>
    /// <param name="fixedseed">Optional fixed RNG seed</param>
    public static void FindWildPIDIV8(PK8 pk, Shiny shiny, int flawless = 0, uint? fixedseed = null)
    {
        // Modified version of the standard XOROSHIRO algorithm (32 bit seed 0, same const seed 1)
        // EC -> PID -> Flawless IV rolls -> Non-flawless IVs -> height -> weight
        uint seed;
        Xoroshiro128Plus rng;

        if (fixedseed != null)
        {
            seed = (uint)fixedseed;
            rng = new Xoroshiro128Plus(seed);

            pk.EncryptionConstant = (uint)rng.NextInt();
            pk.PID = (uint)rng.NextInt();
        }
        else
        {
            while (true)
            {
                seed = Util.Rand32();
                rng = new Xoroshiro128Plus(seed);

                pk.EncryptionConstant = (uint)rng.NextInt();
                pk.PID = (uint)rng.NextInt();

                var xor = pk.ShinyXor;
                switch (shiny)
                {
                    case Shiny.Never when xor < 16:
                    case Shiny.Always when xor >= 16:
                    case Shiny.AlwaysStar when xor is 0 or >= 16:
                    case Shiny.AlwaysSquare when xor != 0:
                        continue;
                }

                // Every other case can be valid and genned, so break out
                break;
            }
        }

        // Square shiny: if not xor0, force xor0
        // Always shiny: if not xor0-15, force xor0
        bool editnecessary = shiny switch
        {
            Shiny.AlwaysSquare when pk.ShinyXor != 0 => true,
            Shiny.Always when !pk.IsShiny => true,
            _ => false,
        };
        if (editnecessary)
            pk.PID = SimpleEdits.GetShinyPID(pk.TID16, pk.SID16, pk.PID, 0);

        // RNG is fixed now, and you have the requested shiny!
        Span<int> ivs = [-1, -1, -1, -1, -1, -1];
        for (int i = ivs.Count(31); i < flawless; i++)
        {
            int index = (int)rng.NextInt(6);
            while (ivs[index] != -1)
            {
                index = (int)rng.NextInt(6);
            }

            ivs[index] = 31;
        }

        for (int i = 0; i < 6; i++)
        {
            if (ivs[i] == -1)
                ivs[i] = (int)rng.NextInt(32);
        }

        pk.IV_HP = ivs[0];
        pk.IV_ATK = ivs[1];
        pk.IV_DEF = ivs[2];
        pk.IV_SPA = ivs[3];
        pk.IV_SPD = ivs[4];
        pk.IV_SPE = ivs[5];

        var height = (int)rng.NextInt(0x81) + (int)rng.NextInt(0x80);
        var weight = (int)rng.NextInt(0x81) + (int)rng.NextInt(0x80);
        pk.HeightScalar = (byte)height;
        pk.WeightScalar = (byte)weight;
    }

    /// <summary>
    /// Egg PID IVs being set through XOROSHIRO1288b
    /// </summary>
    /// <param name="pk">Pokémon to edit</param>
    /// <param name="shiny">Shinytype requested</param>
    /// <param name="criteria"></param>
    public static void FindEggPIDIV8b(PKM pk, Shiny shiny, EncounterCriteria criteria, IEncounterTemplate enc)
    {
        Span<int> ivs = stackalloc int[6];
        ReadOnlySpan<int> requiredIVs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE];
        var pi = PersonalTable.BDSP.GetFormEntry(enc.Species, enc.Form);
        var ratio = pi.Gender;
        var species = enc.Species;

        Span<uint> randomivs = stackalloc uint[6];
        while (true)
        {
            var seed = (ulong)(int)Util.Rand32(); // sign extend when casting to ulong
            var rng = new Xoroshiro128Plus8b(seed);

            var nido = (uint)(species - (int)Species.NidoranF) / 3;
            if (nido < 2)
            {
                // 0: M, 1: F. nido is F=0; reject if equals (mismatch).
                if (rng.NextUInt(2) == nido)
                    continue;
            }
            else if (species is (int)Species.Illumise or (int)Species.Volbeat)
            {
                // 0: M, 1: F. Delta is F=0, reject if equals (mismatch).
                if (rng.NextUInt(2) == (int)Species.Illumise - species)
                    continue;
            }
            else if (species == (int)Species.Indeedee)
            {
                if (rng.NextUInt(2) != enc.Form)
                    continue;
            }

            if (ratio is not (PersonalInfo.RatioMagicMale or PersonalInfo.RatioMagicFemale or PersonalInfo.RatioMagicGenderless))
            {
                var rand = rng.NextUInt(252) + 1;
                if (criteria.IsSpecifiedGender())
                {
                    var roll = rand < ratio ? 1 : 0;
                    if ((byte)criteria.Gender != roll)
                        continue;
                }
            }

            // nature
            _ = rng.NextUInt(25); // Assume one parent always carries an Everstone.

            // ability
            _ = rng.NextUInt(100); // Ability can be changed using Capsule/Patch (Assume parent is ability 0/1).

            // The game does a rand(6) to decide which IV's inheritance to check.
            // If that IV isn't marked to inherit from a parent, it does a rand(2) to pick the parent.
            // When generating egg IVs, it first randomly fills in the egg IVs with rand(32) x6, then overwrites with parent IVs based on tracking.
            // We'll assume both parents have the perfect IVs and copy over the parent IV as it's inherited, then fill in blanks afterward.

            // assume other parent always has destiny knot
            const int inheritCount = 5;
            var inherited = 0;
            ivs.Fill(-1);
            while (inherited < inheritCount)
            {
                var stat = (int)rng.NextUInt(6); // Decides which IV to check.
                if (ivs[stat] != -1) // Only -1 if not already inherited.
                    continue;

                _ = rng.NextUInt(2); // Decides which parent's IV to inherit. Assume both parents have the same desired IVs.
                ivs[stat] = requiredIVs[stat];
                inherited++;
            }

            // Roll all 6 IVs. Parent inheritance will override.
            for (int i = 0; i < randomivs.Length; i++)
                randomivs[i] = rng.NextUInt(32);
            for (int i = 0; i < 6; i++)
            {
                if (ivs[i] == -1)
                    ivs[i] = (int)randomivs[i];
            }

            if (!criteria.IsIVsCompatibleSpeedLast(ivs))
                continue;
            pk.IV_HP = ivs[0];
            pk.IV_ATK = ivs[1];
            pk.IV_DEF = ivs[2];
            pk.IV_SPA = ivs[3];
            pk.IV_SPD = ivs[4];
            pk.IV_SPE = ivs[5];

            pk.EncryptionConstant = rng.NextUInt();

            // PID dissociated completely (assume no masuda and no shiny charm)
            if (shiny is Shiny.Never or Shiny.Random)
            {
                pk.SetUnshiny();
            }
            else
            {
                pk.PID = SimpleEdits.GetShinyPID(pk.TID16, pk.SID16, pk.PID, shiny == Shiny.AlwaysSquare ? 0 : 1);
            }

            break;
        }
    }

    private static bool IsMatchCriteria9(PK9 pk, IBattleTemplate template, EncounterCriteria criteria, bool compromise = false)
    {
        // compromise on nature since they can be minted
        if (criteria.Nature != Nature.Random && criteria.Nature != pk.Nature && !compromise) // match nature
            return false;

        if (template.Gender is (0 or 1) && template.Gender != pk.Gender) // match gender
            return false;

        if (template.Form != pk.Form && !FormInfo.IsFormChangeable(pk.Species, pk.Form, template.Form, EntityContext.Gen9, pk.Context)) // match form -- Toxtricity etc
            return false;
        return template.Shiny == pk.IsShiny;
    }

    /// <summary>
    /// Method to set PID, IV while validating nature.
    /// </summary>
    /// <param name="pk">PKM to modify</param>
    /// <param name="method">Given Method</param>
    /// <param name="hiddenPower">HPType INT for preserving Hidden powers</param>
    /// <param name="shiny">Only used for CHANNEL RNG type</param>
    /// <param name="enc"></param>
    /// <param name="set"></param>
    private static void FindPIDIV(PKM pk, PIDType method, int hiddenPower, bool shiny, IEncounterTemplate enc, IBattleTemplate set)
    {
        if (method == PIDType.None)
        {
            method = FindLikelyPIDType(enc);
            if (method == PIDType.None && enc.Generation >= 3)
                pk.SetPIDGender(pk.Gender);
        }
        switch (method)
        {
            case PIDType.Method_1_Roamer when pk.HPType != (int)MoveType.Fighting - 1: // M1 Roamers can only be HP fighting
            case PIDType.Pokewalker when (pk.Nature >= Nature.Quirky || pk.AbilityNumber == 4): // No possible pokewalker matches
                return;
        }
        var request = pk.Clone();
        // Requested Pokémon may be an evolution, guess index based on requested species ability
        var ability_idx = GetRequiredAbilityIdx(request, set);

        if (request.AbilityNumber >> 1 != ability_idx && set.Ability != -1 && ability_idx != -1)
            request.SetAbilityIndex(ability_idx);

        var count = 0;
        var compromise = false;
        var gr = pk.PersonalInfo.Gender;
        uint seed = Util.Rand32();
        if (IsMatchFromPKHeX(pk, request, hiddenPower, shiny, gr, enc, seed, method))
            return;
        do
        {
            if (count >= 2_500_000 && enc.Species != (ushort)Species.Unown)
                compromise = true;

            seed = Util.Rand32();
            if (PokeWalkerSeedFail(seed, method, pk, request))
                continue;
            PIDGenerator.SetValuesFromSeed(pk, method, seed);
            if (pk.AbilityNumber != request.AbilityNumber )
                continue;
            if (!compromise && pk.Nature != request.Nature)
                continue;
            if (pk.PIDAbility != request.PIDAbility)
                continue;

            if (hiddenPower >= 0 && pk.HPType != hiddenPower)
                continue;

            if (pk.PID % 25 != (int)request.Nature) // Util.Rand32 is the way to go
                continue;

            if (pk.Gender != EntityGender.GetFromPIDAndRatio(pk.PID, gr))
                continue;

            if (pk.Version == GameVersion.CXD && method == PIDType.CXD) // verify locks
            {
                pk.EncryptionConstant = pk.PID;
                var ec = pk.PID;
                bool xorPID = ((pk.TID16 ^ pk.SID16 ^ (int)(ec & 0xFFFF) ^ (int)(ec >> 16)) & ~0x7) == 8;
                if (enc is EncounterStatic3XD && enc.Species == (int)Species.Eevee && (shiny != pk.IsShiny || xorPID)) // Starter Correlation
                    continue;

                var la = new LegalityAnalysis(pk);
                if (la.Info.PIDIV.Type is not PIDType.CXD and not PIDType.CXD_ColoStarter || !la.Info.PIDIVMatches || !pk.IsValidGenderPID(enc))
                    continue;
            }
            if (pk.TID16 == 06930 && !MystryMew.IsValidSeed(seed))
                continue;

            break;
        } while (++count < 5_000_000);
    }

    private static bool IsMatchFromPKHeX(PKM pk, PKM request, int hiddenPower, bool shiny, byte gr, IEncounterTemplate enc, uint seed, PIDType Method)
    {
        if (pk.AbilityNumber != request.AbilityNumber && pk.Nature != request.Nature)
            return false;

        if (pk.PIDAbility != request.PIDAbility)
            return false;

        if (hiddenPower >= 0 && pk.HPType != hiddenPower)
            return false;

        if (pk.PID % 25 != (int)request.Nature) // Util.Rand32 is the way to go
            return false;

        if (pk.Gender != EntityGender.GetFromPIDAndRatio(pk.PID, gr))
            return false;
        if (Method == PIDType.Pokewalker)
            return false;
        if (!new LegalityAnalysis(pk).Valid)
            return false;
        return true;
    }

    private static int GetRequiredAbilityIdx(PKM pkm, IBattleTemplate set)
    {
        if (set.Ability == -1)
            return -1;

        var temp = pkm.Clone();
        temp.Species = set.Species;
        temp.SetAbilityIndex(pkm.AbilityNumber >> 1);
        if (temp.Ability == set.Ability)
            return -1;

        return temp.PersonalInfo.GetIndexOfAbility(set.Ability);
    }

    /// <summary>
    /// Checks if a Pokewalker seed failed, and if it did, randomizes TID and SID (to retry in the future)
    /// </summary>
    /// <param name="seed">Seed</param>
    /// <param name="method">RNG method (every method except pokewalker is ignored)</param>
    /// <param name="pk">PKM object</param>
    /// <param name="original">original encounter pkm</param>
    private static bool PokeWalkerSeedFail(uint seed, PIDType method, PKM pk, PKM original)
    {
        if (method != PIDType.Pokewalker)
            return false;

        if (seed % 24 != (int)original.Nature)
            return true;

        pk.TID16 = (ushort)Util.Rand.Next(65535);
        pk.SID16 = (ushort)Util.Rand.Next(65535);
        return false;
    }

    /// <summary>
    /// Secondary fallback if PIDType.None to slot the PKM into its most likely type
    /// </summary>
    /// <param name="enc"></param>
    /// <returns>PIDType that is likely used</returns>
    private static PIDType FindLikelyPIDType(IEncounterTemplate enc) => enc switch
    {
        EncounterSlot3 s3 => s3.Species == (int)Species.Unown ? PIDType.Method_1_Unown : PIDType.Method_1,
        EncounterStatic3 => PIDType.Method_1,
        EncounterSlot3XD => PIDType.PokeSpot,
        EncounterGift3 g => g.Method,
        EncounterGift3JPN or EncounterGift3NY => PIDType.BACD_U_AX,
        EncounterGift3Colo  or { Version: GameVersion.COLO or GameVersion.XD } => PIDType.CXD,

        EncounterStatic4 s => s.Shiny switch
        {
            Shiny.Always => PIDType.ChainShiny, // Lake of Rage Gyarados
            Shiny.Never => PIDType.Pokewalker, // Spiky Eared Pichu
            _ => PIDType.Method_1,
        },
        EncounterStatic4Pokewalker => PIDType.Pokewalker,
        PGT { GiftType: GiftType4.ManaphyEgg } => PIDType.Method_1,

        _ => PIDType.None,
    };

    /// <summary>
    /// Method to get the correct met level for a Pokémon. Move up the met level till all moves are legal
    /// </summary>
    /// <param name="pk">Pokémon</param>
    /// <param name="enc"></param>
    public static void SetCorrectMetLevel(this PKM pk, IEncounterTemplate enc)
    {
        var current = pk.CurrentLevel;
        var met = pk.MetLevel;
        if (met > current)
            pk.MetLevel = current;

        if (met == current)
            return;

        bool wasMetLost = enc.Context switch
        {
            EntityContext.Gen1 or EntityContext.Gen2 => pk.Context is not (EntityContext.Gen1 or EntityContext.Gen2),
            EntityContext.Gen3 => pk.Context is not EntityContext.Gen3,
            EntityContext.Gen4 => pk.Context is not EntityContext.Gen4,
            _ => enc.Version is GameVersion.GO && pk.MetLocation is Locations.GO8,
        };
        if (!wasMetLost)
            return;

        if (new LegalityAnalysis(pk).Info.Moves.All(z => z.Valid))
            return; // Not an issue with moves

        pk.MetLevel = current;
        var range = Math.Min(3, current - met);
        for (int i = 0; i < range; i++)
        {
            var la = new LegalityAnalysis(pk);
            if (la.Info.Moves.All(z => z.Valid))
                return;
            pk.MetLevel--;
        }

        pk.MetLevel = met; // Set back to normal if nothing legalized
    }

    /// <summary>
    /// Edge case memes for weird properties that I have no interest in setting for other Pokémon.
    /// </summary>
    /// <param name="pk">Pokémon to edit</param>
    /// <param name="enc">Encounter the <see cref="pk"/> originated rom</param>
    private static void FixEdgeCases(this PKM pk, IEncounterTemplate enc)
    {
        if (pk.Nickname.Length == 0)
            pk.ClearNickname();

        // Shiny Manaphy Egg
        if (enc is MysteryGift { Species: (int)Species.Manaphy, Generation: 4 } && pk.IsShiny)
        {
            pk.EggLocation = Locations.LinkTrade4;
            if (pk.Format != 4)
                return;

            pk.MetLocation = pk.HGSS ? Locations.HatchLocationHGSS : Locations.HatchLocationDPPt;
        }

        // CXD only has a male trainer
        if (pk is { Version: GameVersion.CXD, OriginalTrainerGender: (int)Gender.Female }) // Colosseum and XD are sexist games.
            pk.OriginalTrainerGender = (int)Gender.Male;

        // VC Games are locked to console region (modify based on language)
        if (pk is PK7 { Generation: <= 2 } pk7)
            pk7.FixVCRegion();

        // Vivillon pattern fixes if necessary
        if (pk is IGeoTrack && pk.Species is (int)Species.Vivillon or (int)Species.Spewpa or (int)Species.Scatterbug)
            pk.FixVivillonRegion();
    }

    /// <summary>
    /// Fix region locked VCs for PK7s
    /// </summary>
    /// <param name="pk7">PK7 to fix</param>
    public static void FixVCRegion(this PK7 pk7)
    {
        var valid = Locale3DS.IsRegionLockedLanguageValidVC(pk7.ConsoleRegion, pk7.Language);
        if (valid)
            return;
        var result = GetVivillonRegion((LanguageID)pk7.Language);
        if (result == default)
            return;
        pk7.ConsoleRegion = result.ConsoleRegion;
        pk7.Country = result.Country;
        pk7.Region = result.Region;
    }

    /// <summary>
    /// Handle search criteria for very specific encounters.
    /// </summary>
    public static EncounterCriteria SetSpecialCriteria(EncounterCriteria criteria, IEncounterTemplate enc, IBattleTemplate set)
    {
        if (enc is EncounterEgg && enc.Version is not (GameVersion.BD or GameVersion.SP))
            return criteria;
        if (enc is EncounterStatic8U)
            criteria = criteria with { Shiny = Shiny.Never };
        if(enc.Generation > 7)
            criteria = criteria with { Nature = Nature.Random };
        return enc.Species switch
        {
            (int)Species.Kartana when criteria is { Nature: Nature.Timid, IV_ATK: <= 21 } => // Beast Boost: Speed
                Revise(criteria, atk: criteria.IV_ATK),
            (int)Species.Stakataka when criteria is { Nature: Nature.Lonely, IV_DEF: <= 17 } => // Beast Boost: Attack
                Revise(criteria, def: criteria.IV_DEF, spe: criteria.IV_SPE),
            (int)Species.Pyukumuku when criteria is { IV_DEF: 0, IV_SPD: 0 } && set.Ability == (int)Ability.InnardsOut =>
                Revise(criteria, def: criteria.IV_DEF, spd: criteria.IV_SPD),
            (int)Species.Unown when enc.Generation is 4 => criteria with { Form = (sbyte)set.Form},

            _ => Revise(criteria, atk: criteria.IV_ATK == 0 ? (sbyte)0 : (sbyte)-1, spe: criteria.IV_SPE == 0 ? (sbyte)0 : (sbyte)-1),
        };
    }

    private static EncounterCriteria Revise(EncounterCriteria enc,
        sbyte hp = -1, sbyte atk = -1, sbyte def = -1, sbyte spa = -1, sbyte spd = -1, sbyte spe = -1)
        => enc with
        {
            IV_HP  = hp,
            IV_ATK = atk,
            IV_DEF = def,
            IV_SPA = spa,
            IV_SPD = spd,
            IV_SPE = spe,
        };

    /// <summary>
    /// Handle edge case Vivillon legality if the Trainer Data region is invalid
    /// </summary>
    /// <param name="pk">pkm to fix</param>
    public static void FixVivillonRegion(this PKM pk)
    {
        if (pk is not IGeoTrack g)
            return;

        var valid = Vivillon3DS.IsPatternValid(pk.Form, g.ConsoleRegion);
        if (valid)
            return;

        var (consoleRegion, region, country) = GetVivillonRegion(pk.Form);
        g.ConsoleRegion = consoleRegion;
        g.Region = region;
        g.Country = country;
    }

    private static (byte ConsoleRegion, byte Region, byte Country) GetVivillonRegion(int form) => form switch
    {
        // 5: JP
        // 7, 14: USA
        // else: EUR
        5       => (0, 0, 001),
        7 or 14 => (1, 0, 049),
        _       => (2, 0, 105),
    };

    private static (byte ConsoleRegion, byte Region, byte Country) GetVivillonRegion(LanguageID language) => language switch
    {
        LanguageID.German or LanguageID.Italian => (2, 0, 105),
        LanguageID.Japanese => (0, 0, 1),
        LanguageID.Korean => (5, 0, 136),
        _ => (1, 0, 49),
    };

    /// <summary>
    /// Wrapper function for GetLegalFromTemplate but with a Timeout
    /// </summary>
    public static AsyncLegalizationResult GetLegalFromTemplateTimeout(this ITrainerInfo dest, PKM template, IBattleTemplate set, bool nativeOnly = false)
    {
        AsyncLegalizationResult GetLegal()
        {
            try
            {
                if (!EnableDevMode && ALMVersion.GetIsMismatch())
                    return new(template, LegalizationResult.VersionMismatch);

                var res = dest.GetLegalFromTemplate(template, set, out var s, nativeOnly);
                return new AsyncLegalizationResult(res, s);
            }
            catch (MissingMethodException)
            {
                return new AsyncLegalizationResult(template, LegalizationResult.VersionMismatch);
            }
        }

        var task = Task.Run(GetLegal);
        var first = task.TimeoutAfter(new TimeSpan(0, 0, 0, Timeout))?.Result;
        return first ?? new AsyncLegalizationResult(template, LegalizationResult.Timeout);
    }

    public static AsyncLegalizationResult AsyncGetLegalFromTemplateTimeout(this ITrainerInfo dest, PKM template, IBattleTemplate set, bool nativeOnly = false) =>
        GetLegalFromTemplateTimeoutAsync(dest, template, set, nativeOnly).ConfigureAwait(false).GetAwaiter().GetResult();

    public static async Task<AsyncLegalizationResult> GetLegalFromTemplateTimeoutAsync(this ITrainerInfo dest, PKM template, IBattleTemplate set, bool nativeOnly = false)
    {
        AsyncLegalizationResult GetLegal()
        {
            try
            {
                if (!EnableDevMode && ALMVersion.GetIsMismatch())
                    return new(template, LegalizationResult.VersionMismatch);

                var res = dest.GetLegalFromTemplate(template, set, out var s, nativeOnly);
                return new AsyncLegalizationResult(res, s);
            }
            catch (MissingMethodException)
            {
                return new AsyncLegalizationResult(template, LegalizationResult.VersionMismatch);
            }
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Timeout));
        try
        {
            return await Task.Run(GetLegal, cts.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return new AsyncLegalizationResult(template, LegalizationResult.Timeout);
        }
    }

    /// <summary>
    /// Async Related actions for global timer.
    /// </summary>
    public record AsyncLegalizationResult(PKM Created, LegalizationResult Status);

    private static async Task<AsyncLegalizationResult?>? TimeoutAfter(this Task<AsyncLegalizationResult> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var delay = Task.Delay(timeout, cts.Token);
        var completedTask = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (completedTask != task)
            return null;

        return await task.ConfigureAwait(false); // will re-fire exception if present
    }

    private static GameVersion[] GetPairedVersions(GameVersion version, IEnumerable<GameVersion> versionlist)
    {
        var group = version switch
        {
            GameVersion.RD or GameVersion.C => version,
            _ => GameUtil.GetMetLocationVersionGroup(version),
        };

        var res = group.GetVersionsWithinRange(versionlist.ToArray());
        return res.Length > 0 ? res : [version];
    }

    public static PKM GenerateEgg(this ITrainerInfo dest, ShowdownSet set, out LegalizationResult result)
    {
        result = LegalizationResult.Failed;
        var template = EntityBlank.GetBlank(dest.Generation);
        template.ApplySetDetails(set);
        var destVer = dest.Version;
        if (destVer <= 0 && dest is SaveFile s)
            destVer = s.Version;
        if (dest.Generation <= 2)
            template.EXP = 0; // no relearn moves in gen 1/2 so pass level 1 to generator
        var encounters = GetAllEncounters(template, template.Moves, [dest.Version]);
        encounters = encounters.Where(z => z.IsEgg);
        if (!encounters.Any())
        {
            result = LegalizationResult.Failed;
            return template;
        }
        var mutations = EncounterMutationUtil.GetSuggested(dest.Context, set.Level);
        var criteria = EncounterCriteria.GetCriteria(set, template.PersonalInfo, mutations);
        foreach (var enc in encounters)
        {
            criteria = SetSpecialCriteria(criteria, enc, set);

            // Create the PKM from the template.
            var raw = enc.GetPokemonFromEncounter(dest, criteria, set);
            raw.IsEgg = true;
            raw.CurrentFriendship = (byte)EggStateLegality.GetMinimumEggHatchCycles(raw);

            // if egg wasn't originally obtained by OT => Link Trade, else => None
            if (raw.Format >= 4)
            {
                var sav = dest;
                bool isTraded = sav.OT != raw.OriginalTrainerName || sav.TID16 != raw.TID16 || sav.SID16 != raw.SID16;
                var loc = isTraded
                    ? Locations.TradedEggLocation(sav.Generation, sav.Version)
                    : LocationEdits.GetNoneLocation(raw);
                raw.MetLocation = (ushort)loc;
            }
            else if (raw is PK3)
            {
                raw.Language = (int)LanguageID.Japanese; // japanese;
            }
            if (raw is PB8)
                raw.NicknameTrash.Clear();
            raw.IsNicknamed = EggStateLegality.IsNicknameFlagSet(raw);
            raw.Nickname = SpeciesName.GetEggName(raw.Language, raw.Format);

            // Wipe egg memories
            if (raw.Format >= 6)
                raw.ClearMemories();

            if (raw is PK9) // Eggs in S/V have a Version value of 0 until hatched.
                raw.Version = 0;
            if(new LegalityAnalysis(raw).Valid)
            {
                result = LegalizationResult.Regenerated;
                return raw;
            }
        }
        return template;
    }
}