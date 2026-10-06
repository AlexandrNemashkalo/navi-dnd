using System.Text;

namespace NaviDnD.Helpers;

public static class TextWrapper
{
    /// <summary>
    /// Разбивает текст на строки, сохраняя целостность слов, с возможностью задать разную ширину для первой строки.
    /// </summary>
    public static List<string> WrapText(string text, int maxWidth, int? firstLineWidth = null)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0) return lines;

        // Перевод строки внутри текста (записка, описание предмета от ИИ) — новый абзац, а не символ в «слове»:
        // иначе он уходил в консоль как настоящий перенос и ломал вёрстку панели.
        text = text.Replace("\r", "").Replace('\t', ' ');
        if (text.Contains('\n'))
        {
            bool first = true;
            foreach (var paragraph in text.Split('\n'))
            {
                if (paragraph.Trim().Length == 0) lines.Add("");
                else lines.AddRange(WrapText(paragraph, maxWidth, first ? firstLineWidth : null));
                first = false;
            }
            return lines;
        }

        int actualFirstWidth = firstLineWidth ?? maxWidth;
        if (actualFirstWidth <= 0) actualFirstWidth = maxWidth;

        string[] words = text.Split(' ');
        var currentLine = new StringBuilder();
        int currentLength = 0;
        bool firstLine = true;

        foreach (string word in words)
        {
            int wordLen = word.Length;
            int spaceNeeded = currentLine.Length == 0 ? 0 : 1;
            int availableWidth = firstLine ? actualFirstWidth : maxWidth;

            if (wordLen > availableWidth)
            {
                if (currentLine.Length > 0)
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLength = 0;
                }
                for (int i = 0; i < wordLen; i += availableWidth)
                {
                    int len = Math.Min(availableWidth, wordLen - i);
                    lines.Add(word.Substring(i, len));
                }
                firstLine = false;
                continue;
            }

            if (currentLength + spaceNeeded + wordLen <= availableWidth)
            {
                if (currentLine.Length == 0)
                    currentLine.Append(word);
                else
                    currentLine.Append(' ').Append(word);
                currentLength += spaceNeeded + wordLen;
            }
            else
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
                currentLine.Append(word);
                currentLength = wordLen;
                firstLine = false;
            }
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString());

        if (lines.Count == 0 && text.Length > 0)
        {
            string single = text.Length > maxWidth ? text.Substring(0, maxWidth - 3) + "…" : text;
            lines.Add(single);
        }

        return lines;
    }
}
