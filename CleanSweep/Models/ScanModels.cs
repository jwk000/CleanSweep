using System;

namespace CleanSweep.Models
{
    public sealed class ScanProgress
    {
        public string Stage { get; set; }
        public string Detail { get; set; }
        public double Percent { get; set; }
        public int FoundItems { get; set; }
        public long FoundBytes { get; set; }
    }

    public sealed class ScanResult
    {
        public ScanResult()
        {
            Items = new System.Collections.Generic.List<FindingItem>();
            DuplicateGroups = new System.Collections.Generic.List<DuplicateGroup>();
            Notes = new System.Collections.Generic.List<string>();
        }

        public System.Collections.Generic.List<FindingItem> Items { get; private set; }
        public System.Collections.Generic.List<DuplicateGroup> DuplicateGroups { get; private set; }
        public System.Collections.Generic.List<string> Notes { get; private set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public sealed class CleanProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string CurrentPath { get; set; }
        public double Percent { get; set; }
        public long FreedBytes { get; set; }
        public long RecycledBytes { get; set; }
    }

    public sealed class CleanRecord
    {
        public FindingItem Item { get; set; }
        public bool Success { get; set; }
        public bool Partial { get; set; }
        public string Error { get; set; }
        public string BackupPath { get; set; }
        public long FreedBytes { get; set; }
        public long RecycledBytes { get; set; }
        public int DeletedCount { get; set; }
        public bool Cancelled { get; set; }
    }

    public sealed class CleanResult
    {
        public CleanResult()
        {
            Records = new System.Collections.Generic.List<CleanRecord>();
            Notes = new System.Collections.Generic.List<string>();
        }

        public System.Collections.Generic.List<CleanRecord> Records { get; private set; }
        public System.Collections.Generic.List<string> Notes { get; private set; }
        public long FreedBytes { get; set; }
        public long RecycledBytes { get; set; }
        public int SuccessCount { get; set; }
        public int PartialCount { get; set; }
        public int FailureCount { get; set; }
        public int RecycledCount { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public string LogPath { get; set; }
        public string TextLogPath { get; set; }
        public string BackupDir { get; set; }
        public bool Cancelled { get; set; }
        public bool IsElevated { get; set; }
    }
}
