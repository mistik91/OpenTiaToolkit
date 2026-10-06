using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class GetProjectTreeCommandTests
    {
        [TestMethod]
        public async Task OmittedDepth_ForwardsNullForUnlimitedTraversal()
        {
            int? observedDepth = 123;
            var service = CreateService(depth => observedDepth = depth);

            var result = await new GetProjectTreeCommand(service.Object).ExecuteAsync(new CommandArgs(new Dictionary<string, object?>()), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            Assert.IsNull(observedDepth);
        }

        [TestMethod]
        public async Task ExplicitDepth_IsForwarded()
        {
            int? observedDepth = null;
            var service = CreateService(depth => observedDepth = depth);

            var result = await new GetProjectTreeCommand(service.Object).ExecuteAsync(new CommandArgs(new Dictionary<string, object?> { ["depth"] = 2 }), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, observedDepth);
        }

        private static Mock<IPortalService> CreateService(System.Action<int?> observe)
        {
            var service = new Mock<IPortalService>();
            service.SetupGet(candidate => candidate.IsConnected).Returns(true);
            service.Setup(candidate => candidate.GetProjectTreeAsync(It.IsAny<int?>()))
                .Returns<int?>(depth => { observe(depth); return Task.FromResult<ProjectTreeNode?>(new ProjectTreeNode("P", "Project", "P", System.Array.Empty<ProjectTreeNode>())); });
            return service;
        }
    }
}
