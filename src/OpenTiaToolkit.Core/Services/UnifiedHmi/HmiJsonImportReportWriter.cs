using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiJsonImportReportWriter : IHmiJsonImportReportWriter
    {
        public void TryWrite(HmiJsonImportResult result, string inputPath, bool dryRun)
        {
            try
            {
                var reportPath = HmiJsonDocumentStore.ResolveReportFilePath(inputPath, dryRun);
                HmiJsonDocumentStore.SaveImportReport(result, reportPath);
                result.ReportPath = reportPath;
            }
            catch
            {
                // Best-effort only; avoid hiding the main result on report write failures.
            }
        }
    }
}
