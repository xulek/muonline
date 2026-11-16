using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Client.Main.Data;

namespace Client.Main.Core.Utilities
{
    /// <summary>
    /// SourceMain5.2 GlobalText (GlobalText.h, TGlobalText::Load): text_eng.bmd with header
    /// {WORD signature 0x5447, DWORD count} followed by {DWORD key, DWORD size, size bytes XOR {0xFC,0xCF,0xAB}} UTF-8 entries.
    /// Loaded lazily and thread-safe. Missing keys return an empty string.
    /// </summary>
    public static class GlobalText
    {
        private const ushort Signature = 0x5447;
        private static readonly byte[] XorKey = { 0xFC, 0xCF, 0xAB };

        private static readonly Lazy<Dictionary<int, string>> s_texts =
            new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Returns the text for <paramref name="key"/> or an empty string.</summary>
        public static string Get(int key)
            => s_texts.Value.TryGetValue(key, out var text) ? text : string.Empty;

        public static bool TryGet(int key, out string text) => s_texts.Value.TryGetValue(key, out text);

        /// <summary>Indexer-style access used by the ported code: <c>GlobalText.Table[61]</c>.</summary>
        public static readonly TextTable Table = new();

        public sealed class TextTable
        {
            public string this[int key] => Get(key);
        }

        private static Dictionary<int, string> Load()
        {
            var result = new Dictionary<int, string>();
            try
            {
                using var stream = EmbeddedS6Data.Open("text_eng.bmd");
                using var reader = new BinaryReader(stream);
                if (reader.ReadUInt16() != Signature)
                    return result;

                uint count = reader.ReadUInt32();
                for (uint i = 0; i < count; i++)
                {
                    int key = (int)reader.ReadUInt32();
                    int size = (int)reader.ReadUInt32();
                    byte[] buffer = reader.ReadBytes(size);
                    for (int b = 0; b < buffer.Length; b++)
                        buffer[b] ^= XorKey[b % 3];

                    int length = Array.IndexOf(buffer, (byte)0);
                    if (length < 0) length = buffer.Length;
                    result[key] = Encoding.UTF8.GetString(buffer, 0, length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GlobalText: failed to load text_eng.bmd: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Minimal swprintf-compatible formatter: supports %d %i %u %s %c %x %X %f %%, flags (-, 0, +, space),
        /// width (number or *) and precision. Missing arguments format as empty/0 like a defensive printf.
        /// </summary>
        public static string Format(string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format))
                return string.Empty;
            if (format.IndexOf('%') < 0)
                return format;

            var sb = new StringBuilder(format.Length + 16);
            int argIndex = 0;
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c != '%')
                {
                    sb.Append(c);
                    continue;
                }

                if (i + 1 >= format.Length)
                {
                    sb.Append(c);
                    break;
                }

                if (format[i + 1] == '%')
                {
                    sb.Append('%');
                    i++;
                    continue;
                }

                int j = i + 1;
                bool leftAlign = false, zeroPad = false, plus = false, space = false;
                for (; j < format.Length; j++)
                {
                    char f = format[j];
                    if (f == '-') leftAlign = true;
                    else if (f == '0') zeroPad = true;
                    else if (f == '+') plus = true;
                    else if (f == ' ') space = true;
                    else if (f == '#') { }
                    else break;
                }

                int width = 0;
                if (j < format.Length && format[j] == '*')
                {
                    width = ToInt(Next(args, ref argIndex));
                    j++;
                }
                else
                {
                    while (j < format.Length && char.IsDigit(format[j]))
                        width = width * 10 + (format[j++] - '0');
                }

                int precision = -1;
                if (j < format.Length && format[j] == '.')
                {
                    j++;
                    precision = 0;
                    if (j < format.Length && format[j] == '*')
                    {
                        precision = ToInt(Next(args, ref argIndex));
                        j++;
                    }
                    else
                    {
                        while (j < format.Length && char.IsDigit(format[j]))
                            precision = precision * 10 + (format[j++] - '0');
                    }
                }

                // length modifiers (h, l, ll, I64, ...)
                while (j < format.Length && (format[j] == 'h' || format[j] == 'l' || format[j] == 'L' || format[j] == 'I' || format[j] == '6' || format[j] == '4' || format[j] == 'z'))
                    j++;

                if (j >= format.Length)
                {
                    sb.Append(format, i, format.Length - i);
                    break;
                }

                char conv = format[j];
                string piece;
                bool numeric = false;
                switch (conv)
                {
                    case 'd':
                    case 'i':
                    {
                        long v = ToLong(Next(args, ref argIndex));
                        piece = v.ToString(CultureInfo.InvariantCulture);
                        if (precision >= 0) piece = PadDigits(piece, precision);
                        if (v >= 0 && plus) piece = "+" + piece;
                        else if (v >= 0 && space) piece = " " + piece;
                        numeric = true;
                        break;
                    }
                    case 'u':
                    {
                        long v = ToLong(Next(args, ref argIndex));
                        piece = ((ulong)(uint)v).ToString(CultureInfo.InvariantCulture);
                        if (v > uint.MaxValue) piece = v.ToString(CultureInfo.InvariantCulture);
                        if (precision >= 0) piece = PadDigits(piece, precision);
                        numeric = true;
                        break;
                    }
                    case 'x':
                    case 'X':
                    {
                        long v = ToLong(Next(args, ref argIndex));
                        piece = ((uint)v).ToString(conv == 'x' ? "x" : "X", CultureInfo.InvariantCulture);
                        numeric = true;
                        break;
                    }
                    case 'f':
                    case 'F':
                    {
                        double v = ToDouble(Next(args, ref argIndex));
                        piece = v.ToString("F" + (precision < 0 ? 6 : precision), CultureInfo.InvariantCulture);
                        numeric = true;
                        break;
                    }
                    case 'c':
                    {
                        object a = Next(args, ref argIndex);
                        piece = a is char ch ? ch.ToString() : ((char)ToInt(a)).ToString();
                        break;
                    }
                    case 's':
                    case 'S':
                    {
                        piece = Next(args, ref argIndex)?.ToString() ?? string.Empty;
                        if (precision >= 0 && piece.Length > precision) piece = piece.Substring(0, precision);
                        break;
                    }
                    default:
                        piece = format.Substring(i, j - i + 1);
                        break;
                }

                if (piece.Length < width)
                {
                    if (leftAlign) piece = piece.PadRight(width);
                    else if (zeroPad && numeric && precision < 0)
                    {
                        bool neg = piece.StartsWith('-') || piece.StartsWith('+');
                        piece = neg ? piece[0] + piece.Substring(1).PadLeft(width - 1, '0') : piece.PadLeft(width, '0');
                    }
                    else piece = piece.PadLeft(width);
                }

                sb.Append(piece);
                i = j;
            }

            return sb.ToString();
        }

        private static string PadDigits(string digits, int precision)
        {
            bool neg = digits.StartsWith('-');
            string body = neg ? digits.Substring(1) : digits;
            if (body.Length < precision) body = body.PadLeft(precision, '0');
            return neg ? "-" + body : body;
        }

        private static object Next(object[] args, ref int index)
            => args != null && index < args.Length ? args[index++] : null;

        private static int ToInt(object value) => (int)ToLong(value);

        private static long ToLong(object value) => value switch
        {
            null => 0,
            bool b => b ? 1 : 0,
            char ch => ch,
            string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0,
            IConvertible conv => Convert.ToInt64(conv, CultureInfo.InvariantCulture),
            _ => 0,
        };

        private static double ToDouble(object value) => value switch
        {
            null => 0,
            IConvertible conv => Convert.ToDouble(conv, CultureInfo.InvariantCulture),
            _ => 0,
        };
    }
}
