using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions
{
    /// <summary>
    /// Defines project text and language operations.
    /// </summary>
    public interface ITextService
    {
        /// <summary>
        /// Lists the project languages and active language settings.
        /// </summary>
        /// <returns>The project language settings, or null if no project is open.</returns>
        Task<ProjectLanguageSettingsInfo?> GetProjectLanguagesAsync();

        /// <summary>
        /// Exports project texts to a Siemens project text workbook.
        /// </summary>
        /// <param name="outputPath">Target workbook file path.</param>
        /// <param name="sourceCulture">Source language culture name.</param>
        /// <param name="targetCultures">Target language culture names.</param>
        /// <param name="categoryIdentifiers">Optional text category identifiers to export.</param>
        /// <param name="objectNames">Top-level project object names to export.</param>
        /// <returns>The export result, or null if no project is open.</returns>
        Task<ProjectTextExportResult?> ExportTextsAsync(
            string outputPath,
            string sourceCulture,
            IReadOnlyList<string> targetCultures,
            IReadOnlyList<string>? categoryIdentifiers = null,
            IReadOnlyList<string>? objectNames = null);

        /// <summary>
        /// Imports project texts from a Siemens project text workbook.
        /// </summary>
        /// <param name="inputPath">Source workbook path.</param>
        /// <param name="updateSourceLanguage">Whether the import may update source-language text.</param>
        /// <param name="dryRun">Whether to validate only and skip the Siemens import call.</param>
        /// <returns>The import result, or null if no project is open.</returns>
        Task<ProjectTextImportResult?> ImportTextsAsync(string inputPath, bool updateSourceLanguage, bool dryRun = true);
    }
}
