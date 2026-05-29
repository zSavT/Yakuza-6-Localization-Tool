using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Yakuza6LocalizationTool
{
    public static class PoSplitterMerger
    {
        /// <summary>
        /// Splits a master .po file into smaller files based on the second parameter of msgctxt.
        /// </summary>
        public static void SplitPoFile(string sourcePoPath, string outputDir)
        {
            if (!File.Exists(sourcePoPath)) return;

            var lines = File.ReadLines(sourcePoPath);
            string header = "";
            
            // Dictionary to map the category name (e.g. speech_list_main01) to its content
            Dictionary<string, StringBuilder> filesContent = new Dictionary<string, StringBuilder>();
            
            StringBuilder currentBlock = new StringBuilder();
            string currentCategory = "uncategorized";
            bool isHeader = true;

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) // Empty line indicates the end of a block
                {
                    if (currentBlock.Length > 0)
                    {
                        if (isHeader)
                        {
                            header = currentBlock.ToString().TrimEnd() + Environment.NewLine + Environment.NewLine;
                            isHeader = false;
                        }
                        else
                        {
                            if (!filesContent.ContainsKey(currentCategory))
                                filesContent[currentCategory] = new StringBuilder();
                            
                            filesContent[currentCategory].AppendLine(currentBlock.ToString().TrimEnd());
                            filesContent[currentCategory].AppendLine();
                        }
                        currentBlock.Clear();
                        currentCategory = "uncategorized";
                    }
                }
                else
                {
                    currentBlock.AppendLine(line);
                    if (line.StartsWith("msgctxt"))
                    {
                        // Extracts the category: from "1||speech_list_main01||table..." it gets "speech_list_main01"
                        string[] parts = line.Split(new string[] { "||" }, StringSplitOptions.None);
                        if (parts.Length > 1)
                        {
                            currentCategory = parts[1];
                        }
                    }
                }
            }

            // Saves the last block if the file did not end with an empty line
            if (currentBlock.Length > 0 && !isHeader)
            {
                if (!filesContent.ContainsKey(currentCategory))
                    filesContent[currentCategory] = new StringBuilder();
                filesContent[currentCategory].AppendLine(currentBlock.ToString().TrimEnd());
                filesContent[currentCategory].AppendLine();
            }

            // File generation
            Directory.CreateDirectory(outputDir);
            string baseName = Path.GetFileNameWithoutExtension(sourcePoPath);

            foreach (var kvp in filesContent)
            {
                string outPath = Path.Combine(outputDir, $"{baseName}_{kvp.Key}.po");
                File.WriteAllText(outPath, header + kvp.Value.ToString(), new UTF8Encoding(true));
            }
        }

        /// <summary>
        /// Recombines the split files into a single master .po file ready for JSON.
        /// </summary>
        public static void MergePoFiles(string splitFilesDir, string baseName, string outputPoPath)
        {
            string[] files = Directory.GetFiles(splitFilesDir, $"{baseName}_*.po")
                .OrderBy(f => f)
                .ToArray();
            if (files.Length == 0) return;

            string header = "";
            StringBuilder mergedContent = new StringBuilder();

            foreach (string file in files)
            {
                // Extract the content of each fragmented file
                string content = File.ReadAllText(file).Replace("\r\n", "\n");
                
                // Remove the header from the split file (using double newline as the header separator)
                int firstDoubleEnter = content.IndexOf("\n\n");
                if (firstDoubleEnter != -1)
                {
                    if (string.IsNullOrEmpty(header))
                        header = content.Substring(0, firstDoubleEnter + 2).Replace("\n", "\r\n");
                    
                    mergedContent.Append(content.Substring(firstDoubleEnter + 2).Replace("\n", "\r\n"));
                }
            }

            File.WriteAllText(outputPoPath, header + mergedContent.ToString(), new UTF8Encoding(true));
        }
    }
}