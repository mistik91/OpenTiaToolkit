using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.Hardware.Patching
{
    internal sealed class HardwareGsdPatchHandler : IHardwarePatchOperationHandler
    {
        public IReadOnlyCollection<string> Operations { get; } = new[] { "set-gsd-parameter-data", "get-gsd-parameter-data" };

        public HardwareOperationResult Execute(string operation, int index, HardwarePatchOperation request, HardwarePatchExecutionContext context) =>
            operation == "set-gsd-parameter-data" ? Set(index, request, context) : Get(index, request, context);

        private static HardwareOperationResult Set(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            if (!op.DatasetNumber.HasValue) throw new InvalidOperationException("datasetNumber is required for set-gsd-parameter-data.");
            HardwarePatchRules.RequireText(op.DataHex, "dataHex");
            var target = context.Resolve(op);
            var bytes = HardwareHexCodec.Parse(op.DataHex!);
            var before = context.Attributes.GetGsdParameterData(target.Object, op.DatasetNumber.Value, op.ByteOffset, bytes.Length);
            if (!string.IsNullOrWhiteSpace(op.ExpectedDataHex))
            {
                var expected = HardwareHexCodec.Parse(op.ExpectedDataHex!);
                if (before == null || !before.SequenceEqual(expected))
                    throw new InvalidOperationException($"GSD parameter data differs from expectedDataHex at dataset {op.DatasetNumber.Value}, offset {op.ByteOffset}.");
            }
            context.Attributes.SetGsdParameterData(target.Object, op.DatasetNumber.Value, op.ByteOffset, bytes);
            return context.Success(index, op, target.Reference, $"Wrote {bytes.Length} GSD parameter byte(s).", new HardwareGsdParameterChangeInfo
            {
                DatasetNumber = op.DatasetNumber.Value,
                ByteOffset = op.ByteOffset,
                BeforeHex = before == null ? null : HardwareHexCodec.Format(before),
                WrittenHex = HardwareHexCodec.Format(bytes)
            });
        }

        private static HardwareOperationResult Get(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            if (!op.DatasetNumber.HasValue) throw new InvalidOperationException("datasetNumber is required for get-gsd-parameter-data.");
            if (!op.Length.HasValue || op.Length.Value <= 0) throw new InvalidOperationException("length is required for get-gsd-parameter-data.");
            var target = context.Resolve(op);
            var bytes = context.Attributes.GetGsdParameterData(target.Object, op.DatasetNumber.Value, op.ByteOffset, op.Length.Value)
                ?? throw new InvalidOperationException("GSD parameter data could not be read.");
            return context.Success(index, op, target.Reference, $"Read {bytes.Length} GSD parameter byte(s).", new HardwareGsdParameterData
            {
                Target = target.Reference,
                DatasetNumber = op.DatasetNumber.Value,
                ByteOffset = op.ByteOffset,
                Length = bytes.Length,
                DataHex = HardwareHexCodec.Format(bytes)
            });
        }
    }
}
