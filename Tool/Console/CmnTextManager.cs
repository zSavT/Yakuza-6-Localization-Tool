using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Yakuza6LocalizationTool
{
    public class CmnTextManager
    {
        // ------------
        // TEXT EXTRACTION
        // ------------
        public static Dictionary<string, string> ExtractTexts(string cmnFilePath)
        {
            Dictionary<string, string> extractedTexts = new Dictionary<string, string>();
            byte[] data = File.ReadAllBytes(cmnFilePath);
            
            int currentLength = 0;
            int startOffset = 0;
            var utf8 = new UTF8Encoding(false, true); // True to throw on invalid bytes
            Dictionary<string, int> textOccurrences = new Dictionary<string, int>();

            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if ((b >= 0x20 && b <= 0x7E) || b > 0x7F || b == 0x0A || b == 0x0D || b == 0x09)
                {
                    if (currentLength == 0) startOffset = i;
                    currentLength++;
                }
                else if (b == 0 && currentLength >= 2)
                {
                    try
                    {
                        string text = utf8.GetString(data, startOffset, currentLength);
                        
                        if (global::PoConverter.PoConverter.IsValidTranslationString(text, true))
                        {
                            string contextId = $"Offset_0x{startOffset:X}_Len_{currentLength}";
                            extractedTexts[contextId] = text;

                            if (textOccurrences.ContainsKey(text))
                                textOccurrences[text]++;
                            else
                                textOccurrences[text] = 1;
                        }
                    }
                    catch (ArgumentException) { } // Ignore decoding errors
                    currentLength = 0;
                }
                else
                {
                    currentLength = 0;
                }
            }

            if (currentLength >= 2)
            {
                try
                {
                    string text = utf8.GetString(data, startOffset, currentLength);
                    if (global::PoConverter.PoConverter.IsValidTranslationString(text, true))
                    {
                        string contextId = $"Offset_0x{startOffset:X}_Len_{currentLength}";
                        extractedTexts[contextId] = text;

                        if (textOccurrences.ContainsKey(text))
                            textOccurrences[text]++;
                        else
                            textOccurrences[text] = 1;
                    }
                }
                catch (ArgumentException) { }
            }

            // Filter out any string that repeats too many times, unless it looks like a natural language string.
            // Strings longer than 7 characters have a higher repetition tolerance (10 occurrences instead of 3).
            if (extractedTexts.Count > 0 && textOccurrences.Count > 0)
            {
                var keysToRemove = extractedTexts
                    .Where(kvp =>
                    {
                        string text = kvp.Value;
                        int count = textOccurrences[text];
                        int maxOccurrences = text.Length > 7 ? 10 : 3;
                        return count > maxOccurrences && !IsNaturalLanguage(text);
                    })
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in keysToRemove)
                {
                    extractedTexts.Remove(key);
                }
            }

            return extractedTexts;
        }

        // ------------
        // TEXT INJECTION
        // ------------
        public static void InjectTextsAndSave(string originalCmnPath, string outputCmnPath, Dictionary<string, string> translatedTexts, string? warningsFilePath = null)
        {
            byte[] data = File.ReadAllBytes(originalCmnPath);

            foreach (var kvp in translatedTexts)
            {
                string contextId = kvp.Key;     // Ex: "Offset_0x1FEC0_Len_17"
                string translatedText = kvp.Value;

                if (string.IsNullOrWhiteSpace(translatedText)) continue;

                string[] parts = contextId.Split('_');
                if (parts.Length >= 4 && parts[0] == "Offset" && parts[2] == "Len")
                {
                    try
                    {
                        string hexOffset = parts[1].Replace("0x", "");
                        int offset = Convert.ToInt32(hexOffset, 16);
                        int maxBytes = int.Parse(parts[3]);

                        byte[] tradBytes = Encoding.UTF8.GetBytes(translatedText);
                        int translatedByteCount = tradBytes.Length;

                        if (tradBytes.Length > maxBytes)
                        {
                            List<byte> origBytes = new List<byte>();
                            for (int i = 0; i < maxBytes; i++)
                            {
                                if (data[offset + i] == 0x00) break;
                                origBytes.Add(data[offset + i]);
                            }
                            string originalText = Encoding.UTF8.GetString(origBytes.ToArray());

                            string warningMsg = $"[!] WARNING: Translation for offset 0x{hexOffset} in {Path.GetFileName(originalCmnPath)} exceeds {maxBytes} bytes. Truncating!";
                            global::PoConverter.Pipeline.PrintWarning($"  {warningMsg}");

                            if (!string.IsNullOrEmpty(warningsFilePath))
                            {
                                string detailedWarning = $"[WARNING] Truncated Text\r\n" +
                                                         $"File: {originalCmnPath}\r\n" +
                                                         $"Original Text   ({origBytes.Count} bytes / Max allowed: {maxBytes} bytes): {originalText}\r\n" +
                                                         $"Translated Text ({translatedByteCount} bytes): {translatedText}\r\n" +
                                                         "--------------------------------------------------\r\n";
                                try { File.AppendAllText(warningsFilePath, detailedWarning); } catch { }
                            }

                            while (Encoding.UTF8.GetByteCount(translatedText) > maxBytes)
                            {
                                translatedText = translatedText.Substring(0, translatedText.Length - 1);
                            }
                            tradBytes = Encoding.UTF8.GetBytes(translatedText);
                        }

                        if (offset + maxBytes > data.Length)
                        {
                            global::PoConverter.Pipeline.PrintWarning($"  [!] Warning: Offset 0x{hexOffset} + Len {maxBytes} exceeds binary size {data.Length}. Skipping.");
                            continue;
                        }

                        // Inject bytes
                        for (int i = 0; i < tradBytes.Length; i++)
                            data[offset + i] = tradBytes[i];

                        // Fill remaining space with null bytes
                        for (int i = tradBytes.Length; i < maxBytes; i++)
                            data[offset + i] = 0x00;
                    }
                    catch (Exception ex)
                    {
                        global::PoConverter.Pipeline.PrintWarning($"  [!] Warning: Failed to inject text at {contextId}: {ex.Message}");
                    }
                }
            }

            File.WriteAllBytes(outputCmnPath, data);
        }

        private static bool IsNaturalLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            foreach (char c in text)
            {
                if (char.IsLetter(c) || char.IsWhiteSpace(c)) continue;

                // Allow common punctuation and symbols typical in natural language
                if (c == '\'' || c == '’' || c == '-' || c == ',' || c == '.' || c == '!' || c == '?' || c == '"' || c == ':') continue;

                return false;
            }

            return true;
        }
    }
}
