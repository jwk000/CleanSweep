using System;
using System.IO;
using System.Threading;
using CleanSweep.Models;

namespace CleanSweep.Services
{
    public static class RegistryBackup
    {
        public static string Export(RegistryTarget target, string tag, CancellationToken token)
        {
            if (target == null || !target.IsSupportedUninstallKey)
            {
                return null;
            }

            AppPaths.Ensure();
            string safeTag = string.IsNullOrWhiteSpace(tag) ? "uninstall-entry" : MakeSafe(tag);
            string file = Path.Combine(AppPaths.BackupsDir,
                safeTag + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".reg");

            string arguments = "export \"" + target.FullPath + "\" \"" + file + "\" /y";
            var result = ProcessRunner.Run("reg.exe", arguments, 45000, token);
            if (!result.Success || !File.Exists(file) || new FileInfo(file).Length < 32)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }

                return null;
            }

            return file;
        }

        private static string MakeSafe(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            value = value.Trim();
            if (value.Length > 60)
            {
                value = value.Substring(0, 60);
            }

            return string.IsNullOrWhiteSpace(value) ? "entry" : value;
        }
    }
}
