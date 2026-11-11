using System;
using System.Diagnostics;
using System.IO;

namespace CleanSweep.Services
{
    public static class AppPaths
    {
        private static readonly object Sync = new object();
        private static string _baseDir;

        public static string ExeDir
        {
            get
            {
                try
                {
                    return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                }
                catch
                {
                    return AppDomain.CurrentDomain.BaseDirectory;
                }
            }
        }

        public static string BaseDir
        {
            get
            {
                lock (Sync)
                {
                    if (_baseDir != null)
                    {
                        return _baseDir;
                    }

                    // Optional portable mode: put an empty file named "portable.flag"
                    // next to CleanSweep.exe. Otherwise data lives in LocalAppData.
                    bool portable = false;
                    try
                    {
                        portable = File.Exists(Path.Combine(ExeDir, "portable.flag"));
                    }
                    catch
                    {
                        portable = false;
                    }

                    _baseDir = portable
                        ? Path.Combine(ExeDir, "CleanSweepData")
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "CleanSweep");
                    return _baseDir;
                }
            }
        }

        public static string LogsDir
        {
            get { return Path.Combine(BaseDir, "Logs"); }
        }

        public static string BackupsDir
        {
            get { return Path.Combine(BaseDir, "Backups"); }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(BaseDir, "settings.json"); }
        }

        public static void Ensure()
        {
            Directory.CreateDirectory(BaseDir);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(BackupsDir);
        }
    }
}
