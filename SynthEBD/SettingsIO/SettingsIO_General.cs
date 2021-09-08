﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace SynthEBD.SettingsIO
{
    public class SettingsIO_General
    {
        public static SynthEBD.Settings_General.Settings_General loadGeneralSettings()
        {
            Settings_General.Settings_General generalSettings = new Settings_General.Settings_General();

            string loadLoc = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Settings\\GeneralSettings.json");

            if (File.Exists(loadLoc))
            {
                string text = File.ReadAllText(loadLoc);
                generalSettings = JsonConvert.DeserializeObject<Settings_General.Settings_General>(text);
            }

            return generalSettings;
        }

        public static void saveGeneralSettings(SynthEBD.Settings_General.Settings_General settings)
        {
            string saveLoc = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Settings\\GeneralSettings.json");

            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);

            File.WriteAllText(saveLoc, json);
        }
    }
}