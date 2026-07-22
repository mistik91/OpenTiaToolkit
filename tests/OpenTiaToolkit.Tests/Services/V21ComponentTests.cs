using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class V21ComponentTests
    {
        [TestMethod]
        public void TypeResolver_EmptyNamesReturnNull()
        {
            var resolver = new V21TypeServiceResolver();
            Assert.IsNull(resolver.FindTypeByName(string.Empty));
            Assert.IsNull(resolver.FindTypeByFullName(" "));
        }

        [TestMethod]
        public void TypeResolver_UnsupportedServiceObjectReturnsNull()
        {
            var resolver = new V21TypeServiceResolver();
            Assert.IsNull(resolver.GetService(new object(), typeof(IDisposable)));
        }

        [TestMethod]
        public void TypeResolver_AmbiguousShortNameReturnsNull()
        {
            var resolver = new V21TypeServiceResolver();
            Assert.IsNull(resolver.FindTypeByName("AmbiguousResolverFixture"));
        }

        [TestMethod]
        public void TypeResolver_FullNameIsExactAndCaseSensitive()
        {
            var resolver = new V21TypeServiceResolver();
            Assert.AreEqual(typeof(ResolverFixture.One.AmbiguousResolverFixture), resolver.FindTypeByFullName(typeof(ResolverFixture.One.AmbiguousResolverFixture).FullName!));
            Assert.IsNull(resolver.FindTypeByFullName(typeof(ResolverFixture.One.AmbiguousResolverFixture).FullName!.ToLowerInvariant()));
        }

        [TestMethod]
        public void DocumentAccess_ResolvesProgrammingLanguageWithoutFacade()
        {
            var documents = new V21DocumentAccess(new V21TypeServiceResolver());
            Assert.AreEqual("ProDiag", documents.GetProgrammingLanguageValue("PRODIAG")?.ToString());
        }

        [TestMethod]
        public void CompilationProjector_FlattensDiagnosticTree()
        {
            var roots = new[] { new DiagnosticNode("Warning", "PLC", "root", new[] { new DiagnosticNode("Error", "PLC/Main", "child") }) };
            var result = V21CompilationDiagnosticProjector.Project(roots, node => node.State, node => node.Path, node => node.Description, node => node.Children);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("PLC/Main", result[1].Path);
            Assert.AreEqual("child", result[1].Description);
        }

        [TestMethod]
        public void OnlineAccess_UnsupportedObjectsReturnFailureWithoutMutation()
        {
            var online = new V21OnlineVerificationAccess(new V21TypeServiceResolver());
            Assert.IsFalse(online.TryCompareToOnline(new object(), out _, out var compareFailure));
            Assert.IsFalse(online.TryDownloadSoftware(new object(), false, out _, out var downloadFailure));
            Assert.IsFalse(online.TryGoOnline(new object(), out _, out var onlineFailure));
            Assert.IsFalse(string.IsNullOrWhiteSpace(compareFailure));
            Assert.IsFalse(string.IsNullOrWhiteSpace(downloadFailure));
            Assert.IsFalse(string.IsNullOrWhiteSpace(onlineFailure));
        }

        [TestMethod]
        public void DocumentImport_CreatesDefaultTrailingArgument()
        {
            var target = new ThreeArgumentImportTarget();
            var documents = new V21DocumentAccess(new V21TypeServiceResolver());
            Assert.IsTrue(documents.TryImport(target, new FileInfo(Path.Combine(Path.GetTempPath(), "input.xml")), 1, out _));
            Assert.AreEqual(FakeImportMode.Override, target.Mode);
            Assert.IsFalse(target.ValidateOnly);
        }

        private sealed class DiagnosticNode
        {
            public DiagnosticNode(string state, string path, string description, IEnumerable<DiagnosticNode>? children = null)
            { State = state; Path = path; Description = description; Children = children ?? Enumerable.Empty<DiagnosticNode>(); }
            public string State { get; }
            public string Path { get; }
            public string Description { get; }
            public IEnumerable<DiagnosticNode> Children { get; }
        }

        private enum FakeImportMode { None, Override }
        private sealed class ThreeArgumentImportTarget
        {
            public FakeImportMode Mode { get; private set; }
            public bool ValidateOnly { get; private set; }
            public object Import(FileInfo file, FakeImportMode mode, bool validateOnly)
            { Mode = mode; ValidateOnly = validateOnly; return this; }
        }
    }
}

namespace ResolverFixture.One { internal sealed class AmbiguousResolverFixture { } }
namespace ResolverFixture.Two { internal sealed class AmbiguousResolverFixture { } }
