using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Yakuza6LocalizationTool
{
    public class CmnTextManager
    {
        // Function to extract texts via raw binary scanning
        public static Dictionary<string, string> ExtractTexts(string cmnFilePath)
        {
            Dictionary<string, string> extractedTexts = new Dictionary<string, string>();
            byte[] data = File.ReadAllBytes(cmnFilePath);
            
            List<byte> currentStr = new List<byte>();
            int startOffset = 0;
            var utf8 = new UTF8Encoding(false, true); // True to throw on invalid bytes
            Dictionary<string, int> textOccurrences = new Dictionary<string, int>();

            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if ((b >= 0x20 && b <= 0x7E) || b > 0x7F)
                {
                    if (currentStr.Count == 0) startOffset = i;
                    currentStr.Add(b);
                }
                else if (b == 0 && currentStr.Count >= 3)
                {
                    try
                    {
                        string text = utf8.GetString(currentStr.ToArray());
                        
                        bool hasSpace = text.Contains(" ");
                        var letters = text.Where(char.IsLetter).ToList();
                        bool hasLetters = letters.Any();
                        bool isOnlyJapanese = hasLetters && letters.All(c => (c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u4dbf') || (c >= '\u4e00' && c <= '\u9fff'));

                        // Filter: Must contain a space, not be purely Japanese, and not be an internal ID
                        bool isInternalId = text.Contains("_") || text.Contains(".dds") || text.Contains(".bin");
                        bool isThreeBytesWithSpace = currentStr.Count == 3 && hasSpace;
                        bool hasRepeatedChars = Regex.IsMatch(text, @"(.)\1{4,}");

                        if (hasSpace && !isOnlyJapanese && hasLetters && !isInternalId && !isThreeBytesWithSpace && !hasRepeatedChars)
                        {
                            int capacity = currentStr.Count;
                            string contextId = $"Offset_0x{startOffset:X}_Len_{capacity}";
                            extractedTexts[contextId] = text;

                            if (textOccurrences.ContainsKey(text))
                                textOccurrences[text]++;
                            else
                                textOccurrences[text] = 1;
                        }
                    }
                    catch (ArgumentException) { } // Ignore decoding errors
                    currentStr.Clear();
                }
                else
                {
                    currentStr.Clear();
                }
            }

            // Filter out any string that repeats more than 3 times
            if (extractedTexts.Count > 0 && textOccurrences.Count > 0)
            {
                var keysToRemove = extractedTexts.Where(kvp => textOccurrences[kvp.Value] > 3).Select(kvp => kvp.Key).ToList();
                foreach (var key in keysToRemove)
                {
                    extractedTexts.Remove(key);
                }
            }

            return extractedTexts;
        }

        // Function to inject translated texts at explicit binary offsets
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

                        if (tradBytes.Length > maxBytes)
                        {
                            string warningMsg = $"[!] WARNING: Translation for offset 0x{hexOffset} in {Path.GetFileName(originalCmnPath)} exceeds {maxBytes} bytes. Truncating!";
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"  {warningMsg}");
                            Console.ResetColor();

                            if (!string.IsNullOrEmpty(warningsFilePath)) { try { File.AppendAllText(warningsFilePath, warningMsg + Environment.NewLine); } catch { } }

                            while (Encoding.UTF8.GetByteCount(translatedText) > maxBytes)
                            {
                                translatedText = translatedText.Substring(0, translatedText.Length - 1);
                            }
                            tradBytes = Encoding.UTF8.GetBytes(translatedText);
                        }

                        // Inject bytes
                        for (int i = 0; i < tradBytes.Length; i++)
                            data[offset + i] = tradBytes[i];

                        // Fill remaining space with null bytes
                        for (int i = tradBytes.Length; i < maxBytes; i++)
                            data[offset + i] = 0x00;
                    }
                    catch { }
                }
            }

            File.WriteAllBytes(outputCmnPath, data);
        }
    }
}
