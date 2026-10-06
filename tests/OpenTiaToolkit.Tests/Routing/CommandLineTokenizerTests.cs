using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.CLI.Routing;

namespace OpenTiaToolkit.Tests.Routing
{
    [TestClass]
    public class CommandLineTokenizerTests
    {
        [TestMethod]
        public void TryTokenize_HandlesQuotedValuesAndEscapedQuotes()
        {
            var success = CommandLineTokenizer.TryTokenize(
                "open-project --project-path=\"C:\\Projects\\My Project.ap21\" --name \"Panel \\\"A\\\"\"",
                out var args,
                out var error);

            Assert.IsTrue(success, error);
            CollectionAssert.AreEqual(
                new[]
                {
                    "open-project",
                    "--project-path=C:\\Projects\\My Project.ap21",
                    "--name",
                    "Panel \"A\""
                },
                args);
        }

        [TestMethod]
        public void TryTokenize_ReturnsErrorForUnbalancedQuote()
        {
            var success = CommandLineTokenizer.TryTokenize(
                "open-project --project-path=\"C:\\Projects\\My Project.ap21",
                out var args,
                out var error);

            Assert.IsFalse(success);
            Assert.AreEqual(0, args.Length);
            StringAssert.Contains(error, "Unbalanced quote");
        }
    }
}
