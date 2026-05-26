using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models
{
    public enum VciSyncDirection
    {
        ProjectToWorkspace,
        WorkspaceToProject
    }

    public enum VciPlcScope
    {
        Blocks,
        BlocksAndTypes,
        AllSupported
    }

    public sealed class VciWorkspaceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public string? WorkspaceLanguage { get; set; }
        public int MappingCount { get; set; }
    }

    public sealed class VciMappingInfo
    {
        public string WorkspaceName { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string? DirectoryPath { get; set; }
        public string? FileNameWithoutExtension { get; set; }
        public string? FileFormat { get; set; }
        public string? Status { get; set; }
    }

    public sealed class VciSyncItemResult
    {
        public string ObjectName { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string ObjectPath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public bool Skipped { get; set; }
        public bool MappingCreated { get; set; }
        public string? Status { get; set; }
        public string? Message { get; set; }
    }

    public sealed class VciBulkSyncResult
    {
        public int CandidateCount { get; set; }
        public int SynchronizedCount { get; set; }
        public int MappedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public List<VciSyncItemResult> Items { get; set; } = new List<VciSyncItemResult>();
        public List<string> Errors { get; set; } = new List<string>();
    }
}
