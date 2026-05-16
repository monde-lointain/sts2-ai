using System.Text.RegularExpressions;

namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Minimal port of upstream <c>MegaCrit.Sts2.Core.Helpers.StringHelper</c> — only the helpers
/// the M5 Determinism Kernel needs (<see cref="GetDeterministicHashCode"/> for seed-by-name
/// derivation, and <see cref="SnakeCase"/> for RunRngType / PlayerRngType identifier formatting).
/// Other upstream helpers (Slugify, Unslugify, CompactText, Radix, RatioFormat, Capitalize,
/// StripBbCode) are intentionally not ported — they have Godot / SaveManager / LocString
/// dependencies and are out of scope for the headless domain.
///
/// <para>
/// <b>Visibility:</b> upstream <c>StringHelper</c> is public; we mirror that
/// because B.1-alpha-T1 (RC-2 master seed hash) requires the Host's
/// <c>CompositionRoot.Build</c> to derive the master seed via
/// <see cref="GetDeterministicHashCode(string)"/>. Test fixtures and the
/// Host both consume the helper directly.
/// </para>
/// </summary>
public static partial class StringHelpers
{
    [GeneratedRegex("([A-Za-z0-9]|\\G(?!^))([A-Z])")]
    private static partial Regex CamelCaseRegex();

    /// <summary>
    /// Convert a CamelCase / PascalCase identifier to snake_case. Direct port of upstream
    /// <c>StringHelper.SnakeCase</c>.
    /// </summary>
    public static string SnakeCase(string txt) =>
        CamelCaseRegex().Replace(txt.Trim(), "$1_$2").ToLowerInvariant();

    /// <summary>
    /// Stable, version-independent string hash. Direct port of upstream
    /// <c>StringHelper.GetDeterministicHashCode</c>. Unlike <see cref="string.GetHashCode()"/>
    /// this is guaranteed identical across processes / runtimes, which is required because
    /// it feeds RNG seed derivation.
    /// </summary>
    public static int GetDeterministicHashCode(string str)
    {
        int hash1 = 352654597;
        int hash2 = hash1;
        for (int i = 0; i < str.Length; i += 2)
        {
            hash1 = ((hash1 << 5) + hash1) ^ str[i];
            if (i == str.Length - 1)
            {
                break;
            }
            hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
        }
        return hash1 + (hash2 * 1566083941);
    }
}
