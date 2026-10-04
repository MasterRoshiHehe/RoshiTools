using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// A small JSON reader for the editor API's replies, so the tool needs no JSON library reference. Objects become
    /// Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, and values string / double / bool / null.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            int index = 0;
            object value = ReadValue(json, ref index);
            return value;
        }

        private static void SkipSpace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
        }

        private static object ReadValue(string text, ref int index)
        {
            SkipSpace(text, ref index);
            if (index >= text.Length)
                throw new FormatException("Unexpected end of JSON.");

            char ch = text[index];
            switch (ch)
            {
                case '{':
                    return ReadObject(text, ref index);
                case '[':
                    return ReadArray(text, ref index);
                case '"':
                    return ReadString(text, ref index);
                case 't':
                    Expect(text, ref index, "true");
                    return true;
                case 'f':
                    Expect(text, ref index, "false");
                    return false;
                case 'n':
                    Expect(text, ref index, "null");
                    return null;
                default:
                    return ReadNumber(text, ref index);
            }
        }

        private static void Expect(string text, ref int index, string word)
        {
            if (string.CompareOrdinal(text, index, word, 0, word.Length) != 0)
                throw new FormatException($"Unexpected JSON at position {index}.");
            index += word.Length;
        }

        private static Dictionary<string, object> ReadObject(string text, ref int index)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            index++; // {
            SkipSpace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipSpace(text, ref index);
                string key = ReadString(text, ref index);
                SkipSpace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                    throw new FormatException($"Expected ':' at position {index}.");
                index++;
                result[key] = ReadValue(text, ref index);
                SkipSpace(text, ref index);
                if (index >= text.Length)
                    throw new FormatException("Unexpected end of JSON object.");
                if (text[index] == ',')
                {
                    index++;
                    continue;
                }
                if (text[index] == '}')
                {
                    index++;
                    return result;
                }
                throw new FormatException($"Expected ',' or '}}' at position {index}.");
            }
        }

        private static List<object> ReadArray(string text, ref int index)
        {
            var result = new List<object>();
            index++; // [
            SkipSpace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return result;
            }

            while (true)
            {
                result.Add(ReadValue(text, ref index));
                SkipSpace(text, ref index);
                if (index >= text.Length)
                    throw new FormatException("Unexpected end of JSON array.");
                if (text[index] == ',')
                {
                    index++;
                    continue;
                }
                if (text[index] == ']')
                {
                    index++;
                    return result;
                }
                throw new FormatException($"Expected ',' or ']' at position {index}.");
            }
        }

        private static string ReadString(string text, ref int index)
        {
            if (index >= text.Length || text[index] != '"')
                throw new FormatException($"Expected a string at position {index}.");
            index++;

            var result = new StringBuilder();
            while (index < text.Length)
            {
                char ch = text[index++];
                if (ch == '"')
                    return result.ToString();
                if (ch != '\\')
                {
                    result.Append(ch);
                    continue;
                }

                if (index >= text.Length)
                    break;
                char escape = text[index++];
                switch (escape)
                {
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'u':
                        if (index + 4 > text.Length)
                            throw new FormatException("Bad \\u escape in JSON.");
                        result.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 4;
                        break;
                    default: result.Append(escape); break; // \" \\ \/
                }
            }
            throw new FormatException("Unterminated JSON string.");
        }

        private static object ReadNumber(string text, ref int index)
        {
            int start = index;
            while (index < text.Length && (char.IsDigit(text[index]) || text[index] is '-' or '+' or '.' or 'e' or 'E'))
                index++;
            if (index == start)
                throw new FormatException($"Unexpected character '{text[start]}' at position {start}.");
            return double.Parse(text.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
