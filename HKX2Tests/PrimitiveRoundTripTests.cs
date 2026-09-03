namespace HKX2.Tests
{
    /// <summary>
    /// Regression tests for value-level fidelity. These need no corpus.
    ///
    /// They cover the two failure modes the deep-compare tests structurally
    /// cannot see: when a reader mangles a value it mangles it identically on
    /// both sides of the comparison, so the object graphs still match.
    /// </summary>
    [TestClass]
    public class PrimitiveRoundTripTests
    {
        private static readonly float[] TrickyFloats =
        {
            0f,
            -0f,
            1f,
            -1f,
            1f / 3f,                  // 0.33333334 - needs more than 6 decimals
            4f / 3f,                  // 1.3333334
            0.1f,
            1e-7f,                    // rounds to 1e-07 at 6 decimals
            5.010725e-07f,            // seen in vanilla skeletons
            1e-8f,                    // would be flushed to zero by rounding
            float.Epsilon,            // denormal
            -float.Epsilon,
            13.5894165f,              // seen in vanilla skeletons
            float.MaxValue,
            float.MinValue,
            float.PositiveInfinity,
            float.NegativeInfinity,
        };

        /// <summary>
        /// Every float must survive a write/read cycle with its exact bit pattern.
        /// ReadSingle previously applied Math.Round(value, 6), which silently
        /// perturbed roughly 150k values across the vanilla corpus and flushed
        /// small magnitudes to zero.
        /// </summary>
        [TestMethod]
        public void SingleRoundTripsBitExactly()
        {
            foreach (var value in TrickyFloats)
            {
                var ms = new MemoryStream();
                new BinaryWriterEx(ms).WriteSingle(value);

                var read = new BinaryReaderEx(ms.ToArray()).ReadSingle();

                Assert.AreEqual(
                    BitConverter.SingleToUInt32Bits(value),
                    BitConverter.SingleToUInt32Bits(read),
                    $"float {value:R} did not round trip bit-exactly (got {read:R})");
            }
        }

        /// <summary>
        /// NaN is a meaningful value in Havok data, not a decoding error.
        /// ReadSingle previously replaced it with 0.
        /// </summary>
        [TestMethod]
        public void NaNSurvivesRoundTrip()
        {
            var ms = new MemoryStream();
            new BinaryWriterEx(ms).WriteSingle(float.NaN);

            var read = new BinaryReaderEx(ms.ToArray()).ReadSingle();

            Assert.IsTrue(float.IsNaN(read), $"NaN was not preserved (got {read:R})");
        }

        /// <summary>
        /// A null string pointer and a pointer to an empty string are different
        /// things on disk, and must stay different through a packfile round trip.
        /// Reading a missing fixup as string.Empty made the writer materialize a
        /// real empty-string entry, growing 780 corpus files.
        /// </summary>
        [TestMethod]
        public void NullAndEmptyStringPointersStayDistinctThroughHkx()
        {
            var root = MakeRoot(name: null, className: "");

            var bytes = Util.WriteHKX(root, HKXHeader.SkyrimSE());
            var read = (hkRootLevelContainer)Util.ReadHKX(bytes);

            var variant = read.m_namedVariants.Single();
            Assert.IsNotNull(variant);
            Assert.IsNull(variant!.m_name, "null string pointer came back as something else");
            Assert.AreEqual("", variant.m_className, "empty string came back as something else");
        }

        /// <summary>
        /// The same distinction must survive the xml round trip, which encodes
        /// null as U+2400.
        /// </summary>
        [TestMethod]
        public void NullAndEmptyStringPointersStayDistinctThroughXml()
        {
            var root = MakeRoot(name: null, className: "");

            MemoryStream ms = new();
            Util.WriteXml(root, HKXHeader.SkyrimSE(), ms);
            ms.Position = 0;

            var read = (hkRootLevelContainer)Util.ReadXml(ms, HKXHeader.SkyrimSE());

            var variant = read.m_namedVariants.Single();
            Assert.IsNotNull(variant);
            Assert.IsNull(variant!.m_name, "null string pointer came back as something else");
            Assert.AreEqual("", variant.m_className, "empty string came back as something else");
        }

        private static hkRootLevelContainer MakeRoot(string? name, string className) => new()
        {
            m_namedVariants = new List<hkRootLevelContainerNamedVariant?>
            {
                new()
                {
                    m_name = name!,
                    m_className = className,
                    m_variant = null,
                },
            },
        };
    }
}
