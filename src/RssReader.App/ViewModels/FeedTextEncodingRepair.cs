using System.Buffers;
using System.Text;

namespace RssReader.App.ViewModels;

internal static class FeedTextEncodingRepair
{
    private static readonly IReadOnlyDictionary<char, byte> Windows1252ByteMap = new Dictionary<char, byte>
    {
        ['€'] = 0x80,
        ['‚'] = 0x82,
        ['ƒ'] = 0x83,
        ['„'] = 0x84,
        ['…'] = 0x85,
        ['†'] = 0x86,
        ['‡'] = 0x87,
        ['ˆ'] = 0x88,
        ['‰'] = 0x89,
        ['Š'] = 0x8A,
        ['‹'] = 0x8B,
        ['Œ'] = 0x8C,
        ['Ž'] = 0x8E,
        ['‘'] = 0x91,
        ['’'] = 0x92,
        ['“'] = 0x93,
        ['”'] = 0x94,
        ['•'] = 0x95,
        ['–'] = 0x96,
        ['—'] = 0x97,
        ['˜'] = 0x98,
        ['™'] = 0x99,
        ['š'] = 0x9A,
        ['›'] = 0x9B,
        ['œ'] = 0x9C,
        ['ž'] = 0x9E,
        ['Ÿ'] = 0x9F
    };

    public static string Repair(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        var segmentStart = 0;
        while (segmentStart < value.Length)
        {
            if (value[segmentStart] <= 0x7F)
            {
                var asciiEnd = segmentStart + 1;
                while (asciiEnd < value.Length && value[asciiEnd] <= 0x7F)
                {
                    asciiEnd++;
                }

                result.Append(value, segmentStart, asciiEnd - segmentStart);
                segmentStart = asciiEnd;
                continue;
            }

            var segmentEnd = segmentStart + 1;
            while (segmentEnd < value.Length && value[segmentEnd] > 0x7F)
            {
                segmentEnd++;
            }

            result.Append(RepairNonAsciiSegment(value.AsSpan(segmentStart, segmentEnd - segmentStart)));
            segmentStart = segmentEnd;
        }

        return result.ToString();
    }

    private static string RepairNonAsciiSegment(ReadOnlySpan<char> segment)
    {
        var current = segment.ToString();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!TryDecodeWindows1252AsUtf8(current, out var decoded) ||
                string.Equals(current, decoded, StringComparison.Ordinal))
            {
                break;
            }

            current = decoded;
        }

        return current;
    }

    private static bool TryDecodeWindows1252AsUtf8(string value, out string decoded)
    {
        var bytes = new byte[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character <= 0x7F || character is >= '\u00A0' and <= '\u00FF' ||
                character is >= '\u0080' and <= '\u009F')
            {
                bytes[index] = (byte)character;
            }
            else if (Windows1252ByteMap.TryGetValue(character, out var mappedByte))
            {
                bytes[index] = mappedByte;
            }
            else
            {
                decoded = value;
                return false;
            }
        }

        var result = new StringBuilder(value.Length);
        var remainingBytes = bytes.AsSpan();
        while (!remainingBytes.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(remainingBytes, out var rune, out var bytesConsumed) != OperationStatus.Done)
            {
                decoded = value;
                return false;
            }

            result.Append(rune);
            remainingBytes = remainingBytes[bytesConsumed..];
        }

        decoded = result.ToString();
        return true;
    }
}
