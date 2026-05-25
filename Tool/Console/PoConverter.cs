using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace PoConverter
{
    class PoConverter
    {
        // ------------
        // MAIN & ROUTING
        // ------------
        static void Main(string[] args)
        {
            if (args.Length >= 3 && (args[2] == "po2json" || args[2] == "json2po"))
            {
                RunPoConverterDirectly(args);
                return;
            }

            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
            {
                PrintHeaderArgument();
                PrintHelp();
                return;
            }

            Pipeline.Run(args);
        }

        private static void RunPoConverterDirectly(string[] args)
        {
            string inputFile = args[0];
            string outputFile = args[1];
            string typeConversion = args[2];
            string? originalJsonFile = args.Length > 3 ? args[3] : null;
            string? dictionaryFile = args.Length > 4 ? args[4] : null;


            if (!CheckArgs(inputFile, outputFile, typeConversion, originalJsonFile, dictionaryFile))
            {
                return;
            }
            if (typeConversion == "po2json")
            {
                PoToJson(inputFile, outputFile, originalJsonFile, dictionaryFile);
            }
            else if (typeConversion == "json2po")
            {
                JsonToPo(inputFile, outputFile, dictionaryFile);
            }

        }


        // ------------
        // ARGUMENTS & HELP
        // ------------
        private static void PrintHeaderArgument()
        {
            Pipeline.PrintInfo("Program to convert between JSON and PO files for localization.");
            Pipeline.PrintInfo("Made with love by SavT, 2026.");
        }
        private static void PrintHelp()
        {
            Console.WriteLine("Pipeline Usage: PoConverter [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  -g, --game-path <path>  Specify the game folder path.");
            Console.WriteLine("  -e, --extract           Run Extraction phase.");
            Console.WriteLine("  -r, --recreate          Run Recreation phase.");
            Console.WriteLine("  -t, --skip-textures     Skip texture unpacking/repacking with ParTool.");
            Console.WriteLine("  -l, --lang <lang>       Set the language code for PO headers (default: it).");
            Console.WriteLine("  -c, --clean-all         Delete workspace and og files after recreation, keeping only output.");
            Console.WriteLine("  -y, --yes               Skip confirmation prompts and auto-exit.");
            Console.WriteLine("  -q, --quiet             Suppress output logs from external tools (reARMP, ParTool).");
            Console.WriteLine("  -d, --dict <path>       Specify a custom dictionary file (default: dictionary.json).");
            Console.WriteLine();
            Console.WriteLine("Note: You can also use 'config.json' to set default paths and options permanently.");
            Console.WriteLine();
            Console.WriteLine("Raw PoConverter Usage: PoConverter <input.json> <output.po> json2po");
            Console.WriteLine("Usage for po2json: PoConverter <input.po> <output.json> po2json <original.json>");
            Console.WriteLine("Example json2po: PoConverter ui_text.bin.json test1.po json2po");
            Console.WriteLine("Example po2json: PoConverter test1.po output.json po2json ui_text.bin.json");
            Console.WriteLine("Additionally, you can provide a dictionary file as the 5th argument to use for translation. The dictionary file should be a JSON file with key-value where Key is the original file name ed value the json structure. Example: PoConverter test1.po output.json po2json ui_text.bin.json dictionary.json");
            
        }

        private static bool CheckArgs(string inputFile, string outputFile, string typeConversion, string? originalJsonFile, string? dictionaryFile)
        {
            if (!File.Exists(inputFile))
            {
                Pipeline.PrintError($"[!] File {inputFile} does not exist.");
                return false;
            }
            if (File.Exists(outputFile))
            {
                Pipeline.PrintError($"[!] File {outputFile} already exists.");
                return false;
            }
            if (typeConversion != "po2json" && typeConversion != "json2po")
            {
                Pipeline.PrintError($"[!] Type convertion {typeConversion} is not valid.");
                return false;
            }
            if (typeConversion == "po2json" && !File.Exists(originalJsonFile))
            {
                Pipeline.PrintError($"[!] For po2json, you must provide the original JSON file as the 4th argument. File {originalJsonFile} does not exist.");
                return false;
            }
            if (!string.IsNullOrEmpty(dictionaryFile) && !File.Exists(dictionaryFile))
            {
                Pipeline.PrintError($"[!] Dictionary file {dictionaryFile} does not exist.");
                return false;
            }
            return true;
        }

        // ------------
        // STRUCTURE PARSING
        // ------------
        private static List<List<string>> GetStructures(string? dictionaryFile, string? targetFile, JObject? cachedDict = null)
        {
            List<List<string>> structures = new List<List<string>> { new List<string> { "", "text" } }; // Fallback structure

            if (!string.IsNullOrEmpty(targetFile))
            {
                JObject? dict = cachedDict;
                if (dict == null && !string.IsNullOrEmpty(dictionaryFile) && File.Exists(dictionaryFile))
                {
                    string dictJson = File.ReadAllText(dictionaryFile);
                    dict = JObject.Parse(dictJson);
                }
                string fileName = Path.GetFileName(targetFile) ?? string.Empty;

                if (dict != null)
                {
                    JToken? fileConfig = null;
                    if (dict["texts"] is JObject textsObj && textsObj.ContainsKey(fileName))
                        fileConfig = textsObj[fileName];
                    else if (dict["textures"] is JObject texturesObj && texturesObj.ContainsKey(fileName))
                        fileConfig = texturesObj[fileName];
                    else if (dict.ContainsKey(fileName))
                        fileConfig = dict[fileName];

                    if (fileConfig is JArray array)
                    {
                        if (array.Count == 0)
                        {
                            structures = new List<List<string>>();
                        }
                        else if (array[0] is JArray)
                        {
                            structures = array.Select(a => a.Select(t => t.ToString()).ToList()).ToList();
                        }
                        else
                        {
                            structures = new List<List<string>> { array.Select(t => t.ToString()).ToList() };
                        }
                    }
                }
            }
            return structures;
        }

        // ------------
        // PO TO JSON
        // ------------
        internal static void PoToJson(string inputFile, string outputFile, string? originalJsonFile, string? dictionaryFile)
        {
            string jsonString = !string.IsNullOrEmpty(originalJsonFile) ? File.ReadAllText(originalJsonFile) : "{}";
            JObject jsonObject = JObject.Parse(jsonString) ?? new JObject();

            foreach (var entry in ParsePoFile(inputFile))
            {
                try
                {
                    SaveTranslation(jsonObject, entry.Key, entry.MsgId, entry.MsgStr);
                }
                catch (Exception ex)
                {
                    Pipeline.PrintError($"  [!] Error processing PO block around line {entry.LineNumber}: {ex.Message}");
                }
            }

            using (var sw = new StreamWriter(outputFile, false, new UTF8Encoding(false)))
            {
                sw.NewLine = "\r\n";
                using (var jw = new JsonTextWriter(sw))
                {
                    jw.Formatting = Formatting.Indented;
                    jw.IndentChar = ' ';
                    jw.Indentation = 2;
                    jsonObject.WriteTo(jw);
                }
            }
        }

        private static void SaveTranslation(JObject jsonObject, string key, string? msgId, string? msgStr)
        {
            try
            {
                string finalString = GetFinalTranslationText(msgId ?? string.Empty, msgStr ?? string.Empty);
                string keyUnescaped = UnescapeString(key);

                string[] pathParts = keyUnescaped.Split(new string[] { "||" }, StringSplitOptions.None);
                JToken? target = jsonObject;
                
                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    if (target != null) target = target[pathParts[i]];
                }
                
                if (target != null && target[pathParts.Last()] != null)
                {
                    target[pathParts.Last()] = finalString;
                }
                else
                {
                    Pipeline.PrintWarning($"  [!] Warning: Could not find path '{keyUnescaped}' in original JSON.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save translation for key '{key}'.", ex);
            }
        }

        // ------------
        // STRING UTILITIES
        // ------------
        private static string ExtractString(string line)
        {
            int firstQuote = line.IndexOf('"');
            int lastQuote = line.LastIndexOf('"');
            if (firstQuote != -1 && lastQuote > firstQuote)
            {
                return line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
            }
            return string.Empty;
        }

        private static string EscapeString(string text)
        {
            return text.Replace("\\", "\\\\")
                       .Replace("\"", "\\\"")
                       .Replace("\r", "\\r")
                       .Replace("\n", "\\n");
        }

        private static string UnescapeString(string text)
        {
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    switch (text[i + 1])
                    {
                        case 'n': sb.Append('\n'); i++; break;
                        case 'r': sb.Append('\r'); i++; break;
                        case '"': sb.Append('"'); i++; break;
                        case '\\': sb.Append('\\'); i++; break;
                        default: sb.Append(text[i]); break;
                    }
                }
                else
                {
                    sb.Append(text[i]);
                }
            }
            return sb.ToString();
        }

        private static void ExtractValues(JToken? currentToken, List<string> structure, int structureIndex, List<string> currentPath, List<(string Key, string Text)> results)
        {
            if (currentToken == null) return;

            if (structureIndex >= structure.Count)
            {
                if (currentToken.Type == JTokenType.String)
                {
                    results.Add((string.Join("||", currentPath), currentToken.ToString()));
                }
                return;
            }

            string nextKey = structure[structureIndex];

            if (nextKey == "*")
            {
                if (currentToken is JObject obj)
                {
                    foreach (var prop in obj.Properties())
                    {
                        currentPath.Add(prop.Name);
                        ExtractValues(prop.Value, structure, structureIndex + 1, currentPath, results);
                        currentPath.RemoveAt(currentPath.Count - 1);
                    }
                }
            }
            else
            {
                if (currentToken is JObject obj && obj.ContainsKey(nextKey))
                {
                    currentPath.Add(nextKey);
                    ExtractValues(obj[nextKey], structure, structureIndex + 1, currentPath, results);
                    currentPath.RemoveAt(currentPath.Count - 1);
                }
            }
        }

        // ------------
        // JSON TO PO
        // ------------
        internal static int JsonToPo(string inputFile, string outputFile, string? dictionaryFile, string language = "it", JObject? cachedDict = null)
        {
            List<string> lines = new List<string>();
            string jsonString = File.ReadAllText(inputFile);
            JObject jsonObject = JObject.Parse(jsonString) ?? new JObject();
            StringBuilder poContent = new StringBuilder();
            List<List<string>> structures = GetStructures(dictionaryFile, inputFile, cachedDict);

            AppendPoHeader(poContent, language);

            var results = new List<(string Key, string Text)>();

            foreach (var structure in structures)
            {
                // Start recursion directly from the root object
                ExtractValues(jsonObject, structure, 0, new List<string>(), results);
            }

            var validResults = new List<(string Key, string Text)>();
            foreach (var item in results)
            {
                if (!IsValidTranslationString(item.Text)) continue;
                
                validResults.Add(item);
                poContent.AppendLine($"msgctxt \"{EscapeString(item.Key)}\"");
                poContent.AppendLine($"msgid \"{EscapeString(item.Text)}\"");
                poContent.AppendLine($"msgstr \"\"");
                poContent.AppendLine();
            }

            if (validResults.Count == 0) return 0;

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            File.WriteAllText(outputFile, poContent.ToString());
            return validResults.Count;
        }

        // ------------
        // TEXT VALIDATION
        // ------------
        internal static bool IsValidTranslationString(string text, bool requireSpaceAndLetters = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            bool hasSpace = text.Contains(" ");
            var letters = text.Where(char.IsLetter).ToList();
            bool hasLetters = letters.Any();

            if (requireSpaceAndLetters)
            {
                if (hasSpace)
                {
                    if (!hasLetters) return false;
                }
                else
                {
                    // For single words (no spaces)
                    if (text.Length < 2) return false; // Skip single characters (usually variables)

                    // Skip camelCase (e.g., "myVariable", "getPlayer")
                    if (Regex.IsMatch(text, @"[a-z][A-Z]")) return false;

                    // Skip strings with digits (e.g., "Btn01", "Text2")
                    if (text.Any(char.IsDigit)) return false;

                    // Skip technical strings with code-like characters
                    char[] codeChars = { '(', ')', '{', '}', '[', ']', '<', '>', ';', '=', '+', '*', '/', '&', '|', '%', '$', '@', '^' };
                    if (text.Any(c => codeChars.Contains(c))) return false;
                }
            }

            bool isOnlyJapanese = hasLetters && letters.All(c => (c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u4dbf') || (c >= '\u4e00' && c <= '\u9fff'));
            if (isOnlyJapanese) return false;

            bool isInternalId = text.Contains("_") || text.Contains(".dds") || text.Contains(".bin") || text.Contains("[IK]");
            if (isInternalId) return false;

            bool isThreeBytesWithSpace = Encoding.UTF8.GetByteCount(text) == 3 && hasSpace;
            if (isThreeBytesWithSpace) return false;

            bool hasRepeatedChars = Regex.IsMatch(text, @"(.)\1{4,}");
            if (hasRepeatedChars) return false;

            return true;
        }

        // ------------
        // DICTIONARY & PO CONVERSION
        // ------------
        internal static void DictToPo(Dictionary<string, string> dict, string outputFile, string language = "it")
        {
            StringBuilder poContent = new StringBuilder();
            
            AppendPoHeader(poContent, language);

            foreach (var kvp in dict)
            {
                string[] parts = kvp.Key.Split('_');
                if (parts.Length >= 4 && parts[0] == "Offset" && parts[2] == "Len")
                {
                    poContent.AppendLine($"#. Max bytes: {parts[3]}");
                }
                
                poContent.AppendLine($"msgctxt \"{EscapeString(kvp.Key)}\"");
                poContent.AppendLine($"msgid \"{EscapeString(kvp.Value)}\"");
                poContent.AppendLine($"msgstr \"\"");
                poContent.AppendLine();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            File.WriteAllText(outputFile, poContent.ToString());
        }

        internal static Dictionary<string, string> PoToDict(string inputFile)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>();
            
            foreach (var entry in ParsePoFile(inputFile))
            {
                dict[UnescapeString(entry.Key)] = GetFinalTranslationText(entry.MsgId, entry.MsgStr);
            }
            
            return dict;
        }

        // ------------
        // PO PARSER
        // ------------
        internal class PoEntry
        {
            public string Key { get; set; } = string.Empty;
            public string MsgId { get; set; } = string.Empty;
            public string MsgStr { get; set; } = string.Empty;
            public int LineNumber { get; set; }
        }

        private static IEnumerable<PoEntry> ParsePoFile(string inputFile)
        {
            string[] lines = File.ReadAllLines(inputFile);
            string? currentKey = null;
            string? currentMsgId = null;
            string? currentMsgStr = null;
            string currentSection = "";
            int lineNumber = 0;
            int lastKeyLineNumber = 0;

            foreach (string line in lines)
            {
                lineNumber++;
                string trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("msgctxt"))
                {
                    currentKey = ExtractString(trimmedLine);
                    currentSection = "msgctxt";
                    lastKeyLineNumber = lineNumber;
                }
                else if (trimmedLine.StartsWith("msgid"))
                {
                    currentMsgId = ExtractString(trimmedLine);
                    currentSection = "msgid";
                }
                else if (trimmedLine.StartsWith("msgstr"))
                {
                    currentMsgStr = ExtractString(trimmedLine);
                    currentSection = "msgstr";
                }
                else if (trimmedLine.StartsWith("\""))
                {
                    if (currentSection == "msgid") currentMsgId += ExtractString(trimmedLine);
                    else if (currentSection == "msgstr") currentMsgStr += ExtractString(trimmedLine);
                }
                else if (string.IsNullOrEmpty(trimmedLine) && currentKey != null)
                {
                    yield return new PoEntry { Key = currentKey, MsgId = currentMsgId ?? "", MsgStr = currentMsgStr ?? "", LineNumber = lastKeyLineNumber };
                    currentKey = null;
                    currentMsgId = null;
                    currentMsgStr = null;
                    currentSection = "";
                }
            }
            
            if (currentKey != null)
            {
                yield return new PoEntry { Key = currentKey, MsgId = currentMsgId ?? "", MsgStr = currentMsgStr ?? "", LineNumber = lastKeyLineNumber };
            }
        }

        private static string GetFinalTranslationText(string msgId, string msgStr)
        {
            string msgstrUnescaped = UnescapeString(msgStr);
            string msgidUnescaped = UnescapeString(msgId);
            return string.IsNullOrEmpty(msgstrUnescaped) ? msgidUnescaped : msgstrUnescaped;
        }

        private static void AppendPoHeader(StringBuilder sb, string language)
        {
            sb.AppendLine("msgid \"\"");
            sb.AppendLine("msgstr \"\"");
            sb.AppendLine("\"Project-Id-Version: Yakuza 6 Translation\\n\"");
            sb.AppendLine("\"Last-Translator: SavT\\n\"");
            sb.AppendLine("\"MIME-Version: 1.0\\n\"");
            sb.AppendLine("\"Content-Type: text/plain; charset=UTF-8\\n\"");
            sb.AppendLine("\"Content-Transfer-Encoding: 8bit\\n\"");
            sb.AppendLine($"\"Language: {language}\\n\"");
            sb.AppendLine();
        }
    }
}
