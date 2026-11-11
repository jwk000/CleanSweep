using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace CleanSweep.Services
{
    public static class ProcessRunner
    {
        public static ProcessResult Run(string fileName, string arguments, int timeoutMs, CancellationToken token)
        {
            var result = new ProcessResult();
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = new Process { StartInfo = start })
                {
                    var output = new StringBuilder();
                    var error = new StringBuilder();
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null) output.AppendLine(e.Data);
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null) error.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    int remaining = timeoutMs;
                    while (!process.HasExited)
                    {
                        if (token.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            result.Cancelled = true;
                            result.ExitCode = -1;
                            result.Output = output.ToString();
                            result.Error = error.ToString();
                            return result;
                        }

                        if (remaining <= 0)
                        {
                            try { process.Kill(); } catch { }
                            result.TimedOut = true;
                            result.ExitCode = -1;
                            result.Output = output.ToString();
                            result.Error = error.ToString();
                            return result;
                        }

                        int sleep = Math.Min(200, remaining);
                        process.WaitForExit(sleep);
                        remaining -= sleep;
                    }

                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                    result.Output = output.ToString();
                    result.Error = error.ToString();
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Error = ex.Message;
                return result;
            }
        }
    }

    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public bool TimedOut { get; set; }
        public bool Cancelled { get; set; }
        public bool Success
        {
            get { return ExitCode == 0 && !TimedOut && !Cancelled; }
        }
    }
}
