using System.Diagnostics;

namespace HKX2.Tests
{
    /// <summary>
    /// Round-trip tests over the whole .hkx corpus. See <see cref="Corpus"/> for
    /// how the corpus is located.
    /// </summary>
    [TestClass]
    public class CompareTests
    {
        private static HKXHeader Header => HKXHeader.SkyrimSE();

        /// <summary>
        /// hkx -> object graph -> hkx -> object graph must produce an equal graph.
        /// </summary>
        [TestMethod]
        public void HkxToHkxDeepCompare()
        {
            var files = Corpus.RequireFiles();
            var failures = new FailureLog();

            foreach (var item in files)
            {
                try
                {
                    var root = Util.ReadHKX(item);
                    var bytes = Util.WriteHKX(root, Header);
                    var root2 = Util.ReadHKX(bytes);

                    if (!root.Equals(root2))
                        failures.Add(item, "deep compare failed after hkx round trip");
                }
                catch (Exception ex)
                {
                    failures.Add(item, $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            failures.AssertNone(files.Count, nameof(HkxToHkxDeepCompare));
        }

        /// <summary>
        /// hkx -> object graph -> xml -> object graph must produce an equal graph.
        /// </summary>
        [TestMethod]
        public void HkxToXmlToHkxDeepCompare()
        {
            var files = Corpus.RequireFiles();
            var failures = new FailureLog();

            foreach (var item in files)
            {
                try
                {
                    var root = (hkRootLevelContainer)Util.ReadHKX(item);

                    MemoryStream ms = new();
                    Util.WriteXml(root, Header, ms);
                    ms.Position = 0;

                    var root2 = (hkRootLevelContainer)Util.ReadXml(ms, Header);

                    if (!root.Equals(root2))
                        failures.Add(item, "deep compare failed after xml round trip");
                }
                catch (Exception ex)
                {
                    failures.Add(item, $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            failures.AssertNone(files.Count, nameof(HkxToXmlToHkxDeepCompare));
        }

        /// <summary>
        /// Serialization must be idempotent: once a file has been through the
        /// library, writing it again must produce identical bytes. A file that
        /// keeps changing on every pass indicates non-deterministic layout.
        /// </summary>
        [TestMethod]
        public void HkxSerializationIsIdempotent()
        {
            var files = Corpus.RequireFiles();
            var failures = new FailureLog();

            foreach (var item in files)
            {
                try
                {
                    var pass1 = Util.WriteHKX(Util.ReadHKX(item), Header);
                    var pass2 = Util.WriteHKX(Util.ReadHKX(pass1), Header);

                    if (!pass1.AsSpan().SequenceEqual(pass2))
                        failures.Add(item, $"second serialization differs ({pass1.Length} vs {pass2.Length} bytes)");
                }
                catch (Exception ex)
                {
                    failures.Add(item, $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            failures.AssertNone(files.Count, nameof(HkxSerializationIsIdempotent));
        }

        /// <summary>
        /// Round-tripping must never make a file grow. Growth means the writer is
        /// materializing something the reader invented - the null-vs-empty string
        /// pointer confusion did exactly this, adding an empty string entry plus a
        /// local fixup for every null string pointer.
        /// </summary>
        [TestMethod]
        public void HkxRoundTripDoesNotGrowFiles()
        {
            var files = Corpus.RequireFiles();
            var failures = new FailureLog();

            foreach (var item in files)
            {
                try
                {
                    var original = File.ReadAllBytes(item);
                    var written = Util.WriteHKX(Util.ReadHKX(item), Header);

                    if (written.Length > original.Length)
                        failures.Add(item, $"grew from {original.Length} to {written.Length} bytes");
                }
                catch (Exception ex)
                {
                    failures.Add(item, $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            failures.AssertNone(files.Count, nameof(HkxRoundTripDoesNotGrowFiles));
        }
    }
}
