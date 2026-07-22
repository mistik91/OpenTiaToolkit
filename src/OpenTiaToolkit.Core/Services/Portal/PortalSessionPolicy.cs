using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>Pure path, archive, authentication, result, and exception policies.</summary>
    internal static class PortalSessionPolicy
    {
        public static bool IsLocalSessionFile(string path) => Regex.IsMatch(path, @"\.als\d+$", RegexOptions.IgnoreCase);

        public static UmacDelegate? CreateUmacDelegate(ProjectAuthenticationOptions? authentication)
        {
            if (authentication == null) return null;
            return credentials =>
            {
                credentials.SetAttribute("Name", authentication.UserName);
                credentials.SetAttribute("Type", ParseUmacUserType(authentication.UserType));
                credentials.SetPassword(authentication.Password);
            };
        }

        public static UmacUserType ParseUmacUserType(string type) => string.Equals(type, "global", StringComparison.OrdinalIgnoreCase) ? UmacUserType.Global : UmacUserType.Project;
        public static ProjectArchivationMode ParseArchivationMode(string mode) => string.IsNullOrWhiteSpace(mode) ? ProjectArchivationMode.Compressed : (ProjectArchivationMode)Enum.Parse(typeof(ProjectArchivationMode), mode, true);
        public static string BuildDeviceTypeIdentifier(string orderNumber, string? version) => string.IsNullOrWhiteSpace(version) || orderNumber.IndexOf(version, StringComparison.OrdinalIgnoreCase) >= 0 ? orderNumber : $"{orderNumber}/{version}";

        public static (DirectoryInfo Directory, string Name) SplitProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Project path is required.", nameof(path));
            var full = Path.GetFullPath(path); var name = Path.GetFileNameWithoutExtension(full); var parent = Path.GetDirectoryName(full);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parent)) throw new ArgumentException("Project path must include a project name and parent directory.", nameof(path));
            return (new DirectoryInfo(parent), name);
        }

        public static (DirectoryInfo Directory, string Name, string ExpectedPath) SplitArchivePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Archive path is required.", nameof(path));
            var full = Path.GetFullPath(path); var parent = Path.GetDirectoryName(full); var name = Path.GetFileName(full);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Archive path must include a file name and parent directory.", nameof(path));
            return (new DirectoryInfo(parent), name, full);
        }

        public static ProjectLifecycleResult ToLifecycleResult(ProjectBase project) => new ProjectLifecycleResult(project.Name, PortalProjectStateInspector.TryGetProjectPath(project) ?? string.Empty);
        public static string FormatExceptionMessage(Exception ex) => ex.InnerException?.Message ?? ex.Message;
    }
}
