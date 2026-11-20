using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace OpenTiaToolkit.Core.Services;

internal sealed class HmiTagBatchService
{
    private static readonly string[] ReadbackAttributes =
    {
        nameof(HmiTag.Name), nameof(HmiTag.DataType), nameof(HmiTag.Connection),
        nameof(HmiTag.PlcName), nameof(HmiTag.PlcTag), nameof(HmiTag.AccessMode),
        nameof(HmiTag.InitialValue), nameof(HmiTag.TagType), nameof(HmiTag.TagTableName),
        nameof(HmiTag.HmiDataType)
    };

    private readonly EngineeringSessionContext _session;

    internal HmiTagBatchService(EngineeringSessionContext session) => _session = session;

    internal HmiTagBatchResult Execute(HmiTagBatchRequest request, CancellationToken cancellation)
    {
        _session.ThrowIfProjectUnavailable();
        var software = _session.HmiSoftware ?? throw new InvalidOperationException("No Unified HMI is selected.");
        if (!string.Equals(_session.CurrentHmiName, request.HmiName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected Unified HMI changed. Retry against the intended HMI.");

        if (request.DryRun)
            return Run(software, request, cancellation);

        var portal = _session.Portal ?? throw new InvalidOperationException("No TIA Portal is connected.");
        var project = _session.Project ?? throw new InvalidOperationException("No TIA project is open.");
        const string description = "Apply Unified HMI tag batch";
        using (var exclusive = portal.ExclusiveAccess(description))
        using (var transaction = exclusive.Transaction(project, description))
        {
            var result = Run(software, request, cancellation);
            cancellation.ThrowIfCancellationRequested();
            transaction.CommitOnDispose();
            return result;
        }
    }

    private static HmiTagBatchResult Run(HmiSoftware software,
        HmiTagBatchRequest request, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var target = HmiTagTablePath.Resolve(software, request.TablePath, out var tableName);
        var table = target.Find(tableName);
        var existingNames = new List<string>();
        var partners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var spec in request.Tags)
        {
            cancellation.ThrowIfCancellationRequested();
            if (spec.Connection != null && !partners.ContainsKey(spec.Connection))
            {
                var connection = software.Connections.Find(spec.Connection)
                    ?? throw new InvalidOperationException($"HMI connection '{spec.Connection}' was not found.");
                var partner = connection.Partner;
                if (string.IsNullOrWhiteSpace(partner))
                    throw new InvalidOperationException($"HMI connection '{spec.Connection}' has no integrated PLC partner.");
                partners.Add(spec.Connection, partner);
            }

            var existing = software.Tags.Find(spec.Name);
            if (existing == null)
                continue;
            if (table == null || !string.Equals(existing.TagTableName, tableName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Tag '{spec.Name}' already exists outside the destination table.");
            existingNames.Add(spec.Name);
        }

        var result = new HmiTagBatchResult
        {
            HmiName = request.HmiName,
            TablePath = request.TablePath,
            CreatesTable = table == null,
            DryRun = request.DryRun,
            Tags = request.DryRun ? request.Tags : Array.Empty<HmiTagBatchTag>(),
            ExistingNames = existingNames,
            PlcByConnection = partners,
            RequiresOverwrite = existingNames.Count != 0 && !request.Overwrite
        };
        if (request.DryRun)
            return result;
        if (result.RequiresOverwrite)
            throw new InvalidOperationException(request.ImportDirectory == null
                ? $"Destination already has tags: {string.Join(", ", existingNames)}. Use new tag names."
                : $"Destination has existing tags: {string.Join(", ", existingNames)}. Import requires overwrite=true.");

        table = table ?? target.Create(tableName);
        if (!string.Equals(table.Name, tableName, StringComparison.Ordinal))
            throw new InvalidOperationException($"TIA selected an unexpected table '{table.Name}' for '{tableName}'.");

        if (request.ImportDirectory != null)
        {
            var expectedCount = table.Tags.Count + request.Tags.Count - existingNames.Count;
            if (!table.Tags.Import(request.ImportDirectory))
                throw new InvalidOperationException("TIA reported that the WinCCML import failed.");
            if (table.Tags.Count != expectedCount)
                throw new InvalidOperationException("TIA imported an unexpected number of tags into the destination table.");
        }
        else
        {
            foreach (var spec in request.Tags)
            {
                cancellation.ThrowIfCancellationRequested();
                var tag = table.Tags.Create(spec.Name);
                if (spec.Connection == null)
                {
                    tag.DataType = spec.DataType;
                    if (spec.InitialValue != null)
                        tag.InitialValue = spec.InitialValue;
                }
                else
                {
                    tag.Connection = spec.Connection;
                    tag.AccessMode = HmiAccessMode.SymbolicAccess;
                    tag.PlcTag = spec.PlcTag;
                }
            }
        }

        foreach (var spec in request.Tags)
        {
            cancellation.ThrowIfCancellationRequested();
            var tag = table.Tags.Find(spec.Name);
            if (tag == null)
                throw new InvalidOperationException($"Tag '{spec.Name}' was missing from the destination after the write.");
            var values = ((IEngineeringObject)tag).GetAttributes(ReadbackAttributes);
            var expectedPlc = spec.Connection == null ? null : partners[spec.Connection];
            Verify(spec, tableName, expectedPlc, values, () =>
                tag.Validate().SelectMany(diagnostic => diagnostic.Errors).Select(error => error.ToString()).ToArray());
        }

        result.VerifiedTagCount = request.Tags.Count;
        return result;
    }

    internal static void Verify(HmiTagBatchTag expected, string tableName, string? expectedPlc, IList<object> values,
        Func<IReadOnlyList<string>> validate)
    {
        var errors = validate();
        if (errors.Count != 0)
            throw new InvalidOperationException($"Tag '{expected.Name}' failed native validation: {string.Join("; ", errors)}");
        if (values.Count != ReadbackAttributes.Length)
            throw new InvalidOperationException($"TIA returned incomplete readback for tag '{expected.Name}'.");
        if (!DataTypeMatches(expected, values[1] as string, values[9]?.ToString()))
            throw new InvalidOperationException($"Tag '{expected.Name}' expected type '{expected.DataType}' but read back PLC/tag type '{values[1]}' and HMI type '{values[9]}'.");
        if (
            !string.Equals(values[0] as string, expected.Name, StringComparison.Ordinal) ||
            !string.Equals(values[2] as string, expected.Connection ?? "<Internal tag>", StringComparison.Ordinal) ||
            !string.Equals(values[3] as string ?? "", expectedPlc ?? "", StringComparison.Ordinal) ||
            !string.Equals(values[4] as string ?? "", expected.PlcTag ?? "", StringComparison.Ordinal) ||
            (expected.Connection != null && !Equals(values[5], HmiAccessMode.SymbolicAccess)) ||
            (expected.InitialValue != null && !Equals(values[6], expected.InitialValue)) ||
            !Equals(values[7], HmiTagType.Simple) ||
            !string.Equals(values[8] as string, tableName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Tag '{expected.Name}' read back with an unexpected table, tag type, binding, or initial value.");
        }
    }

    private static bool DataTypeMatches(HmiTagBatchTag expected, string? tagType, string? hmiType)
    {
        // V21 serializes PLC TIME as the HMI representation LTime with this controller type.
        if (expected.Connection != null && expected.WinCcMlControllerDataType == "S7PTransTimeType" &&
            string.Equals(expected.DataType, "LTime", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(tagType, "Time", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(hmiType, "LTime", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(tagType, expected.DataType, StringComparison.OrdinalIgnoreCase);
    }
}
