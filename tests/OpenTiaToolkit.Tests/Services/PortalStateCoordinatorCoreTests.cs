using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class PortalStateCoordinatorCoreTests
    {
        [TestMethod]
        public void CommitProject_ClearsDependentSelectionsAndClosesPreviousOwnedSession()
        {
            var store = ExistingState(ownsSession: true);
            var closed = new List<Resource>();
            var core = Create(store, closeSession: closed.Add);
            var replacement = new Resource("new-project");

            core.CommitProject(null, replacement, ownsProject: false, ownsSession: false);

            Assert.AreSame(replacement, store.Project);
            Assert.IsTrue(store.DependentSelectionsCleared);
            CollectionAssert.AreEqual(new[] { new Resource("old-session") }, closed);
        }

        [TestMethod]
        public void CommitProject_DoesNotClosePreviousUnownedResources()
        {
            var store = ExistingState();
            var closed = new List<Resource>();
            var core = Create(store, closeSession: closed.Add, closeProject: closed.Add);

            core.CommitProject(null, new Resource("new-project"), false, false);

            Assert.AreEqual(0, closed.Count);
        }

        [TestMethod]
        public void CommitProject_ClosesPreviousOwnedProjectWhenNoSessionIsOwned()
        {
            var store = ExistingState(ownsProject: true);
            var closed = new List<Resource>();
            var core = Create(store, closeProject: closed.Add);

            core.CommitProject(null, new Resource("new-project"), false, false);

            CollectionAssert.AreEqual(new[] { new Resource("old-project") }, closed);
        }

        [TestMethod]
        public void CommitPortal_DisposesObsoletePortalAfterReplacementIsVisible()
        {
            var store = ExistingState();
            var observedReplacement = false;
            var replacement = new Resource("new-portal");
            var core = Create(store, disposePortal: old =>
            {
                Assert.AreEqual("old-portal", old.Name);
                observedReplacement = ReferenceEquals(store.Portal, replacement);
            });

            core.CommitPortal(replacement, 42, null, null, false, false, true);

            Assert.IsTrue(observedReplacement);
            Assert.AreEqual(42, store.AttachedProcessId);
            Assert.IsTrue(store.IsPortalVisible);
        }

        [TestMethod]
        public void CleanupFailure_DoesNotEraseCommittedReplacement()
        {
            var store = ExistingState(ownsProject: true);
            var replacement = new Resource("new-project");
            var warnings = new List<string>();
            var core = Create(store,
                closeProject: _ => throw new InvalidOperationException("close failed"),
                warning: (_, operation) => warnings.Add(operation));

            core.CommitProject(null, replacement, false, false);

            Assert.AreSame(replacement, store.Project);
            Assert.AreEqual(1, warnings.Count);
        }

        [TestMethod]
        public void Dispose_ClearsAllStateDespiteCleanupExceptions()
        {
            var store = ExistingState(ownsSession: true);
            var core = Create(store,
                closeSession: _ => throw new InvalidOperationException("close failed"),
                disposePortal: _ => throw new InvalidOperationException("dispose failed"));

            core.Dispose();

            Assert.IsNull(store.Portal);
            Assert.IsNull(store.Project);
            Assert.IsNull(store.Session);
            Assert.IsNull(store.AttachedProcessId);
        }

        private static FakeStore ExistingState(bool ownsProject = false, bool ownsSession = false) => new FakeStore
        {
            Portal = new Resource("old-portal"),
            Project = new Resource("old-project"),
            Session = new Resource("old-session"),
            OwnsProject = ownsProject,
            OwnsSession = ownsSession,
            AttachedProcessId = 7,
            IsPortalVisible = true
        };

        private static PortalStateCoordinatorCore<Resource, Resource, Resource> Create(
            FakeStore store,
            Action<Resource>? closeSession = null,
            Action<Resource>? closeProject = null,
            Action<Resource>? disposePortal = null,
            Action<Exception, string>? warning = null) =>
            new PortalStateCoordinatorCore<Resource, Resource, Resource>(
                store,
                closeSession ?? (_ => { }),
                closeProject ?? (_ => { }),
                disposePortal ?? (_ => { }),
                warning);

        private sealed class FakeStore : IPortalStateStore<Resource, Resource, Resource>
        {
            public Resource? Portal { get; set; }
            public int? AttachedProcessId { get; set; }
            public bool IsPortalVisible { get; set; }
            public Resource? Project { get; set; }
            public Resource? Session { get; set; }
            public bool OwnsProject { get; set; }
            public bool OwnsSession { get; set; }
            public bool DependentSelectionsCleared { get; private set; }
            public void ClearPortalSelection() { Portal = null; AttachedProcessId = null; IsPortalVisible = false; ClearProjectSelection(); }
            public void ClearProjectSelection() { Project = null; Session = null; OwnsProject = false; OwnsSession = false; DependentSelectionsCleared = true; }
        }

        private sealed class Resource
        {
            public Resource(string name) => Name = name;
            public string Name { get; }
            public override bool Equals(object? obj) => obj is Resource other && other.Name == Name;
            public override int GetHashCode() => Name.GetHashCode();
        }
    }
}
