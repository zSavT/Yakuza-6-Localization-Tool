using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PoConverter
{
    class PoConverter
    {
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
            string typeConvertion = args[2];
            string? originalJsonFile = args.Length > 3 ? args[3] : null;
            string? dictionaryFile = args.Length > 4 ? args[4] : null;


            if (!checkArgs(inputFile, outputFile, typeConvertion, originalJsonFile, dictionaryFile))
            {
                return;
            }
            if (typeConvertion == "po2json")
            {
                PoToJson(inputFile, outputFile, originalJsonFile, dictionaryFile);
            }
            else if (typeConvertion == "json2po")
            {
                JsonToPo(inputFile, outputFile, dictionaryFile);
            }

        }


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
            Console.WriteLine("  -d, --dict <path>       Specify a custom dictionary file (default: dictionary.json).");
            Console.WriteLine();
            Console.WriteLine("Raw PoConverter Usage: PoConverter <input.json> <output.po> json2po");
            Console.WriteLine("Usage for po2json: PoConverter <input.po> <output.json> po2json <original.json>");
            Console.WriteLine("Example json2po: PoConverter ui_text.bin.json test1.po json2po");
            Console.WriteLine("Example po2json: PoConverter test1.po output.json po2json ui_text.bin.json");
            Console.WriteLine("Additionally, you can provide a dictionary file as the 5th argument to use for translation. The dictionary file should be a JSON file with key-value where Key is the original file name ed value the json structure. Example: PoConverter test1.po output.json po2json ui_text.bin.json dictionary.json");
            
        }

        private static bool checkArgs(string inputFile, string outputFile, string typeConvertion, string? originalJsonFile, string? dictionaryFile)
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
            if (typeConvertion != "po2json" && typeConvertion != "json2po")
            {
                Pipeline.PrintError($"[!] Type convertion {typeConvertion} is not valid.");
                return false;
            }
            if (typeConvertion == "po2json" && !File.Exists(originalJsonFile))
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

        private static List<List<string>> GetStructures(string? dictionaryFile, string? targetFile)
        {
            List<List<string>> structures = new List<List<string>> { new List<string> { "", "text" } }; // Fallback structure

            if (!string.IsNullOrEmpty(dictionaryFile) && File.Exists(dictionaryFile) && !string.IsNullOrEmpty(targetFile))
            {
                string dictJson = File.ReadAllText(dictionaryFile);
                JObject? dict = JObject.Parse(dictJson);
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

        internal static void PoToJson(string inputFile, string outputFile, string? originalJsonFile, string? dictionaryFile)
        {
            string[] lines = File.ReadAllLines(inputFile);
            string jsonString = !string.IsNullOrEmpty(originalJsonFile) ? File.ReadAllText(originalJsonFile) : "{}";
            JObject jsonObject = JObject.Parse(jsonString) ?? new JObject();

            string? currentKey = null;
            string? currentMsgId = null;
            string? currentMsgStr = null;
            string currentSection = "";
            int lineNumber = 0;
            int lastKeyLineNumber = 0;

            foreach (string line in lines)
            {
                lineNumber++;
                try
                {
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
                        SaveTranslation(jsonObject, currentKey, currentMsgId, currentMsgStr);
                        currentKey = null;
                        currentMsgId = null;
                        currentMsgStr = null;
                        currentSection = "";
                    }
                }
                catch (Exception ex)
                {
                    Pipeline.PrintError($"  [!] Error processing PO file at line {lineNumber} ({line}): {ex.Message}");
                        // Reset the state to recover and continue with the next blocks
                    currentKey = null;
                    currentMsgId = null;
                    currentMsgStr = null;
                    currentSection = "";
                }
            }

            if (currentKey != null)
            {
                try
                {
                    SaveTranslation(jsonObject, currentKey, currentMsgId, currentMsgStr);
                }
                catch (Exception ex)
                {
                    Pipeline.PrintError($"  [!] Error processing final PO block starting around line {lastKeyLineNumber}: {ex.Message}");
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
                string msgstr = UnescapeString(msgStr ?? string.Empty);
                string msgid = UnescapeString(msgId ?? string.Empty);
                string finalString = string.IsNullOrEmpty(msgstr) ? msgid : msgstr;
                finalString = finalString.Replace("\r\n", "\n").Replace("\n", "\r\n"); // Force Windows line endings (CRLF)
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
            return text.Replace("\\n", "\n")
                       .Replace("\\r", "\r")
                       .Replace("\\\"", "\"")
                       .Replace("\\\\", "\\");
        }

        private static void ExtractValues(JToken? currentToken, List<string> remainingPath, List<string> currentPath, List<(string Key, string Text)> risultati)
        {
            if (currentToken == null) return;

            if (remainingPath.Count == 0)
            {
                if (currentToken.Type == JTokenType.String)
                {
                    risultati.Add((string.Join("||", currentPath), currentToken.ToString()));
                }
                return;
            }

            string nextKey = remainingPath[0];
            List<string> nextRemaining = remainingPath.Skip(1).ToList();

            if (nextKey == "*")
            {
                if (currentToken is JObject obj)
                {
                    foreach (var prop in obj.Properties())
                    {
                        var newPath = new List<string>(currentPath) { prop.Name };
                        ExtractValues(prop.Value, nextRemaining, newPath, risultati);
                    }
                }
            }
            else
            {
                if (currentToken is JObject obj && obj.ContainsKey(nextKey))
                {
                    var newPath = new List<string>(currentPath) { nextKey };
                    ExtractValues(obj[nextKey], nextRemaining, newPath, risultati);
                }
            }
        }

        internal static void JsonToPo(string inputFile, string outputFile, string? dictionaryFile, string language = "it")
        {
            List<string> lines = new List<string>();
            string jsonString = File.ReadAllText(inputFile);
            JObject jsonObject = JObject.Parse(jsonString) ?? new JObject();
            StringBuilder poContent = new StringBuilder();
            List<List<string>> structures = GetStructures(dictionaryFile, inputFile);

            // Add standard Gettext header
            poContent.AppendLine("msgid \"\"");
            poContent.AppendLine("msgstr \"\"");
            poContent.AppendLine("\"Project-Id-Version: Yakuza 6 Translation\\n\"");
            poContent.AppendLine("\"Last-Translator: SavT\\n\"");
            poContent.AppendLine("\"MIME-Version: 1.0\\n\"");
            poContent.AppendLine("\"Content-Type: text/plain; charset=UTF-8\\n\"");
            poContent.AppendLine("\"Content-Transfer-Encoding: 8bit\\n\"");
            poContent.AppendLine($"\"Language: {language}\\n\"");
            poContent.AppendLine();

            var risultati = new List<(string Key, string Text)>();

            foreach (var structure in structures)
            {
                // Start recursion directly from the root object
                ExtractValues(jsonObject, structure, new List<string>(), risultati);
            }

            foreach (var item in risultati)
            {
                poContent.AppendLine($"msgctxt \"{EscapeString(item.Key)}\"");
                poContent.AppendLine($"msgid \"{EscapeString(item.Text)}\"");
                poContent.AppendLine($"msgstr \"\"");
                poContent.AppendLine();
            }
            File.WriteAllText(outputFile, poContent.ToString());
        }
    }
}
