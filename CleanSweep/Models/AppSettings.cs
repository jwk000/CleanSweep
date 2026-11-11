using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CleanSweep.Models
{
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public int TempAgeHours { get; set; }
        [DataMember] public int DuplicateMinSizeKb { get; set; }
        [DataMember] public bool IncludeBrowserCache { get; set; }
        [DataMember] public bool IncludeCrashDumps { get; set; }
        [DataMember] public bool IncludeSystemCache { get; set; }
        [DataMember] public List<string> ExcludedDuplicateFolders { get; set; }

        public AppSettings()
        {
            TempAgeHours = 24;
            DuplicateMinSizeKb = 256;
            IncludeBrowserCache = true;
            IncludeCrashDumps = true;
            IncludeSystemCache = true;
            ExcludedDuplicateFolders = new List<string>();
        }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(Services.AppPaths.SettingsFile))
                {
                    return new AppSettings();
                }

                using (var stream = File.OpenRead(Services.AppPaths.SettingsFile))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    var settings = serializer.ReadObject(stream) as AppSettings;
                    return settings ?? new AppSettings();
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            Services.AppPaths.Ensure();
            using (var stream = File.Create(Services.AppPaths.SettingsFile))
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, this);
            }
        }

        public AppSettings Clone()
        {
            return new AppSettings
            {
                TempAgeHours = TempAgeHours,
                DuplicateMinSizeKb = DuplicateMinSizeKb,
                IncludeBrowserCache = IncludeBrowserCache,
                IncludeCrashDumps = IncludeCrashDumps,
                IncludeSystemCache = IncludeSystemCache,
                ExcludedDuplicateFolders = new List<string>(ExcludedDuplicateFolders ?? new List<string>())
            };
        }
    }

    public sealed class OptionItem
    {
        public int Value { get; set; }
        public string Label { get; set; }
    }
}
