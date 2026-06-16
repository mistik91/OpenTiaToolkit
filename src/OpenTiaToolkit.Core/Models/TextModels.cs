using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models
{
    public sealed class ProjectLanguageSettingsInfo
    {
        public IReadOnlyList<ProjectLanguageInfo> Languages { get; set; } = new List<ProjectLanguageInfo>();

        public IReadOnlyList<ProjectLanguageInfo> ActiveLanguages { get; set; } = new List<ProjectLanguageInfo>();

        public ProjectLanguageInfo? EditingLanguage { get; set; }

        public ProjectLanguageInfo? ReferenceLanguage { get; set; }
    }

    public sealed class ProjectLanguageInfo
    {
        public string CultureName { get; set; } = string.Empty;

        public string EnglishName { get; set; } = string.Empty;

        public string NativeName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public bool IsEditingLanguage { get; set; }

        public bool IsReferenceLanguage { get; set; }
    }

    public sealed class ProjectTextExportResult
    {
        public string OutputPath { get; set; } = string.Empty;

        public string Format { get; set; } = "xlsx";

        public long BytesWritten { get; set; }

        public string SourceCulture { get; set; } = string.Empty;

        public IReadOnlyList<string> TargetCultures { get; set; } = new List<string>();

        public IReadOnlyList<string> CategoryIdentifiers { get; set; } = new List<string>();

        public IReadOnlyList<string> ObjectNames { get; set; } = new List<string>();
    }

    public sealed class ProjectTextImportResult
    {
        public string InputPath { get; set; } = string.Empty;

        public bool DryRun { get; set; }

        public bool UpdateSourceLanguage { get; set; }

        public bool ImportAttempted { get; set; }

        public string? SiemensResult { get; set; }

        public IReadOnlyList<string> Diagnostics { get; set; } = new List<string>();
    }
}
