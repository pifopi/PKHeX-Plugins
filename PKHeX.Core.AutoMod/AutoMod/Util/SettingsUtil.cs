using System;
using System.Collections.Generic;
using System.Linq;

namespace PKHeX.Core.AutoMod
{
    public static class SettingsUtil
    {
        public static List<GameVersion> SanitizePriorityOrder(List<GameVersion> versionList) // Thank you Anubis and Koi
        {
            var validVersions = Enum.GetValues<GameVersion>()
                .Where(ver => ver is > GameVersion.Any and <= GameVersion.VL)
                .ToList();

            // Reverse the order of entries in validVersions since users will prefer latest games.
            validVersions.Reverse();

            foreach (var ver in validVersions)
            {
                if (!versionList.Contains(ver))
                    versionList.Add(ver); // Add any missing versions.
            }

            // Remove any versions in cfg.PriorityOrder that are not in validVersions and clean up duplicates in the process.
            return [.. versionList.Intersect(validVersions)];
        }
    }
}
