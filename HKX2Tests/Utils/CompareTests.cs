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
        /// Re-serializing a vanilla file reproduces it byte for byte.
        ///
        /// This is the check that catches read-side data loss, which the
        /// deep-compare tests structurally cannot see: a lossy reader damages
        /// both sides of the comparison identically, so the graphs still match
        /// while the bytes drift. When ReadSingle rounded floats to 6 decimals
        /// only 3% of the corpus survived this.
        /// </summary>
        [TestMethod]
        public void HkxRoundTripIsByteIdentical()
        {
            var files = Corpus.RequireFiles();
            var failures = new FailureLog();

            foreach (var item in files)
            {
                try
                {
                    var original = File.ReadAllBytes(item);
                    var written = Util.WriteHKX(Util.ReadHKX(item), Header);

                    if (original.AsSpan().SequenceEqual(written)) continue;

                    var where = FirstDifference(original, written);
                    failures.Add(item, original.Length != written.Length
                        ? $"length changed, {original.Length} -> {written.Length} bytes (first difference at 0x{where:X})"
                        : $"differs from offset 0x{where:X}");
                }
                catch (Exception ex)
                {
                    failures.Add(item, $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            failures.AssertNone(files.Count, nameof(HkxRoundTripIsByteIdentical));
        }

        private static int FirstDifference(byte[] a, byte[] b)
        {
            var shared = Math.Min(a.Length, b.Length);
            for (var i = 0; i < shared; i++)
                if (a[i] != b[i])
                    return i;

            return shared;
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
