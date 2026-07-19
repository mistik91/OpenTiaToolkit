using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class PortalProjectStateInspectorTests
    {
        [TestMethod]
        public void ProjectMatches_WhenPathsDiffer_DoesNotFallBackToMatchingName()
        {
            var matches = PortalProjectStateInspector.ProjectMatches(
                @"C:\Projects\A\Line1.ap21",
                "Line1",
                @"C:\Projects\B\Line1.ap21",
                "Line1");

            Assert.IsFalse(matches);
        }

        [TestMethod]
        public void ProjectMatches_WhenCandidatePathIsUnavailable_FallsBackToName()
        {
            var matches = PortalProjectStateInspector.ProjectMatches(
                null,
                "Line1",
                @"C:\Projects\Line1.ap21",
                "Line1");

            Assert.IsTrue(matches);
        }

        [TestMethod]
        public void PathEquals_NormalizesCaseAndTrailingSeparators()
        {
            Assert.IsTrue(PortalProjectStateInspector.PathEquals(
                @"C:\Projects\Line1.ap21\",
                @"c:\projects\LINE1.ap21"));
        }
    }
}
