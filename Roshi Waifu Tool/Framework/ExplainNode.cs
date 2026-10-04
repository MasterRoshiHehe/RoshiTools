using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RoshiWaifuTool.Framework
{
    /// <summary>One node of a framework's Explain tree: { Id, Title, Text, Status, Children }.</summary>
    internal sealed class ExplainNode
    {
        public string Id;
        public string Title;
        public string Text;
        /// <summary>winner, pass, fail, skipped, info or warning (anything else is shown as info).</summary>
        public string Status = "info";
        public List<ExplainNode> Children = new();

        public bool IsHiddenWhenFiltering => this.Status is "fail" or "skipped";

        /// <summary>Read a framework's Explain reply. Never throws: a broken reply becomes a warning node.</summary>
        public static ExplainNode FromReply(string framework, string json)
        {
            var root = new ExplainNode { Id = framework, Title = framework, Status = "info" };
            try
            {
                if (MiniJson.Parse(json) is not Dictionary<string, object> reply)
                {
                    root.Children.Add(new ExplainNode { Title = "No data", Status = "warning", Text = "The framework returned nothing." });
                    return root;
                }

                string summary = reply.TryGetValue("Summary", out object rawSummary) ? rawSummary as string : null;
                if (!string.IsNullOrWhiteSpace(summary))
                    root.Title = $"{framework}: {summary}";

                if (reply.TryGetValue("Sections", out object sections) && sections is List<object> list)
                {
                    foreach (object section in list)
                    {
                        ExplainNode node = FromObject(section);
                        if (node != null)
                            root.Children.Add(node);
                    }
                }
            }
            catch (Exception ex)
            {
                root.Children.Add(new ExplainNode { Title = "Couldn't read the framework's reply", Status = "warning", Text = ex.Message });
            }
            return root;
        }

        private static ExplainNode FromObject(object raw)
        {
            if (raw is not Dictionary<string, object> data)
                return null;

            var node = new ExplainNode
            {
                Title = UiText.Clean(data.TryGetValue("Title", out object title) ? title as string : null) ?? "",
                Text = UiText.Clean(data.TryGetValue("Text", out object text) ? text as string : null),
                Status = (data.TryGetValue("Status", out object status) ? status as string : null)?.ToLowerInvariant() ?? "info"
            };
            node.Id = (data.TryGetValue("Id", out object id) ? id as string : null) ?? node.Title;

            if (data.TryGetValue("Children", out object children) && children is List<object> list)
            {
                foreach (object child in list)
                {
                    ExplainNode childNode = FromObject(child);
                    if (childNode != null)
                        node.Children.Add(childNode);
                }
            }
            return node;
        }
    }

    /// <summary>Text helpers for drawing with the game's fonts.</summary>
    internal static class UiText
    {
        /// <summary>Replace characters the game's small font may not have, so drawing never fails on them.</summary>
        public static string Clean(string text)
        {
            if (text == null)
                return null;

            var result = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '\n':
                        result.Append('\n');
                        break;
                    case '\r':
                        break;
                    case '\t':
                        result.Append("  ");
                        break;
                    case '→': // →
                        result.Append("to");
                        break;
                    case '—': // —
                    case '–': // –
                        result.Append('-');
                        break;
                    case '×': // ×
                        result.Append('x');
                        break;
                    case '‘':
                    case '’':
                        result.Append('\'');
                        break;
                    case '“':
                    case '”':
                        result.Append('"');
                        break;
                    case '…': // …
                        result.Append("...");
                        break;
                    default:
                        result.Append(ch is >= ' ' and <= '~' ? ch : '?');
                        break;
                }
            }
            // the game font draws '>' as a triangle, so an ASCII arrow would look odd
            return result.ToString().Replace("->", "to");
        }

        /// <summary>Break text into lines that fit a width, using a measuring function (pixels for a string).</summary>
        public static List<string> Wrap(string text, float maxWidth, Func<string, float> measure)
        {
            var lines = new List<string>();
            foreach (string paragraph in (text ?? "").Split('\n'))
            {
                string line = "";
                foreach (string word in paragraph.Split(' ').Where(word => word.Length > 0))
                {
                    string piece = word;
                    // a single word wider than the panel (asset names, long conditions): cut it
                    while (measure(piece) > maxWidth && piece.Length > 8)
                    {
                        int cut = piece.Length - 1;
                        while (cut > 8 && measure(piece.Substring(0, cut)) > maxWidth)
                            cut--;
                        if (line.Length > 0)
                        {
                            lines.Add(line);
                            line = "";
                        }
                        lines.Add(piece.Substring(0, cut));
                        piece = piece.Substring(cut);
                    }

                    string candidate = line.Length == 0 ? piece : line + " " + piece;
                    if (line.Length > 0 && measure(candidate) > maxWidth)
                    {
                        lines.Add(line);
                        line = piece;
                    }
                    else
                        line = candidate;
                }
                lines.Add(line);
            }
            return lines;
        }
    }
}
