using System.Diagnostics;

namespace HKX2.Tests
{
    /// <summary>
    /// Locates the corpus of .hkx files the round-trip tests run against.
    ///
    /// The corpus is not part of the repository (it is extracted game data), so it
    /// is supplied out of band via the HKX2_CORPUS environment variable, e.g.
    ///
    ///     HKX2_CORPUS=~/Dev/BSAFileExtractor/extracted/meshes dotnet test
    ///
    /// Failing that, a "corpus" directory next to the test binary is used.
    /// When neither exists the corpus tests report Inconclusive rather than fail,
    /// so `dotnet test` still works on a machine without the game files.
    /// </summary>
    public static class Corpus
    {
        public const string EnvVar = "HKX2_CORPUS";

        public static string? Root
        {
            get
            {
                var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
                if (!string.IsNullOrWhiteSpace(fromEnv))
                {
                    var expanded = fromEnv.StartsWith("~/")
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fromEnv[2..])
                        : fromEnv;
                    return Directory.Exists(expanded) ? expanded : null;
                }

                var local = Path.Combine(AppContext.BaseDirectory, "corpus");
                return Directory.Exists(local) ? local : null;
            }
        }

        /// <summary>
        /// All .hkx files in the corpus, in a stable order so failures are reproducible.
        /// Returns an empty list when no corpus is configured.
        /// </summary>
        public static IReadOnlyList<string> Files()
        {
            var root = Root;
            if (root is null) return Array.Empty<string>();

            return Directory.EnumerateFiles(root, "*.hkx", SearchOption.AllDirectories)
                            .OrderBy(f => f, StringComparer.Ordinal)
                            .ToList();
        }

        /// <summary>
        /// Skips the calling test (Inconclusive) unless a corpus is available.
        /// </summary>
        public static IReadOnlyList<string> RequireFiles()
        {
            var root = Root;
            if (root is null)
                Assert.Inconclusive(
                    $"No HKX corpus configured. Set {EnvVar} to a directory of .hkx files " +
                    $"(or place one at {Path.Combine(AppContext.BaseDirectory, "corpus")}) to run this test.");

            var files = Files();
            if (files.Count == 0)
                Assert.Inconclusive($"Corpus at '{root}' contains no .hkx files.");

            Trace.WriteLine($"Corpus: {files.Count} files under {root}");
            return files;
        }

        public static string Rel(string file) => Path.GetRelativePath(Root ?? "", file);
    }

    /// <summary>
    /// Accumulates per-file failures so one run reports every bad file, instead of
    /// aborting on the first one.
    /// </summary>
    public sealed class FailureLog
    {
        private readonly List<string> _failures = new();
        private const int MaxReported = 25;

        public int Count => _failures.Count;

        public void Add(string file, string reason) => _failures.Add($"{Corpus.Rel(file)}: {reason}");

        public void AssertNone(int total, string what)
        {
            if (_failures.Count == 0) return;

            var shown = string.Join(Environment.NewLine, _failures.Take(MaxReported).Select(f => "  " + f));
            var more = _failures.Count > MaxReported
                ? $"{Environment.NewLine}  ... and {_failures.Count - MaxReported} more"
                : "";

            Assert.Fail($"{what}: {_failures.Count} of {total} files failed.{Environment.NewLine}{shown}{more}");
        }
    }
}
