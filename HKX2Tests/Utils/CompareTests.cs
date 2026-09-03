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
        /// The xml representation must be a fixpoint: everything xml is able to
        /// express has to survive being read back and written again.
        ///
        /// This deliberately compares the xml documents rather than the object
        /// graphs. Havok's xml writer composes every matrix type out of just two
        /// group formats, "(%f %f %f)" and "(%f %f %f %f)", so hkMatrix3 is
        /// written as 9 floats, hkQsTransform as 10 and hkTransform as 12. The
        /// unused w lane of each 3-float group is simply not in the format, and a
        /// round trip through Havok itself drops it too. Demanding an equal object
        /// graph would be demanding something xml cannot represent.
        ///
        /// Comparing documents tolerates that omission while still catching any
        /// value xml does carry being mangled. Precision, which this check cannot
        /// see because a lossy format is lossy in both directions, is pinned
        /// separately by PrimitiveRoundTripTests.
        /// </summary>
        [TestMethod]
        public void XmlRoundTripIsStable()
        {
            var files = Corpus.RequireFiles();
            var failures = new FailureLog();

            foreach (var item in files)
            {
                try
                {
                    var first = ToXml((hkRootLevelContainer)Util.ReadHKX(item));

                    MemoryStream ms = new(first);
                    var second = ToXml((hkRootLevelContainer)Util.ReadXml(ms, Header));

                    if (!first.AsSpan().SequenceEqual(second))
                        failures.Add(item, $"xml changed on the second pass ({first.Length} vs {second.Length} bytes)");
                }
                catch (Exception ex)
                {
                    failures.Add(item, $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            failures.AssertNone(files.Count, nameof(XmlRoundTripIsStable));
        }

        private static byte[] ToXml(hkRootLevelContainer root)
        {
            MemoryStream ms = new();
            Util.WriteXml(root, Header, ms);
            return ms.ToArray();
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
        /// Re-serializing a vanilla file should reproduce it byte for byte.
        ///
        /// It does not quite hold for the whole corpus: a handful of files still
        /// differ in layout rather than in data.
        ///
        /// So this is a ratchet rather than an equality check. It exists to catch
        /// read-side data loss, which is otherwise invisible to the deep-compare
        /// tests above: a lossy read corrupts both sides of the comparison
        /// identically, so the graphs still match while the bytes drift. When
        /// ReadSingle rounded floats to 6 decimals this figure was 3%.
        /// </summary>
        [TestMethod]
        public void HkxRoundTripIsByteIdenticalForMostOfCorpus()
        {
            const double MinimumRatio = 0.99;

            var files = Corpus.RequireFiles();
            int identical = 0, grew = 0, shrank = 0, sameLengthDiff = 0;
            var failures = new FailureLog();

            foreach (var item in files)
            {
                try
                {
                    var original = File.ReadAllBytes(item);
                    var written = Util.WriteHKX(Util.ReadHKX(item), Header);

                    if (original.AsSpan().SequenceEqual(written)) identical++;
                    else if (written.Length > original.Length) grew++;
                    else if (written.Length < original.Length) shrank++;
                    else sameLengthDiff++;
                }
                catch (Exception ex)
                {
                    failures.Add(item, $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            failures.AssertNone(files.Count, nameof(HkxRoundTripIsByteIdenticalForMostOfCorpus));

            var ratio = (double)identical / files.Count;
            Trace.WriteLine($"byte-identical {identical}/{files.Count} ({ratio:P2}); " +
                            $"same-length differences {sameLengthDiff}, grew {grew}, shrank {shrank}");

            Assert.IsTrue(ratio >= MinimumRatio,
                $"Byte fidelity regressed: {identical}/{files.Count} ({ratio:P2}) files re-serialize " +
                $"byte-identically, below the {MinimumRatio:P0} floor. " +
                $"(same-length differences {sameLengthDiff}, grew {grew}, shrank {shrank}). " +
                $"A large drop here usually means a reader is discarding data.");
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
