using System;
using System.IO;
using System.Linq;

namespace PKHeX.Core.AutoMod;

/// <summary>
/// Logic to load <see cref="ITrainerInfo"/> from a saved text file.
/// </summary>
public static class TrainerSettings
{
    private static readonly string ProcessPath = Environment.ProcessPath ?? string.Empty;
    private static readonly TrainerDatabase Database = new();
    private static readonly string TrainerPath = Path.Combine(Path.GetDirectoryName(ProcessPath) ?? string.Empty , "trainers");
    private static readonly SimpleTrainerInfo DefaultFallback9 = new(GameVersion.VL) { Generation = 9 };
    private static readonly SimpleTrainerInfo DefaultFallback8 = new(GameVersion.SW) { Generation = 8 };
    private static readonly SimpleTrainerInfo DefaultFallback7 = new(GameVersion.UM) { Generation = 7 };
    
    private static ReadOnlySpan<GameVersion> FringeVersions =>
    [
        GameVersion.GG,
        GameVersion.BDSP,
        GameVersion.PLA,
    ];

    public static string DefaultOT { get; set; } = "ALM";
    public static ushort DefaultTID16 { get; set; } = 54321; // reverse of PKHeX defaults
    public static ushort DefaultSID16 { get; set; } = 12345; // reverse of PKHeX defaults

    public static ITrainerInfo DefaultFallback(byte gen = 8, LanguageID? lang = null)
    {
        var fallback = gen > 8 ? DefaultFallback9 : gen > 7 ? DefaultFallback8 : DefaultFallback7;
        return lang == null ? fallback : (ITrainerInfo)new SimpleTrainerInfo(fallback.Version) { Language = (int)lang };
    }

    public static ITrainerInfo DefaultFallback(GameVersion ver, LanguageID? lang = null)
    {
        if (!ver.IsValidSavedVersion())
            ver = GameUtil.GameVersions.First(z => ver.Contains(z));

        var ctx = ver.GetContext();
        var fallback = lang == null ? new SimpleTrainerInfo(ver) { Context = ctx, OT = DefaultOT, TID16 = DefaultTID16, SID16 = DefaultSID16 } : new SimpleTrainerInfo(ver) { Language = (int)lang, Context = ctx, OT = DefaultOT, TID16 = DefaultTID16, SID16 = DefaultSID16 };
        return fallback;
    }

    static TrainerSettings() => LoadTrainerDatabaseFromPath(TrainerPath);

    /// <summary>
    /// Loads possible <see cref="PKM"/> data from the path, and registers them to the <see cref="Database"/>.
    /// </summary>
    public static void LoadTrainerDatabaseFromPath(string path)
    {
        if (!Directory.Exists(path))
            return;

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
        var pk = BoxUtil.GetPKMsFromPaths(files, EntityContext.None);
        foreach (var f in pk)
            Database.RegisterCopy(f);
    }

    /// <summary>
    /// Gets a possible Trainer Data for the requested <see cref="generation"/>.
    /// </summary>
    /// <param name="generation">Generation of origin requested.</param>
    /// <param name="ver"></param>
    /// <param name="fallback">Fallback trainer data if no new parent is found.</param>
    /// <param name="lang">Language to request for</param>
    /// <returns>Parent trainer data that originates from the <see cref="PKM.Version"/>. If none found, will return the <see cref="fallback"/>.</returns>
    public static ITrainerInfo GetSavedTrainerData(byte generation, GameVersion ver = GameVersion.Any, ITrainerInfo? fallback = null, LanguageID? lang = null)
    {
        bool isSpecialVersion = IsSpecialVersion(ver);
        if (isSpecialVersion)
        {
            var tr = ver switch
            {
                GameVersion.BD => GameVersion.SP,
                GameVersion.SP => GameVersion.BD,
                GameVersion.GE => GameVersion.GP,
                GameVersion.GP => GameVersion.GE,
                _ => GameVersion.PLA,
            };
            var trainer = Database.GetTrainer(tr, lang);
            if (trainer is not null)
                return trainer;
        }
        else
        {
            var trainer = Database.GetTrainerFromGen(generation, lang);
            if (trainer is not null)
                return trainer;
        }

        if (fallback == null)
            return isSpecialVersion ? DefaultFallback(ver, lang) : DefaultFallback(generation, lang);

        if (lang == null)
            return fallback;

        return lang == (LanguageID)fallback.Language ? fallback : isSpecialVersion ? DefaultFallback(ver, lang) : DefaultFallback(generation, lang);
    }

    private static bool IsSpecialVersion(GameVersion ver)
    {
        foreach (var fringe in FringeVersions)
        {
            if (fringe.Contains(ver))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets a possible Trainer Data for the requested <see cref="version"/>.
    /// </summary>
    /// <param name="version">Version of origin requested.</param>
    /// <param name="lang">Language to request for</param>
    /// <returns>Parent trainer data that originates from the <see cref="PKM.Version"/>. If none found, will return the <see cref="fallback"/>.</returns>
    public static ITrainerInfo GetSavedTrainerData(GameVersion version, LanguageID? lang = null)
    {
        var byVer = Database.GetTrainer(version, lang);
        return byVer ?? DefaultFallback(version, lang);
    }

    /// <summary>
    /// Gets a possible Trainer Data for the provided <see cref="pk"/>.
    /// </summary>
    /// <param name="pk">Pok�mon that will receive the trainer details.</param>
    /// <param name="template_save">Fallback trainer data if no new parent is found.</param>
    /// <param name="lang">Language to request for</param>
    /// <returns>Parent trainer data that originates from the <see cref="PKM.Version"/>. If none found, will return the <see cref="template_save"/>.</returns>
    public static ITrainerInfo GetSavedTrainerData(PKM pk, ITrainerInfo template_save, LanguageID? lang = null)
    {
        byte origin = pk.Generation;
        byte format = pk.Format;
        return format != origin ? GetSavedTrainerData(format, template_save.Version, fallback: template_save, lang: lang) : GetSavedTrainerData((byte)origin, pk.Version, template_save, lang);
    }

    /// <summary>
    /// Registers the Trainer Data to the <see cref="Database"/>.
    /// </summary>
    /// <param name="tr">Trainer Data</param>
    public static void Register(ITrainerInfo tr) => Database.Register(tr);

    /// <summary>
    /// Clears the Trainer Data in the <see cref="Database"/>.
    /// </summary>
    public static void Clear() => Database.Clear();
}
