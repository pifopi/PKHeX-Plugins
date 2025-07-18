using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using AutoModPlugins.Properties;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
namespace AutoModPlugins
{
    public class TrainerTemplates : AutoModPlugin
    {
        public override string Name => "Generate Trainer Templates";
        public override int Priority => 2;
        private static readonly string ProcessPath = Environment.ProcessPath ?? string.Empty;
        private static readonly string TrainerPath = Path.Combine(Path.GetDirectoryName(ProcessPath) ?? string.Empty, "trainers");

        protected override void AddPluginControl(ToolStripDropDownItem modmenu)
        {
            var ctrl = new ToolStripMenuItem(Name) { Image = Resources.settings };
            ctrl.Click += CreateTrainerTemplates;
            ctrl.Name = "Menu_TrainerTemplates";
            modmenu.DropDownItems.Add(ctrl);
        }
        private void CreateTrainerTemplates(object? sender, EventArgs e)
        {
            if (!Directory.Exists(TrainerPath))
                Directory.CreateDirectory(TrainerPath);
            for (int i = 1; i < 52; i++)
            {
                try
                {
                    var gen = ((GameVersion)i).GetGeneration() == 0 ? 1 : ((GameVersion)i).GetGeneration();
                    var text = $"Pikachu\nOT: {TrainerSettings.DefaultOT}\nTID: {Random.Shared.Next(ushort.MaxValue)}\nSID: {Random.Shared.Next(ushort.MaxValue)}\n.Version={(GameVersion)i}";
                    var set = new RegenTemplate(new ShowdownSet(text), (byte)gen);
                    var temp = SaveUtil.GetBlankSAV((GameVersion)i, TrainerSettings.DefaultOT);
                    var result = temp.GetLegalFromSet(set);
                    File.WriteAllBytes(TrainerPath + "/" + result.Created.FileName, result.Created.EncryptedBoxData);
                }
                catch (Exception ex)
                {
                    continue;
                }
            }
        }

    }
}
