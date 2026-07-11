using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration
{
    /// <summary>
    /// Integration tests for Unified HMI operations against a real TIA Portal project.
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [DoNotParallelize]
    public sealed class Test10HmiUnified
    {
        private ILoggerFactory? _loggerFactory;
        private ILogger? _logger;
        private IPortalService? _portalService;
        private IHmiService? _hmiService;

        [TestInitialize]
        public async Task TestInitialize()
        {
            if (string.IsNullOrWhiteSpace(Settings.UnifiedHmiProjectPath) ||
                !File.Exists(Settings.UnifiedHmiProjectPath))
            {
                Assert.Inconclusive(
                    $"Unified HMI test project not found. Set OPEN_TIA_TEST_HMI_PROJECT_PATH to a .ap21 project path. Current value: {Settings.UnifiedHmiProjectPath}");
            }

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            _logger = _loggerFactory.CreateLogger<Test10HmiUnified>();
            _portalService = TestHelpers.CreatePortalService(_loggerFactory);
            _hmiService = new HmiService(_portalService, _loggerFactory.CreateLogger<HmiService>());

            await _portalService.ConnectAsync();
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService, Settings.UnifiedHmiProjectPath, _logger);
            Assert.IsTrue(projectOpened, $"Failed to open Unified HMI test project: {Settings.UnifiedHmiProjectPath}");
        }

        [TestCleanup]
        public async Task TestCleanup()
        {
            if (_portalService?.IsConnected == true)
            {
                try
                {
                    await _portalService.CloseProjectAsync(false);
                    await _portalService.DisconnectAsync();
                }
                catch
                {
                    // Ignore cleanup errors.
                }
            }

            _portalService?.Dispose();
            _loggerFactory?.Dispose();
        }

        [TestMethod]
        public async Task Test_1001_ListHmiScreens_ReturnsScreenList()
        {
            var command = new ListHmiScreensCommand(_hmiService!);
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                { "hmi-name", Settings.UnifiedHmiName }
            });

            var result = await command.ExecuteAsync(args, _logger!);

            Assert.IsTrue(result.Success, $"Failed to list Unified HMI screens: {result.Error}");
            Assert.IsNotNull(result.Data, "Expected screen data");

            var screens = result.Data as IEnumerable<HmiScreenInfo>;
            Assert.IsNotNull(screens, "Expected a sequence of HmiScreenInfo");
            Assert.IsTrue(screens!.Any(), "Expected at least one screen in the Unified HMI project");
        }

        [TestMethod]
        public async Task Test_1002_CreateScreenItemAndPropertyRoundTrip_WorksCorrectly()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var screenPath = $"Codex/{nameof(Test10HmiUnified)}_{suffix}";
            var itemName = $"Btn_{suffix}";

            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var createScreenArgs = new CommandArgs(new Dictionary<string, object?>
            {
                { "hmi-name", Settings.UnifiedHmiName },
                { "screen-path", screenPath },
                { "width", 1280 },
                { "height", 720 }
            });

            var createScreenResult = await createScreenCommand.ExecuteAsync(createScreenArgs, _logger!);
            Assert.IsTrue(createScreenResult.Success, $"Failed to create Unified HMI screen: {createScreenResult.Error}");

            var createItemCommand = new CreateHmiItemCommand(_hmiService!);
            var createItemArgs = new CommandArgs(new Dictionary<string, object?>
            {
                { "hmi-name", Settings.UnifiedHmiName },
                { "screen-path", screenPath },
                { "item-type", "button" },
                { "item-name", itemName },
                { "left", 24 },
                { "top", 36 },
                { "width", 140 },
                { "height", 42 }
            });

            var createItemResult = await createItemCommand.ExecuteAsync(createItemArgs, _logger!);
            Assert.IsTrue(createItemResult.Success, $"Failed to create Unified HMI item: {createItemResult.Error}");

            var setPropertyCommand = new SetHmiItemPropertyCommand(_hmiService!);
            var setPropertyArgs = new CommandArgs(new Dictionary<string, object?>
            {
                { "hmi-name", Settings.UnifiedHmiName },
                { "screen-path", screenPath },
                { "item-name", itemName },
                { "property", "Visible" },
                { "value", "false" }
            });

            var setPropertyResult = await setPropertyCommand.ExecuteAsync(setPropertyArgs, _logger!);
            Assert.IsTrue(setPropertyResult.Success, $"Failed to update Unified HMI item property: {setPropertyResult.Error}");

            var setTextResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "property", "Text" },
                    { "value", "Codex button" }
                }),
                _logger!);
            Assert.IsTrue(setTextResult.Success, $"Failed to update Unified HMI text property: {setTextResult.Error}");

            var getPropertiesCommand = new GetHmiItemPropertiesCommand(_hmiService!);
            var getPropertiesArgs = new CommandArgs(new Dictionary<string, object?>
            {
                { "hmi-name", Settings.UnifiedHmiName },
                { "screen-path", screenPath },
                { "item-name", itemName },
                { "properties", new[] { "Visible", "Left", "Top", "Width", "Height", "Text" } }
            });

            var getPropertiesResult = await getPropertiesCommand.ExecuteAsync(getPropertiesArgs, _logger!);
            Assert.IsTrue(getPropertiesResult.Success, $"Failed to read Unified HMI item properties: {getPropertiesResult.Error}");

            var properties = (getPropertiesResult.Data as IEnumerable<HmiItemPropertyInfo>)?.ToList();
            Assert.IsNotNull(properties, "Expected property data");
            Assert.IsTrue(properties!.Any(property => property.Name.Equals("Visible", StringComparison.OrdinalIgnoreCase) &&
                                                      string.Equals(property.Value, "False", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(properties.Any(property => property.Name.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
                                                     string.Equals(property.Value, "Codex button", StringComparison.Ordinal)));

            var getScreenInfoCommand = new GetHmiScreenInfoCommand(_hmiService!);
            var getScreenInfoArgs = new CommandArgs(new Dictionary<string, object?>
            {
                { "hmi-name", Settings.UnifiedHmiName },
                { "screen-path", screenPath }
            });

            var getScreenInfoResult = await getScreenInfoCommand.ExecuteAsync(getScreenInfoArgs, _logger!);
            Assert.IsTrue(getScreenInfoResult.Success, $"Failed to read Unified HMI screen info: {getScreenInfoResult.Error}");

            var screenInfo = getScreenInfoResult.Data as HmiScreenInfo;
            Assert.IsNotNull(screenInfo, "Expected screen info");
            Assert.AreEqual(1280, screenInfo!.Width);
            Assert.AreEqual(720, screenInfo.Height);
            Assert.IsTrue(screenInfo.Items.Any(item => item.Name == itemName));
        }

        [TestMethod]
        public async Task Test_1003_CopyScreenFromTemplate_RoundTripsItemState()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var templatePath = $"Codex/Templates/Template_{suffix}";
            var targetPath = $"Codex/Copies/Screen_{suffix}";
            var itemName = $"Btn_{suffix}";

            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var createScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", templatePath },
                    { "width", 1024 },
                    { "height", 600 }
                }),
                _logger!);
            Assert.IsTrue(createScreenResult.Success, $"Failed to create template screen: {createScreenResult.Error}");

            var createItemCommand = new CreateHmiItemCommand(_hmiService!);
            var createItemResult = await createItemCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", templatePath },
                    { "item-type", "button" },
                    { "item-name", itemName },
                    { "left", 100 },
                    { "top", 120 },
                    { "width", 180 },
                    { "height", 50 }
                }),
                _logger!);
            Assert.IsTrue(createItemResult.Success, $"Failed to create template item: {createItemResult.Error}");

            var setPropertyCommand = new SetHmiItemPropertyCommand(_hmiService!);
            var visibleResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", templatePath },
                    { "item-name", itemName },
                    { "property", "Visible" },
                    { "value", "false" }
                }),
                _logger!);
            Assert.IsTrue(visibleResult.Success, $"Failed to set template visibility: {visibleResult.Error}");

            var textResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", templatePath },
                    { "item-name", itemName },
                    { "property", "Text" },
                    { "value", "Hello Template" }
                }),
                _logger!);
            Assert.IsTrue(textResult.Success, $"Failed to set template button text: {textResult.Error}");

            var copyCommand = new CopyHmiScreenFromTemplateCommand(_hmiService!);
            var copyResult = await copyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "template-screen-path", templatePath },
                    { "target-screen-path", targetPath },
                    { "find-text", "Template" },
                    { "replace-text", "Copied" }
                }),
                _logger!);
            Assert.IsTrue(copyResult.Success, $"Failed to copy template screen: {copyResult.Error}");

            var copiedScreen = copyResult.Data as HmiScreenCopyResult;
            Assert.IsNotNull(copiedScreen, "Expected screen copy result");
            Assert.AreEqual(1, copiedScreen!.CopiedItemCount);
            Assert.AreEqual(targetPath, copiedScreen.TargetPath);
            Assert.IsTrue(copiedScreen.ReplacementApplied);

            var getScreenInfoCommand = new GetHmiScreenInfoCommand(_hmiService!);
            var targetScreenResult = await getScreenInfoCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", targetPath }
                }),
                _logger!);
            Assert.IsTrue(targetScreenResult.Success, $"Failed to read copied screen: {targetScreenResult.Error}");

            var targetScreen = targetScreenResult.Data as HmiScreenInfo;
            Assert.IsNotNull(targetScreen, "Expected copied screen info");
            Assert.AreEqual(1024, targetScreen!.Width);
            Assert.AreEqual(600, targetScreen.Height);
            Assert.IsTrue(targetScreen.Items.Any(item => item.Name == itemName));

            var getPropertiesCommand = new GetHmiItemPropertiesCommand(_hmiService!);
            var targetPropertiesResult = await getPropertiesCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", targetPath },
                    { "item-name", itemName },
                    { "properties", new[] { "Visible", "Left", "Top", "Width", "Height", "Text" } }
                }),
                _logger!);
            Assert.IsTrue(targetPropertiesResult.Success, $"Failed to read copied item properties: {targetPropertiesResult.Error}");

            var targetProperties = (targetPropertiesResult.Data as IEnumerable<HmiItemPropertyInfo>)?.ToList();
            Assert.IsNotNull(targetProperties, "Expected copied item property data");
            Assert.IsTrue(targetProperties!.Any(property => property.Name.Equals("Visible", StringComparison.OrdinalIgnoreCase) &&
                                                            string.Equals(property.Value, "False", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(targetProperties.Any(property => property.Name.Equals("Left", StringComparison.OrdinalIgnoreCase) &&
                                                           string.Equals(property.Value, "100", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(targetProperties.Any(property => property.Name.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
                                                           string.Equals(property.Value, "Hello Copied", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task Test_1004_ListTagsAndCreateTrend_WorksCorrectly()
        {
            var listTagsCommand = new ListHmiTagsCommand(_hmiService!);
            var listTagsResult = await listTagsCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName }
                }),
                _logger!);
            Assert.IsTrue(listTagsResult.Success, $"Failed to list HMI tags: {listTagsResult.Error}");

            var tags = (listTagsResult.Data as IEnumerable<HmiTagInfo>)?.ToList();
            Assert.IsNotNull(tags, "Expected HMI tag data");
            Assert.IsTrue(tags!.Any(), "Expected at least one HMI tag for trend configuration");

            var selectedTag = tags.FirstOrDefault(tag => !tag.HasMembers && tag.LoggingTagCount == 0) ??
                              tags.FirstOrDefault(tag => !tag.HasMembers) ??
                              tags.First();

            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var dataLogName = $"CodexTrend_{suffix}";
            var createTrendCommand = new CreateHmiTrendCommand(_hmiService!);

            var firstTrendResult = await createTrendCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "tag-path", selectedTag.Path },
                    { "datalog-name", dataLogName },
                    { "cyclic", true },
                    { "cycle", "T2s" }
                }),
                _logger!);
            Assert.IsTrue(firstTrendResult.Success, $"Failed to configure HMI trend: {firstTrendResult.Error}");

            var firstTrend = firstTrendResult.Data as HmiTrendInfo;
            Assert.IsNotNull(firstTrend, "Expected trend result");
            Assert.AreEqual(selectedTag.Path, firstTrend!.TagPath);
            Assert.AreEqual(dataLogName, firstTrend.DataLogName);
            Assert.AreEqual("Cyclic", firstTrend.LoggingMode);
            Assert.AreEqual("T2s", firstTrend.Cycle);
            Assert.IsNotNull(firstTrend.StorageFolder, "Expected the storage folder reported by TIA.");
            Assert.AreEqual(2000, firstTrend.LogMaxSize);
            Assert.AreEqual(365, firstTrend.DurationDays);
            Assert.AreEqual(500, firstTrend.SegmentMaxSize);

            if (selectedTag.LoggingTagCount == 0)
            {
                Assert.IsTrue(firstTrend.LoggingTagCreated, "Expected a new logging tag to be created for an unused tag.");
            }

            var secondTrendResult = await createTrendCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "tag-path", selectedTag.Path },
                    { "datalog-name", dataLogName },
                    { "cyclic", true },
                    { "cycle", "T2s" }
                }),
                _logger!);
            Assert.IsTrue(secondTrendResult.Success, $"Failed to reconfigure HMI trend: {secondTrendResult.Error}");

            var secondTrend = secondTrendResult.Data as HmiTrendInfo;
            Assert.IsNotNull(secondTrend, "Expected second trend result");
            Assert.IsFalse(secondTrend!.DataLogCreated, "Expected the data log to be reused on the second call.");
            Assert.IsFalse(secondTrend.LoggingTagCreated, "Expected the logging tag to be reused on the second call.");
        }

        [TestMethod]
        public async Task Test_1005_CopyScreenWindow_RetainsNestedProperties()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var referencedScreenPath = $"Codex/Referenced_{suffix}";
            var templatePath = $"Codex/WindowTemplate_{suffix}";
            var targetPath = $"Codex/WindowCopy_{suffix}";
            var itemName = $"Win_{suffix}";

            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var referencedScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", referencedScreenPath },
                    { "width", 640 },
                    { "height", 360 }
                }),
                _logger!);
            Assert.IsTrue(referencedScreenResult.Success, $"Failed to create referenced screen: {referencedScreenResult.Error}");

            var templateScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", templatePath },
                    { "width", 800 },
                    { "height", 480 }
                }),
                _logger!);
            Assert.IsTrue(templateScreenResult.Success, $"Failed to create screen window template: {templateScreenResult.Error}");

            var createItemCommand = new CreateHmiItemCommand(_hmiService!);
            var createItemResult = await createItemCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", templatePath },
                    { "item-type", "screenwindow" },
                    { "item-name", itemName },
                    { "left", 30 },
                    { "top", 40 },
                    { "width", 320 },
                    { "height", 200 }
                }),
                _logger!);
            Assert.IsTrue(createItemResult.Success, $"Failed to create screen window: {createItemResult.Error}");

            var setPropertyCommand = new SetHmiItemPropertyCommand(_hmiService!);
            var setScreenResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", templatePath },
                    { "item-name", itemName },
                    { "property", "Screen" },
                    { "value", referencedScreenPath }
                }),
                _logger!);
            Assert.IsTrue(setScreenResult.Success, $"Failed to set screen window target screen: {setScreenResult.Error}");

            var setCaptionResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", templatePath },
                    { "item-name", itemName },
                    { "property", "Caption.Text" },
                    { "value", "Window Template" }
                }),
                _logger!);
            Assert.IsTrue(setCaptionResult.Success, $"Failed to set screen window caption: {setCaptionResult.Error}");

            var copyCommand = new CopyHmiScreenFromTemplateCommand(_hmiService!);
            var copyResult = await copyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "template-screen-path", templatePath },
                    { "target-screen-path", targetPath },
                    { "find-text", "Template" },
                    { "replace-text", "Copied" }
                }),
                _logger!);
            Assert.IsTrue(copyResult.Success, $"Failed to copy screen window template: {copyResult.Error}");

            var getPropertiesCommand = new GetHmiItemPropertiesCommand(_hmiService!);
            var targetPropertiesResult = await getPropertiesCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", targetPath },
                    { "item-name", itemName },
                    { "properties", new[] { "Screen", "Caption.Text" } }
                }),
                _logger!);
            Assert.IsTrue(targetPropertiesResult.Success, $"Failed to read copied screen window properties: {targetPropertiesResult.Error}");

            var targetProperties = (targetPropertiesResult.Data as IEnumerable<HmiItemPropertyInfo>)?.ToList();
            Assert.IsNotNull(targetProperties, "Expected copied screen window property data");
            Assert.IsTrue(targetProperties!.Any(property => property.Name.Equals("Screen", StringComparison.OrdinalIgnoreCase) &&
                                                            string.Equals(property.Value, referencedScreenPath, StringComparison.Ordinal)));
            Assert.IsTrue(targetProperties.Any(property => property.Name.Equals("Caption.Text", StringComparison.OrdinalIgnoreCase) &&
                                                           string.Equals(property.Value, "Window Copied", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task Test_1006_JsonExportDiffAndImport_RoundTripsSupportedProperties()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var screenPath = $"Codex/Json_{suffix}";
            var itemName = $"Btn_{suffix}";
            var rootPath = Path.Combine(Settings.TestOutputPath, "HmiJson", suffix);
            var basePath = Path.Combine(rootPath, "base");
            var editedPath = Path.Combine(rootPath, "edited");
            var patchPath = Path.Combine(rootPath, "patch");
            var backupPath = Path.Combine(rootPath, "backup");

            Directory.CreateDirectory(rootPath);

            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var createScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "width", 1024 },
                    { "height", 600 }
                }),
                _logger!);
            Assert.IsTrue(createScreenResult.Success, $"Failed to create JSON test screen: {createScreenResult.Error}");

            var createItemCommand = new CreateHmiItemCommand(_hmiService!);
            var createItemResult = await createItemCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-type", "button" },
                    { "item-name", itemName },
                    { "left", 48 },
                    { "top", 60 },
                    { "width", 180 },
                    { "height", 44 }
                }),
                _logger!);
            Assert.IsTrue(createItemResult.Success, $"Failed to create JSON test item: {createItemResult.Error}");

            var setPropertyCommand = new SetHmiItemPropertyCommand(_hmiService!);
            var setTextResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "property", "Text" },
                    { "value", "Base Json Text" }
                }),
                _logger!);
            Assert.IsTrue(setTextResult.Success, $"Failed to set JSON test button text: {setTextResult.Error}");

            var exportCommand = new ExportHmiJsonCommand(_hmiService!);
            var exportResult = await exportCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "output-path", basePath },
                    { "screens", new[] { screenPath } },
                    { "properties", new[] { "Text", "Visible", "Left", "Top", "Width", "Height" } }
                }),
                _logger!);
            Assert.IsTrue(exportResult.Success, $"Failed to export JSON: {exportResult.Error}");

            var baseFilePath = HmiJsonDocumentStore.ResolveExportFilePath(basePath);
            var editedFilePath = HmiJsonDocumentStore.ResolveExportFilePath(editedPath);
            var editedDocument = HmiJsonDocumentStore.LoadDocument(baseFilePath);

            var exportedItem = editedDocument.Screens.Single().Items.Single();
            exportedItem.Properties["Text"] = "Edited Json Text";
            HmiJsonDocumentStore.SaveDocument(editedDocument, editedFilePath);

            var diffCommand = new DiffHmiJsonCommand(_hmiService!);
            var diffResult = await diffCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "base-path", basePath },
                    { "modified-path", editedPath },
                    { "patch-path", patchPath }
                }),
                _logger!);
            Assert.IsTrue(diffResult.Success, $"Failed to create JSON patch: {diffResult.Error}");

            var patchSummary = diffResult.Data as HmiJsonDiffResult;
            Assert.IsNotNull(patchSummary, "Expected JSON diff result");
            Assert.AreEqual(1, patchSummary!.PropertyChangeCount);

            var preflightCommand = new ImportHmiJsonCommand(_hmiService!);
            var preflightResult = await preflightCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "input-path", patchPath },
                    { "dry-run", true },
                    { "fail-on-warning", true }
                }),
                _logger!);
            Assert.IsTrue(preflightResult.Success, $"Failed to preflight JSON patch: {preflightResult.Error}");

            var preflightSummary = preflightResult.Data as HmiJsonImportResult;
            Assert.IsNotNull(preflightSummary, "Expected JSON preflight result");
            Assert.IsFalse(preflightSummary!.SafetyBlocked, "Did not expect the JSON preflight to be safety-blocked.");
            Assert.AreEqual(1, preflightSummary.PropertySetCount);

            var importResult = await preflightCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "input-path", patchPath },
                    { "dry-run", false },
                    { "apply", true },
                    { "confirm", true },
                    { "require-clean-preflight", true },
                    { "backup", true },
                    { "backup-path", backupPath }
                }),
                _logger!);
            Assert.IsTrue(importResult.Success, $"Failed to import JSON patch: {importResult.Error}");

            var importSummary = importResult.Data as HmiJsonImportResult;
            Assert.IsNotNull(importSummary, "Expected JSON import result");
            Assert.IsFalse(importSummary!.SafetyBlocked, "Did not expect the JSON import to be safety-blocked.");
            Assert.AreEqual(1, importSummary.PropertySetCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(importSummary.BackupPath), "Expected a backup export path.");
            Assert.IsTrue(File.Exists(importSummary.BackupPath!), $"Expected backup export file to exist: {importSummary.BackupPath}");

            var getPropertiesCommand = new GetHmiItemPropertiesCommand(_hmiService!);
            var getPropertiesResult = await getPropertiesCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "properties", new[] { "Text" } }
                }),
                _logger!);
            Assert.IsTrue(getPropertiesResult.Success, $"Failed to read imported JSON property: {getPropertiesResult.Error}");

            var properties = (getPropertiesResult.Data as IEnumerable<HmiItemPropertyInfo>)?.ToList();
            Assert.IsNotNull(properties, "Expected JSON-imported property data");
            Assert.IsTrue(properties!.Any(property => property.Name.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
                                                     string.Equals(property.Value, "Edited Json Text", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task Test_1007_JsonImport_RoundTripsEventHandlersAndDynamizations()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var screenPath = $"Codex/JsonComposite_{suffix}";
            var itemName = $"Btn_{suffix}";
            var rootPath = Path.Combine(Settings.TestOutputPath, "HmiJsonComposite", suffix);
            var basePath = Path.Combine(rootPath, "base");
            var editedPath = Path.Combine(rootPath, "edited");
            var patchPath = Path.Combine(rootPath, "patch");
            var backupPath = Path.Combine(rootPath, "backup");
            var verifyPath = Path.Combine(rootPath, "verify");

            Directory.CreateDirectory(rootPath);

            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var createScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "width", 1024 },
                    { "height", 600 }
                }),
                _logger!);
            Assert.IsTrue(createScreenResult.Success, $"Failed to create JSON composite test screen: {createScreenResult.Error}");

            var createItemCommand = new CreateHmiItemCommand(_hmiService!);
            var createItemResult = await createItemCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-type", "button" },
                    { "item-name", itemName },
                    { "left", 48 },
                    { "top", 60 },
                    { "width", 180 },
                    { "height", 44 }
                }),
                _logger!);
            Assert.IsTrue(createItemResult.Success, $"Failed to create JSON composite test item: {createItemResult.Error}");

            var tags = (await _hmiService!.GetTagsAsync()).ToList();
            var target = SelectCompositeDynamizationTarget(tags);
            Assert.IsFalse(string.IsNullOrWhiteSpace(target.TagPath), "Expected at least one compatible HMI tag for dynamization testing.");
            var eventType = ResolveSupportedItemEventType(screenPath, itemName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(eventType), "Expected at least one supported event handler type for the created item.");
            var propertyEventType = ResolveSupportedItemPropertyEventType(screenPath, itemName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(propertyEventType), "Expected at least one supported property event handler type for the created item.");

            var exportCommand = new ExportHmiJsonCommand(_hmiService!);
            var exportProperties = new[] { "Text", target.PropertyName, "EventHandlers", "PropertyEventHandlers", "Dynamizations" };
            var exportResult = await exportCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "output-path", basePath },
                    { "screens", new[] { screenPath } },
                    { "properties", exportProperties }
                }),
                _logger!);
            Assert.IsTrue(exportResult.Success, $"Failed to export composite JSON: {exportResult.Error}");

            var baseFilePath = HmiJsonDocumentStore.ResolveExportFilePath(basePath);
            var editedFilePath = HmiJsonDocumentStore.ResolveExportFilePath(editedPath);
            var editedDocument = HmiJsonDocumentStore.LoadDocument(baseFilePath);
            var editedItem = editedDocument.Screens.Single().Items.Single();
            editedItem.EventHandlers = new List<HmiJsonEventHandler>
            {
                new HmiJsonEventHandler
                {
                    EventType = eventType,
                    Script = new HmiJsonScriptPayload
                    {
                        ScriptCode = "return 42;"
                    }
                }
            };
            editedItem.PropertyEventHandlers = new List<HmiJsonPropertyEventHandler>
            {
                new HmiJsonPropertyEventHandler
                {
                    PropertyName = target.PropertyName,
                    EventType = propertyEventType,
                    Script = new HmiJsonScriptPayload
                    {
                        ScriptCode = "return 24;"
                    }
                }
            };
            editedItem.Dynamizations = new List<HmiJsonDynamization>
            {
                new HmiJsonDynamization
                {
                    Property = target.PropertyName,
                    Type = "Tag",
                    Binding = new HmiJsonDynamizationBinding
                    {
                        Tag = target.TagPath
                    }
                }
            };
            HmiJsonDocumentStore.SaveDocument(editedDocument, editedFilePath);

            var diffCommand = new DiffHmiJsonCommand(_hmiService!);
            var diffResult = await diffCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "base-path", basePath },
                    { "modified-path", editedPath },
                    { "patch-path", patchPath }
                }),
                _logger!);
            Assert.IsTrue(diffResult.Success, $"Failed to create composite JSON patch: {diffResult.Error}");

            var diffSummary = diffResult.Data as HmiJsonDiffResult;
            Assert.IsNotNull(diffSummary, "Expected composite diff result");
            Assert.AreEqual(3, diffSummary!.PropertyChangeCount);

            var importCommand = new ImportHmiJsonCommand(_hmiService!);
            var preflightResult = await importCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "input-path", patchPath },
                    { "dry-run", true },
                    { "fail-on-warning", true }
                }),
                _logger!);
            Assert.IsTrue(preflightResult.Success, $"Failed to preflight composite JSON patch: {preflightResult.Error}");

            var preflightSummary = preflightResult.Data as HmiJsonImportResult;
            Assert.IsNotNull(preflightSummary, "Expected composite preflight result");
            Assert.IsFalse(preflightSummary!.SafetyBlocked, "Did not expect the composite preflight to be safety-blocked.");
            Assert.AreEqual(3, preflightSummary.PropertySetCount);

            var importResult = await importCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "input-path", patchPath },
                    { "dry-run", false },
                    { "apply", true },
                    { "confirm", true },
                    { "require-clean-preflight", true },
                    { "backup", true },
                    { "backup-path", backupPath }
                }),
                _logger!);
            Assert.IsTrue(importResult.Success, $"Failed to import composite JSON patch: {importResult.Error}");

            var importSummary = importResult.Data as HmiJsonImportResult;
            Assert.IsNotNull(importSummary, "Expected composite import result");
            Assert.IsFalse(importSummary!.SafetyBlocked, "Did not expect the composite JSON import to be safety-blocked.");
            Assert.AreEqual(3, importSummary.PropertySetCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(importSummary.BackupPath), "Expected a backup export path for the composite import.");
            Assert.IsTrue(File.Exists(importSummary.BackupPath!), $"Expected composite backup export file to exist: {importSummary.BackupPath}");

            var verifyExportResult = await exportCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "output-path", verifyPath },
                    { "screens", new[] { screenPath } },
                    { "properties", exportProperties }
                }),
                _logger!);
            Assert.IsTrue(verifyExportResult.Success, $"Failed to verify composite JSON export: {verifyExportResult.Error}");

            var verifyDocument = HmiJsonDocumentStore.LoadDocument(HmiJsonDocumentStore.ResolveExportFilePath(verifyPath));
            var verifyItem = verifyDocument.Screens.Single().Items.Single();
            Assert.IsNotNull(verifyItem.EventHandlers, "Expected imported event handlers to round-trip.");
            Assert.AreEqual(eventType, verifyItem.EventHandlers!.Single().EventType);
            Assert.AreEqual("return 42;", verifyItem.EventHandlers.Single().Script!.ScriptCode);
            Assert.IsNotNull(verifyItem.PropertyEventHandlers, "Expected imported property event handlers to round-trip.");
            var importedPropertyEventHandler = verifyItem.PropertyEventHandlers!.Single(handler =>
                handler.PropertyName.Equals(target.PropertyName, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(propertyEventType, importedPropertyEventHandler.EventType);
            Assert.AreEqual("return 24;", importedPropertyEventHandler.Script!.ScriptCode);
            Assert.IsNotNull(verifyItem.Dynamizations, "Expected imported dynamizations to round-trip.");

            var importedDynamization = verifyItem.Dynamizations!.Single(dynamization =>
                dynamization.Property.Equals(target.PropertyName, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("Tag", importedDynamization.Type);
            Assert.AreEqual(target.TagPath, importedDynamization.Binding!.Tag);
        }

        [TestMethod]
        public async Task Test_1008_AgentScreenJsonEdit_DryRunsAndAppliesSingleScreenEdit()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var screenPath = $"Codex/AgentJson_{suffix}";
            var itemName = $"Btn_{suffix}";
            var rootPath = Path.Combine(Settings.TestOutputPath, "HmiAgentJson", suffix);

            Directory.CreateDirectory(rootPath);

            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var createScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "width", 1024 },
                    { "height", 600 }
                }),
                _logger!);
            Assert.IsTrue(createScreenResult.Success, $"Failed to create agent JSON test screen: {createScreenResult.Error}");

            var createItemCommand = new CreateHmiItemCommand(_hmiService!);
            var createItemResult = await createItemCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-type", "button" },
                    { "item-name", itemName },
                    { "left", 48 },
                    { "top", 60 },
                    { "width", 180 },
                    { "height", 44 }
                }),
                _logger!);
            Assert.IsTrue(createItemResult.Success, $"Failed to create agent JSON test item: {createItemResult.Error}");

            var setPropertyCommand = new SetHmiItemPropertyCommand(_hmiService!);
            var setTextResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "property", "Text" },
                    { "value", "Agent Base" }
                }),
                _logger!);
            Assert.IsTrue(setTextResult.Success, $"Failed to set agent JSON base text: {setTextResult.Error}");

            var getJsonCommand = new GetHmiScreenJsonCommand(_hmiService!);
            var snapshotResult = await getJsonCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "properties", new[] { "Text" } },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(snapshotResult.Success, $"Failed to get agent screen JSON: {snapshotResult.Error}");

            var snapshot = snapshotResult.Data as HmiScreenJsonSnapshotResult;
            Assert.IsNotNull(snapshot, "Expected agent screen JSON snapshot.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot!.BaseHash), "Expected a base hash for stale edit detection.");
            Assert.IsNotNull(snapshot.Screen, "Expected a screen JSON payload.");

            var editedScreen = snapshot.Screen!;
            var editedItem = editedScreen.Items.Single(item => item.Name == itemName);
            editedItem.Properties["Text"] = "Agent Edited";

            var applyCommand = new ApplyHmiScreenJsonEditCommand(_hmiService!);
            var dryRunResult = await applyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "edited-screen", editedScreen },
                    { "base-hash", snapshot.BaseHash },
                    { "dry-run", true },
                    { "properties", new[] { "Text" } },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(dryRunResult.Success, $"Failed to dry-run agent screen JSON edit: {dryRunResult.Error}");

            var dryRunSummary = dryRunResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(dryRunSummary, "Expected dry-run edit result.");
            Assert.IsTrue(dryRunSummary!.SafeToApply, dryRunSummary.SafetyMessage);
            Assert.IsFalse(dryRunSummary.Applied, "Dry-run should not apply changes.");
            Assert.AreEqual(1, dryRunSummary.Diff!.PropertyChangeCount);
            Assert.AreEqual(1, dryRunSummary.Preflight!.PropertySetCount);

            var applyResult = await applyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "edited-screen", editedScreen },
                    { "base-hash", snapshot.BaseHash },
                    { "dry-run", false },
                    { "properties", new[] { "Text" } },
                    { "working-path", rootPath },
                    { "backup", true }
                }),
                _logger!);
            Assert.IsTrue(applyResult.Success, $"Failed to apply agent screen JSON edit: {applyResult.Error}");

            var applySummary = applyResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(applySummary, "Expected apply edit result.");
            Assert.IsTrue(applySummary!.Applied, applySummary.SafetyMessage);
            Assert.IsFalse(string.IsNullOrWhiteSpace(applySummary.BackupPath), "Expected backup path from safe apply.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(applySummary.VerifyExportPath), "Expected verify export path after apply.");

            var getPropertiesCommand = new GetHmiItemPropertiesCommand(_hmiService!);
            var getPropertiesResult = await getPropertiesCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "properties", new[] { "Text" } }
                }),
                _logger!);
            Assert.IsTrue(getPropertiesResult.Success, $"Failed to verify agent JSON edit: {getPropertiesResult.Error}");

            var properties = (getPropertiesResult.Data as IEnumerable<HmiItemPropertyInfo>)?.ToList();
            Assert.IsNotNull(properties, "Expected edited property data.");
            Assert.IsTrue(properties!.Any(property => property.Name.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
                                                     string.Equals(property.Value, "Agent Edited", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task Test_1009_AgentScreenJsonPatch_DryRunsAndAppliesCompactOperations()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var screenPath = $"Codex/AgentPatch_{suffix}";
            var itemName = $"Btn_{suffix}";
            var rootPath = Path.Combine(Settings.TestOutputPath, "HmiAgentPatch", suffix);
            var patchProperties = new[] { "Text", "Left", "Top", "Width", "Height" };

            Directory.CreateDirectory(rootPath);

            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var createScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "width", 1024 },
                    { "height", 600 }
                }),
                _logger!);
            Assert.IsTrue(createScreenResult.Success, $"Failed to create agent patch test screen: {createScreenResult.Error}");

            var createItemCommand = new CreateHmiItemCommand(_hmiService!);
            var createItemResult = await createItemCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-type", "button" },
                    { "item-name", itemName },
                    { "left", 50 },
                    { "top", 70 },
                    { "width", 160 },
                    { "height", 40 }
                }),
                _logger!);
            Assert.IsTrue(createItemResult.Success, $"Failed to create agent patch test item: {createItemResult.Error}");

            var setPropertyCommand = new SetHmiItemPropertyCommand(_hmiService!);
            var setTextResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "property", "Text" },
                    { "value", "Patch Base" }
                }),
                _logger!);
            Assert.IsTrue(setTextResult.Success, $"Failed to set agent patch base text: {setTextResult.Error}");

            var getJsonCommand = new GetHmiScreenJsonCommand(_hmiService!);
            var snapshotResult = await getJsonCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-names", new[] { itemName } },
                    { "properties", patchProperties },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(snapshotResult.Success, $"Failed to get agent patch screen JSON: {snapshotResult.Error}");

            var snapshot = snapshotResult.Data as HmiScreenJsonSnapshotResult;
            Assert.IsNotNull(snapshot, "Expected agent patch screen JSON snapshot.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot!.BaseHash), "Expected a base hash for patch stale edit detection.");
            Assert.IsNotNull(snapshot.SnapshotFilters, "Expected snapshot filters to be echoed for agent reuse.");
            Assert.IsNotNull(snapshot.RecommendedPatchArgs, "Expected recommended patch arguments for agent reuse.");
            CollectionAssert.AreEqual(new[] { itemName.ToLowerInvariant() }, snapshot.RecommendedPatchArgs!.ItemNames.ToArray());
            CollectionAssert.AreEqual(patchProperties, snapshot.RecommendedPatchArgs.Properties.ToArray());

            var operations = new[]
            {
                new HmiScreenJsonPatchOperation
                {
                    Op = "set-property",
                    Item = itemName,
                    Property = "Text",
                    Value = "Patch Edited"
                },
                new HmiScreenJsonPatchOperation
                {
                    Op = "set-layout",
                    Item = itemName,
                    Left = 72,
                    Top = 84,
                    Width = 190,
                    Height = 48
                }
            };

            var patchCommand = new ApplyHmiScreenJsonPatchCommand(_hmiService!);
            var dryRunResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", operations },
                    { "base-hash", snapshot.BaseHash },
                    { "dry-run", true },
                    { "item-names", new[] { itemName } },
                    { "properties", patchProperties },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(dryRunResult.Success, $"Failed to dry-run agent screen JSON patch: {dryRunResult.Error}");

            var dryRunSummary = dryRunResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(dryRunSummary, "Expected dry-run patch result.");
            Assert.IsTrue(dryRunSummary!.SafeToApply, dryRunSummary.SafetyMessage);
            Assert.IsFalse(dryRunSummary.Applied, "Dry-run should not apply patch changes.");
            Assert.AreEqual(2, dryRunSummary.PatchOperationCount);
            Assert.AreEqual(0, dryRunSummary.PatchOperationFailedCount);
            Assert.AreEqual(5, dryRunSummary.PatchOperationSucceededCount);
            Assert.AreEqual(5, dryRunSummary.Diff!.PropertyChangeCount);
            Assert.AreEqual(5, dryRunSummary.Preflight!.PropertySetCount);

            var applyResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", operations },
                    { "base-hash", snapshot.BaseHash },
                    { "dry-run", false },
                    { "item-names", new[] { itemName } },
                    { "properties", patchProperties },
                    { "working-path", rootPath },
                    { "backup", true }
                }),
                _logger!);
            Assert.IsTrue(applyResult.Success, $"Failed to apply agent screen JSON patch: {applyResult.Error}");

            var applySummary = applyResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(applySummary, "Expected apply patch result.");
            Assert.IsTrue(applySummary!.Applied, applySummary.SafetyMessage);
            Assert.AreEqual(0, applySummary.PatchOperationFailedCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(applySummary.BackupPath), "Expected backup path from safe patch apply.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(applySummary.VerifyExportPath), "Expected verify export path after patch apply.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(applySummary.NewBaseHash), "Expected verified base hash after patch apply.");
            Assert.IsNotNull(applySummary.Import, "Expected applied import details after patch apply.");
            Assert.IsTrue(applySummary.Import!.TransactionUsed, "Expected safe patch apply to use a Siemens transaction.");
            Assert.IsFalse(applySummary.Import.RolledBack, "Expected successful patch apply to commit its transaction.");

            var getPropertiesCommand = new GetHmiItemPropertiesCommand(_hmiService!);
            var getPropertiesResult = await getPropertiesCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "properties", patchProperties }
                }),
                _logger!);
            Assert.IsTrue(getPropertiesResult.Success, $"Failed to verify agent JSON patch: {getPropertiesResult.Error}");

            var properties = (getPropertiesResult.Data as IEnumerable<HmiItemPropertyInfo>)?.ToList();
            Assert.IsNotNull(properties, "Expected patched property data.");
            Assert.IsTrue(properties!.Any(property => property.Name.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
                                                     string.Equals(property.Value, "Patch Edited", StringComparison.Ordinal)));
            Assert.IsTrue(properties.Any(property => property.Name.Equals("Left", StringComparison.OrdinalIgnoreCase) &&
                                                    string.Equals(property.Value, "72", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(properties.Any(property => property.Name.Equals("Top", StringComparison.OrdinalIgnoreCase) &&
                                                    string.Equals(property.Value, "84", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(properties.Any(property => property.Name.Equals("Width", StringComparison.OrdinalIgnoreCase) &&
                                                    string.Equals(property.Value, "190", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(properties.Any(property => property.Name.Equals("Height", StringComparison.OrdinalIgnoreCase) &&
                                                    string.Equals(property.Value, "48", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public async Task Test_1010_AgentScreenJsonPatch_CreatesItemAndBlocksStaleBase()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var screenPath = $"Codex/AgentPatchCreate_{suffix}";
            var itemName = $"Created_{suffix}";
            var rootPath = Path.Combine(Settings.TestOutputPath, "HmiAgentPatchCreate", suffix);
            var patchProperties = new[] { "Text", "Left", "Top", "Width", "Height" };

            Directory.CreateDirectory(rootPath);

            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var createScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "width", 1024 },
                    { "height", 600 }
                }),
                _logger!);
            Assert.IsTrue(createScreenResult.Success, $"Failed to create agent create-item test screen: {createScreenResult.Error}");

            var getJsonCommand = new GetHmiScreenJsonCommand(_hmiService!);
            var snapshotResult = await getJsonCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-names", new[] { itemName } },
                    { "properties", patchProperties },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(snapshotResult.Success, $"Failed to get create-item screen JSON: {snapshotResult.Error}");

            var snapshot = snapshotResult.Data as HmiScreenJsonSnapshotResult;
            Assert.IsNotNull(snapshot, "Expected create-item screen JSON snapshot.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot!.BaseHash), "Expected a base hash for create-item patch.");
            Assert.IsNotNull(snapshot.RecommendedPatchArgs, "Expected recommended patch arguments on create-item snapshot.");
            CollectionAssert.AreEqual(new[] { itemName.ToLowerInvariant() }, snapshot.RecommendedPatchArgs!.ItemNames.ToArray());
            CollectionAssert.AreEqual(patchProperties, snapshot.RecommendedPatchArgs.Properties.ToArray());

            var createOperations = new[]
            {
                new HmiScreenJsonPatchOperation
                {
                    Op = "create-item",
                    Item = itemName,
                    ItemType = "button",
                    Left = 96,
                    Top = 108,
                    Width = 210,
                    Height = 52,
                    Properties = new Dictionary<string, object?>
                    {
                        ["Text"] = "Created By Patch"
                    }
                }
            };

            var patchCommand = new ApplyHmiScreenJsonPatchCommand(_hmiService!);
            var dryRunResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", createOperations },
                    { "base-hash", snapshot.BaseHash },
                    { "dry-run", true },
                    { "item-names", snapshot.RecommendedPatchArgs.ItemNames.ToArray() },
                    { "properties", snapshot.RecommendedPatchArgs.Properties.ToArray() },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(dryRunResult.Success, $"Failed to dry-run create-item patch: {dryRunResult.Error}");

            var dryRunSummary = dryRunResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(dryRunSummary, "Expected create-item dry-run result.");
            Assert.IsTrue(dryRunSummary!.SafeToApply, dryRunSummary.SafetyMessage);
            Assert.AreEqual(1, dryRunSummary.Diff!.ItemCreateCount);
            Assert.AreEqual(1, dryRunSummary.Preflight!.ItemCreated);
            Assert.AreEqual(5, dryRunSummary.Preflight.PropertySetCount);

            var applyResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", createOperations },
                    { "base-hash", snapshot.BaseHash },
                    { "dry-run", false },
                    { "item-names", snapshot.RecommendedPatchArgs.ItemNames.ToArray() },
                    { "properties", snapshot.RecommendedPatchArgs.Properties.ToArray() },
                    { "working-path", rootPath },
                    { "backup", true }
                }),
                _logger!);
            Assert.IsTrue(applyResult.Success, $"Failed to apply create-item patch: {applyResult.Error}");

            var applySummary = applyResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(applySummary, "Expected create-item apply result.");
            Assert.IsTrue(applySummary!.Applied, applySummary.SafetyMessage);
            Assert.AreEqual(0, applySummary.PatchOperationFailedCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(applySummary.BackupPath), "Expected backup path from create-item patch apply.");

            var getPropertiesCommand = new GetHmiItemPropertiesCommand(_hmiService!);
            var createdPropertiesResult = await getPropertiesCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "properties", patchProperties }
                }),
                _logger!);
            Assert.IsTrue(createdPropertiesResult.Success, $"Failed to verify create-item patch: {createdPropertiesResult.Error}");

            var createdProperties = (createdPropertiesResult.Data as IEnumerable<HmiItemPropertyInfo>)?.ToList();
            Assert.IsNotNull(createdProperties, "Expected created item property data.");
            Assert.IsTrue(createdProperties!.Any(property => property.Name.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
                                                            string.Equals(property.Value, "Created By Patch", StringComparison.Ordinal)));
            Assert.IsTrue(createdProperties.Any(property => property.Name.Equals("Left", StringComparison.OrdinalIgnoreCase) &&
                                                           string.Equals(property.Value, "96", StringComparison.OrdinalIgnoreCase)));

            var postCreateSnapshotResult = await getJsonCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-names", new[] { itemName } },
                    { "properties", new[] { "Text" } },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(postCreateSnapshotResult.Success, $"Failed to get stale-base snapshot: {postCreateSnapshotResult.Error}");

            var staleSnapshot = postCreateSnapshotResult.Data as HmiScreenJsonSnapshotResult;
            Assert.IsNotNull(staleSnapshot, "Expected stale-base snapshot.");

            var setPropertyCommand = new SetHmiItemPropertyCommand(_hmiService!);
            var externalChangeResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "property", "Text" },
                    { "value", "External Change" }
                }),
                _logger!);
            Assert.IsTrue(externalChangeResult.Success, $"Failed to make external stale-base change: {externalChangeResult.Error}");

            var staleApplyResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", new[]
                        {
                            new HmiScreenJsonPatchOperation
                            {
                                Op = "set-property",
                                Item = itemName,
                                Property = "Text",
                                Value = "Should Be Blocked"
                            }
                        }
                    },
                    { "base-hash", staleSnapshot!.BaseHash },
                    { "dry-run", false },
                    { "item-names", staleSnapshot.RecommendedPatchArgs!.ItemNames.ToArray() },
                    { "properties", staleSnapshot.RecommendedPatchArgs.Properties.ToArray() },
                    { "working-path", rootPath },
                    { "backup", true }
                }),
                _logger!);
            Assert.IsTrue(staleApplyResult.Success, $"Expected stale-base patch command to return a structured blocked result: {staleApplyResult.Error}");

            var staleSummary = staleApplyResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(staleSummary, "Expected stale-base patch result.");
            Assert.IsTrue(staleSummary!.SafetyBlocked, "Expected stale base hash to block the patch.");
            Assert.IsFalse(staleSummary.Applied, "Stale-base patch should not apply.");
            Assert.IsTrue(staleSummary.SafetyMessage!.Contains("base hash"), staleSummary.SafetyMessage);

            var blockedVerifyResult = await getPropertiesCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", itemName },
                    { "properties", new[] { "Text" } }
                }),
                _logger!);
            Assert.IsTrue(blockedVerifyResult.Success, $"Failed to verify stale-base block: {blockedVerifyResult.Error}");

            var blockedProperties = (blockedVerifyResult.Data as IEnumerable<HmiItemPropertyInfo>)?.ToList();
            Assert.IsNotNull(blockedProperties, "Expected blocked verification property data.");
            Assert.IsTrue(blockedProperties!.Any(property => property.Name.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
                                                            string.Equals(property.Value, "External Change", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task Test_1011_AgentScreenJsonPatch_DuplicatesAndDeletesItems()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var screenPath = $"Codex/AgentPatchObject_{suffix}";
            var sourceItemName = $"Source_{suffix}";
            var duplicateItemName = $"Duplicate_{suffix}";
            var deletedItemName = $"Delete_{suffix}";
            var rootPath = Path.Combine(Settings.TestOutputPath, "HmiAgentPatchObject", suffix);
            var patchProperties = new[] { "Text", "Left", "Top", "Width", "Height" };

            Directory.CreateDirectory(rootPath);

            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var createScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "width", 1024 },
                    { "height", 600 }
                }),
                _logger!);
            Assert.IsTrue(createScreenResult.Success, $"Failed to create duplicate/delete patch screen: {createScreenResult.Error}");

            var createItemCommand = new CreateHmiItemCommand(_hmiService!);
            var sourceCreateResult = await createItemCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-type", "button" },
                    { "item-name", sourceItemName },
                    { "left", 36 },
                    { "top", 48 },
                    { "width", 150 },
                    { "height", 42 }
                }),
                _logger!);
            Assert.IsTrue(sourceCreateResult.Success, $"Failed to create duplicate source item: {sourceCreateResult.Error}");

            var deleteCreateResult = await createItemCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-type", "button" },
                    { "item-name", deletedItemName },
                    { "left", 220 },
                    { "top", 48 },
                    { "width", 150 },
                    { "height", 42 }
                }),
                _logger!);
            Assert.IsTrue(deleteCreateResult.Success, $"Failed to create delete target item: {deleteCreateResult.Error}");

            var setPropertyCommand = new SetHmiItemPropertyCommand(_hmiService!);
            var sourceTextResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", sourceItemName },
                    { "property", "Text" },
                    { "value", "Source Base" }
                }),
                _logger!);
            Assert.IsTrue(sourceTextResult.Success, $"Failed to set duplicate source text: {sourceTextResult.Error}");

            var deleteTextResult = await setPropertyCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", deletedItemName },
                    { "property", "Text" },
                    { "value", "Delete Me" }
                }),
                _logger!);
            Assert.IsTrue(deleteTextResult.Success, $"Failed to set delete target text: {deleteTextResult.Error}");

            var getJsonCommand = new GetHmiScreenJsonCommand(_hmiService!);
            var snapshotResult = await getJsonCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-names", new[] { sourceItemName, duplicateItemName, deletedItemName } },
                    { "properties", patchProperties },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(snapshotResult.Success, $"Failed to get duplicate/delete patch snapshot: {snapshotResult.Error}");

            var snapshot = snapshotResult.Data as HmiScreenJsonSnapshotResult;
            Assert.IsNotNull(snapshot, "Expected duplicate/delete patch snapshot.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot!.BaseHash), "Expected base hash for duplicate/delete patch.");

            var operations = new[]
            {
                new HmiScreenJsonPatchOperation
                {
                    Op = "duplicate-item",
                    Source = sourceItemName,
                    TargetName = duplicateItemName,
                    Left = 96,
                    Top = 120,
                    Properties = new Dictionary<string, object?>
                    {
                        ["Text"] = "Duplicated By Patch"
                    }
                },
                new HmiScreenJsonPatchOperation
                {
                    Op = "delete-item",
                    Item = deletedItemName
                }
            };

            var patchCommand = new ApplyHmiScreenJsonPatchCommand(_hmiService!);
            var dryRunResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", operations },
                    { "base-hash", snapshot.BaseHash },
                    { "dry-run", true },
                    { "item-names", snapshot.RecommendedPatchArgs!.ItemNames.ToArray() },
                    { "properties", snapshot.RecommendedPatchArgs.Properties.ToArray() },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(dryRunResult.Success, $"Failed to dry-run duplicate/delete patch: {dryRunResult.Error}");

            var dryRunSummary = dryRunResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(dryRunSummary, "Expected duplicate/delete dry-run result.");
            Assert.IsTrue(dryRunSummary!.SafeToApply, dryRunSummary.SafetyMessage);
            Assert.AreEqual(1, dryRunSummary.Diff!.ItemCreateCount);
            Assert.AreEqual(1, dryRunSummary.Diff.ItemDeleteCount);
            Assert.AreEqual(1, dryRunSummary.Preflight!.ItemCreated);
            Assert.AreEqual(1, dryRunSummary.Preflight.ItemDeleted);

            var applyResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", operations },
                    { "base-hash", snapshot.BaseHash },
                    { "dry-run", false },
                    { "item-names", snapshot.RecommendedPatchArgs.ItemNames.ToArray() },
                    { "properties", snapshot.RecommendedPatchArgs.Properties.ToArray() },
                    { "working-path", rootPath },
                    { "backup", true }
                }),
                _logger!);
            Assert.IsTrue(applyResult.Success, $"Failed to apply duplicate/delete patch: {applyResult.Error}");

            var applySummary = applyResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(applySummary, "Expected duplicate/delete apply result.");
            Assert.IsTrue(applySummary!.Applied, applySummary.SafetyMessage);
            Assert.AreEqual(0, applySummary.PatchOperationFailedCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(applySummary.BackupPath), "Expected backup path from duplicate/delete patch apply.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(applySummary.VerifyExportPath), "Expected verify export path after duplicate/delete patch apply.");

            var getPropertiesCommand = new GetHmiItemPropertiesCommand(_hmiService!);
            var duplicatePropertiesResult = await getPropertiesCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-name", duplicateItemName },
                    { "properties", patchProperties }
                }),
                _logger!);
            Assert.IsTrue(duplicatePropertiesResult.Success, $"Failed to verify duplicated item: {duplicatePropertiesResult.Error}");

            var duplicateProperties = (duplicatePropertiesResult.Data as IEnumerable<HmiItemPropertyInfo>)?.ToList();
            Assert.IsNotNull(duplicateProperties, "Expected duplicated item properties.");
            Assert.IsTrue(duplicateProperties!.Any(property => property.Name.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
                                                               string.Equals(property.Value, "Duplicated By Patch", StringComparison.Ordinal)));
            Assert.IsTrue(duplicateProperties.Any(property => property.Name.Equals("Left", StringComparison.OrdinalIgnoreCase) &&
                                                             string.Equals(property.Value, "96", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(duplicateProperties.Any(property => property.Name.Equals("Top", StringComparison.OrdinalIgnoreCase) &&
                                                             string.Equals(property.Value, "120", StringComparison.OrdinalIgnoreCase)));

            var getScreenInfoCommand = new GetHmiScreenInfoCommand(_hmiService!);
            var screenInfoResult = await getScreenInfoCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath }
                }),
                _logger!);
            Assert.IsTrue(screenInfoResult.Success, $"Failed to verify deleted item through screen info: {screenInfoResult.Error}");

            var screenInfo = screenInfoResult.Data as HmiScreenInfo;
            Assert.IsNotNull(screenInfo, "Expected screen info for delete verification.");
            Assert.IsTrue(screenInfo!.Items.Any(item => item.Name == sourceItemName), "Expected source item to remain after duplicate/delete patch.");
            Assert.IsTrue(screenInfo.Items.Any(item => item.Name == duplicateItemName), "Expected duplicated item to exist.");
            Assert.IsFalse(screenInfo.Items.Any(item => item.Name == deletedItemName), "Expected delete target item to be removed.");
        }

        [TestMethod]
        public async Task Test_1012_AgentScreenJsonPatch_InspectsCapabilitiesAndPatchesComposites()
        {
            var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var screenPath = $"Codex/AgentPatchComposite_{suffix}";
            var itemName = $"Btn_{suffix}";
            var rootPath = Path.Combine(Settings.TestOutputPath, "HmiAgentPatchComposite", suffix);

            Directory.CreateDirectory(rootPath);

            var listTagsCommand = new ListHmiTagsCommand(_hmiService!);
            var listTagsResult = await listTagsCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName }
                }),
                _logger!);
            Assert.IsTrue(listTagsResult.Success, $"Failed to list HMI tags for composite patch: {listTagsResult.Error}");

            var tags = (listTagsResult.Data as IEnumerable<HmiTagInfo>)?.ToList();
            Assert.IsNotNull(tags, "Expected HMI tags for composite patch.");
            var target = SelectCompositeDynamizationTarget(tags!);
            if (string.IsNullOrWhiteSpace(target.PropertyName))
            {
                Assert.Inconclusive("No compatible HMI tag was available for composite patch dynamization.");
            }

            var compositeProperties = new[] { "Text", target.PropertyName, "EventHandlers", "PropertyEventHandlers", "Dynamizations" };
            var createScreenCommand = new CreateHmiScreenCommand(_hmiService!);
            var createScreenResult = await createScreenCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "width", 1024 },
                    { "height", 600 }
                }),
                _logger!);
            Assert.IsTrue(createScreenResult.Success, $"Failed to create composite patch screen: {createScreenResult.Error}");

            var createItemCommand = new CreateHmiItemCommand(_hmiService!);
            var createItemResult = await createItemCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-type", "button" },
                    { "item-name", itemName },
                    { "left", 72 },
                    { "top", 84 },
                    { "width", 190 },
                    { "height", 48 }
                }),
                _logger!);
            Assert.IsTrue(createItemResult.Success, $"Failed to create composite patch item: {createItemResult.Error}");

            var eventType = ResolveSupportedItemEventType(screenPath, itemName);
            var propertyEventType = ResolveSupportedItemPropertyEventType(screenPath, itemName);

            var inspectCommand = new InspectHmiScreenJsonCapabilitiesCommand(_hmiService!);
            var inspectResult = await inspectCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-names", new[] { itemName } },
                    { "properties", compositeProperties },
                    { "include-item-types", true },
                    { "include-writable-properties", false },
                    { "include-snapshot", true },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(inspectResult.Success, $"Failed to inspect composite patch capabilities: {inspectResult.Error}");

            var capabilities = inspectResult.Data as HmiScreenJsonCapabilitiesResult;
            Assert.IsNotNull(capabilities, "Expected capabilities result.");
            Assert.IsNotNull(capabilities!.Snapshot, "Expected inspected snapshot.");
            Assert.IsTrue(capabilities.Capabilities!.SupportedPatchOperations.Contains("set-event-handler"));
            Assert.IsTrue(capabilities.SupportedItemTypes.Any(type => type.Aliases.Contains("button", StringComparer.OrdinalIgnoreCase)));
            Assert.AreEqual(itemName, capabilities.Items.Single().Name);

            var patchCommand = new ApplyHmiScreenJsonPatchCommand(_hmiService!);
            var setOperations = new[]
            {
                new HmiScreenJsonPatchOperation
                {
                    Op = "set-event-handler",
                    Item = itemName,
                    EventType = eventType,
                    ScriptCode = "return 42;"
                },
                new HmiScreenJsonPatchOperation
                {
                    Op = "set-property-event-handler",
                    Item = itemName,
                    Property = target.PropertyName,
                    EventType = propertyEventType,
                    ScriptCode = "return 24;"
                },
                new HmiScreenJsonPatchOperation
                {
                    Op = "set-dynamization",
                    Item = itemName,
                    Property = target.PropertyName,
                    Tag = target.TagPath
                }
            };

            var dryRunResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", setOperations },
                    { "base-hash", capabilities.Snapshot!.BaseHash },
                    { "dry-run", true },
                    { "item-names", capabilities.RecommendedPatchArgs!.ItemNames.ToArray() },
                    { "properties", capabilities.RecommendedPatchArgs.Properties.ToArray() },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(dryRunResult.Success, $"Failed to dry-run composite patch: {dryRunResult.Error}");

            var dryRunSummary = dryRunResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(dryRunSummary, "Expected composite dry-run result.");
            Assert.IsTrue(dryRunSummary!.SafeToApply, dryRunSummary.SafetyMessage);
            Assert.AreEqual(3, dryRunSummary.Diff!.PropertyChangeCount);
            Assert.AreEqual(3, dryRunSummary.Preflight!.PropertySetCount);
            Assert.AreEqual("apply-with-dry-run-false", dryRunSummary.Guidance!.RecommendedNextAction);

            var applyResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", setOperations },
                    { "base-hash", capabilities.Snapshot.BaseHash },
                    { "dry-run", false },
                    { "item-names", capabilities.RecommendedPatchArgs.ItemNames.ToArray() },
                    { "properties", capabilities.RecommendedPatchArgs.Properties.ToArray() },
                    { "working-path", rootPath },
                    { "backup", true }
                }),
                _logger!);
            Assert.IsTrue(applyResult.Success, $"Failed to apply composite patch: {applyResult.Error}");

            var applySummary = applyResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(applySummary, "Expected composite apply result.");
            Assert.IsTrue(applySummary!.Applied, applySummary.SafetyMessage);
            Assert.AreEqual("verify-result", applySummary.Guidance!.RecommendedNextAction);
            Assert.IsNotNull(applySummary.VerifiedScreen, "Expected verified screen after composite patch.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(applySummary.NewBaseHash), "Expected verified base hash after composite patch.");
            Assert.IsTrue(applySummary.Import!.TransactionUsed, "Expected composite patch apply to use a Siemens transaction.");
            Assert.IsFalse(applySummary.Import.RolledBack, "Expected composite patch apply to commit its transaction.");

            var verifiedItem = applySummary.VerifiedScreen!.Items.Single();
            Assert.IsNotNull(verifiedItem.EventHandlers, "Expected event handler after composite patch.");
            Assert.AreEqual(eventType, verifiedItem.EventHandlers!.Single().EventType);
            Assert.AreEqual("return 42;", verifiedItem.EventHandlers.Single().Script!.ScriptCode);
            Assert.IsNotNull(verifiedItem.PropertyEventHandlers, "Expected property event handler after composite patch.");
            Assert.AreEqual(propertyEventType, verifiedItem.PropertyEventHandlers!.Single().EventType);
            Assert.AreEqual("return 24;", verifiedItem.PropertyEventHandlers.Single().Script!.ScriptCode);
            Assert.IsNotNull(verifiedItem.Dynamizations, "Expected dynamization after composite patch.");
            Assert.AreEqual(target.TagPath, verifiedItem.Dynamizations!.Single().Binding!.Tag);

            var refreshedSnapshotResult = await new GetHmiScreenJsonCommand(_hmiService!).ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "item-names", new[] { itemName } },
                    { "properties", compositeProperties },
                    { "working-path", rootPath }
                }),
                _logger!);
            Assert.IsTrue(refreshedSnapshotResult.Success, $"Failed to refresh composite snapshot for removal: {refreshedSnapshotResult.Error}");
            var refreshedSnapshot = refreshedSnapshotResult.Data as HmiScreenJsonSnapshotResult;
            Assert.IsNotNull(refreshedSnapshot, "Expected refreshed composite snapshot.");

            var removeOperations = new[]
            {
                new HmiScreenJsonPatchOperation
                {
                    Op = "remove-event-handler",
                    Item = itemName,
                    EventType = eventType
                },
                new HmiScreenJsonPatchOperation
                {
                    Op = "remove-property-event-handler",
                    Item = itemName,
                    Property = target.PropertyName,
                    EventType = propertyEventType
                },
                new HmiScreenJsonPatchOperation
                {
                    Op = "remove-dynamization",
                    Item = itemName,
                    Property = target.PropertyName
                }
            };

            var removeResult = await patchCommand.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    { "hmi-name", Settings.UnifiedHmiName },
                    { "screen-path", screenPath },
                    { "operations", removeOperations },
                    { "base-hash", refreshedSnapshot!.BaseHash },
                    { "dry-run", false },
                    { "item-names", refreshedSnapshot.RecommendedPatchArgs!.ItemNames.ToArray() },
                    { "properties", refreshedSnapshot.RecommendedPatchArgs.Properties.ToArray() },
                    { "working-path", rootPath },
                    { "backup", true }
                }),
                _logger!);
            Assert.IsTrue(removeResult.Success, $"Failed to remove composite patch state: {removeResult.Error}");

            var removeSummary = removeResult.Data as HmiScreenJsonEditResult;
            Assert.IsNotNull(removeSummary, "Expected composite removal result.");
            Assert.IsTrue(removeSummary!.Applied, removeSummary.SafetyMessage);
            Assert.IsNotNull(removeSummary.VerifiedScreen, "Expected verified screen after composite removals.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(removeSummary.NewBaseHash), "Expected verified base hash after composite removals.");
            Assert.IsTrue(removeSummary.Import!.TransactionUsed, "Expected composite removal apply to use a Siemens transaction.");
            Assert.IsFalse(removeSummary.Import.RolledBack, "Expected composite removal apply to commit its transaction.");

            var removedItem = removeSummary.VerifiedScreen!.Items.Single();
            Assert.IsTrue(removedItem.EventHandlers == null || removedItem.EventHandlers.Count == 0, "Expected event handlers to be removed.");
            Assert.IsTrue(removedItem.PropertyEventHandlers == null || removedItem.PropertyEventHandlers.Count == 0, "Expected property event handlers to be removed.");
            Assert.IsTrue(removedItem.Dynamizations == null || removedItem.Dynamizations.Count == 0, "Expected dynamizations to be removed.");
        }

        private static (string PropertyName, string TagPath) SelectCompositeDynamizationTarget(IReadOnlyList<HmiTagInfo> tags)
        {
            var booleanTag = tags.FirstOrDefault(tag => !tag.HasMembers && IsBooleanTag(tag));
            if (booleanTag != null)
            {
                return ("Visible", booleanTag.Path);
            }

            var numericTag = tags.FirstOrDefault(tag => !tag.HasMembers && IsNumericTag(tag));
            if (numericTag != null)
            {
                return ("Left", numericTag.Path);
            }

            return (string.Empty, string.Empty);
        }

        private static bool IsBooleanTag(HmiTagInfo tag)
        {
            var dataType = (tag.HmiDataType ?? tag.DataType ?? string.Empty).Trim();
            return dataType.Equals("Bool", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("Boolean", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNumericTag(HmiTagInfo tag)
        {
            var dataType = (tag.HmiDataType ?? tag.DataType ?? string.Empty).Trim();
            return dataType.Equals("Byte", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("SByte", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("Short", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("Int", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("UInt", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("DInt", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("UDInt", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("LInt", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("ULInt", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("Real", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("Float", StringComparison.OrdinalIgnoreCase) ||
                   dataType.Equals("Double", StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveSupportedItemEventType(string screenPath, string itemName)
        {
            var accessor = _portalService as IEngineeringSessionContextAccessor;
            Assert.IsNotNull(accessor, "Expected a session-context-aware portal service.");
            Assert.IsNotNull(accessor!.SessionContext.HmiSoftware, "Expected an open Unified HMI in the session context.");

            var adapter = new V21UnifiedHmiAdapter(_loggerFactory!.CreateLogger<V21UnifiedHmiAdapter>());
            var resolved = adapter.TryGetScreen(accessor.SessionContext.HmiSoftware!, screenPath, out var screen, out _);
            Assert.IsTrue(resolved, $"Failed to resolve screen '{screenPath}' while inspecting supported event types.");
            Assert.IsNotNull(screen, "Expected the created screen to be available.");

            var item = screen!.ScreenItems.Find(itemName);
            Assert.IsNotNull(item, $"Expected item '{itemName}' to be available for event type inspection.");

            var composition = item!.GetType()
                .GetProperty("EventHandlers", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?
                .GetValue(item);
            Assert.IsNotNull(composition, "Expected the created item to expose EventHandlers.");

            var createMethod = composition!.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase) &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.IsEnum);
            Assert.IsNotNull(createMethod, "Expected the EventHandlers composition to expose Create(enum).");

            var eventNames = Enum.GetNames(createMethod!.GetParameters()[0].ParameterType);
            var preferredNames = new[] { "Click", "Press", "Tapped", "Released", "Down", "Up" };

            return preferredNames.FirstOrDefault(preferred =>
                       eventNames.Contains(preferred, StringComparer.OrdinalIgnoreCase))
                   ?? eventNames.First();
        }

        private string ResolveSupportedItemPropertyEventType(string screenPath, string itemName)
        {
            var accessor = _portalService as IEngineeringSessionContextAccessor;
            Assert.IsNotNull(accessor, "Expected a session-context-aware portal service.");
            Assert.IsNotNull(accessor!.SessionContext.HmiSoftware, "Expected an open Unified HMI in the session context.");

            var adapter = new V21UnifiedHmiAdapter(_loggerFactory!.CreateLogger<V21UnifiedHmiAdapter>());
            var resolved = adapter.TryGetScreen(accessor.SessionContext.HmiSoftware!, screenPath, out var screen, out _);
            Assert.IsTrue(resolved, $"Failed to resolve screen '{screenPath}' while inspecting supported property event types.");
            Assert.IsNotNull(screen, "Expected the created screen to be available.");

            var item = screen!.ScreenItems.Find(itemName);
            Assert.IsNotNull(item, $"Expected item '{itemName}' to be available for property event type inspection.");

            var composition = item!.GetType()
                .GetProperty("PropertyEventHandlers", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?
                .GetValue(item);
            Assert.IsNotNull(composition, "Expected the created item to expose PropertyEventHandlers.");

            var createMethod = composition!.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase) &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(string) &&
                    method.GetParameters()[1].ParameterType.IsEnum);
            Assert.IsNotNull(createMethod, "Expected the PropertyEventHandlers composition to expose Create(string, enum).");

            var eventNames = Enum.GetNames(createMethod!.GetParameters()[1].ParameterType);
            var preferredNames = new[] { "Change", "Changed", "ValueChanged" };

            return preferredNames.FirstOrDefault(preferred =>
                       eventNames.Contains(preferred, StringComparer.OrdinalIgnoreCase))
                   ?? eventNames.First();
        }
    }
}
