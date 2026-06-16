using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Provides project language and project text import/export operations.
    /// </summary>
    public class TextService : ITextService
    {
        private readonly IPortalService _portalService;
        private readonly EngineeringSessionContext _sessionContext;
        private readonly ILogger<TextService>? _logger;

        public TextService(IPortalService portalService, ILogger<TextService>? logger = null)
            : this(portalService, ResolveSessionContext(portalService), logger)
        {
        }

        internal TextService(
            IPortalService portalService,
            EngineeringSessionContext sessionContext,
            ILogger<TextService>? logger = null)
        {
            _portalService = portalService ?? throw new ArgumentNullException(nameof(portalService));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _logger = logger;
        }

        public Task<ProjectLanguageSettingsInfo?> GetProjectLanguagesAsync()
        {
            return Task.FromResult(GetProjectLanguages());
        }

        public Task<ProjectTextExportResult?> ExportTextsAsync(
            string outputPath,
            string sourceCulture,
            IReadOnlyList<string> targetCultures,
            IReadOnlyList<string>? categoryIdentifiers = null,
            IReadOnlyList<string>? objectNames = null)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path cannot be empty.", nameof(outputPath));
            }

            if (string.IsNullOrWhiteSpace(sourceCulture))
            {
                throw new ArgumentException("Source culture cannot be empty.", nameof(sourceCulture));
            }

            if (targetCultures == null || targetCultures.Count == 0)
            {
                throw new ArgumentException("At least one target culture is required.", nameof(targetCultures));
            }

            var project = GetCurrentProject();
            if (project == null)
            {
                return Task.FromResult<ProjectTextExportResult?>(null);
            }

            var fullOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath) ?? ".");

            var sourceLanguage = FindLanguage(project, sourceCulture);
            var targetLanguages = targetCultures.Select(culture => FindLanguage(project, culture)).ToList();
            var categories = ResolveCategories(project, categoryIdentifiers);
            var objects = ResolveObjects(project, objectNames);

            project.ExportProjectTexts(
                new FileInfo(fullOutputPath),
                sourceLanguage,
                targetLanguages,
                categories,
                objects);

            var fileInfo = new FileInfo(fullOutputPath);
            var result = new ProjectTextExportResult
            {
                OutputPath = fullOutputPath,
                BytesWritten = fileInfo.Exists ? fileInfo.Length : 0,
                SourceCulture = sourceLanguage.Culture.Name,
                TargetCultures = targetLanguages.Select(language => language.Culture.Name).ToList(),
                CategoryIdentifiers = categories.Select(category => category.Identifier).ToList(),
                ObjectNames = objects.Select(GetObjectDisplayName).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToList()
            };

            _logger?.LogInformation("Exported project texts to {OutputPath}", fullOutputPath);
            return Task.FromResult<ProjectTextExportResult?>(result);
        }

        public Task<ProjectTextImportResult?> ImportTextsAsync(string inputPath, bool updateSourceLanguage, bool dryRun = true)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("Input path cannot be empty.", nameof(inputPath));
            }

            var project = GetCurrentProject();
            if (project == null)
            {
                return Task.FromResult<ProjectTextImportResult?>(null);
            }

            var fullInputPath = Path.GetFullPath(inputPath);
            if (!File.Exists(fullInputPath))
            {
                throw new FileNotFoundException("Project text import file was not found.", fullInputPath);
            }

            var diagnostics = new List<string>();
            string? siemensResult = null;
            var importAttempted = false;
            if (dryRun)
            {
                diagnostics.Add("Dry run validated that the project is open and the workbook exists. Siemens Openness does not expose a separate text-import preflight.");
            }
            else
            {
                importAttempted = true;
                var result = project.ImportProjectTexts(new FileInfo(fullInputPath), updateSourceLanguage);
                siemensResult = result?.ToString();
            }

            return Task.FromResult<ProjectTextImportResult?>(new ProjectTextImportResult
            {
                InputPath = fullInputPath,
                DryRun = dryRun,
                UpdateSourceLanguage = updateSourceLanguage,
                ImportAttempted = importAttempted,
                SiemensResult = siemensResult,
                Diagnostics = diagnostics
            });
        }

        private ProjectLanguageSettingsInfo? GetProjectLanguages()
        {
            var project = GetCurrentProject();
            if (project == null)
            {
                return null;
            }

            var settings = project.LanguageSettings;
            var activeCultures = settings.ActiveLanguages
                .Cast<Language>()
                .Select(language => language.Culture.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var editingCulture = settings.EditingLanguage?.Culture.Name;
            var referenceCulture = settings.ReferenceLanguage?.Culture.Name;

            var languages = settings.Languages
                .Cast<Language>()
                .Select(language => CreateLanguageInfo(
                    language,
                    activeCultures.Contains(language.Culture.Name),
                    string.Equals(editingCulture, language.Culture.Name, StringComparison.OrdinalIgnoreCase),
                    string.Equals(referenceCulture, language.Culture.Name, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(language => language.CultureName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var infoByCulture = languages.ToDictionary(language => language.CultureName, StringComparer.OrdinalIgnoreCase);
            return new ProjectLanguageSettingsInfo
            {
                Languages = languages,
                ActiveLanguages = languages.Where(language => language.IsActive).ToList(),
                EditingLanguage = editingCulture != null && infoByCulture.TryGetValue(editingCulture, out var editingLanguage) ? editingLanguage : null,
                ReferenceLanguage = referenceCulture != null && infoByCulture.TryGetValue(referenceCulture, out var referenceLanguage) ? referenceLanguage : null
            };
        }

        private ProjectBase? GetCurrentProject()
        {
            return _sessionContext.Project ?? _portalService.Project;
        }

        private static Language FindLanguage(ProjectBase project, string cultureName)
        {
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(cultureName.Trim());
            }
            catch (CultureNotFoundException ex)
            {
                throw new ArgumentException($"Invalid culture name: {cultureName}", nameof(cultureName), ex);
            }

            var language = project.LanguageSettings.Languages.Find(culture);
            if (language == null)
            {
                throw new InvalidOperationException($"Project language is not available: {culture.Name}");
            }

            return language;
        }

        private static IReadOnlyList<TextCategory> ResolveCategories(ProjectBase project, IReadOnlyList<string>? categoryIdentifiers)
        {
            if (categoryIdentifiers == null || categoryIdentifiers.Count == 0)
            {
                return project.TextCategories.Cast<TextCategory>().ToList();
            }

            var categories = new List<TextCategory>();
            foreach (var identifier in categoryIdentifiers.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))
            {
                var category = project.TextCategories.Find(identifier);
                if (category == null)
                {
                    throw new InvalidOperationException($"Project text category was not found: {identifier}");
                }

                categories.Add(category);
            }

            return categories;
        }

        private static IReadOnlyList<IEngineeringObject> ResolveObjects(ProjectBase project, IReadOnlyList<string>? objectNames)
        {
            if (objectNames == null || objectNames.Count == 0)
            {
                throw new InvalidOperationException("No project export object names were provided. Provide one or more device or device-item names with --objects.");
            }

            var requested = objectNames
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToList();
            if (requested.Count == 0)
            {
                throw new InvalidOperationException("No project export object names were provided. Provide one or more device or device-item names with --objects.");
            }

            var matches = new List<IEngineeringObject>();
            foreach (var name in requested)
            {
                var match = EnumerateProjectObjects(project)
                    .FirstOrDefault(item => string.Equals(GetObjectDisplayName(item), name, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    throw new InvalidOperationException($"Project export object was not found: {name}");
                }

                matches.Add(match);
            }

            return matches;
        }

        private static IEnumerable<IEngineeringObject> EnumerateProjectObjects(ProjectBase project)
        {
            var devices = TiaReflectionHelper.GetProperty(project, "Devices") as System.Collections.IEnumerable;
            if (devices == null)
            {
                yield break;
            }

            foreach (var device in devices)
            {
                if (device is IEngineeringObject engineeringObject)
                {
                    yield return engineeringObject;
                }

                if (device is Device typedDevice)
                {
                    foreach (var deviceItem in typedDevice.DeviceItems)
                    {
                        yield return deviceItem;
                    }
                }
            }
        }

        private static string? GetObjectDisplayName(IEngineeringObject engineeringObject)
        {
            return TiaReflectionHelper.GetName(engineeringObject) ?? engineeringObject.ToString();
        }

        private static ProjectLanguageInfo CreateLanguageInfo(
            Language language,
            bool isActive,
            bool isEditingLanguage,
            bool isReferenceLanguage)
        {
            var culture = language.Culture;
            return new ProjectLanguageInfo
            {
                CultureName = culture.Name,
                EnglishName = culture.EnglishName,
                NativeName = culture.NativeName,
                DisplayName = culture.DisplayName,
                IsActive = isActive,
                IsEditingLanguage = isEditingLanguage,
                IsReferenceLanguage = isReferenceLanguage
            };
        }

        private static EngineeringSessionContext ResolveSessionContext(IPortalService portalService)
        {
            if (portalService is IEngineeringSessionContextAccessor accessor)
            {
                return accessor.SessionContext;
            }

            throw new ArgumentException(
                "TextService requires an IPortalService created by OpenTiaToolkit so it can share the active EngineeringSessionContext.",
                nameof(portalService));
        }
    }
}
