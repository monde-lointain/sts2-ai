// Minimal stub of upstream MegaCrit.Sts2.Core.Helpers.StringHelper exposing the
// single helper the file-linked upstream Rng.cs needs:
// GetDeterministicHashCode(string). Copied verbatim (byte-for-byte) from
// ~/development/projects/godot/sts2/src/Core/Helpers/StringHelper.cs so the
// corpus we emit is identical to what upstream would produce.
//
// Intentionally NOT in src/ or test/ — this file lives only in the corpus
// generator harness.

namespace MegaCrit.Sts2.Core.Helpers;

internal static class StringHelper
{
    public static int GetDeterministicHashCode(string str)
    {
        int num = 352654597;
        int num2 = num;
        for (int i = 0; i < str.Length; i += 2)
        {
            num = ((num << 5) + num) ^ str[i];
            if (i == str.Length - 1)
            {
                break;
            }
            num2 = ((num2 << 5) + num2) ^ str[i + 1];
        }
        return num + num2 * 1566083941;
    }
}
