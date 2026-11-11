using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CleanSweep.Services
{
    public static class PathSafety
    {
        private static readonly string[] AlwaysProtected =
        {
            @"\System32",
            @"\SysWOW64",
            @"\WinSxS",
            @"\Installer",
            @"\Boot",
            @"\Fonts",
            @"\assembly"
        };

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                string full = Path.GetFullPath(expanded);
                if (full.Length > 3)
                {
                    full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }

                return full;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool IsUnder(string path, string root)
        {
            string candidate = Normalize(path);
            string parent = Normalize(root);
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(parent))
            {
                return false;
            }

            if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string prefix = parent.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? parent
                : parent + Path.DirectorySeparatorChar;

            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsInsideOneOf(string path, IEnumerable<string> roots)
        {
            if (roots == null)
            {
                return false;
            }

            foreach (string root in roots)
            {
                if (IsUnder(path, root))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsDriveRoot(string path)
        {
            string normalized = Normalize(path);
            return normalized.Length == 2 && normalized[1] == ':' ||
                   normalized.Length == 3 && normalized[1] == ':' &&
                   (normalized[2] == Path.DirectorySeparatorChar || normalized[2] == Path.AltDirectorySeparatorChar);
        }

        public static bool IsProtected(string path)
        {
            string normalized = Normalize(path);
            if (string.IsNullOrEmpty(normalized) || IsDriveRoot(normalized))
            {
                return true;
            }

            string windows = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string programFiles = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            string programFilesX86 = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            string userProfile = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            string programData = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            string usersRoot = string.Empty;
            try
            {
                usersRoot = Normalize(Path.GetDirectoryName(userProfile));
            }
            catch
            {
            }

            if (string.Equals(normalized, windows, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, programFiles, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, programFilesX86, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, userProfile, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, programData, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(usersRoot) &&
                 string.Equals(normalized, usersRoot, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            foreach (string prefix in AlwaysProtected)
            {
                string full = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + prefix);
                if (string.Equals(normalized, full, StringComparison.OrdinalIgnoreCase) ||
                    IsUnder(normalized, full))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsReparsePoint(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        public static bool IsLocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':')
                {
                    return false;
                }

                return new DriveInfo(root).IsReady;
            }
            catch
            {
                return false;
            }
        }

        public static bool CanDelete(string path, IEnumerable<string> allowedRoots, bool allowDirectory)
        {
            string normalized = Normalize(path);
            if (string.IsNullOrEmpty(normalized) || IsProtected(normalized))
            {
                return false;
            }

            if (!IsInsideOneOf(normalized, allowedRoots))
            {
                return false;
            }

            if (!allowDirectory && Directory.Exists(normalized))
            {
                return false;
            }

            return File.Exists(normalized) || Directory.Exists(normalized);
        }

        public static void TryClearReadOnly(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch
            {
                // The delete attempt will report the real error.
            }
        }
    }

    public sealed class FileWalkResult
    {
        public FileWalkResult()
        {
            Files = new List<string>();
        }

        public List<string> Files { get; private set; }
        public long Size { get; set; }
        public bool AccessLimited { get; set; }
    }

    public static class FileWalker
    {
        public static FileWalkResult EnumerateFiles(string root, Func<string, bool> directoryFilter,
            Func<string, bool> fileFilter, System.Threading.CancellationToken token, int maxFiles = int.MaxValue)
        {
            var result = new FileWalkResult();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return result;
            }

            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0 && result.Files.Count < maxFiles)
            {
                token.ThrowIfCancellationRequested();
                string dir = stack.Pop();
                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(dir);
                }
                catch
                {
                    result.AccessLimited = true;
                    continue;
                }

                foreach (string entry in entries)
                {
                    if (result.Files.Count >= maxFiles)
                    {
                        break;
                    }

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch
                    {
                        result.AccessLimited = true;
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (directoryFilter == null || directoryFilter(entry))
                        {
                            stack.Push(entry);
                        }
                    }
                    else
                    {
                        if (fileFilter != null && !fileFilter(entry))
                        {
                            continue;
                        }

                        try
                        {
                            var info = new FileInfo(entry);
                            result.Size += info.Length;
                            result.Files.Add(entry);
                        }
                        catch
                        {
                            result.AccessLimited = true;
                        }
                    }
                }
            }

            return result;
        }

        public static FileWalkResult EnumerateFiles(string root, System.Threading.CancellationToken token,
            int maxFiles = int.MaxValue)
        {
            return EnumerateFiles(root, null, null, token, maxFiles);
        }

        public static bool TryDeleteEmptyDirectoryTree(string root, System.Threading.CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return false;
            }

            bool allRemoved = true;
            var dirs = new List<string>();
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string dir = stack.Pop();
                dirs.Add(dir);
                try
                {
                    foreach (string child in Directory.GetDirectories(dir))
                    {
                        if (!PathSafety.IsReparsePoint(child))
                        {
                            stack.Push(child);
                        }
                    }
                }
                catch
                {
                    allRemoved = false;
                }
            }

            foreach (string dir in dirs.OrderByDescending(d => d.Length))
            {
                if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                    {
                        Directory.Delete(dir, false);
                    }
                }
                catch
                {
                    allRemoved = false;
                }
            }

            return allRemoved;
        }

        public static void DeleteDirectoryTree(string root, System.Threading.CancellationToken token)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            token.ThrowIfCancellationRequested();
            foreach (string entry in Directory.GetFileSystemEntries(root))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry);
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                bool isLink = (attributes & FileAttributes.ReparsePoint) != 0;

                if (isDirectory && !isLink)
                {
                    DeleteDirectoryTree(entry, token);
                }
                else
                {
                    if (isDirectory)
                    {
                        Directory.Delete(entry, false);
                    }
                    else
                    {
                        PathSafety.TryClearReadOnly(entry);
                        File.Delete(entry);
                    }
                }
            }

            Directory.Delete(root, false);
        }
    }
}
