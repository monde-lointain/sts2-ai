// HARD GATE — M5 Determinism Kernel differential parity test.
//
// For each seed in 0..99 we drive the Q1 port (Sts2Headless.Domain.Determinism.Rng)
// through the exact same fixed call sequence the corpus generator drove the
// upstream MegaCrit.Sts2.Core.Random.Rng through, then byte-compare the
// produced stream against the on-disk corpus file. Any divergence is a
// determinism leak and fails the build.
//
// Corpus location: Fixtures/Determinism/rng-corpus/seed-NNNN.bin
// Corpus regenerator: scripts/gen-rng-corpus/ (run with
//     `dotnet run --project scripts/gen-rng-corpus`). The generator
// file-links upstream Rng.cs unmodified so the ground truth is precisely what
// upstream produces.
//
// If this test fails, do NOT just regenerate the corpus to make it pass — that
// hides the regression. The expected workflow is:
//   1. Determine whether the port or the upstream changed.
//   2. If upstream changed and the change is intentional, regenerate the
//      corpus and bump the M5 schema version.
//   3. If the port drifted, fix the port.
//
// The exact section schema is documented in scripts/gen-rng-corpus/Program.cs.
// This test must stay in lockstep with that schema; the two share constants by
// being committed together.

using System;
using System.Collections.Generic;
using System.IO;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Determinism;

public class RngDifferentialParityTests
{
    private const int SeedCount = 100;
    private const int N = 32;
    private const int GaussN = 16;
    private const int NextItemN = 16;
    private const int ShufflePermutations = 8;
    private const int ShuffleSize = 10;
    private const int NameSeedSamples = 32;
    private const int FastForwardSteps = 100;

    private static readonly string[] Names =
    {
        "Rewards",
        "Shops",
        "Transformations",
        "UpFront",
        "Shuffle",
        "UnknownMapPoint",
        "CombatCardGeneration",
        "CombatPotionGeneration",
        "CombatCardSelection",
        "CombatEnergyCosts",
        "CombatTargets",
        "MonsterAi",
        "Niche",
        "CombatOrbs",
        "TreasureRoomRelics",
    };

    private static string CorpusDir()
    {
        // Fixtures/ is copied to the test output dir via CopyToOutputDirectory.
        string baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Fixtures", "Determinism", "rng-corpus");
    }

    [Fact]
    public void CorpusFilesArePresent()
    {
        string dir = CorpusDir();
        Assert.True(Directory.Exists(dir), $"corpus dir missing: {dir}");
        for (int s = 0; s < SeedCount; s++)
        {
            string path = Path.Combine(dir, $"seed-{s:0000}.bin");
            Assert.True(File.Exists(path), $"missing corpus file: {path}");
        }
    }

    [Theory]
    [MemberData(nameof(AllSeeds))]
    public void Q1PortByteMatchesUpstreamCorpus(uint seed)
    {
        byte[] produced = GenerateProducedBytesForSeed(seed);
        string path = Path.Combine(CorpusDir(), $"seed-{seed:0000}.bin");
        byte[] expected = File.ReadAllBytes(path);

        Assert.Equal(expected.Length, produced.Length);
        if (!expected.AsSpan().SequenceEqual(produced))
        {
            // Locate first divergence for a useful failure message.
            int firstDiff = -1;
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != produced[i])
                {
                    firstDiff = i;
                    break;
                }
            }
            Assert.Fail(
                $"seed={seed} first byte divergence at offset {firstDiff}: "
                    + $"expected=0x{expected[firstDiff]:X2} produced=0x{produced[firstDiff]:X2}"
            );
        }
    }

    public static IEnumerable<object[]> AllSeeds()
    {
        for (uint s = 0; s < SeedCount; s++)
            yield return new object[] { s };
    }

    private static byte[] GenerateProducedBytesForSeed(uint seed)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // NextBool
        {
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
                w.Write((byte)(rng.NextBool() ? 1 : 0));
        }
        // NextInt(maxExclusive)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
                w.Write(rng.NextInt(1000));
        }
        // NextInt(min,max)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
                w.Write(rng.NextInt(-50, 50));
        }
        // NextUnsignedInt(maxExclusive)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
                w.Write(rng.NextUnsignedInt(1000u));
        }
        // NextUnsignedInt(min,max)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
                w.Write(rng.NextUnsignedInt(10u, 1_000_000u));
        }
        // NextFloat(max)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
                w.Write(rng.NextFloat(10f));
        }
        // NextFloat(min,max)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
                w.Write(rng.NextFloat(-3.5f, 7.25f));
        }
        // NextDouble()
        {
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
                w.Write(rng.NextDouble());
        }
        // NextDouble(min,max)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
                w.Write(rng.NextDouble(-1.0, 1.0));
        }
        // NextGaussianFloat
        {
            var rng = new Rng(seed);
            for (int i = 0; i < GaussN; i++)
                w.Write(rng.NextGaussianFloat(0.5f, 0.2f, 0f, 1f));
        }
        // NextGaussianDouble
        {
            var rng = new Rng(seed);
            for (int i = 0; i < GaussN; i++)
                w.Write(rng.NextGaussianDouble(0.5, 0.2, 0.0, 1.0));
        }
        // NextGaussianInt
        {
            var rng = new Rng(seed);
            for (int i = 0; i < GaussN; i++)
                w.Write(rng.NextGaussianInt(50, 10, 0, 100));
        }
        // NextItem on fixed array
        {
            var rng = new Rng(seed);
            int[] items = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            for (int i = 0; i < NextItemN; i++)
            {
                int picked = rng.NextItem<int>(items);
                w.Write(picked);
            }
        }
        // Shuffle 8 permutations
        {
            var rng = new Rng(seed);
            for (int p = 0; p < ShufflePermutations; p++)
            {
                var list = new List<int>(ShuffleSize);
                for (int i = 0; i < ShuffleSize; i++)
                    list.Add(i);
                rng.Shuffle(list);
                foreach (int v in list)
                    w.Write(v);
            }
        }
        // FastForwardCounter
        {
            var rng = new Rng(seed);
            rng.FastForwardCounter(FastForwardSteps);
            w.Write(rng.Counter);
            w.Write(rng.NextInt(1_000_000));
        }
        // Name-seeded ctor
        foreach (string name in Names)
        {
            var rng = new Rng(seed, name);
            w.Write(rng.Seed);
            for (int i = 0; i < NameSeedSamples; i++)
                w.Write(rng.NextInt(1000));
        }

        w.Flush();
        return ms.ToArray();
    }
}
