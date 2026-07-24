using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal static class PortalProjectStateInspector
    {
        internal static bool TryGetSingleOpenProject(
            TiaPortal portal,
            out LocalSession? selectedSession,
            out ProjectBase? selectedProject)
        {
            selectedSession = null;
            selectedProject = null;
            var count = 0;

            if (portal.LocalSessions != null)
            {
                foreach (var session in portal.LocalSessions)
                {
                    if (session.Project == null)
                    {
                        continue;
                    }

                    count++;
                    selectedSession = session;
                    selectedProject = session.Project;
                }
            }

            if (portal.Projects != null)
            {
                foreach (var project in portal.Projects)
                {
                    count++;
                    selectedSession = null;
                    selectedProject = project;
                }
            }

            return count == 1 && selectedProject != null;
        }

        internal static string DescribeOpenProjects(TiaPortal? portal)
        {
            if (portal == null)
            {
                return "none";
            }

            var descriptions = new List<string>();
            if (portal.Projects != null)
            {
                descriptions.AddRange(portal.Projects.Select(project => TryGetProjectPath(project) ?? project.Name));
            }

            if (portal.LocalSessions != null)
            {
                descriptions.AddRange(portal.LocalSessions
                    .Where(session => session.Project != null)
                    .Select(session => TryGetProjectPath(session.Project) ?? session.Project.Name));
            }

            return descriptions.Count == 0
                ? "none"
                : string.Join(", ", descriptions.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        internal static bool PortalContainsProject(TiaPortal? portal, string? projectPath, string? projectName)
        {
            if (portal == null)
            {
                return false;
            }

            bool Matches(ProjectBase candidate)
            {
                var candidatePath = TryGetProjectPath(candidate);
                if (!string.IsNullOrWhiteSpace(projectPath) && !string.IsNullOrWhiteSpace(candidatePath))
                {
                    return PathEquals(candidatePath, projectPath);
                }

                return !string.IsNullOrWhiteSpace(projectName) &&
                       string.Equals(candidate.Name, projectName, StringComparison.OrdinalIgnoreCase);
            }

            if (portal.Projects != null && portal.Projects.Any(Matches))
            {
                return true;
            }

            return portal.LocalSessions != null && portal.LocalSessions.Any(session =>
                session.Project != null && Matches(session.Project));
        }

        internal static string? TryGetProjectPath(ProjectBase? project)
        {
            try
            {
                return project?.Path?.FullName;
            }
            catch
            {
                return null;
            }
        }

        internal static string NormalizeProjectPath(string projectPath)
        {
            return Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal static bool PathEquals(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                NormalizeProjectPath(left!),
                NormalizeProjectPath(right!),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ProjectMatches(
            string? candidatePath,
            string? candidateName,
            string normalizedProjectPath,
            string projectName)
        {
            if (!string.IsNullOrWhiteSpace(candidatePath))
            {
                return PathEquals(candidatePath, normalizedProjectPath);
            }

            return !string.IsNullOrWhiteSpace(candidateName) &&
                   !string.IsNullOrWhiteSpace(projectName) &&
                   string.Equals(candidateName, projectName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
