using System;
using System.Collections.Generic;
using System.Security;

namespace OpenTiaToolkit.Core.Models;

/// <summary>
/// Represents information about a running TIA Portal instance.
/// </summary>
public class TiaPortalInstance
{
    public TiaPortalInstance(int processId, string? version, bool isAttached)
    {
        ProcessId = processId;
        Version = version;
        IsAttached = isAttached;
    }

    /// <summary>Gets the process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the TIA Portal version.</summary>
    public string? Version { get; }

    /// <summary>Gets whether this instance is currently attached.</summary>
    public bool IsAttached { get; }
}

/// <summary>
/// Represents a node in the project tree structure.
/// </summary>
public class ProjectTreeNode
{
    public ProjectTreeNode(string name, string type, string path, IEnumerable<ProjectTreeNode> children)
    {
        Name = name;
        Type = type;
        Path = path;
        Children = new List<ProjectTreeNode>(children);
    }

    /// <summary>Gets the node name.</summary>
    public string Name { get; }

    /// <summary>Gets the node type (e.g., "Device", "Folder", "PlcSoftware").</summary>
    public string Type { get; }

    /// <summary>Gets the full path to the node.</summary>
    public string Path { get; }

    /// <summary>Gets the child nodes.</summary>
    public IReadOnlyList<ProjectTreeNode> Children { get; }
}

/// <summary>
/// Represents information about a TIA Portal project.
/// </summary>
public class ProjectInfo
{
    public ProjectInfo(string name, string path, string? version, DateTime? created)
    {
        Name = name;
        Path = path;
        Version = version;
        Created = created;
    }

    /// <summary>Gets the project name.</summary>
    public string Name { get; }

    /// <summary>Gets the full path to the project file.</summary>
    public string Path { get; }

    /// <summary>Gets the TIA Portal version.</summary>
    public string? Version { get; }

    /// <summary>Gets when the project was created.</summary>
    public DateTime? Created { get; }
}

/// <summary>
/// Credentials used when Siemens Openness requests UMAC authentication.
/// </summary>
public class ProjectAuthenticationOptions
{
    public ProjectAuthenticationOptions(string userName, SecureString password, string userType)
    {
        UserName = userName;
        Password = password;
        UserType = userType;
    }

    /// <summary>Gets the UMAC user name.</summary>
    public string UserName { get; }

    /// <summary>Gets the UMAC password.</summary>
    public SecureString Password { get; }

    /// <summary>Gets the UMAC user type, such as Project or Global.</summary>
    public string UserType { get; }
}

/// <summary>
/// Result of creating or retrieving a project.
/// </summary>
public class ProjectLifecycleResult
{
    public ProjectLifecycleResult(string name, string path)
    {
        Name = name;
        Path = path;
    }

    /// <summary>Gets the project name.</summary>
    public string Name { get; }

    /// <summary>Gets the full path to the project.</summary>
    public string Path { get; }
}

/// <summary>
/// Result of archiving a project.
/// </summary>
public class ProjectArchiveResult
{
    public ProjectArchiveResult(string archivePath, string mode)
    {
        ArchivePath = archivePath;
        Mode = mode;
    }

    /// <summary>Gets the expected archive file path.</summary>
    public string ArchivePath { get; }

    /// <summary>Gets the archive mode.</summary>
    public string Mode { get; }
}

/// <summary>
/// Represents information about a device in a TIA Portal project.
/// </summary>
public class DeviceInfo
{
    public DeviceInfo(string name, string type, string? identification, string? ipAddress)
    {
        Name = name;
        Type = type;
        Identification = identification;
        IpAddress = ipAddress;
    }

    /// <summary>Gets the device name.</summary>
    public string Name { get; }

    /// <summary>Gets the device type (e.g., "PLC_1200", "HMI_Comfort").</summary>
    public string Type { get; }

    /// <summary>Gets the device identification.</summary>
    public string? Identification { get; }

    /// <summary>Gets the device IP address if configured.</summary>
    public string? IpAddress { get; }
}

/// <summary>
/// Represents detailed information about a device.
/// </summary>
public class DeviceItemInfo
{
    public DeviceItemInfo(string name, string type, string? identification)
    {
        Name = name;
        Type = type;
        Identification = identification;
    }

    /// <summary>Gets the device item name.</summary>
    public string Name { get; }

    /// <summary>Gets the device item type.</summary>
    public string Type { get; }

    /// <summary>Gets the device item identification.</summary>
    public string? Identification { get; }
}

/// <summary>
/// Information about a PLC device found in the project.
/// </summary>
public class PlcInfo
{
    /// <summary>Gets or sets the name of the PLC device.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the type/classification of the PLC.</summary>
    public string? Type { get; set; }

    /// <summary>Gets or sets the parent device name.</summary>
    public string? ParentDevice { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a confirmed PLC.</summary>
    public bool IsConfirmedPlc { get; set; }
}

/// <summary>
/// Information about an HMI device found in the project.
/// </summary>
public class HmiInfo
{
    /// <summary>Gets or sets the name of the HMI device.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the type/classification of the HMI.</summary>
    public string? Type { get; set; }

    /// <summary>Gets or sets the parent device name.</summary>
    public string? ParentDevice { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a confirmed HMI.</summary>
    public bool IsConfirmedHmi { get; set; }
}
