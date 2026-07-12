using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models;

public sealed class MultiuserServerInfo
{
    public string Alias { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}

public sealed class MultiuserServerProjectInfo
{
    public string ProjectName { get; set; } = string.Empty;
    public string ServerAlias { get; set; } = string.Empty;
}

public sealed class MultiuserLocalSessionInfo
{
    public int SessionId { get; set; }
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
}

public sealed class MultiuserMarkingInfo
{
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

public sealed class MultiuserSessionStatus
{
    public bool IsMultiuser { get; set; }
    public bool IsOpen { get; set; }
    public bool? IsUpToDate { get; set; }
    public int MarkingCount { get; set; }
    public int ConflictCount { get; set; }
    public IReadOnlyList<MultiuserMarkingInfo> Markings { get; set; } = new List<MultiuserMarkingInfo>();
}

public sealed class MultiuserCommitResult
{
    public int Revision { get; set; }
    public string Comment { get; set; } = string.Empty;
}
