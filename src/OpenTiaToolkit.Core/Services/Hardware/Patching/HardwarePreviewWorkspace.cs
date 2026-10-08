using Siemens.Engineering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services.Hardware.Patching
{
    internal static class HardwarePreviewPolicy
    {
        public static bool RequiresIsolation(bool dryRun, IEnumerable<HardwarePatchOperation> operations) =>
            dryRun && operations.Any(operation =>
                HardwarePatchRules.NormalizeOperation(operation.Op) is "create-device" or "create-device-with-item");
    }

    /// <summary>A saved snapshot and an independently owned portal; never closes or saves the source.</summary>
    internal sealed class HardwarePreviewWorkspace : IDisposable
    {
        private readonly string _root;
        private TiaPortal? _portal;
        private Project? _project;

        public HardwarePreviewWorkspace()
        {
            _root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "HardwarePreview", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public string ArchiveSha256 { get; private set; } = "";
        public EngineeringSessionContext Session { get; } = new EngineeringSessionContext();

        public void Snapshot(ProjectBase source)
        {
            if (source is not Project project)
                throw new InvalidOperationException("Device-creation preview requires a saved standalone project; Multiuser projects are unsupported for isolated preview.");
            if (project.IsModified)
                throw new InvalidOperationException("Device-creation preview requires a saved project snapshot. Save the intended changes explicitly, then retry; preview does not save your project.");
            project.Archive(new DirectoryInfo(_root), "PreviewSource.zap21", ProjectArchivationMode.Compressed);
            var archives = Directory.GetFiles(_root, "*.zap*");
            if (archives.Length != 1) throw new InvalidOperationException("Device-creation preview did not produce exactly one project archive.");
            using (var stream = File.OpenRead(archives[0]))
            using (var sha = SHA256.Create())
                ArchiveSha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public void Open()
        {
            var archive = Directory.GetFiles(_root, "*.zap*").Single();
            _portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
            var copyPath = Path.Combine(_root, "Copy");
            Directory.CreateDirectory(copyPath);
            // net48 CreateDirectory returns a DirectoryInfo whose ToString() is only the leaf name.
            // Siemens marshals that display path; construct it explicitly with the absolute path.
            _project = _portal.Projects.Retrieve(new FileInfo(archive), new DirectoryInfo(copyPath));
            Session.Portal = _portal;
            Session.Project = _project;
            Session.OwnsCurrentProject = true;
        }

        public void Dispose()
        {
            Exception? failure = null;
            try { _project?.Close(); }
            catch (Exception ex) { failure = ex; }
            try { _portal?.Dispose(); }
            catch (Exception ex) { throw new InvalidOperationException("Could not dispose the isolated preview portal; scratch files retained at " + _root, ex); }
            _project = null;
            _portal = null;
            Session.ClearPortalSelection();
            // A successful owned-portal disposal also releases an already disposed scratch project.
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "HardwarePreview")) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(_root);
            if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Preview cleanup path is outside its owned workspace.");
            try { if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true); }
            catch (Exception ex) { throw new InvalidOperationException("Could not discard isolated preview files at " + _root, failure == null ? ex : new AggregateException(failure, ex)); }
        }
    }
}
