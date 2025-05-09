﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PKHeX.Core.Enhancements;

public sealed class PKMPreview(PKM p, GameStrings strings) : EntitySummary(p, strings)
{
    public static void ExportCSV(IEnumerable<PKMPreview> pklist, string path)
    {
        var sortedprev = pklist.OrderBy(p => p.Species).ToList();
        // Todo: Complete function. POC
        var sb = new StringBuilder();
        const string headers = "Nickname, Species, Nature, Gender, ESV, Hidden Power, Ability, Move 1, Move 2, Move 3, Move 4, Held Item, HP, ATK, DEF, SPA, SPD, SPE, Met Location, Egg Location, Ball, OT, Version, OT Language, Legal, Country, Region, 3DS Region, PID, EC, HP IVs, ATK IVs, DEF IVs, SPA IVs, SPD IVs, SPE IVs, EXP, Level, Markings, Handling Trainer, Met Level, Shiny, TID, SID, Friendship, Met Year, Met Month, Met Day";
        sb.AppendLine(headers);
        foreach (PKMPreview p in sortedprev)
        {
            var country = "N/A";
            var region = "N/A";
            var DSRegion = "N/A";
            if (p.Entity is IGeoTrack gt)
            {
                country = gt.Country.ToString();
                region = gt.Region.ToString();
                DSRegion = gt.ConsoleRegion.ToString();
            }
            sb.AppendJoin(",",
                p.Nickname,
                p.Species,
                p.Nature,
                p.Gender,
                p.ESV,
                p.HP_Type,
                p.Ability,
                p.Move1,
                p.Move2,
                p.Move3,
                p.Move4,
                p.HeldItem,
                p.HP,
                p.ATK,
                p.DEF,
                p.SPA,
                p.SPD,
                p.SPE,
                p.MetLoc,
                p.EggLoc,
                p.Ball,
                p.OT,
                p.Version,
                p.OTLang,
                p.Legal,
                country,
                region,
                DSRegion,
                p.PID,
                p.EC,
                p.IV_HP.ToString(),
                p.IV_ATK.ToString(),
                p.IV_DEF.ToString(),
                p.IV_SPA.ToString(),
                p.IV_SPD.ToString(),
                p.IV_SPE.ToString(),
                p.EXP.ToString(),
                p.Level.ToString(),
                p.NotOT,
                p.MetLevel.ToString(),
                p.IsShiny.ToString(),
                p.TID16.ToString(),
                p.SID16.ToString(),
                p.Friendship.ToString(),
                p.MetYear.ToString(),
                p.MetMonth.ToString(),
                p.MetDay.ToString()).AppendLine();
        }

        File.WriteAllText(Path.Combine(path, "boxdump.csv"), sb.ToString(), Encoding.UTF8);
    }
}
