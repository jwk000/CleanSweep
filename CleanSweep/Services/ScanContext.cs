using System;
using System.Collections.Generic;
using System.Threading;
using CleanSweep.Models;

namespace CleanSweep.Services
{
    public sealed class ScanContext
    {
        public ScanContext()
        {
            Notes = new List<string>();
        }

        public AppSettings Settings { get; set; }
        public bool IsElevated { get; set; }
        public CancellationToken Token { get; set; }
        public Action<string> Status { get; set; }
        public Action<int, long> Found { get; set; }
        public Action<double> SubProgress { get; set; }
        public List<string> Notes { get; private set; }

        public void Report(string text)
        {
            var handler = Status;
            if (handler != null)
            {
                handler(text);
            }
        }

        public void ReportFound(int items, long bytes)
        {
            var handler = Found;
            if (handler != null)
            {
                handler(items, bytes);
            }
        }

        public void AddNote(string text)
        {
            if (!string.IsNullOrWhiteSpace(text) && !Notes.Contains(text))
            {
                Notes.Add(text);
            }
        }
    }
}
