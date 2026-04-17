using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class UnifiedRichTextCodecTests
    {
        [TestMethod]
        public void Encode_WrapsPlainTextInUnifiedMarkup()
        {
            var encoded = UnifiedRichTextCodec.Encode("Hello & world");

            Assert.AreEqual("<body><p>Hello &amp; world</p></body>", encoded);
        }

        [TestMethod]
        public void Encode_LeavesExistingRichTextUntouched()
        {
            var encoded = UnifiedRichTextCodec.Encode("<body><p>Hello</p></body>");

            Assert.AreEqual("<body><p>Hello</p></body>", encoded);
        }

        [TestMethod]
        public void Decode_UnwrapsUnifiedMarkupToPlainText()
        {
            var decoded = UnifiedRichTextCodec.Decode("<body><p>Hello &amp; world</p><p>Second line</p></body>");

            Assert.AreEqual("Hello & world\nSecond line", decoded);
        }
    }
}
