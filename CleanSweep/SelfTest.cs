using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CleanSweep.Models;
using CleanSweep.Services;

namespace CleanSweep
{
    internal static class SelfTest
    {
        private static int _failures;

        public static int Run()
        {
            _failures = 0;
            string root = Path.Combine(Path.GetTempPath(), "CleanSweepSelfTest-" + Guid.NewGuid().ToString("N"));
            var log = new StringBuilder();
            int exitCode = 0;
            try
            {
                Directory.CreateDirectory(root);
                string a = Path.Combine(root, "a.txt");
                string b = Path.Combine(root, "b.txt");
                string c = Path.Combine(root, "unique.txt");
                File.WriteAllText(a, new string('A', 4096));
                File.WriteAllText(b, new string('A', 4096));
                File.WriteAllText(c, new string('B', 5000));

                var groups = DuplicateScanner.ScanRootsForTests(new[] { root }, 1024, false);
                Check(log, groups.Count == 1, "duplicate group count = " + groups.Count);
                if (groups.Count == 1)
                {
                    Check(log, groups[0].Files.Count == 2, "duplicate file count = " + groups[0].Files.Count);
                    Check(log, groups[0].KeepItem != null, "keep item selected");
                    Check(log, !groups[0].IsSelected, "duplicate group default unselected");
                    groups[0].IsSelected = true;
                    Check(log, groups[0].SelectedCount == 1, "selected duplicate count = " + groups[0].SelectedCount);
                }

                Check(log, PathSafety.CanDelete(a, new[] { root }, false), "fixture can be deleted");
                Check(log, !PathSafety.CanDelete(@"C:\Windows\System32\drivers\etc\hosts", new[] { @"C:\" }, false),
                    "system path is protected");

                var walk = FileWalker.EnumerateFiles(root, CancellationToken.None);
                Check(log, walk.Files.Count == 3, "file walker count = " + walk.Files.Count);
                Check(log, walk.Size > 0, "file walker size = " + walk.Size);

                var item = new FindingItem
                {
                    Id = "selftest.files",
                    Category = CategoryKeys.Temp,
                    RuleId = "selftest",
                    Title = "selftest",
                    DisplayPath = root,
                    Paths = new List<string> { a },
                    AllowedRoots = new List<string> { root },
                    Action = CleanAction.DeleteFiles,
                    Size = new FileInfo(a).Length,
                    FileCount = 1,
                    Risk = RiskLevel.Safe,
                    Basis = "selftest"
                };

                var result = new CleanService().CleanAsync(new[] { item }, null, CancellationToken.None).Result;
                Check(log, result.SuccessCount == 1, "clean success count = " + result.SuccessCount);
                Check(log, !File.Exists(a), "clean removed fixture file");
                if (!string.IsNullOrEmpty(result.LogPath))
                {
                    try { File.Delete(result.LogPath); } catch { }
                }
                if (!string.IsNullOrEmpty(result.TextLogPath))
                {
                    try { File.Delete(result.TextLogPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                exitCode = 2;
                log.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                if (_failures > 0 && exitCode == 0)
                {
                    exitCode = 1;
                }

                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }
                }
                catch
                {
                }

                try
                {
                    string output = Path.Combine(Path.GetTempPath(), "CleanSweepSelfTest-result.txt");
                    File.WriteAllText(output, (exitCode == 0 ? "PASS" : "FAIL") + Environment.NewLine + log,
                        new UTF8Encoding(false));
                }
                catch
                {
                }
            }

            return exitCode;
        }

        private static void Check(StringBuilder log, bool condition, string label)
        {
            if (!condition)
            {
                _failures++;
            }

            log.AppendLine((condition ? "PASS " : "FAIL ") + label);
        }
    }
}
