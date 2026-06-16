using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models;

public sealed class GlobalLibraryInfoModel
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? LibraryType { get; set; }
    public bool IsOpen { get; set; }
    public bool IsReadOnly { get; set; }
    public bool? IsModified { get; set; }
    public string? Version { get; set; }
}

public sealed class LibraryMasterCopyInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string? Author { get; set; }
    public DateTime? CreationDate { get; set; }
    public string? ObjectType { get; set; }
}

public sealed class CopyFromMasterCopyResult
{
    public bool Success { get; set; }
    public bool DryRun { get; set; }
    public bool Applied { get; set; }
    public string Message { get; set; } = string.Empty;
    public string LibraryScope { get; set; } = string.Empty;
    public string? GlobalLibrary { get; set; }
    public string MasterCopyPath { get; set; } = string.Empty;
    public string PlcName { get; set; } = string.Empty;
    public string? TargetGroupPath { get; set; }
    public string? CreatedName { get; set; }
    public string? CreatedType { get; set; }
}

public sealed class LibraryTypeInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public Guid? Guid { get; set; }
    public string? Namespace { get; set; }
    public string? Author { get; set; }
    public bool? DoNotUse { get; set; }
    public bool? SetForUpdate { get; set; }
    public string? Status { get; set; }
    public int VersionCount { get; set; }
}

public sealed class LibraryTypeVersionInfo
{
    public string TypeName { get; set; } = string.Empty;
    public string TypePath { get; set; } = string.Empty;
    public Guid? Guid { get; set; }
    public string? VersionNumber { get; set; }
    public bool? IsDefault { get; set; }
    public string? State { get; set; }
    public string? OriginalLibrary { get; set; }
    public string? Author { get; set; }
    public DateTime? ModifiedDate { get; set; }
}

public sealed class LibraryUpdateCheckResult
{
    public bool Success { get; set; }
    public string LibraryScope { get; set; } = string.Empty;
    public string? GlobalLibrary { get; set; }
    public bool IncludeUpToDate { get; set; }
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<LibraryUpdateCheckMessage> Messages { get; set; } = Array.Empty<LibraryUpdateCheckMessage>();
}

public sealed class LibraryUpdateCheckMessage
{
    public string Path { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyDictionary<string, string?> MessageParts { get; set; } = new Dictionary<string, string?>();
    public IReadOnlyList<LibraryUpdateCheckMessage> Messages { get; set; } = Array.Empty<LibraryUpdateCheckMessage>();
}
