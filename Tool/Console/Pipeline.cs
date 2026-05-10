using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace PoConverter
{
    public class Pipeline
    {
        public static void Run(string[] args)
        {
            Console.CancelKeyPress += (sender, e) =>
            {
                PrintWarning("\n[!] Ctrl+C detected. Terminating external processes...");
                KillProcesses("reARMP");
                KillProcesses("ParTool");
            };

            string? folderPath = null;
            string? choice = null;
            bool skipTextures = false;
            string language = "it";
            bool cleanAll = false;
            bool autoYes = false;
            string dictFile = "dictionary.json";

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLower())
                    {
                        case "-g":
                        case "--game-path":
                            if (i + 1 < args.Length) folderPath = args[++i].Trim('"');
                            break;
                        case "-e":
                        case "--extract":
                            choice = "1";
                            break;
                        case "-r":
                        case "--recreate":
                            choice = "2";
                            break;
                        case "-t":
                        case "--skip-textures":
                            skipTextures = true;
                            break;
                        case "-l":
                        case "--lang":
                            if (i + 1 < args.Length) language = args[++i];
                            break;
                        case "-c":
                        case "--clean-all":
                            cleanAll = true;
                            break;
                        case "-y":
                        case "--yes":
                            autoYes = true;
                            break;
                        case "-d":
                        case "--dict":
                            if (i + 1 < args.Length) dictFile = args[++i].Trim('"');
                            break;
                    }
                }
            }

            PrintHeader("==================================================================");
            PrintHeader("--- Ultimate Automation (reARMP + ParTool + PoConverter) in C# ---");
            PrintHeader("==================================================================\n");

            if (string.IsNullOrEmpty(folderPath))
            {
                Console.Write("Enter the path of the 'Yakuza 6 - The Song of Life' folder: ");
                folderPath = Console.ReadLine()?.Trim().Trim('"');
            }

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                PrintError($"[!] Error: '{folderPath}' is not a valid folder.");
                return;
            }

            if (string.IsNullOrEmpty(choice))
            {
                PrintInfo("\nSelect the operation to perform:");
                Console.WriteLine("1. Extraction: From .bin file -> extract JSON with reARMP -> convert to .po");
                Console.WriteLine("2. Recreation: From .po file -> update JSON with PoConverter -> recreate .bin with reARMP");
                Console.Write("Choice (1 or 2): ");
                choice = Console.ReadLine()?.Trim();
            }

            if (choice != "1" && choice != "2")
            {
                PrintError("[!] Invalid choice.");
                return;
            }

            string rearmpCmd = "reARMP.exe";
            string rearmpArgsPrefix = "";

            HashSet<string> allowedBinFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> allowedTextureFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(dictFile))
            {
                try
                {
                    string dictJson = File.ReadAllText(dictFile);
                    JObject dictObj = JObject.Parse(dictJson);
                    
                    if (dictObj["texts"] is JObject textsObj)
                    {
                        foreach (var prop in textsObj.Properties())
                            allowedBinFiles.Add(prop.Name.Replace(".json", ""));
                    }
                    if (dictObj["textures"] is JObject texturesObj)
                    {
                        foreach (var prop in texturesObj.Properties())
                            allowedTextureFiles.Add(prop.Name);
                    }
                    
                    if (allowedBinFiles.Count == 0)
                    {
                        allowedBinFiles = dictObj.Properties()
                            .Where(p => p.Name.EndsWith(".json"))
                            .Select(p => p.Name.Replace(".json", "")).ToHashSet();
                    }
                }
                catch (Exception ex)
                {
                    PrintError($"  [!] Error reading {dictFile}: {ex.Message}");
                    return;
                }
            }
            else
            {
                PrintError($"  [!] Error: {dictFile} not found. Please create it first.");
                return;
            }

            string patchDir = Path.Combine(Environment.CurrentDirectory, "Yakuza 6 - Patch");
            string ogFileDir = Path.Combine(patchDir, "og file");
            string ogJsonDir = Path.Combine(patchDir, "og json");
            string workspaceDir = Path.Combine(patchDir, "workspace");
            string outputDir = Path.Combine(patchDir, "output");
            string convertedJsonDir = Path.Combine(patchDir, "converted json");

            if (choice == "1")
            {
                if (Directory.Exists(patchDir))
                {
                    bool doClean = autoYes;
                    if (!doClean)
                    {
                        Console.Write("\nThe 'Yakuza 6 - Patch' folder already exists. Do you want to CLEAR previous extraction files (workspace, og file, og json) before starting? (y/n): ");
                        string? cleanChoice = Console.ReadLine()?.Trim().ToLower();
                        doClean = (cleanChoice == "y" || cleanChoice == "yes");
                    }
                    if (doClean)
                    {
                        PrintStep("  -> Cleaning up previous extraction...");
                        try { if (Directory.Exists(ogFileDir)) Directory.Delete(ogFileDir, true); } catch { }
                        try { if (Directory.Exists(ogJsonDir)) Directory.Delete(ogJsonDir, true); } catch { }
                        try { if (Directory.Exists(workspaceDir)) Directory.Delete(workspaceDir, true); } catch { }
                    }
                }
            }
            else if (choice == "2")
            {
                if (Directory.Exists(outputDir))
                {
                    bool doClean = autoYes;
                    if (!doClean)
                    {
                        Console.Write("\nThe 'output' folder already exists. Do you want to CLEAR it before starting to avoid leftovers? (y/n): ");
                        string? cleanChoice = Console.ReadLine()?.Trim().ToLower();
                        doClean = (cleanChoice == "y" || cleanChoice == "yes");
                    }
                    if (doClean)
                    {
                        PrintStep("  -> Cleaning up previous output...");
                        try { Directory.Delete(outputDir, true); } catch { }
                        try { if (Directory.Exists(convertedJsonDir)) Directory.Delete(convertedJsonDir, true); } catch { }
                    }
                }
            }

            Directory.CreateDirectory(patchDir);
            Directory.CreateDirectory(ogFileDir);
            Directory.CreateDirectory(ogJsonDir);
            Directory.CreateDirectory(workspaceDir);
            Directory.CreateDirectory(outputDir);
            if (choice == "2") Directory.CreateDirectory(convertedJsonDir);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            if (choice == "1")
            {
                string uiParPath = Path.Combine(folderPath, "data", "ui.par");
                string uiParUnpackDir = Path.Combine(folderPath, "data", "ui.par.unpack");

                if (!skipTextures && File.Exists(uiParPath) && !Directory.Exists(uiParUnpackDir))
                {
                    if (File.Exists("ParTool.exe"))
                    {
                        PrintHeader("\n--- [ParTool] Unpacking ui.par ---");
                        RunProcess("ParTool.exe", $"extract \"{uiParPath}\" \"{uiParUnpackDir}\"");
                    }
                    else
                    {
                        PrintWarning("\n  [!] Warning: ParTool.exe not found. Cannot automatically unpack ui.par.");
                    }
                }

                var files = new List<string>();
                string dbPath = Path.Combine(folderPath, "data", "db", "e");

                if (Directory.Exists(dbPath))
                {
                    files.AddRange(Directory.GetFiles(dbPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".bin.json")));
                }
                if (!skipTextures && Directory.Exists(uiParUnpackDir))
                {
                    files.AddRange(Directory.GetFiles(uiParUnpackDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".bin.json")));
                }

                if (files.Count == 0)
                {
                    PrintWarning("No files found in the folder.");
                    return;
                }

                PrintInfo($"\nFound {files.Count} total files to copy to 'og file'.");

                int targetCount = 0;
                foreach (var binPath in files)
                {
                    string filename = Path.GetFileName(binPath);

                    string fileDir = Path.GetDirectoryName(binPath) ?? "";
                    string relPath = "";
                    string normalizedDir = fileDir.Replace('/', '\\');
                    int dataIdx = normalizedDir.IndexOf("\\data\\", StringComparison.OrdinalIgnoreCase);
                    
                    if (dataIdx != -1) relPath = normalizedDir.Substring(dataIdx + 1);
                    else if (normalizedDir.EndsWith("\\data", StringComparison.OrdinalIgnoreCase)) relPath = "data";
                    else if (normalizedDir.StartsWith("data\\", StringComparison.OrdinalIgnoreCase)) relPath = normalizedDir;
                    else if (normalizedDir.Equals("data", StringComparison.OrdinalIgnoreCase)) relPath = "data";

                    string currentOgFileDir = Path.Combine(ogFileDir, relPath);
                    Directory.CreateDirectory(currentOgFileDir);

                    string copiedBinPath = Path.Combine(currentOgFileDir, filename);
                    if (File.Exists(copiedBinPath)) File.SetAttributes(copiedBinPath, File.GetAttributes(copiedBinPath) & ~FileAttributes.ReadOnly);
                    File.Copy(binPath, copiedBinPath, true);
                    File.SetAttributes(copiedBinPath, File.GetAttributes(copiedBinPath) & ~FileAttributes.ReadOnly);

                    bool isTargetBin = allowedBinFiles.Contains(filename) && relPath.StartsWith("data\\db\\e", StringComparison.OrdinalIgnoreCase);
                    bool isTargetTex = allowedTextureFiles.Contains(filename) && relPath.StartsWith("data\\ui.par.unpack\\e\\texture", StringComparison.OrdinalIgnoreCase);

                    if (isTargetBin || isTargetTex)
                    {
                        targetCount++;
                        PrintHeader($"\n--- Processing Targeted File: {filename} ---");
                        
                        string currentOgJsonDir = Path.Combine(ogJsonDir, relPath);
                        string currentWorkspaceDir = Path.Combine(workspaceDir, relPath);
                        Directory.CreateDirectory(currentOgJsonDir);
                        Directory.CreateDirectory(currentWorkspaceDir);

                        if (isTargetTex)
                        {
                            string copiedWorkspacePath = Path.Combine(currentWorkspaceDir, filename);
                            if (File.Exists(copiedWorkspacePath)) File.SetAttributes(copiedWorkspacePath, File.GetAttributes(copiedWorkspacePath) & ~FileAttributes.ReadOnly);
                            File.Copy(binPath, copiedWorkspacePath, true);
                            File.SetAttributes(copiedWorkspacePath, File.GetAttributes(copiedWorkspacePath) & ~FileAttributes.ReadOnly);
                            PrintSuccess($"  [V] Finished! Texture copied to workspace in {relPath}");
                            continue;
                        }

                    PrintStep("  -> [Pipeline] Executing reARMP for JSON extraction...");
                    if (!RunProcess(rearmpCmd, $"{rearmpArgsPrefix}\"{copiedBinPath}\"")) continue;

                    string generatedJsonPathInPlace = copiedBinPath + ".json";
                    string generatedJsonPathCWD = Path.Combine(Environment.CurrentDirectory, filename + ".json");
                    string targetJsonPath = Path.Combine(currentOgJsonDir, filename + ".json");
                    string poPath = Path.Combine(currentWorkspaceDir, filename.Replace(".bin", ".po"));

                    string? actualGeneratedJson = null;
                    if (File.Exists(generatedJsonPathCWD)) actualGeneratedJson = generatedJsonPathCWD;
                    else if (File.Exists(generatedJsonPathInPlace)) actualGeneratedJson = generatedJsonPathInPlace;

                    if (actualGeneratedJson != null)
                    {
                        if (actualGeneratedJson != targetJsonPath)
                        {
                            if (File.Exists(targetJsonPath)) File.Delete(targetJsonPath);
                            File.Move(actualGeneratedJson, targetJsonPath);
                        }

                        PrintStep("  -> [PoConverter] Generating PO file internally...");
                        try
                        {
                            PoConverter.JsonToPo(targetJsonPath, poPath, dictFile, language);
                            PrintSuccess($"  [V] Finished! Created {Path.GetFileName(poPath)} in {relPath}");
                        }
                        catch (Exception ex)
                        {
                            PrintError($"  [!] Error during PO conversion for {filename}: {ex.Message}");
                        }
                    }
                    else
                    {
                        PrintError($"  [!] JSON file not generated for {filename}.");
                    }
                }
            }
            PrintInfo($"\nFinished copying all files and processed {targetCount} targeted files.");
        }
        else if (choice == "2")
            {
                var poFiles = Directory.GetFiles(workspaceDir, "*.po", SearchOption.AllDirectories)
                    .Where(f => allowedBinFiles.Contains(Path.GetFileName(f).Replace(".po", ".bin")))
                    .ToList();
                var texFiles = new List<string>();
                
                if (!skipTextures)
                {
                    texFiles = Directory.GetFiles(workspaceDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => allowedTextureFiles.Contains(Path.GetFileName(f)))
                        .ToList();
                }

                if (poFiles.Count == 0 && texFiles.Count == 0)
                {
                    PrintWarning("No .po or texture files found in the workspace folder.");
                    return;
                }

                PrintHeader("\n--- Copying original files from 'og file' to 'output' ---");
                CopyDirectory(ogFileDir, outputDir);

                PrintInfo($"\nFound {poFiles.Count} .po files and {texFiles.Count} textures to process.");

                foreach (var poPath in poFiles)
                {
                    string poFilename = Path.GetFileName(poPath);
                    PrintHeader($"\n--- Processing: {poFilename} ---");

                    string fileDir = Path.GetDirectoryName(poPath) ?? "";
                    string relPath = "";
                    if (fileDir.StartsWith(workspaceDir, StringComparison.OrdinalIgnoreCase))
                    {
                        relPath = fileDir.Substring(workspaceDir.Length).TrimStart('\\', '/');
                    }

                    string currentOgJsonDir = Path.Combine(ogJsonDir, relPath);
                    string currentOutputDir = Path.Combine(outputDir, relPath);
                    Directory.CreateDirectory(currentOutputDir);

                    string baseName = poFilename.Replace(".po", "");
                    string jsonFilename = baseName + ".bin.json";
                    string ogJsonPath = Path.Combine(currentOgJsonDir, jsonFilename);
                    string outputJsonPath = Path.Combine(currentOutputDir, jsonFilename);

                    if (!File.Exists(ogJsonPath))
                    {
                        PrintError($"  [!] Original JSON file missing: {jsonFilename} in 'og json\\{relPath}'. Make sure you have extracted first.");
                        continue;
                    }

                    PrintStep($"  -> [PoConverter] Injecting translation from {poFilename} into JSON...");
                    try
                    {
                        PoConverter.PoToJson(poPath, outputJsonPath, ogJsonPath, dictFile);
                    }
                    catch (Exception ex)
                    {
                        PrintError($"  [!] Error during PO injection into JSON for {poFilename}: {ex.Message}");
                        continue;
                    }

                    PrintStep("  -> [Pipeline] Executing reARMP to recreate .bin...");
                    
                    string targetBinPath = Path.Combine(currentOutputDir, baseName + ".bin");

                    if (RunProcess(rearmpCmd, $"{rearmpArgsPrefix}\"{outputJsonPath}\""))
                    {
                        string generatedBinPathInPlace = Path.Combine(currentOutputDir, jsonFilename + ".bin");
                        string generatedBinPathCWD = Path.Combine(Environment.CurrentDirectory, jsonFilename + ".bin");

                        string? actualGeneratedBin = null;
                        if (File.Exists(generatedBinPathCWD)) actualGeneratedBin = generatedBinPathCWD;
                        else if (File.Exists(generatedBinPathInPlace)) actualGeneratedBin = generatedBinPathInPlace;

                        if (actualGeneratedBin != null)
                        {
                            if (actualGeneratedBin != targetBinPath)
                            {
                                if (File.Exists(targetBinPath))
                                {
                                    File.SetAttributes(targetBinPath, File.GetAttributes(targetBinPath) & ~FileAttributes.ReadOnly);
                                    File.Delete(targetBinPath);
                                }
                                File.Move(actualGeneratedBin, targetBinPath);
                            }
                            PrintSuccess($"  [V] Finished! .bin file updated successfully in {relPath}.");
                        }
                        else
                        {
                            PrintError($"  [!] Error: {baseName}.bin was not generated by reARMP.");
                        }
                    }

                    if (File.Exists(outputJsonPath))
                    {
                        string targetConvertedJsonDir = Path.Combine(convertedJsonDir, relPath);
                        Directory.CreateDirectory(targetConvertedJsonDir);
                        string targetConvertedJsonPath = Path.Combine(targetConvertedJsonDir, jsonFilename);
                        if (File.Exists(targetConvertedJsonPath)) File.Delete(targetConvertedJsonPath);
                        File.Move(outputJsonPath, targetConvertedJsonPath);
                    }
                }

                if (!skipTextures)
                {
                    foreach (var texPath in texFiles)
                    {
                    string texFilename = Path.GetFileName(texPath);
                        PrintHeader($"\n--- Processing Texture: {texFilename} ---");

                    string fileDir = Path.GetDirectoryName(texPath) ?? "";
                    string relPath = "";
                    if (fileDir.StartsWith(workspaceDir, StringComparison.OrdinalIgnoreCase))
                    {
                        relPath = fileDir.Substring(workspaceDir.Length).TrimStart('\\', '/');
                    }

                    string currentOutputDir = Path.Combine(outputDir, relPath);
                    Directory.CreateDirectory(currentOutputDir);

                    string targetPath = Path.Combine(currentOutputDir, texFilename);
                    if (File.Exists(targetPath)) File.SetAttributes(targetPath, File.GetAttributes(targetPath) & ~FileAttributes.ReadOnly);
                    File.Copy(texPath, targetPath, true);
                    File.SetAttributes(targetPath, File.GetAttributes(targetPath) & ~FileAttributes.ReadOnly);
                        PrintSuccess($"  [V] Finished! Texture copied to output in {relPath}.");
                }
                }

                string outputUiParUnpack = Path.Combine(outputDir, "data", "ui.par.unpack");
                if (!skipTextures && Directory.Exists(outputUiParUnpack))
                {
                    if (File.Exists("ParTool.exe"))
                    {
                        PrintHeader("\n--- [ParTool] Repacking modified files into ui.par ---");
                        string finalUiPar = Path.Combine(outputDir, "data", "ui.par");
                        if (RunProcess("ParTool.exe", $"create \"{outputUiParUnpack}\" \"{finalUiPar}\" -c 1"))
                        {
                            PrintSuccess($"  [V] Finished! Repacked ui.par created in output\\data.");
                            PrintStep($"  -> Cleaning up temporary ui.par.unpack folder...");
                            try
                            {
                                Directory.Delete(outputUiParUnpack, true);
                            }
                            catch (Exception ex)
                            {
                                PrintWarning($"  [!] Warning: Could not delete temporary ui.par.unpack folder: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        PrintWarning("\n  [!] Warning: ParTool.exe not found. Cannot automatically repack ui.par.");
                    }
                }

                PrintHeader("\n--- Cleaning up output folder ---");
                int removedCount = 0;
                var allOutputBins = Directory.GetFiles(outputDir, "*.bin", SearchOption.AllDirectories);
                foreach (var bin in allOutputBins)
                {
                    string binName = Path.GetFileName(bin);
                    if (!allowedBinFiles.Contains(binName))
                    {
                        try 
                        { 
                            File.SetAttributes(bin, File.GetAttributes(bin) & ~FileAttributes.ReadOnly);
                            File.Delete(bin); 
                            removedCount++;
                        } 
                        catch { }
                    }
                }
                if (removedCount > 0)
                {
                    PrintSuccess($"  [V] Cleaned up {removedCount} unmodified .bin files to keep output strictly for modding.");
                }

                if (cleanAll)
                {
                    PrintHeader("\n--- Cleaning up all folders except 'output' ---");
                    try { if (Directory.Exists(ogFileDir)) Directory.Delete(ogFileDir, true); } catch { }
                    try { if (Directory.Exists(ogJsonDir)) Directory.Delete(ogJsonDir, true); } catch { }
                    try { if (Directory.Exists(workspaceDir)) Directory.Delete(workspaceDir, true); } catch { }
                    try { if (Directory.Exists(convertedJsonDir)) Directory.Delete(convertedJsonDir, true); } catch { }
                    PrintSuccess("  [V] Cleanup completed.");
                }
            }

            stopwatch.Stop();
            PrintInfo($"\n==================================================================");
            PrintInfo($"--- Batch Operation completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds! ---");
            PrintInfo($"==================================================================");
            if (!autoYes)
            {
                Console.WriteLine("Press ENTER to close...");
                Console.ReadLine();
            }
        }

        private static void KillProcesses(string name)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
            }
            catch { }
        }

        private static bool RunProcess(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process? process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        string toolName = Path.GetFileNameWithoutExtension(fileName);
                        process.OutputDataReceived += (sender, e) => { if (e.Data != null) PrintStep($"    [{toolName}] {e.Data}"); };
                        process.ErrorDataReceived += (sender, e) => { if (e.Data != null) PrintError($"    [{toolName} ERR] {e.Data}"); };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            PrintError($"  [!] {toolName} failed with exit code {process.ExitCode}");
                            return false;
                        }
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                PrintError($"  [!] Unable to start {fileName}: {ex.Message}");
                return false;
            }
        }

        internal static void PrintError(string msg) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintWarning(string msg) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintSuccess(string msg) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintInfo(string msg) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintStep(string msg) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintHeader(string msg) { Console.ForegroundColor = ConsoleColor.Magenta; Console.WriteLine(msg); Console.ResetColor(); }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            if (!Directory.Exists(sourceDir)) return;
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(sourceDir.Length).TrimStart('\\', '/');
                string targetPath = Path.Combine(destinationDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (File.Exists(targetPath)) File.SetAttributes(targetPath, File.GetAttributes(targetPath) & ~FileAttributes.ReadOnly);
                File.Copy(file, targetPath, true);
                File.SetAttributes(targetPath, File.GetAttributes(targetPath) & ~FileAttributes.ReadOnly);
            }
        }
    }
}