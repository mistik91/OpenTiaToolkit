using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class EngineeringSessionContextTests
    {
        [ClassInitialize]
        public static void RegisterSiemensAssemblyResolver(TestContext _)
        {
            TiaPortalAssemblyResolver.Register();
        }

        [TestMethod]
        public void ClearProjectSelection_ClearsBothPlcAndHmiNamesAndOwnership()
        {
            var context = new EngineeringSessionContext
            {
                CurrentPlcName = "PLC_1",
                CurrentHmiName = "HMI_1",
                OwnsCurrentProject = true,
                OwnsCurrentSession = true
            };

            context.ClearProjectSelection();

            Assert.IsNull(context.CurrentPlcName);
            Assert.IsNull(context.CurrentHmiName);
            Assert.IsFalse(context.OwnsCurrentProject);
            Assert.IsFalse(context.OwnsCurrentSession);
        }
    }
}
