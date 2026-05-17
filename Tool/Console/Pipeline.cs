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
        public static int ErrorCount = 0;
        public static int WarningCount = 0;
        public static bool QuietLogs = false;

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

            ErrorCount = 0;
            WarningCount = 0;
            QuietLogs = false;

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
                        case "-q":
                        case "--quiet":
                            QuietLogs = true;
                            break;
                        case "-d":
                        case "--dict":
                            if (i + 1 < args.Length) dictFile = args[++i].Trim('"');
                            break;
                    }
                }
            }

            PrintHeader("==================================================================");
            PrintHeader("   Yakuza 6 Localization Tool - Ultimate Automation Pipeline");
            PrintHeader("==================================================================");
            PrintInfo("   Credits:");
            PrintInfo("   - reARMP (by Ret-HZ): Parse Yakuza .bin files to .json and vice-versa");
            PrintInfo("   - ParTool (by Kaplas80): Extract and compress .par archives");
            PrintHeader("==================================================================\n");

            if (string.IsNullOrEmpty(folderPath))
            {
                Console.Write("Enter the path of the 'Yakuza 6 - The Song of Life' folder: ");
                folderPath = Console.ReadLine()?.Trim().Trim('"');
            }

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                PrintError($"[!] Error: '{folderPath}' is not a valid folder. (Please provide the full path to the 'Yakuza 6 - The Song of Life' installation directory.)");
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
            HashSet<string> allowedCmnFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> folderFilters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(dictFile))
            {
                try
                {
                    string dictJson = File.ReadAllText(dictFile);
                    JObject dictObj = JObject.Parse(dictJson);
                    
                    if (dictObj["folder_filters"] is JObject folderFiltersObj)
                    {
                        foreach (var prop in folderFiltersObj.Properties())
                        {
                            var list = new List<string>();
                            if (prop.Value is JArray arr)
                            {
                                foreach (var item in arr) list.Add(item.ToString());
                            }
                            folderFilters[prop.Name] = list;
                        }
                    }

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
                    if (dictObj["cmns"] is JObject cmnsObj)
                    {
                        foreach (var prop in cmnsObj.Properties())
                            allowedCmnFiles.Add(prop.Name.Replace("cmn.par/", ""));
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
                PrintError($"  [!] Error: {dictFile} not found. (Please ensure the dictionary file is in the tool folder.)");
                return;
            }

            string patchDir = Path.Combine(Environment.CurrentDirectory, "Yakuza 6 - Patch");
            string ogFileDir = Path.Combine(patchDir, "og file");
            string ogJsonDir = Path.Combine(patchDir, "og json");
            string workspaceDir = Path.Combine(patchDir, "workspace");
            string outputDir = Path.Combine(patchDir, "output");
            string convertedJsonDir = Path.Combine(patchDir, "converted json");
            string warningsFile = Path.Combine(patchDir, "warnings.txt");

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
                        try { if (File.Exists(warningsFile)) File.Delete(warningsFile); } catch { }
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

            int targetCount = 0;
            int updatedTextsCount = 0;
            int updatedTexturesCount = 0;

            if (choice == "1")
            {
                string uiParPath = Path.Combine(folderPath, "data", "ui.par");
                string uiParUnpackDir = Path.Combine(folderPath, "data", "ui.par.unpack");
                string talkParPath = Path.Combine(folderPath, "data", "talk.par");
                string talkParUnpackDir = Path.Combine(folderPath, "data", "talk.par.unpack");

                if (!skipTextures && File.Exists(uiParPath) && !Directory.Exists(uiParUnpackDir))
                {
                    if (File.Exists("ParTool.exe"))
                    {
                        PrintHeader("\n[*] [ParTool] Unpacking ui.par...");
                        Stopwatch ptTimer = Stopwatch.StartNew();
                        if (RunProcess("ParTool.exe", $"extract \"{uiParPath}\" \"{uiParUnpackDir}\""))
                        {
                            ptTimer.Stop();
                            PrintSuccess($"  [OK] Unpacked ui.par. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
                        }
                    }
                    else
                    {
                        PrintWarning("\n  [!] Warning: ParTool.exe not found. Cannot automatically unpack ui.par. (Please place ParTool.exe in the tool folder.)");
                    }
                }

                if (File.Exists(talkParPath) && !Directory.Exists(talkParUnpackDir))
                {
                    if (File.Exists("ParTool.exe"))
                    {
                        PrintHeader("\n[*] [ParTool] Unpacking talk.par (recursive)...");
                        Stopwatch ptTimer = Stopwatch.StartNew();
                        if (RunProcess("ParTool.exe", $"extract \"{talkParPath}\" \"{talkParUnpackDir}\" -r"))
                        {
                            ptTimer.Stop();
                            PrintSuccess($"  [OK] Unpacked talk.par. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
                        }
                    }
                    else
                    {
                        PrintWarning("\n  [!] Warning: ParTool.exe not found. Cannot automatically unpack talk.par. (Please place ParTool.exe in the tool folder.)");
                    }
                }

                // Unpack all .par files inside "auth" folder
                string authDirPath = Path.Combine(folderPath, "data", "auth");
                if (Directory.Exists(authDirPath))
                {
                    foreach (string authPar in Directory.GetFiles(authDirPath, "*.par"))
                    {
                        string authParUnpackDir = authPar + ".unpack";
                        if (!Directory.Exists(authParUnpackDir))
                        {
                            if (File.Exists("ParTool.exe"))
                            {
                                PrintHeader($"\n[*] [ParTool] Unpacking {Path.GetFileName(authPar)} (recursive)...");
                                Stopwatch ptTimer = Stopwatch.StartNew();
                                if (RunProcess("ParTool.exe", $"extract \"{authPar}\" \"{authParUnpackDir}\" -r"))
                                {
                                    ptTimer.Stop();
                                    PrintSuccess($"  [OK] Unpacked {Path.GetFileName(authPar)}. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
                                }
                            }
                            else
                            {
                                PrintWarning($"\n  [!] Warning: ParTool.exe not found. Cannot automatically unpack {Path.GetFileName(authPar)}. (Please place ParTool.exe in the tool folder.)");
                            }
                        }
                    }
                }

                // Unpack dynamically specified .par files from folder_filters
                foreach (var kvp in folderFilters)
                {
                    string folderName = kvp.Key;
                    foreach (var fileName in kvp.Value)
                    {
                        if (fileName.EndsWith(".par", StringComparison.OrdinalIgnoreCase))
                        {
                            string parPath = Path.Combine(folderPath, "data", folderName, fileName);
                            string parUnpackDir = Path.Combine(folderPath, "data", folderName, fileName + ".unpack");

                            if (File.Exists(parPath) && !Directory.Exists(parUnpackDir))
                            {
                                if (File.Exists("ParTool.exe"))
                                {
                                    PrintHeader($"\n[*] [ParTool] Unpacking {fileName} (recursive)...");
                                    Stopwatch ptTimer = Stopwatch.StartNew();
                                    if (RunProcess("ParTool.exe", $"extract \"{parPath}\" \"{parUnpackDir}\" -r"))
                                    {
                                        ptTimer.Stop();
                                        PrintSuccess($"  [OK] Unpacked {fileName}. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
                                    }
                                }
                                else
                                {
                                    PrintWarning($"\n  [!] Warning: ParTool.exe not found. Cannot automatically unpack {fileName}. (Please place ParTool.exe in the tool folder.)");
                                }
                            }
                        }
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
                if (Directory.Exists(talkParUnpackDir))
                {
                    files.AddRange(Directory.GetFiles(talkParUnpackDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".bin.json")));
                }

                if (Directory.Exists(authDirPath))
                {
                    foreach (string authParUnpackDir in Directory.GetDirectories(authDirPath, "*.par.unpack"))
                    {
                        files.AddRange(Directory.GetFiles(authParUnpackDir, "*.*", SearchOption.AllDirectories)
                            .Where(f => !f.EndsWith(".bin.json")));
                    }
                }

                // Gather files from folder_filters
                foreach (var kvp in folderFilters)
                {
                    string folderName = kvp.Key;
                    foreach (var fileName in kvp.Value)
                    {
                        if (fileName.EndsWith(".par", StringComparison.OrdinalIgnoreCase))
                        {
                            string parUnpackDir = Path.Combine(folderPath, "data", folderName, fileName + ".unpack");
                            if (Directory.Exists(parUnpackDir))
                            {
                                files.AddRange(Directory.GetFiles(parUnpackDir, "*.*", SearchOption.AllDirectories)
                                    .Where(f => !f.EndsWith(".bin.json")));
                            }
                        }
                        else
                        {
                            string filePath = Path.Combine(folderPath, "data", folderName, fileName);
                            if (File.Exists(filePath))
                            {
                                files.Add(filePath);
                            }
                        }
                    }
                }

                files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                if (files.Count == 0)
                {
                    PrintWarning("No files found in the folder.");
                    return;
                }

                PrintInfo($"\n[*] Found {files.Count} total files to evaluate.");

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

                    bool isTargetBin = IsFileAllowed(filename, allowedBinFiles);
                    bool isTargetTex = IsFileAllowed(filename, allowedTextureFiles);
                    bool isTargetCmn = IsFileAllowed(filename, allowedCmnFiles);

                    var pathParts = relPath.Split('\\', '/');
                    bool isInEFolder = pathParts.Contains("e", StringComparer.OrdinalIgnoreCase);
                    string[] otherLangs = { "c", "k", "j", "zh", "ko", "ja", "zh-hant", "zh-hans", "tw" };
                    bool isInOtherLangFolder = pathParts.Any(p => otherLangs.Contains(p.ToLower()));

                    bool isAuth = pathParts.Contains("auth", StringComparer.OrdinalIgnoreCase);
                    bool isHact = pathParts.Contains("hact", StringComparer.OrdinalIgnoreCase);

                    if (isAuth)
                    {
                        // Nessun file .po per auth, consentiamo solo le texture
                        isTargetBin = false;
                        isTargetCmn = false;
                    }

                    if (isHact && !isInEFolder)
                    {
                        // Per hact consideriamo esclusivamente i file dentro la cartella "e"
                        isTargetBin = false;
                        isTargetCmn = false;
                        isTargetTex = false;
                    }

                    bool isNeutralAllowed = folderFilters.Keys.Any(k => pathParts.Contains(k, StringComparer.OrdinalIgnoreCase) && !k.Equals("hact", StringComparison.OrdinalIgnoreCase)) || isAuth;

                    bool shouldProcess = false;
                    if (isTargetBin || isTargetCmn) 
                    {
                        shouldProcess = isInEFolder || isNeutralAllowed;
                    }
                    else if (isTargetTex) 
                    {
                        shouldProcess = isInEFolder || isNeutralAllowed;
                    }

                    if (shouldProcess)
                    {
                        string currentOgFileDir = Path.Combine(ogFileDir, relPath);
                        Directory.CreateDirectory(currentOgFileDir);

                        string copiedBinPath = Path.Combine(currentOgFileDir, filename);
                        if (File.Exists(copiedBinPath)) File.SetAttributes(copiedBinPath, File.GetAttributes(copiedBinPath) & ~FileAttributes.ReadOnly);
                        File.Copy(binPath, copiedBinPath, true);
                        File.SetAttributes(copiedBinPath, File.GetAttributes(copiedBinPath) & ~FileAttributes.ReadOnly);

                        targetCount++;
                        PrintHeader($"\n[*] Processing Targeted File: {filename}");
                        
                        string currentOgJsonDir = Path.Combine(ogJsonDir, relPath);
                        string currentWorkspaceDir = Path.Combine(workspaceDir, relPath);

                        bool kept = false;
                        string poPath = Path.Combine(currentWorkspaceDir, filename.Replace(".bin", ".po"));

                        if (isTargetTex)
                        {
                            Directory.CreateDirectory(currentWorkspaceDir);
                            string copiedWorkspacePath = Path.Combine(currentWorkspaceDir, filename);
                            if (File.Exists(copiedWorkspacePath)) File.SetAttributes(copiedWorkspacePath, File.GetAttributes(copiedWorkspacePath) & ~FileAttributes.ReadOnly);
                            File.Copy(binPath, copiedWorkspacePath, true);
                            File.SetAttributes(copiedWorkspacePath, File.GetAttributes(copiedWorkspacePath) & ~FileAttributes.ReadOnly);
                            PrintSuccess($"  [OK] Texture copied to workspace: {relPath}\\{filename}");
                            kept = true;
                        }
                        else if (isTargetCmn)
                        {
                            PrintStep("  -> [BinaryScanner] Extracting texts from .bin...");
                            try
                            {
                                var texts = Yakuza6LocalizationTool.CmnTextManager.ExtractTexts(copiedBinPath);
                                if (texts.Count > 0)
                                {
                                    Directory.CreateDirectory(currentWorkspaceDir);
                                    PoConverter.DictToPo(texts, poPath, language);
                                    PrintSuccess($"  [OK] Created PO file: {relPath}\\{Path.GetFileName(poPath)}");
                                    kept = true;
                                }
                                else
                                {
                                    PrintWarning($"  [-] No valid translatable text found. Skipping.");
                                }
                            }
                            catch (Exception ex)
                            {
                                PrintError($"  [!] Error during CMN extraction for {filename}: {ex.Message}");
                            }
                        }
                        else if (isTargetBin)
                        {
                            PrintStep("  -> [Pipeline] Executing reARMP for JSON extraction...");
                            if (RunProcess(rearmpCmd, $"{rearmpArgsPrefix}\"{copiedBinPath}\""))
                            {
                                string generatedJsonPathInPlace = copiedBinPath + ".json";
                                string generatedJsonPathCWD = Path.Combine(Environment.CurrentDirectory, filename + ".json");
                                string targetJsonPath = Path.Combine(currentOgJsonDir, filename + ".json");

                                string? actualGeneratedJson = null;
                                if (File.Exists(generatedJsonPathCWD)) actualGeneratedJson = generatedJsonPathCWD;
                                else if (File.Exists(generatedJsonPathInPlace)) actualGeneratedJson = generatedJsonPathInPlace;

                                if (actualGeneratedJson != null)
                                {
                                    Directory.CreateDirectory(currentOgJsonDir);
                                    if (File.Exists(targetJsonPath)) File.Delete(targetJsonPath);
                                    File.Move(actualGeneratedJson, targetJsonPath);

                                    PrintStep("  -> [PoConverter] Generating PO file internally...");
                                    try
                                    {
                                        int count = PoConverter.JsonToPo(targetJsonPath, poPath, dictFile, language);
                                        if (count > 0)
                                        {
                                            PrintSuccess($"  [OK] Created PO file: {relPath}\\{Path.GetFileName(poPath)}");
                                            kept = true;
                                        }
                                        else
                                        {
                                            PrintWarning($"  [-] No valid strings found in {filename}. Skipping.");
                                            File.Delete(targetJsonPath);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        PrintError($"  [!] Error during PO conversion for {filename}: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    PrintError($"  [!] JSON file not generated for {filename}. (Check if reARMP.exe is in the folder and works properly.)");
                                }
                            }
                        }

                        if (!kept)
                        {
                            // Remove unused copied file from 'og file'
                            if (File.Exists(copiedBinPath)) File.Delete(copiedBinPath);
                            targetCount--;
                            
                            // Cleanup empty directories
                            if (Directory.Exists(currentOgFileDir) && !Directory.EnumerateFileSystemEntries(currentOgFileDir).Any()) Directory.Delete(currentOgFileDir);
                            if (Directory.Exists(currentOgJsonDir) && !Directory.EnumerateFileSystemEntries(currentOgJsonDir).Any()) Directory.Delete(currentOgJsonDir);
                        }
                    }
                }
                PrintInfo($"\n[*] Finished extraction! Processed {targetCount} targeted files.");
        }
        else if (choice == "2")
            {
                var poFiles = Directory.GetFiles(workspaceDir, "*.po", SearchOption.AllDirectories)
                    .Where(f => IsFileAllowed(Path.GetFileName(f).Replace(".po", ".bin"), allowedBinFiles) || IsFileAllowed(Path.GetFileName(f).Replace(".po", ".bin"), allowedCmnFiles))
                    .ToList();
                var texFiles = new List<string>();
                
                if (!skipTextures)
                {
                    texFiles = Directory.GetFiles(workspaceDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsFileAllowed(Path.GetFileName(f), allowedTextureFiles))
                        .ToList();
                }

                if (poFiles.Count == 0 && texFiles.Count == 0)
                {
                    PrintWarning("No .po or texture files found in the workspace folder.");
                    return;
                }

                PrintHeader("\n[*] Copying original files from 'og file' to 'output'...");
                CopyDirectory(ogFileDir, outputDir);

                PrintInfo($"\n[*] Found {poFiles.Count} .po files and {texFiles.Count} textures to process.");

                int currentPo = 0;
                foreach (var poPath in poFiles)
                {
                    currentPo++;
                    string poFilename = Path.GetFileName(poPath);
                    PrintHeader($"\n[*] Processing PO File [{currentPo}/{poFiles.Count}]: {poFilename}");

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

                    bool isCmn = IsFileAllowed(baseName + ".bin", allowedCmnFiles);
                    if (isCmn)
                    {
                        PrintStep($"  -> [BinaryScanner] Injecting translation from {poFilename} into .bin...");
                        string originalCmnPath = Path.Combine(ogFileDir, relPath, baseName + ".bin");
                        string outputCmnPath = Path.Combine(currentOutputDir, baseName + ".bin");

                        if (!File.Exists(originalCmnPath))
                        {
                            PrintError($"  [!] Original CMN file missing: {baseName}.bin in 'og file\\{relPath}'. (Did you delete the 'og file' folder? Try running Phase 1 Extraction again.)");
                            continue;
                        }

                        try
                        {
                            var translatedTexts = PoConverter.PoToDict(poPath);
                            Yakuza6LocalizationTool.CmnTextManager.InjectTextsAndSave(originalCmnPath, outputCmnPath, translatedTexts, warningsFile);
                            PrintSuccess($"  [OK] CMN file updated successfully in {relPath}.");
                            updatedTextsCount++;
                        }
                        catch (Exception ex)
                        {
                            PrintError($"  [!] Error during PO injection into CMN for {poFilename}: {ex.Message}");
                        }
                        continue;
                    }

                    if (!File.Exists(ogJsonPath))
                    {
                        PrintError($"  [!] Original JSON file missing: {jsonFilename} in 'og json\\{relPath}'. (Did you delete the 'og json' folder? Try running Phase 1 Extraction again.)");
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
                            PrintSuccess($"  [OK] BIN file updated successfully in {relPath}.");
                            updatedTextsCount++;
                        }
                        else
                        {
                            PrintError($"  [!] Error: {baseName}.bin was not generated by reARMP. (Check if reARMP.exe is in the folder and works properly.)");
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
                    int currentTex = 0;
                    foreach (var texPath in texFiles)
                    {
                        currentTex++;
                        string texFilename = Path.GetFileName(texPath);
                        PrintHeader($"\n[*] Processing Texture [{currentTex}/{texFiles.Count}]: {texFilename}");

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
                    PrintSuccess($"  [OK] Texture copied to output in {relPath}.");
                    updatedTexturesCount++;
                }
                }

                string outputUiParUnpack = Path.Combine(outputDir, "data", "ui.par.unpack");
                if (!skipTextures && Directory.Exists(outputUiParUnpack))
                {
                    if (File.Exists("ParTool.exe"))
                    {
                        string originalUiPar = Path.Combine(folderPath, "data", "ui.par");
                        string finalUiPar = Path.Combine(outputDir, "data", "ui.par");
                        PrintHeader("\n[*] [ParTool] Injecting modified files into ui.par...");
                        PrintFilesBeingAdded(outputUiParUnpack);
                        Stopwatch ptTimer = Stopwatch.StartNew();
                        if (RunProcess("ParTool.exe", $"add \"{originalUiPar}\" \"{outputUiParUnpack}\" \"{finalUiPar}\" -c 1"))
                        {
                            ptTimer.Stop();
                            PrintSuccess($"  [OK] Updated ui.par created in output\\data. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
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
                        PrintWarning("\n  [!] Warning: ParTool.exe not found. Cannot automatically repack ui.par. (Please place ParTool.exe in the tool folder.)");
                    }
                }

                string outputTalkParUnpack = Path.Combine(outputDir, "data", "talk.par.unpack");
                if (Directory.Exists(outputTalkParUnpack))
                {
                    if (File.Exists("ParTool.exe"))
                    {
                        string originalTalkPar = Path.Combine(folderPath, "data", "talk.par");
                        string finalTalkPar = Path.Combine(outputDir, "data", "talk.par");
                        PrintHeader("\n[*] [ParTool] Injecting modified files into talk.par...");
                        PrintFilesBeingAdded(outputTalkParUnpack);
                        Stopwatch ptTimer = Stopwatch.StartNew();
                        if (RunProcess("ParTool.exe", $"add \"{originalTalkPar}\" \"{outputTalkParUnpack}\" \"{finalTalkPar}\" -c 1"))
                        {
                            ptTimer.Stop();
                            PrintSuccess($"  [OK] Updated talk.par created in output\\data. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
                            PrintStep($"  -> Cleaning up temporary talk.par.unpack folder...");
                            try { Directory.Delete(outputTalkParUnpack, true); }
                            catch (Exception ex) { PrintWarning($"  [!] Warning: Could not delete temporary talk.par.unpack folder: {ex.Message}"); }
                        }
                    }
                    else
                    {
                        PrintWarning("\n  [!] Warning: ParTool.exe not found. Cannot automatically repack talk.par. (Please place ParTool.exe in the tool folder.)");
                    }
                }

                // Repack auth .par files
                string outputAuthDirPath = Path.Combine(outputDir, "data", "auth");
                if (Directory.Exists(outputAuthDirPath))
                {
                    foreach (string outputAuthUnpack in Directory.GetDirectories(outputAuthDirPath, "*.par.unpack"))
                    {
                        if (File.Exists("ParTool.exe"))
                        {
                            string fileName = Path.GetFileName(outputAuthUnpack).Replace(".unpack", "");
                            string originalAuthPar = Path.Combine(folderPath, "data", "auth", fileName);
                            string finalPar = Path.Combine(outputDir, "data", "auth", fileName);
                            PrintHeader($"\n[*] [ParTool] Injecting modified files into {fileName}...");
                            PrintFilesBeingAdded(outputAuthUnpack);
                            Stopwatch ptTimer = Stopwatch.StartNew();
                            if (RunProcess("ParTool.exe", $"add \"{originalAuthPar}\" \"{outputAuthUnpack}\" \"{finalPar}\" -c 1"))
                            {
                                ptTimer.Stop();
                                PrintSuccess($"  [OK] Updated {fileName} created in output\\data\\auth. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
                                PrintStep($"  -> Cleaning up temporary {fileName}.unpack folder...");
                                try { Directory.Delete(outputAuthUnpack, true); }
                                catch (Exception ex) { PrintWarning($"  [!] Warning: Could not delete temporary folder: {ex.Message}"); }
                            }
                        }
                        else
                        {
                            PrintWarning($"\n  [!] Warning: ParTool.exe not found. Cannot automatically repack {Path.GetFileName(outputAuthUnpack).Replace(".unpack", "")}. (Please place ParTool.exe in the tool folder.)");
                        }
                    }
                }

                // Repack dynamically specified .par files from folder_filters
                foreach (var kvp in folderFilters)
                {
                    string folderName = kvp.Key;
                    foreach (var fileName in kvp.Value)
                    {
                        if (fileName.EndsWith(".par", StringComparison.OrdinalIgnoreCase))
                        {
                            string outputParUnpack = Path.Combine(outputDir, "data", folderName, fileName + ".unpack");
                            if (Directory.Exists(outputParUnpack))
                            {
                                if (File.Exists("ParTool.exe"))
                                {
                                    string originalPar = Path.Combine(folderPath, "data", folderName, fileName);
                                    string finalPar = Path.Combine(outputDir, "data", folderName, fileName);
                                    PrintHeader($"\n[*] [ParTool] Injecting modified files into {fileName}...");
                                    PrintFilesBeingAdded(outputParUnpack);
                                    Stopwatch ptTimer = Stopwatch.StartNew();
                                    if (RunProcess("ParTool.exe", $"add \"{originalPar}\" \"{outputParUnpack}\" \"{finalPar}\" -c 1"))
                                    {
                                        ptTimer.Stop();
                                        PrintSuccess($"  [OK] Updated {fileName} created in output\\data\\{folderName}. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
                                        PrintStep($"  -> Cleaning up temporary {fileName}.unpack folder...");
                                        try { Directory.Delete(outputParUnpack, true); }
                                        catch (Exception ex) { PrintWarning($"  [!] Warning: Could not delete temporary folder: {ex.Message}"); }
                                    }
                                }
                                else
                                {
                                    PrintWarning($"\n  [!] Warning: ParTool.exe not found. Cannot automatically repack {fileName}. (Please place ParTool.exe in the tool folder.)");
                                }
                            }
                        }
                    }
                }

                PrintHeader("\n[*] Cleaning up output folder...");
                int removedCount = 0;
                var allOutputBins = Directory.GetFiles(outputDir, "*.bin", SearchOption.AllDirectories);
                foreach (var bin in allOutputBins)
                {
                    string binName = Path.GetFileName(bin);
                    if (!IsFileAllowed(binName, allowedBinFiles) && !IsFileAllowed(binName, allowedCmnFiles))
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
                    PrintSuccess($"  [OK] Cleaned up {removedCount} unmodified .bin files.");
                }

                if (cleanAll)
                {
                    PrintHeader("\n[*] Cleaning up all temporary folders...");
                    try { if (Directory.Exists(ogFileDir)) Directory.Delete(ogFileDir, true); } catch { }
                    try { if (Directory.Exists(ogJsonDir)) Directory.Delete(ogJsonDir, true); } catch { }
                    try { if (Directory.Exists(workspaceDir)) Directory.Delete(workspaceDir, true); } catch { }
                    try { if (Directory.Exists(convertedJsonDir)) Directory.Delete(convertedJsonDir, true); } catch { }
                    PrintSuccess("  [OK] Cleanup completed.");
                }
            }

            int cmnWarnings = 0;
            if (File.Exists(warningsFile))
            {
                cmnWarnings = File.ReadAllLines(warningsFile).Length;
            }
            int totalWarnings = WarningCount + cmnWarnings;

            stopwatch.Stop();
            PrintInfo($"\n==================================================================");
            PrintInfo($"   Batch Operation completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds!");
            PrintInfo($"==================================================================");
            if (choice == "1")
            {
                PrintInfo($"    - Files Extracted & Parsed: {targetCount}");
            }
            else if (choice == "2")
            {
                PrintInfo($"    - Texts Updated: {updatedTextsCount}");
                PrintInfo($"    - Textures Updated: {updatedTexturesCount}");
            }
            PrintInfo($"    - Warnings: {totalWarnings}" + (totalWarnings > 0 && File.Exists(warningsFile) ? " (see warnings.txt)" : ""));
            PrintInfo($"    - Errors: {ErrorCount}");
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
                        process.OutputDataReceived += (sender, e) => { if (e.Data != null && !QuietLogs) PrintStep($"    [{toolName}] {e.Data}"); };
                        process.ErrorDataReceived += (sender, e) => { if (e.Data != null && !QuietLogs) PrintError($"    [{toolName} ERR] {e.Data}"); };

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

        internal static void PrintError(string msg) { ErrorCount++; Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintWarning(string msg) { WarningCount++; Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintSuccess(string msg) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintInfo(string msg) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintStep(string msg) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine(msg); Console.ResetColor(); }
        internal static void PrintHeader(string msg) { Console.ForegroundColor = ConsoleColor.Magenta; Console.WriteLine(msg); Console.ResetColor(); }

        private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite = true)
        {
            if (!Directory.Exists(sourceDir)) return;
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(sourceDir.Length).TrimStart('\\', '/');
                string targetPath = Path.Combine(destinationDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (!overwrite && File.Exists(targetPath)) continue;
                if (File.Exists(targetPath)) File.SetAttributes(targetPath, File.GetAttributes(targetPath) & ~FileAttributes.ReadOnly);
                File.Copy(file, targetPath, true);
                File.SetAttributes(targetPath, File.GetAttributes(targetPath) & ~FileAttributes.ReadOnly);
            }
        }

        private static bool IsFileAllowed(string filename, HashSet<string> allowedFiles)
        {
            foreach (var f in allowedFiles)
            {
                if (f.Contains("*"))
                {
                    string start = f.Substring(0, f.IndexOf('*'));
                    string end = f.Substring(f.IndexOf('*') + 1);
                    if (filename.StartsWith(start, StringComparison.OrdinalIgnoreCase) && filename.EndsWith(end, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (f.Equals(filename, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void PrintFilesBeingAdded(string dir)
        {
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string relativePath = file.Substring(dir.Length).TrimStart('\\', '/');
                    PrintStep($"    -> Adding: {relativePath}");
                }
            }
        }
    }
}