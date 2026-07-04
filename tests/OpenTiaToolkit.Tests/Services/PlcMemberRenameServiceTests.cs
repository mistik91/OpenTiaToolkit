using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class PlcMemberRenameServiceTests
    {
        [TestMethod]
        public async Task FindAndReplaceDbMembersAsync_ReportsProgressAndCancelsBetweenMembers()
        {
            var service = CreateService(new FakePlcSoftware
            {
                BlockGroup = new FakeBlockGroup
                {
                    Blocks =
                    {
                        new GlobalDB
                        {
                            Name = "DB_Settings",
                            Structure = new FakeStructure
                            {
                                Composition = new FakeComposition
                                {
                                    Members =
                                    {
                                        new FakeMember { Name = "Signal1" },
                                        new FakeMember { Name = "Signal2" }
                                    }
                                }
                            }
                        }
                    }
                }
            });
            using var cancellation = new CancellationTokenSource();
            var updates = new List<BatchOperationProgress>();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
                await service.FindAndReplaceDbMembersAsync(
                    new Dictionary<string, string> { ["Signal1"] = "SignalA", ["Signal2"] = "SignalB" },
                    dryRun: true,
                    cancellationToken: cancellation.Token,
                    progress: new InlineProgress<BatchOperationProgress>(update =>
                    {
                        updates.Add(update);
                        if (update.Stage == "find-replace-db-members" && update.Completed == 1 && update.Total == null)
                        {
                            cancellation.Cancel();
                        }
                    })));

            Assert.IsTrue(updates.Any(update => update.Stage == "find-replace-db-members" && update.Completed == 1));
        }

        [TestMethod]
        public async Task FindAndReplaceUdtMembersAsync_ReportsProgressAndCancelsBetweenMembers()
        {
            var service = CreateService(new FakePlcSoftware
            {
                TypeGroup = new FakeTypeGroup
                {
                    Types =
                    {
                        new PlcStruct
                        {
                            Name = "DriveData",
                            Structure = new FakeStructure
                            {
                                Members =
                                {
                                    new FakeMember { Name = "Signal1" },
                                    new FakeMember { Name = "Signal2" }
                                }
                            }
                        }
                    }
                }
            });
            using var cancellation = new CancellationTokenSource();
            var updates = new List<BatchOperationProgress>();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
                await service.FindAndReplaceUdtMembersAsync(
                    new Dictionary<string, string> { ["Signal1"] = "SignalA", ["Signal2"] = "SignalB" },
                    dryRun: true,
                    cancellationToken: cancellation.Token,
                    progress: new InlineProgress<BatchOperationProgress>(update =>
                    {
                        updates.Add(update);
                        if (update.Stage == "find-replace-udt-members" && update.Completed == 1 && update.Total == null)
                        {
                            cancellation.Cancel();
                        }
                    })));

            Assert.IsTrue(updates.Any(update => update.Stage == "find-replace-udt-members" && update.Completed == 1));
        }

        private static PlcMemberRenameService CreateService(FakePlcSoftware plcSoftware)
        {
            return new PlcMemberRenameService(
                new EngineeringSessionContext
                {
                    PlcSoftware = plcSoftware,
                    CurrentPlcName = "PLC_1"
                },
                logger: null);
        }

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;

            public InlineProgress(Action<T> report)
            {
                _report = report;
            }

            public void Report(T value)
            {
                _report(value);
            }
        }

        private sealed class FakePlcSoftware
        {
            public FakeBlockGroup? BlockGroup { get; set; }

            public FakeTypeGroup? TypeGroup { get; set; }
        }

        private sealed class FakeBlockGroup
        {
            public List<object> Blocks { get; } = new List<object>();

            public List<FakeBlockGroup> Groups { get; } = new List<FakeBlockGroup>();
        }

        private sealed class FakeTypeGroup
        {
            public List<object> Types { get; } = new List<object>();

            public List<FakeTypeGroup> Groups { get; } = new List<FakeTypeGroup>();
        }

        private sealed class GlobalDB
        {
            public string Name { get; set; } = string.Empty;

            public FakeStructure Structure { get; set; } = new FakeStructure();
        }

        private sealed class PlcStruct
        {
            public string Name { get; set; } = string.Empty;

            public FakeStructure Structure { get; set; } = new FakeStructure();
        }

        private sealed class FakeStructure
        {
            public FakeComposition? Composition { get; set; }

            public List<object> Members { get; } = new List<object>();
        }

        private sealed class FakeComposition
        {
            public List<object> Members { get; } = new List<object>();
        }

        private sealed class FakeMember
        {
            public string Name { get; set; } = string.Empty;

            public FakeComposition? Composition { get; set; }
        }
    }
}
