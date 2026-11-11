using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace CleanSweep.Services
{
    public sealed class ShortcutInfo
    {
        public string LinkPath { get; set; }
        public string TargetPath { get; set; }
        public string Arguments { get; set; }
        public string Error { get; set; }
    }

    public static class ShortcutResolver
    {
        public static List<ShortcutInfo> ResolveMany(IEnumerable<string> linkPaths)
        {
            var input = new List<string>(linkPaths ?? new string[0]);
            var output = new List<ShortcutInfo>();
            if (input.Count == 0)
            {
                return output;
            }

            Thread thread = new Thread(() =>
            {
                object shell = null;
                try
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null)
                    {
                        foreach (string link in input)
                        {
                            output.Add(new ShortcutInfo { LinkPath = link, Error = "WScript.Shell 不可用" });
                        }
                        return;
                    }

                    shell = Activator.CreateInstance(shellType);
                    dynamic dynamicShell = shell;
                    foreach (string link in input)
                    {
                        object shortcut = null;
                        try
                        {
                            shortcut = dynamicShell.CreateShortcut(link);
                            dynamic dynamicShortcut = shortcut;
                            output.Add(new ShortcutInfo
                            {
                                LinkPath = link,
                                TargetPath = Convert.ToString(dynamicShortcut.TargetPath),
                                Arguments = Convert.ToString(dynamicShortcut.Arguments)
                            });
                        }
                        catch (Exception ex)
                        {
                            output.Add(new ShortcutInfo { LinkPath = link, Error = ex.Message });
                        }
                        finally
                        {
                            if (shortcut != null && Marshal.IsComObject(shortcut))
                            {
                                Marshal.FinalReleaseComObject(shortcut);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    foreach (string link in input)
                    {
                        output.Add(new ShortcutInfo { LinkPath = link, Error = ex.Message });
                    }
                }
                finally
                {
                    if (shell != null && Marshal.IsComObject(shell))
                    {
                        Marshal.FinalReleaseComObject(shell);
                    }
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(60000);
            if (thread.IsAlive)
            {
                // The COM call is stuck. Return what we have; the abandoned thread
                // is a background thread and will not block application shutdown.
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in output)
                {
                    known.Add(item.LinkPath);
                }

                foreach (string link in input)
                {
                    if (!known.Contains(link))
                    {
                        output.Add(new ShortcutInfo { LinkPath = link, Error = "解析快捷方式超时" });
                    }
                }
            }

            return output;
        }
    }
}
