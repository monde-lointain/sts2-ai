// Corpus generator for the M5 Determinism Kernel differential parity test.
//
// For each seed in 0..99 we emit, in deterministic order, the byte stream that
// upstream MegaCrit.Sts2.Core.Random.Rng produces when its primitives are
// called in a fixed sequence with fixed arguments. The matching parity test
// (RngDifferentialParityTests.cs) drives the Q1 port through the same fixed
// sequence and asserts byte-for-byte equality.
//
// Output layout, relative to repo root:
//     test/Sts2Headless.Tests.Domain/Fixtures/Determinism/rng-corpus/
//         manifest.txt              (human-readable; one line per primitive +
//                                    args, plus the call count N. Tells you
//                                    what the .bin files mean. Not parsed by
//                                    tests — tests bake the schema in.)
//         seed-0000.bin
//         seed-0001.bin
//         ...
//         seed-0099.bin
//
// Each seed-NNNN.bin is the concatenation, in fixed order, of:
//
//     [bools         ] N=32 bytes (one byte per NextBool() result, 0 or 1)
//     [int_max       ] N=32 calls × 4 bytes LE  (NextInt(1000))
//     [int_min_max   ] N=32 calls × 4 bytes LE  (NextInt(-50, 50))
//     [uint_max      ] N=32 calls × 4 bytes LE  (NextUnsignedInt(1000u))
//     [uint_min_max  ] N=32 calls × 4 bytes LE  (NextUnsignedInt(10u, 1_000_000u))
//     [float_max     ] N=32 calls × 4 bytes LE  (NextFloat(10f))
//     [float_min_max ] N=32 calls × 4 bytes LE  (NextFloat(-3.5f, 7.25f))
//     [double_       ] N=32 calls × 8 bytes LE  (NextDouble())
//     [double_min_max] N=32 calls × 8 bytes LE  (NextDouble(-1.0, 1.0))
//     [gauss_float   ] N=16 calls × 4 bytes LE  (NextGaussianFloat(0.5f, 0.2f, 0f, 1f))
//     [gauss_double  ] N=16 calls × 8 bytes LE  (NextGaussianDouble(0.5, 0.2, 0.0, 1.0))
//     [gauss_int     ] N=16 calls × 4 bytes LE  (NextGaussianInt(50, 10, 0, 100))
//     [next_item     ] N=16 calls × 4 bytes LE  (NextItem on fixed int[] = 0..9; index of returned item)
//     [shuffle       ] N=8  permutations × 10 × 4 bytes LE  (Shuffle on [0..9])
//     [ffwd_counter  ] 4 bytes LE Counter, then 4 bytes LE _random.Next() after fast-forwarding by 100 from a fresh Rng
//     [name_seeded   ] for each name in {"Rewards","Shops","Transformations",
//                                        "UpFront","Shuffle","UnknownMapPoint",
//                                        "CombatCardGeneration","CombatPotionGeneration",
//                                        "CombatCardSelection","CombatEnergyCosts",
//                                        "CombatTargets","MonsterAi","Niche",
//                                        "CombatOrbs","TreasureRoomRelics"}
//                       emit 4 bytes LE = derived Seed, then 32 calls × 4 bytes LE NextInt(1000)
//                       (15 names × (4 + 128) bytes = 1980 bytes)
//
// All multi-byte values are little-endian. The .NET 9 + Xoshiro256** algorithm
// is endian-agnostic at the System.Random level, but BinaryWriter on Linux/x86
// always writes little-endian, so we explicitly use a little-endian-converting
// helper.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MegaCrit.Sts2.Core.Random;

namespace Sts2Headless.RngCorpusGen;

internal static class Program
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
        // PlayerRngType names (in upstream enum-declaration order)
        "Rewards",
        "Shops",
        "Transformations",
        // RunRngType names (in upstream enum-declaration order)
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

    private static int Main(string[] args)
    {
        string outDir =
            args.Length > 0
                ? args[0]
                : Path.GetFullPath(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "..",
                        "..",
                        "..",
                        "..",
                        "..",
                        "test",
                        "Sts2Headless.Tests.Domain",
                        "Fixtures",
                        "Determinism",
                        "rng-corpus"
                    )
                );

        Directory.CreateDirectory(outDir);

        for (uint seed = 0; seed < SeedCount; seed++)
        {
            string path = Path.Combine(outDir, $"seed-{seed:0000}.bin");
            using FileStream fs = File.Create(path);
            using var w = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);

            // NextBool — fresh Rng per primitive so seed/counter starts clean.
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
                    // NextItem returns the element. Since items[i] == i, the
                    // element value IS the index — we write it directly.
                    w.Write(picked);
                }
            }
            // Shuffle: 8 permutations on a fresh [0..9] each time, same Rng
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
                w.Write(rng.Counter); // expect FastForwardSteps
                w.Write(rng.NextInt(1_000_000)); // first call after ffwd
            }
            // Name-seeded ctor
            foreach (string name in Names)
            {
                var rng = new Rng(seed, name);
                w.Write(rng.Seed);
                for (int i = 0; i < NameSeedSamples; i++)
                    w.Write(rng.NextInt(1000));
            }
        }

        // Manifest (informational).
        string manifestPath = Path.Combine(outDir, "manifest.txt");
        using (var mw = new StreamWriter(manifestPath, append: false, Encoding.UTF8))
        {
            mw.NewLine = "\n";
            mw.WriteLine("# rng-corpus manifest (informational, not parsed by tests)");
            mw.WriteLine($"seeds: 0..{SeedCount - 1}");
            mw.WriteLine($"N: {N}");
            mw.WriteLine($"GaussN: {GaussN}");
            mw.WriteLine($"NextItemN: {NextItemN}");
            mw.WriteLine($"ShufflePermutations: {ShufflePermutations}");
            mw.WriteLine($"ShuffleSize: {ShuffleSize}");
            mw.WriteLine($"FastForwardSteps: {FastForwardSteps}");
            mw.WriteLine($"NameSeedSamples: {NameSeedSamples}");
            mw.WriteLine("Names (in emit order):");
            foreach (string n in Names)
                mw.WriteLine($"  - {n}");
            mw.WriteLine();
            mw.WriteLine("Section order per seed file:");
            mw.WriteLine("  NextBool x N (1 byte each)");
            mw.WriteLine("  NextInt(1000) x N (4 LE each)");
            mw.WriteLine("  NextInt(-50, 50) x N (4 LE each)");
            mw.WriteLine("  NextUnsignedInt(1000) x N (4 LE each)");
            mw.WriteLine("  NextUnsignedInt(10, 1_000_000) x N (4 LE each)");
            mw.WriteLine("  NextFloat(10) x N (4 LE each)");
            mw.WriteLine("  NextFloat(-3.5, 7.25) x N (4 LE each)");
            mw.WriteLine("  NextDouble() x N (8 LE each)");
            mw.WriteLine("  NextDouble(-1, 1) x N (8 LE each)");
            mw.WriteLine("  NextGaussianFloat(0.5, 0.2, 0, 1) x GaussN (4 LE each)");
            mw.WriteLine("  NextGaussianDouble(0.5, 0.2, 0, 1) x GaussN (8 LE each)");
            mw.WriteLine("  NextGaussianInt(50, 10, 0, 100) x GaussN (4 LE each)");
            mw.WriteLine("  NextItem on int[0..9] x NextItemN (4 LE each)");
            mw.WriteLine("  Shuffle(int[0..9]) x ShufflePermutations (ShuffleSize * 4 LE each)");
            mw.WriteLine("  FastForwardCounter(100): Counter (4 LE) + NextInt(1_000_000) (4 LE)");
            mw.WriteLine(
                "  Name-seeded ctor for each Name: Seed (4 LE) + NextInt(1000) x NameSeedSamples (4 LE)"
            );
        }

        Console.WriteLine($"Wrote {SeedCount} seed files and manifest to: {outDir}");
        return 0;
    }
}
