using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace PoConverter
{
    public class Pipeline
    {
        public static int ErrorCount = 0;
        public static int WarningCount = 0;
        public static bool QuietLogs = false;
        private static readonly object consoleLock = new object();
        private const string ToolVersion = "0.4.1";

        // ------------
        // PIPELINE CONTEXT
        // ------------
        private class PipelineContext
        {
            public required string FolderPath { get; init; }
            public required string PatchDir { get; init; }
            public required string OgFileDir { get; init; }
            public required string OgJsonDir { get; init; }
            public required string WorkspaceDir { get; init; }
            public required string OutputDir { get; init; }
            public required string ConvertedJsonDir { get; init; }
            public required string WarningsFile { get; init; }
            public required string ErrorsFile { get; init; }
            public required string Language { get; init; }
            public required string DictFile { get; init; }
            public required string RearmpCmd { get; init; }
            public required bool SkipTextures { get; init; }
            public required bool CleanAll { get; init; }
            public required HashSet<string> AllowedBinFiles { get; init; }
            public required HashSet<string> AllowedTextureFiles { get; init; }
            public required HashSet<string> AllowedCmnFiles { get; init; }
            public required Dictionary<string, List<string>> FolderFilters { get; init; }
            public JObject? CachedDict { get; init; }
            public string? CustomDbPath { get; init; }
            public required bool SplitSoundAuth { get; init; }
            public required bool ExtractSystemStrings { get; init; }
            public required bool DebugTextVanillaTexture { get; init; }
            public required bool AutoYes { get; init; }
        }

        // ------------
        // PAR FILE DESCRIPTOR
        // ------------
        private class ParFileInfo
        {
            public required string ParPath { get; init; }
            public required string UnpackDir { get; init; }
            public bool Recursive { get; init; }
            public string SuccessLocation { get; init; } = "";
        }

        // ------------
        // MAIN EXECUTION
        // ------------
        public static void Run(string[] args)
        {
            Console.CancelKeyPress += (sender, e) =>
            {
                PrintWarning("\n[!] Ctrl+C detected. Terminating external processes...");
                KillProcesses("reARMP");
                KillProcesses("ParTool");
            };

            ErrorCount = 0;
            WarningCount = 0;
            QuietLogs = false;

            CheckExternalTools();
            AutoDownloadConfigIfNeeded();

            ParseArguments(args, out string? folderPath, out string? choice, out bool skipTextures, out string language, out bool cleanAll, out bool autoYes, out string dictFile, out string? customDbPath, out bool splitSoundAuth, out bool extractSystemStrings, out bool debugTextVanillaTexture);

            AutoDownloadDictionaryIfNeeded(dictFile);

            PrintHeader("  +------------------------------------------------------------+");
            PrintHeader("  |       YAKUZA 6 LOCALIZATION TOOL - PIPELINE (v0.4.1)       |");
            PrintHeader("  +------------------------------------------------------------+");
            PrintInfo("  +------------------------------------------------------------+");
            PrintInfo("  | Credits & External Tools:                                  |");
            PrintInfo("  |   reARMP  - Parse Yakuza .bin <=> .json  (by Ret-HZ)       |");
            PrintInfo("  |   ParTool - Extract/compress .par archives  (by Kaplas80)  |");
            PrintInfo("  +------------------------------------------------------------+\n");

            if (string.IsNullOrEmpty(customDbPath))
            {
                if (string.IsNullOrEmpty(folderPath))
                {
                    PrintInfo("  Enter the path of the 'Yakuza 6 - The Song of Life' folder:");
                    Console.Write("  -> ");
                    folderPath = Console.ReadLine()?.Trim().Trim('"');
                }

                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    PrintError($"Error: '{folderPath}' is not a valid folder. (Please provide the full path to the 'Yakuza 6 - The Song of Life' installation directory.)");
                    return;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(folderPath))
                {
                    folderPath = "";
                }
            }

            if (debugTextVanillaTexture)
            {
                choice = "1";
            }

            if (string.IsNullOrEmpty(choice))
            {
                PrintInfo("\n  +------------------------------------------------------------+");
                PrintInfo("  |                SELECT OPERATION TO PERFORM                 |");
                PrintInfo("  +------------------------------------------------------------+");
                PrintInfo("  |  [1] EXTRACTION (Extract Bin/Cmn/Textures to PO)           |");
                PrintInfo("  |  [2] RECREATION (Rebuild Bin/Cmn/Textures from PO)         |");
                PrintInfo("  +------------------------------------------------------------+");
                Console.Write("  Your choice (1 or 2) -> ");
                choice = Console.ReadLine()?.Trim();
            }

            if (choice != "1" && choice != "2")
            {
                PrintError("Invalid choice.");
                return;
            }

            if (!LoadDictionary(dictFile, out HashSet<string> allowedBinFiles, out HashSet<string> allowedTextureFiles, out HashSet<string> allowedCmnFiles, out Dictionary<string, List<string>> folderFilters, out JObject? cachedDict))
            {
                return;
            }

            InitializeDirectories(choice, autoYes, out string patchDir, out string ogFileDir, out string ogJsonDir, out string workspaceDir, out string outputDir, out string convertedJsonDir, out string warningsFile, out string errorsFile);

            var ctx = new PipelineContext
            {
                FolderPath = folderPath,
                PatchDir = patchDir,
                OgFileDir = ogFileDir,
                OgJsonDir = ogJsonDir,
                WorkspaceDir = workspaceDir,
                OutputDir = outputDir,
                ConvertedJsonDir = convertedJsonDir,
                WarningsFile = warningsFile,
                ErrorsFile = errorsFile,
                Language = language,
                DictFile = dictFile,
                RearmpCmd = "reARMP.exe",
                SkipTextures = skipTextures,
                CleanAll = cleanAll,
                AllowedBinFiles = allowedBinFiles,
                AllowedTextureFiles = allowedTextureFiles,
                AllowedCmnFiles = allowedCmnFiles,
                FolderFilters = folderFilters,
                CachedDict = cachedDict,
                CustomDbPath = customDbPath,
                SplitSoundAuth = splitSoundAuth,
                ExtractSystemStrings = extractSystemStrings,
                DebugTextVanillaTexture = debugTextVanillaTexture,
                AutoYes = autoYes,
            };

            Stopwatch stopwatch = Stopwatch.StartNew();
            int targetCount = 0;
            int updatedTextsCount = 0;
            int updatedTexturesCount = 0;


            if (ctx.DebugTextVanillaTexture)
            {
                RunDebugTextVanillaTextureWorkflow(ctx);
                return;
            }
            else if (choice == "1")
            {
                targetCount = RunExtraction(ctx);
            }
            else if (choice == "2")
            {
                (updatedTextsCount, updatedTexturesCount) = RunRecreation(ctx);
            }

            int cmnWarnings = 0;
            if (File.Exists(warningsFile))
            {
                cmnWarnings = File.ReadLines(warningsFile).Count(line => line.StartsWith("[WARNING]"));
            }
            int totalWarnings = WarningCount + cmnWarnings;

            stopwatch.Stop();
            PrintHeader($"\n  +------------------------------------------------------------+");
            PrintSuccess($"  |        Batch Operation completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds!       |");
            PrintHeader($"  +------------------------------------------------------------+");
            if (choice == "1")
            {
                PrintInfo($"  |  * Files Extracted & Parsed  : {targetCount,-28} |");
            }
            else if (choice == "2")
            {
                PrintInfo($"  |  * Texts Updated             : {updatedTextsCount,-28} |");
                PrintInfo($"  |  * Textures Updated          : {updatedTexturesCount,-28} |");
            }

            if (totalWarnings > 0)
            {
                string warnStr = $"{totalWarnings} (see warnings.txt)";
                PrintWarning($"  |  * Warnings                  : {warnStr,-28} |");
            }
            else
            {
                PrintInfo($"  |  * Warnings                  : 0                            |");
            }

            if (ErrorCount > 0)
            {
                string errStr = $"{ErrorCount} (see errors.txt)";
                PrintError($"  |  * Errors                    : {errStr,-28} |");
            }
            else
            {
                PrintInfo($"  |  * Errors                    : 0                            |");
            }
            PrintHeader($"  +------------------------------------------------------------+");

            if (!autoYes)
            {
                Console.WriteLine("\n  Press ENTER to close...");
                Console.ReadLine();
            }
        }

        private static void RunDebugTextVanillaTextureWorkflow(PipelineContext ctx)
        {
            PrintHeader("\n  +------------------------------------------------------------+");
            PrintHeader("  |      [PROCESS] DEBUG TEXT & VANILLA TEXTURE RECOMPILE      |");
            PrintHeader("  +------------------------------------------------------------+");

            Stopwatch stopwatch = Stopwatch.StartNew();

            // Step 1: Extract patched texts (SkipTextures = true)
            PrintHeader("\n[+] STEP 1: EXTRACTING TRANSLATED TEXTS (PO) FROM PATCHED GAME");
            var phaseACtx = new PipelineContext
            {
                FolderPath = ctx.FolderPath,
                PatchDir = ctx.PatchDir,
                OgFileDir = ctx.OgFileDir,
                OgJsonDir = ctx.OgJsonDir,
                WorkspaceDir = ctx.WorkspaceDir,
                OutputDir = ctx.OutputDir,
                ConvertedJsonDir = ctx.ConvertedJsonDir,
                WarningsFile = ctx.WarningsFile,
                ErrorsFile = ctx.ErrorsFile,
                Language = ctx.Language,
                DictFile = ctx.DictFile,
                RearmpCmd = ctx.RearmpCmd,
                SkipTextures = true, // We only want texts
                CleanAll = false,
                AllowedBinFiles = ctx.AllowedBinFiles,
                AllowedTextureFiles = ctx.AllowedTextureFiles,
                AllowedCmnFiles = ctx.AllowedCmnFiles,
                FolderFilters = ctx.FolderFilters,
                CachedDict = ctx.CachedDict,
                CustomDbPath = ctx.CustomDbPath,
                SplitSoundAuth = ctx.SplitSoundAuth,
                ExtractSystemStrings = ctx.ExtractSystemStrings,
                DebugTextVanillaTexture = true,
                AutoYes = ctx.AutoYes
            };

            int phaseATargets = RunExtraction(phaseACtx);
            PrintSuccess($"\n  [OK] Step 1 complete. Extracted {phaseATargets} PO files to workspace.");

            // Step 2: Trigger Steam validation to restore vanilla files
            PrintHeader("\n[+] STEP 2: RESTORING GAME FILES TO VANILLA VIA STEAM");
            PrintStep("  -> Triggering Steam verification for Yakuza 6...");
            try
            {
                Process.Start(new ProcessStartInfo("steam://gameproperties/1388590") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                PrintError($"  [!] Failed to start Steam properties window automatically: {ex.Message}");
                PrintInfo("  Please open Steam, go to Yakuza 6 Properties manually.");
            }
            PrintWarning("\n  [IMPORTANT] The properties window for Yakuza 6 should open in Steam.");
            PrintWarning("  Please go to the 'Installed Files' tab and click 'Verify integrity of game files'.");
            PrintWarning("  Wait for Steam to complete the validation and restore files to vanilla.");
            PrintInfo("  Once the validation is 100% complete, press ENTER to continue...");
            Console.ReadLine();

            // Step 3: Re-extract vanilla files (SkipTextures = false)
            PrintHeader("\n[+] STEP 3: EXTRACTING VANILLA TEXTURES AND FILES");
            // Prepare directories for Step 3, skipping cleanup of workspace to keep PO files!
            InitializeDirectories("1", true, out _, out _, out _, out _, out _, out _, out _, out _, skipCleanup: true);

            var phaseCCtx = new PipelineContext
            {
                FolderPath = ctx.FolderPath,
                PatchDir = ctx.PatchDir,
                OgFileDir = ctx.OgFileDir,
                OgJsonDir = ctx.OgJsonDir,
                WorkspaceDir = ctx.WorkspaceDir,
                OutputDir = ctx.OutputDir,
                ConvertedJsonDir = ctx.ConvertedJsonDir,
                WarningsFile = ctx.WarningsFile,
                ErrorsFile = ctx.ErrorsFile,
                Language = ctx.Language,
                DictFile = ctx.DictFile,
                RearmpCmd = ctx.RearmpCmd,
                SkipTextures = false, // We want vanilla textures!
                CleanAll = false,
                AllowedBinFiles = ctx.AllowedBinFiles,
                AllowedTextureFiles = ctx.AllowedTextureFiles,
                AllowedCmnFiles = ctx.AllowedCmnFiles,
                FolderFilters = ctx.FolderFilters,
                CachedDict = ctx.CachedDict,
                CustomDbPath = ctx.CustomDbPath,
                SplitSoundAuth = ctx.SplitSoundAuth,
                ExtractSystemStrings = ctx.ExtractSystemStrings,
                DebugTextVanillaTexture = true, // This will prevent ProcessExtractionFile from overwriting existing POs
                AutoYes = ctx.AutoYes
            };

            int phaseCTargets = RunExtraction(phaseCCtx);
            PrintSuccess($"\n  [OK] Step 3 complete. Re-extracted files and vanilla textures.");

            // Step 4: Recreate patch using the POs and vanilla textures
            PrintHeader("\n[+] STEP 4: RECREATING THE PATCH (TEXT TRANSLATED + VANILLA TEXTURE)");
            // Initialize directories for recreation (choice = "2"), skipping cleanup of output just in case
            InitializeDirectories("2", true, out _, out _, out _, out _, out _, out _, out _, out _, skipCleanup: true);

            var phaseDCtx = new PipelineContext
            {
                FolderPath = ctx.FolderPath,
                PatchDir = ctx.PatchDir,
                OgFileDir = ctx.OgFileDir,
                OgJsonDir = ctx.OgJsonDir,
                WorkspaceDir = ctx.WorkspaceDir,
                OutputDir = ctx.OutputDir,
                ConvertedJsonDir = ctx.ConvertedJsonDir,
                WarningsFile = ctx.WarningsFile,
                ErrorsFile = ctx.ErrorsFile,
                Language = ctx.Language,
                DictFile = ctx.DictFile,
                RearmpCmd = ctx.RearmpCmd,
                SkipTextures = false, // Process vanilla textures into output
                CleanAll = ctx.CleanAll,
                AllowedBinFiles = ctx.AllowedBinFiles,
                AllowedTextureFiles = ctx.AllowedTextureFiles,
                AllowedCmnFiles = ctx.AllowedCmnFiles,
                FolderFilters = ctx.FolderFilters,
                CachedDict = ctx.CachedDict,
                CustomDbPath = ctx.CustomDbPath,
                SplitSoundAuth = ctx.SplitSoundAuth,
                ExtractSystemStrings = ctx.ExtractSystemStrings,
                DebugTextVanillaTexture = true,
                AutoYes = ctx.AutoYes
            };

            var (recreatedTexts, recreatedTextures) = RunRecreation(phaseDCtx);
            stopwatch.Stop();

            PrintHeader($"\n  +------------------------------------------------------------+");
            PrintSuccess($"  |  [OK] Text-Only Recompile Process Completed Successfully!  |");
            PrintHeader($"  +------------------------------------------------------------+");
            PrintInfo($"  |  * Completed in              : {stopwatch.Elapsed.TotalSeconds:F2} seconds      |");
            PrintInfo($"  |  * Texts Recompiled          : {recreatedTexts,-28} |");
            PrintInfo($"  |  * Textures Repacked         : {recreatedTextures,-28} |");
            PrintInfo($"  |  * Final Patch Location      : {phaseDCtx.OutputDir,-28} |");
            PrintHeader($"  +------------------------------------------------------------+");

            if (!ctx.AutoYes)
            {
                Console.WriteLine("\n  Press ENTER to close...");
                Console.ReadLine();
            }
        }

        // ------------
        // EXTRACTION PHASE
        // ------------
        private static int RunExtraction(PipelineContext ctx)
        {
            if (!string.IsNullOrEmpty(ctx.CustomDbPath))
            {
                return RunCustomDbExtraction(ctx);
            }

            var parFiles = GetParFilesToUnpack(ctx);
            foreach (var pf in parFiles)
            {
                UnpackParFile(pf.ParPath, pf.UnpackDir, pf.Recursive);
            }

            var files = GatherFilesToProcess(ctx);

            if (files.Count == 0)
            {
                PrintWarning("No files found in the folder.");
                return 0;
            }

            PrintInfo($"\n[*] Found {files.Count} total files to evaluate.");

            HashSet<string> eVariantFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (file.Contains("\\e\\", StringComparison.OrdinalIgnoreCase) || file.Contains("/e/", StringComparison.OrdinalIgnoreCase))
                {
                    eVariantFilenames.Add(Path.GetFileName(file));
                }
            }

            int targetCount = 0;
            foreach (var binPath in files)
            {
                if (ProcessExtractionFile(binPath, ctx, eVariantFilenames))
                {
                    targetCount++;
                }
            }
            PrintInfo($"\n[*] Finished extraction! Processed {targetCount} targeted files.");
            return targetCount;
        }

        private static int RunCustomDbExtraction(PipelineContext ctx)
        {
            string customDbPath = ctx.CustomDbPath!;
            if (!Directory.Exists(customDbPath))
            {
                PrintError($"[!] Error: custom-db path '{customDbPath}' does not exist.");
                return 0;
            }

            var binFiles = Directory.GetFiles(customDbPath, "*.bin", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".bin.json"))
                .ToList();

            if (binFiles.Count == 0)
            {
                PrintWarning($"No .bin files found in custom-db path '{customDbPath}'.");
                return 0;
            }

            PrintInfo($"\n[*] Custom-DB mode: found {binFiles.Count} .bin files in '{customDbPath}'.");

            int targetCount = 0;
            int current = 0;
            foreach (var binPath in binFiles)
            {
                current++;
                string filename = Path.GetFileName(binPath);
                string relPath = binPath.Substring(customDbPath.Length).TrimStart('\\', '/');
                string relDir = Path.GetDirectoryName(relPath) ?? "";

                PrintHeader($"\n[*] Processing [{current}/{binFiles.Count}]: {relPath}");

                string currentOgFileDir = Path.Combine(ctx.OgFileDir, relDir);
                Directory.CreateDirectory(currentOgFileDir);
                string copiedBinPath = Path.Combine(currentOgFileDir, filename);
                CopyFileUnrestricted(binPath, copiedBinPath);

                string currentOgJsonDir = Path.Combine(ctx.OgJsonDir, relDir);
                string currentWorkspaceDir = Path.Combine(ctx.WorkspaceDir, relDir);

                PrintStep("  -> [Pipeline] Executing reARMP for JSON extraction...");
                DeleteFileIfExists(Path.Combine(Environment.CurrentDirectory, filename + ".json"));
                DeleteFileIfExists(copiedBinPath + ".json");
                if (RunProcess(ctx.RearmpCmd, $"\"{copiedBinPath}\""))
                {
                    string generatedJsonPathInPlace = copiedBinPath + ".json";
                    string targetJsonPath = Path.Combine(currentOgJsonDir, filename + ".json");
                    string? actualGeneratedJson = FindGeneratedFile(filename + ".json", generatedJsonPathInPlace);

                    if (MoveGeneratedFile(actualGeneratedJson, targetJsonPath))
                    {
                        string poPath = Path.Combine(currentWorkspaceDir, Path.GetFileNameWithoutExtension(filename) + ".po");
                        PrintStep("  -> [PoConverter] Generating PO file internally...");
                        try
                        {
                            int count = PoConverter.JsonToPo(targetJsonPath, poPath, ctx.DictFile, ctx.Language, ctx.CachedDict);
                            if (count > 0)
                            {
                                PrintSuccess($"  [OK] Created PO file: {Path.GetFileName(poPath)} ({count} strings)");
                                targetCount++;
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
                        PrintError($"  [!] JSON file not generated for {filename}.");
                    }
                }
            }

            PrintInfo($"\n[*] Custom-DB extraction finished! Processed {targetCount} files.");
            return targetCount;
        }

        // ------------
        // RECREATION PHASE
        // ------------
        private static (int updatedTexts, int updatedTextures) RunRecreation(PipelineContext ctx)
        {
            if (!string.IsNullOrEmpty(ctx.CustomDbPath))
            {
                return RunCustomDbRecreation(ctx);
            }

            var poFiles = Directory.GetFiles(ctx.WorkspaceDir, "*.po", SearchOption.AllDirectories)
                .Where(f => IsFileAllowed(Path.GetFileNameWithoutExtension(f) + ".bin", ctx.AllowedBinFiles) || IsFileAllowed(Path.GetFileNameWithoutExtension(f) + ".bin", ctx.AllowedCmnFiles))
                .ToList();
            var texFiles = new List<string>();

            if (!ctx.SkipTextures)
            {
                texFiles = Directory.GetFiles(ctx.WorkspaceDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsFileAllowed(Path.GetFileName(f), ctx.AllowedTextureFiles))
                    .ToList();
            }

            if (poFiles.Count == 0 && texFiles.Count == 0)
            {
                PrintWarning("No .po or texture files found in the workspace folder.");
                return (0, 0);
            }

            PrintHeader("\n[*] Copying original files from 'og file' to 'output'...");
            CopyDirectory(ctx.OgFileDir, ctx.OutputDir);

            PrintInfo($"\n[*] Found {poFiles.Count} .po files and {texFiles.Count} textures to process.");

            int updatedTextsCount = 0;
            int currentPo = 0;
            foreach (var poPath in poFiles)
            {
                currentPo++;
                string poFilename = Path.GetFileName(poPath);
                PrintHeader($"\n[*] Processing PO File [{currentPo}/{poFiles.Count}]: {poFilename}");

                if (ProcessRecreationPoFile(poPath, ctx))
                {
                    updatedTextsCount++;
                }
            }

            int updatedTexturesCount = 0;
            if (!ctx.SkipTextures)
            {
                int currentTex = 0;
                foreach (var texPath in texFiles)
                {
                    currentTex++;
                    string texFilename = Path.GetFileName(texPath);
                    PrintHeader($"\n[*] Processing Texture [{currentTex}/{texFiles.Count}]: {texFilename}");

                    if (ProcessRecreationTextureFile(texPath, ctx.WorkspaceDir, ctx.OutputDir))
                    {
                        updatedTexturesCount++;
                    }
                }
            }


            var parFiles = GetParFilesToRepack(ctx);
            foreach (var pf in parFiles)
            {
                PackParFile(pf.ParPath, pf.UnpackDir, Path.Combine(ctx.OutputDir, GetRelativeToData(pf.ParPath)), pf.SuccessLocation);
            }

            PrintHeader("\n[*] Cleaning up output folder...");
            int removedCount = 0;
            var allOutputBins = Directory.GetFiles(ctx.OutputDir, "*.bin", SearchOption.AllDirectories);
            foreach (var bin in allOutputBins)
            {
                string binName = Path.GetFileName(bin);
                if (!IsFileAllowed(binName, ctx.AllowedBinFiles) && !IsFileAllowed(binName, ctx.AllowedCmnFiles))
                {
                    try
                    {
                        File.SetAttributes(bin, File.GetAttributes(bin) & ~FileAttributes.ReadOnly);
                        File.Delete(bin);
                        removedCount++;
                    }
                    catch (Exception ex)
                    {
                        PrintStep($"  [!] Could not delete {binName}: {ex.Message}");
                    }
                }
            }
            if (removedCount > 0)
            {
                PrintSuccess($"  [OK] Cleaned up {removedCount} unmodified .bin files.");
            }

            if (ctx.CleanAll)
            {
                PrintHeader("\n[*] Cleaning up all temporary folders...");
                SafeDeleteDirectory(ctx.OgFileDir);
                SafeDeleteDirectory(ctx.OgJsonDir);
                SafeDeleteDirectory(ctx.WorkspaceDir);
                SafeDeleteDirectory(ctx.ConvertedJsonDir);
                PrintSuccess("  [OK] Cleanup completed.");
            }

            return (updatedTextsCount, updatedTexturesCount);
        }

        private static (int, int) RunCustomDbRecreation(PipelineContext ctx)
        {
            var poFiles = Directory.GetFiles(ctx.WorkspaceDir, "*.po", SearchOption.AllDirectories).ToList();

            if (poFiles.Count == 0)
            {
                PrintWarning("No .po files found in the workspace folder.");
                return (0, 0);
            }

            PrintHeader("\n[*] Copying original files from 'og file' to 'output'...");
            CopyDirectory(ctx.OgFileDir, ctx.OutputDir);

            PrintInfo($"\n[*] Custom-DB mode: found {poFiles.Count} .po files to process.");

            int updatedCount = 0;
            int current = 0;
            foreach (var poPath in poFiles)
            {
                current++;
                string poFilename = Path.GetFileName(poPath);
                string relPath = GetRelativeWorkspacePath(poPath, ctx.WorkspaceDir);

                PrintHeader($"\n[*] Processing PO File [{current}/{poFiles.Count}]: {poFilename}");

                string currentOgJsonDir = Path.Combine(ctx.OgJsonDir, relPath);
                string currentOutputDir = Path.Combine(ctx.OutputDir, relPath);
                Directory.CreateDirectory(currentOutputDir);

                string baseName = Path.GetFileNameWithoutExtension(poFilename);
                string jsonFilename = baseName + ".bin.json";
                string ogJsonPath = Path.Combine(currentOgJsonDir, jsonFilename);
                string outputJsonPath = Path.Combine(currentOutputDir, jsonFilename);

                if (!File.Exists(ogJsonPath))
                {
                    PrintError($"  [!] Original JSON file missing: {jsonFilename}. (Try running Phase 1 Extraction again.)");
                    continue;
                }

                PrintStep($"  -> [PoConverter] Injecting translation from {poFilename} into JSON...");
                try
                {
                    PoConverter.PoToJson(poPath, outputJsonPath, ogJsonPath, ctx.DictFile);
                }
                catch (Exception ex)
                {
                    PrintError($"  [!] Error during PO injection for {poFilename}: {ex.Message}");
                    continue;
                }

                PrintStep("  -> [Pipeline] Executing reARMP to recreate .bin...");
                string targetBinPath = Path.Combine(currentOutputDir, baseName + ".bin");
                DeleteFileIfExists(Path.Combine(currentOutputDir, jsonFilename + ".bin"));
                DeleteFileIfExists(Path.Combine(Environment.CurrentDirectory, jsonFilename + ".bin"));

                if (RunProcess(ctx.RearmpCmd, $"\"{outputJsonPath}\""))
                {
                    string generatedBinPathInPlace = Path.Combine(currentOutputDir, jsonFilename + ".bin");
                    string? actualGeneratedBin = FindGeneratedFile(jsonFilename + ".bin", generatedBinPathInPlace);

                    if (MoveGeneratedFile(actualGeneratedBin, targetBinPath))
                    {
                        PrintSuccess($"  [OK] BIN file updated successfully.");

                        if (File.Exists(outputJsonPath))
                        {
                            string targetConvertedJsonDir = Path.Combine(ctx.ConvertedJsonDir, relPath);
                            Directory.CreateDirectory(targetConvertedJsonDir);
                            string targetConvertedJsonPath = Path.Combine(targetConvertedJsonDir, jsonFilename);
                            if (File.Exists(targetConvertedJsonPath)) File.Delete(targetConvertedJsonPath);
                            File.Move(outputJsonPath, targetConvertedJsonPath);
                        }

                        updatedCount++;
                    }
                    else
                    {
                        PrintError($"  [!] Error: {baseName}.bin was not generated by reARMP.");
                    }
                }
            }

            if (ctx.CleanAll)
            {
                PrintHeader("\n[*] Cleaning up all temporary folders...");
                SafeDeleteDirectory(ctx.OgFileDir);
                SafeDeleteDirectory(ctx.OgJsonDir);
                SafeDeleteDirectory(ctx.WorkspaceDir);
                SafeDeleteDirectory(ctx.ConvertedJsonDir);
                PrintSuccess("  [OK] Cleanup completed.");
            }

            PrintInfo($"\n[*] Custom-DB recreation finished! Updated {updatedCount} files.");
            return (updatedCount, 0);
        }

        // ------------
        // PAR FILE MANAGEMENT
        // ------------
        private static List<ParFileInfo> GetParFilesToUnpack(PipelineContext ctx)
        {
            var parFiles = new List<ParFileInfo>();

            if (!ctx.SkipTextures)
            {
                parFiles.Add(new ParFileInfo
                {
                    ParPath = Path.Combine(ctx.FolderPath, "data", "ui.par"),
                    UnpackDir = Path.Combine(ctx.FolderPath, "data", "ui.par.unpack"),
                    Recursive = false
                });
            }

            parFiles.Add(new ParFileInfo
            {
                ParPath = Path.Combine(ctx.FolderPath, "data", "talk.par"),
                UnpackDir = Path.Combine(ctx.FolderPath, "data", "talk.par.unpack"),
                Recursive = true
            });

            // auth/e/*.par
            string authDirPath = Path.Combine(ctx.FolderPath, "data", "auth", "e");
            if (Directory.Exists(authDirPath))
            {
                foreach (string authPar in Directory.GetFiles(authDirPath, "*.par"))
                {
                    parFiles.Add(new ParFileInfo
                    {
                        ParPath = authPar,
                        UnpackDir = authPar + ".unpack",
                        Recursive = true
                    });
                }
            }

            // hact/e/*.par
            string hactDirPath = Path.Combine(ctx.FolderPath, "data", "hact", "e");
            if (Directory.Exists(hactDirPath))
            {
                foreach (string hactPar in Directory.GetFiles(hactDirPath, "*.par"))
                {
                    parFiles.Add(new ParFileInfo
                    {
                        ParPath = hactPar,
                        UnpackDir = hactPar + ".unpack",
                        Recursive = true
                    });
                }
            }

            // folder_filters entries
            foreach (var kvp in ctx.FolderFilters)
            {
                foreach (var fileName in kvp.Value)
                {
                    if (fileName.EndsWith(".par", StringComparison.OrdinalIgnoreCase))
                    {
                        parFiles.Add(new ParFileInfo
                        {
                            ParPath = Path.Combine(ctx.FolderPath, "data", kvp.Key, fileName),
                            UnpackDir = Path.Combine(ctx.FolderPath, "data", kvp.Key, fileName + ".unpack"),
                            Recursive = true
                        });
                    }
                }
            }

            return parFiles;
        }

        private static List<ParFileInfo> GetParFilesToRepack(PipelineContext ctx)
        {
            var parFiles = new List<ParFileInfo>();

            // ui.par
            string outputUiParUnpack = Path.Combine(ctx.OutputDir, "data", "ui.par.unpack");
            if (!ctx.SkipTextures && Directory.Exists(outputUiParUnpack))
            {
                parFiles.Add(new ParFileInfo
                {
                    ParPath = Path.Combine(ctx.FolderPath, "data", "ui.par"),
                    UnpackDir = outputUiParUnpack,
                    SuccessLocation = Path.Combine("output", "data")
                });
            }

            // talk.par
            string outputTalkParUnpack = Path.Combine(ctx.OutputDir, "data", "talk.par.unpack");
            if (Directory.Exists(outputTalkParUnpack))
            {
                parFiles.Add(new ParFileInfo
                {
                    ParPath = Path.Combine(ctx.FolderPath, "data", "talk.par"),
                    UnpackDir = outputTalkParUnpack,
                    SuccessLocation = Path.Combine("output", "data")
                });
            }

            // auth/e/*.par
            string outputAuthDirPath = Path.Combine(ctx.OutputDir, "data", "auth", "e");
            if (Directory.Exists(outputAuthDirPath))
            {
                foreach (string outputAuthUnpack in Directory.GetDirectories(outputAuthDirPath, "*.par.unpack"))
                {
                    string fileName = Path.GetFileName(outputAuthUnpack).Replace(".unpack", "");
                    parFiles.Add(new ParFileInfo
                    {
                        ParPath = Path.Combine(ctx.FolderPath, "data", "auth", "e", fileName),
                        UnpackDir = outputAuthUnpack,
                        SuccessLocation = Path.Combine("output", "data", "auth", "e")
                    });
                }
            }

            // hact/e/*.par
            string outputHactDirPath = Path.Combine(ctx.OutputDir, "data", "hact", "e");
            if (Directory.Exists(outputHactDirPath))
            {
                foreach (string outputHactUnpack in Directory.GetDirectories(outputHactDirPath, "*.par.unpack"))
                {
                    string fileName = Path.GetFileName(outputHactUnpack).Replace(".unpack", "");
                    parFiles.Add(new ParFileInfo
                    {
                        ParPath = Path.Combine(ctx.FolderPath, "data", "hact", "e", fileName),
                        UnpackDir = outputHactUnpack,
                        SuccessLocation = Path.Combine("output", "data", "hact", "e")
                    });
                }
            }

            // folder_filters entries
            foreach (var kvp in ctx.FolderFilters)
            {
                foreach (var fileName in kvp.Value)
                {
                    if (fileName.EndsWith(".par", StringComparison.OrdinalIgnoreCase))
                    {
                        string outputParUnpack = Path.Combine(ctx.OutputDir, "data", kvp.Key, fileName + ".unpack");
                        if (Directory.Exists(outputParUnpack))
                        {
                            parFiles.Add(new ParFileInfo
                            {
                                ParPath = Path.Combine(ctx.FolderPath, "data", kvp.Key, fileName),
                                UnpackDir = outputParUnpack,
                                SuccessLocation = Path.Combine("output", "data", kvp.Key)
                            });
                        }
                    }
                }
            }

            return parFiles;
        }

        /// <summary>
        /// Gets the relative path from data root for a given par file path.
        /// E.g. "C:\game\data\auth\somefile.par" -> "data\auth\somefile.par"
        /// </summary>
        private static string GetRelativeToData(string parPath)
        {
            string normalized = parPath.Replace('/', '\\');
            int dataIdx = normalized.IndexOf("\\data\\", StringComparison.OrdinalIgnoreCase);
            if (dataIdx != -1) return normalized.Substring(dataIdx + 1);
            return Path.GetFileName(parPath);
        }

        // ------------
        // INITIALIZATION & ARGS
        // ------------
        private static void InitializeDirectories(string choice, bool autoYes, out string patchDir, out string ogFileDir, out string ogJsonDir, out string workspaceDir, out string outputDir, out string convertedJsonDir, out string warningsFile, out string errorsFile, bool skipCleanup = false)
        {
            patchDir = Path.Combine(Environment.CurrentDirectory, "Yakuza 6 - Patch");
            ogFileDir = Path.Combine(patchDir, "og file");
            ogJsonDir = Path.Combine(patchDir, "og json");
            workspaceDir = Path.Combine(patchDir, "workspace");
            outputDir = Path.Combine(patchDir, "output");
            convertedJsonDir = Path.Combine(patchDir, "converted json");
            warningsFile = Path.Combine(patchDir, "warnings.txt");
            errorsFile = Path.Combine(patchDir, "errors.txt");

            if (choice == "1")
            {
                if (Directory.Exists(patchDir) && !skipCleanup)
                {
                    bool doClean = autoYes;
                    if (!doClean)
                    {
                        PrintWarning("\n  [WARN] The 'Yakuza 6 - Patch' folder already exists.");
                        Console.Write("  Do you want to CLEAR previous extraction files (workspace, og file, og json) before starting? (y/n) -> ");
                        string? cleanChoice = Console.ReadLine()?.Trim().ToLower();
                        doClean = (cleanChoice == "y" || cleanChoice == "yes");
                    }
                    if (doClean)
                    {
                        PrintStep("  -> Cleaning up previous extraction...");
                        SafeDeleteDirectory(ogFileDir);
                        SafeDeleteDirectory(ogJsonDir);
                        SafeDeleteDirectory(workspaceDir);
                        SafeDeleteFile(errorsFile);
                    }
                }
            }
            else if (choice == "2")
            {
                if (Directory.Exists(outputDir) && !skipCleanup)
                {
                    bool doClean = autoYes;
                    if (!doClean)
                    {
                        PrintWarning("\n  [WARN] The 'output' folder already exists.");
                        Console.Write("  Do you want to CLEAR it before starting to avoid leftovers? (y/n) -> ");
                        string? cleanChoice = Console.ReadLine()?.Trim().ToLower();
                        doClean = (cleanChoice == "y" || cleanChoice == "yes");
                    }
                    if (doClean)
                    {
                        PrintStep("  -> Cleaning up previous output...");
                        SafeDeleteDirectory(outputDir);
                        SafeDeleteDirectory(convertedJsonDir);
                        SafeDeleteFile(warningsFile);
                        SafeDeleteFile(errorsFile);
                    }
                }
            }

            Directory.CreateDirectory(patchDir);
            Directory.CreateDirectory(ogFileDir);
            Directory.CreateDirectory(ogJsonDir);
            Directory.CreateDirectory(workspaceDir);
            Directory.CreateDirectory(outputDir);
            if (choice == "2") Directory.CreateDirectory(convertedJsonDir);
        }

        private static void ParseArguments(string[] args, out string? folderPath, out string? choice, out bool skipTextures, out string language, out bool cleanAll, out bool autoYes, out string dictFile, out string? customDbPath, out bool splitSoundAuth, out bool extractSystemStrings, out bool debugTextVanillaTexture)
        {
            folderPath = null;
            choice = null;
            skipTextures = false;
            language = "it";
            cleanAll = false;
            autoYes = false;
            dictFile = "dictionary.json";
            customDbPath = null;
            splitSoundAuth = true;
            extractSystemStrings = true;
            debugTextVanillaTexture = false;

            string configPath = "config.json";
            if (File.Exists(configPath))
            {
                try
                {
                    string configText = File.ReadAllText(configPath);
                    JObject config = JObject.Parse(configText);

                    if (config["gamePath"] != null && !string.IsNullOrWhiteSpace(config["gamePath"]?.ToString())) folderPath = config["gamePath"]?.ToString();
                    if (config["language"] != null && !string.IsNullOrWhiteSpace(config["language"]?.ToString())) language = config["language"]?.ToString() ?? "it";
                    if (config["dictionaryFile"] != null && !string.IsNullOrWhiteSpace(config["dictionaryFile"]?.ToString())) dictFile = config["dictionaryFile"]?.ToString() ?? "dictionary.json";
                    if (config["custom-db"] != null && !string.IsNullOrWhiteSpace(config["custom-db"]?.ToString())) customDbPath = config["custom-db"]?.ToString();


                    skipTextures = ParseConfigBool(config, "skipTextures", skipTextures);
                    cleanAll = ParseConfigBool(config, "cleanAll", cleanAll);
                    autoYes = ParseConfigBool(config, "autoYes", autoYes);
                    QuietLogs = ParseConfigBool(config, "quietLogs", QuietLogs);
                    extractSystemStrings = ParseConfigBool(config, "extractSystemStrings", extractSystemStrings);
                    splitSoundAuth = ParseConfigBool(config, "splitSoundAuth", splitSoundAuth);
 
 
                    var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { "gamePath", "language", "dictionaryFile", "skipTextures", "cleanAll", "autoYes", "quietLogs", "custom-db", "extractSystemStrings", "splitSoundAuth" };
                    foreach (var prop in config.Properties())
                    {
                        if (!knownKeys.Contains(prop.Name))
                        {
                            PrintWarning($"\n[!] Warning: Unknown config key '{prop.Name}' in config.json (ignored).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    PrintWarning($"\n[!] Warning: Failed to parse config.json: {ex.Message}");
                }
            }

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
                        case "-ns":
                        case "--no-split":
                            splitSoundAuth = false;
                            break;
                        case "-d":
                        case "--dict":
                            if (i + 1 < args.Length) dictFile = args[++i].Trim('"');
                            break;
                        case "-dtvt":
                        case "--debug-text-vanilla-texture":
                            debugTextVanillaTexture = true;
                            break;
                    }
                }
            }
        }

        private static bool ParseConfigBool(JObject config, string key, bool defaultValue)
        {
            if (config[key] == null) return defaultValue;

            if (config[key]!.Type == JTokenType.Boolean)
            {
                return (bool)config[key]!;
            }

            PrintWarning($"\n[!] Warning: config.json key '{key}' should be a boolean (true/false), got '{config[key]}'. Using default: {defaultValue}.");
            return defaultValue;
        }

        // ------------
        // EXTERNAL PROCESSES
        // ------------
        private static void KillProcesses(string name)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch (InvalidOperationException) { }
                    try { process.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                PrintStep($"  [!] Could not kill process '{name}': {ex.Message}");
            }
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
                        process.ErrorDataReceived += (sender, e) => { if (e.Data != null && !QuietLogs) PrintStep($"    [{toolName} STDERR] {e.Data}"); };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        if (!process.WaitForExit(120000)) // 2 minutes timeout
                        {
                            try { process.Kill(); } catch { }
                            PrintError($"  [!] Timeout: {toolName} took longer than 2 minutes and was terminated.");
                            return false;
                        }
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

        // ------------
        // PAR ARCHIVE TOOLS
        // ------------
        private static void UnpackParFile(string parPath, string unpackDir, bool recursive = false)
        {
            if (File.Exists(parPath) && !Directory.Exists(unpackDir))
            {
                if (File.Exists("ParTool.exe"))
                {
                    string fileName = Path.GetFileName(parPath);
                    PrintHeader($"\n[*] [ParTool] Unpacking {fileName}{(recursive ? " (recursive)" : "")}...");
                    Stopwatch ptTimer = Stopwatch.StartNew();
                    string args = $"extract \"{parPath}\" \"{unpackDir}\"{(recursive ? " -r" : "")}";
                    if (RunProcess("ParTool.exe", args))
                    {
                        ptTimer.Stop();
                        PrintSuccess($"  [OK] Unpacked {fileName}. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
                    }
                }
                else
                {
                    PrintWarning($"\n  [!] Warning: ParTool.exe not found. Cannot automatically unpack {Path.GetFileName(parPath)}. (Please place ParTool.exe in the tool folder.)");
                }
            }
        }

        private static void PackParFile(string originalPar, string unpackDir, string finalPar, string successLocation)
        {
            if (Directory.Exists(unpackDir))
            {
                if (File.Exists("ParTool.exe"))
                {
                    string fileName = Path.GetFileName(originalPar);
                    PrintHeader($"\n[*] [ParTool] Injecting modified files into {fileName}...");
                    PrintFilesBeingAdded(unpackDir);
                    Stopwatch ptTimer = Stopwatch.StartNew();
                    if (RunProcess("ParTool.exe", $"add \"{originalPar}\" \"{unpackDir}\" \"{finalPar}\" -c 1"))
                    {
                        ptTimer.Stop();
                        PrintSuccess($"  [OK] Updated {fileName} created in {successLocation}. (Took: {ptTimer.Elapsed.TotalSeconds:F1}s)");
                        PrintStep($"  -> Cleaning up temporary {fileName}.unpack folder...");
                        try { Directory.Delete(unpackDir, true); }
                        catch (Exception ex) { PrintWarning($"  [!] Warning: Could not delete temporary folder: {ex.Message}"); }
                    }
                }
                else
                {
                    PrintWarning($"\n  [!] Warning: ParTool.exe not found. Cannot automatically repack {Path.GetFileName(originalPar)}. (Please place ParTool.exe in the tool folder.)");
                }
            }
        }

        // ------------
        // DICTIONARY & FILTERING
        // ------------
        private static bool LoadDictionary(string dictFile, out HashSet<string> allowedBinFiles, out HashSet<string> allowedTextureFiles, out HashSet<string> allowedCmnFiles, out Dictionary<string, List<string>> folderFilters, out JObject? cachedDict)
        {
            allowedBinFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            allowedTextureFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            allowedCmnFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            folderFilters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            cachedDict = null;

            if (!File.Exists(dictFile))
            {
                PrintError($"  [!] Error: {dictFile} not found. (Please ensure the dictionary file is in the tool folder.)");
                return false;
            }

            try
            {
                string dictJson = File.ReadAllText(dictFile);
                JObject dictObj = JObject.Parse(dictJson);
                cachedDict = dictObj;

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
                    foreach (var prop in textsObj.Properties()) allowedBinFiles.Add(prop.Name.Replace(".json", ""));
                }
                if (dictObj["textures"] is JObject texturesObj)
                {
                    foreach (var prop in texturesObj.Properties()) allowedTextureFiles.Add(prop.Name);
                }
                if (dictObj["cmns"] is JObject cmnsObj)
                {
                    foreach (var prop in cmnsObj.Properties()) allowedCmnFiles.Add(prop.Name.Replace("cmn.par/", ""));
                }

                if (allowedBinFiles.Count == 0)
                {
                    allowedBinFiles = dictObj.Properties()
                        .Where(p => p.Name.EndsWith(".json"))
                        .Select(p => p.Name.Replace(".json", "")).ToHashSet();
                }
                return true;
            }
            catch (Exception ex)
            {
                PrintError($"  [!] Error reading {dictFile}: {ex.Message}");
                return false;
            }
        }

        private static List<string> GatherFilesToProcess(PipelineContext ctx)
        {
            string uiParUnpackDir = Path.Combine(ctx.FolderPath, "data", "ui.par.unpack");
            string talkParUnpackDir = Path.Combine(ctx.FolderPath, "data", "talk.par.unpack");
            string authDirPath = Path.Combine(ctx.FolderPath, "data", "auth", "e");
            string hactDirPath = Path.Combine(ctx.FolderPath, "data", "hact", "e");

            var files = new List<string>();
            string dbPath = Path.Combine(ctx.FolderPath, "data", "db", "e");

            if (Directory.Exists(dbPath))
            {
                files.AddRange(Directory.GetFiles(dbPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".bin.json")));
            }
            if (!ctx.SkipTextures && Directory.Exists(uiParUnpackDir))
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

            if (Directory.Exists(hactDirPath))
            {
                foreach (string hactParUnpackDir in Directory.GetDirectories(hactDirPath, "*.par.unpack"))
                {
                    files.AddRange(Directory.GetFiles(hactParUnpackDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".bin.json")));
                }
            }

            foreach (var kvp in ctx.FolderFilters)
            {
                string folderName = kvp.Key;
                foreach (var fileName in kvp.Value)
                {
                    if (fileName.EndsWith(".par", StringComparison.OrdinalIgnoreCase))
                    {
                        string parUnpackDir = Path.Combine(ctx.FolderPath, "data", folderName, fileName + ".unpack");
                        if (Directory.Exists(parUnpackDir))
                        {
                            files.AddRange(Directory.GetFiles(parUnpackDir, "*.*", SearchOption.AllDirectories)
                                .Where(f => !f.EndsWith(".bin.json")));
                        }
                    }
                    else
                    {
                        string filePath = Path.Combine(ctx.FolderPath, "data", folderName, fileName);
                        if (File.Exists(filePath))
                        {
                            files.Add(filePath);
                        }
                    }
                }
            }

            return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ------------
        // FILE PROCESSING PIPELINE
        // ------------
        private static string GetRelativePath(string fileDir)
        {
            string normalizedDir = fileDir.Replace('/', '\\');
            int dataIdx = normalizedDir.IndexOf("\\data\\", StringComparison.OrdinalIgnoreCase);
            
            if (dataIdx != -1) return normalizedDir.Substring(dataIdx + 1);
            if (normalizedDir.EndsWith("\\data", StringComparison.OrdinalIgnoreCase)) return "data";
            if (normalizedDir.StartsWith("data\\", StringComparison.OrdinalIgnoreCase)) return normalizedDir;
            if (normalizedDir.Equals("data", StringComparison.OrdinalIgnoreCase)) return "data";
            return "";
        }

        private static bool ShouldProcessFile(string filename, string relPath, HashSet<string> allowedBinFiles, HashSet<string> allowedTextureFiles, HashSet<string> allowedCmnFiles, Dictionary<string, List<string>> folderFilters, HashSet<string> eVariantFilenames, out bool isTargetBin, out bool isTargetTex, out bool isTargetCmn)
        {
            isTargetBin = IsFileAllowed(filename, allowedBinFiles);
            isTargetTex = IsFileAllowed(filename, allowedTextureFiles);
            isTargetCmn = IsFileAllowed(filename, allowedCmnFiles);

            var pathParts = relPath.Split('\\', '/');
            bool isInEFolder = pathParts.Contains("e", StringComparer.OrdinalIgnoreCase);
            
            bool isAuth = pathParts.Contains("auth", StringComparer.OrdinalIgnoreCase);
            bool isHact = pathParts.Contains("hact", StringComparer.OrdinalIgnoreCase);

            if (isAuth)
            {
                isTargetBin = false;
                isTargetCmn = false;
            }

            if (isHact && !isInEFolder)
            {
                isTargetBin = false;
                isTargetCmn = false;
            }

            bool isNeutralAllowed = folderFilters.Keys.Any(k => pathParts.Contains(k, StringComparer.OrdinalIgnoreCase) && !k.Equals("hact", StringComparison.OrdinalIgnoreCase)) || isAuth;

            if (isTargetTex)
            {
                if (isInEFolder) return true;

                bool hasEVariant = eVariantFilenames.Contains(filename);

                return !hasEVariant;
            }

            if (isTargetBin || isTargetCmn) 
            {
                return isInEFolder || isNeutralAllowed;
            }
            return false;
        }

        private static bool ProcessExtractionFile(string binPath, PipelineContext ctx, HashSet<string> eVariantFilenames)
        {
            string filename = Path.GetFileName(binPath);
            string fileDir = Path.GetDirectoryName(binPath) ?? "";
            string relPath = GetRelativePath(fileDir);

            if (!ShouldProcessFile(filename, relPath, ctx.AllowedBinFiles, ctx.AllowedTextureFiles, ctx.AllowedCmnFiles, ctx.FolderFilters, eVariantFilenames, out bool isTargetBin, out bool isTargetTex, out bool isTargetCmn))
            {
                return false;
            }

            string currentOgFileDir = Path.Combine(ctx.OgFileDir, relPath);
            Directory.CreateDirectory(currentOgFileDir);

            string copiedBinPath = Path.Combine(currentOgFileDir, filename);
            CopyFileUnrestricted(binPath, copiedBinPath);

            PrintHeader($"\n[*] Processing Targeted File: {filename}");
            
            string currentOgJsonDir = Path.Combine(ctx.OgJsonDir, relPath);
            string currentWorkspaceDir = Path.Combine(ctx.WorkspaceDir, relPath);

            bool kept = false;
            string poPath = Path.Combine(currentWorkspaceDir, Path.GetFileNameWithoutExtension(filename) + ".po");

            if (isTargetTex)
            {
                Directory.CreateDirectory(currentWorkspaceDir);
                string copiedWorkspacePath = Path.Combine(currentWorkspaceDir, filename);
                CopyFileUnrestricted(binPath, copiedWorkspacePath);
                PrintSuccess($"  [OK] Texture copied to workspace: {relPath}\\{filename}");
                kept = true;
            }
            else if (isTargetCmn)
            {
                if (ctx.DebugTextVanillaTexture && File.Exists(poPath))
                {
                    PrintStep($"  -> [Pipeline] PO file already exists at {Path.GetFileName(poPath)}. Skipping CMN text extraction (preserving translation).");
                    kept = true;
                }
                else
                {
                    PrintStep("  -> [BinaryScanner] Extracting texts from .bin...");
                    try
                    {
                        var texts = Yakuza6LocalizationTool.CmnTextManager.ExtractTexts(copiedBinPath);
                        if (texts.Count > 0)
                        {
                            Directory.CreateDirectory(currentWorkspaceDir);
                            PoConverter.DictToPo(texts, poPath, ctx.Language, ctx.ExtractSystemStrings);
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
            }
            else if (isTargetBin)
            {
                PrintStep("  -> [Pipeline] Executing reARMP for JSON extraction...");
                DeleteFileIfExists(Path.Combine(Environment.CurrentDirectory, filename + ".json"));
                DeleteFileIfExists(copiedBinPath + ".json");
                if (RunProcess(ctx.RearmpCmd, $"\"{copiedBinPath}\""))
                {
                    string generatedJsonPathInPlace = copiedBinPath + ".json";
                    string targetJsonPath = Path.Combine(currentOgJsonDir, filename + ".json");

                    string? actualGeneratedJson = FindGeneratedFile(filename + ".json", generatedJsonPathInPlace);

                    if (MoveGeneratedFile(actualGeneratedJson, targetJsonPath))
                    {
                        if (ctx.DebugTextVanillaTexture && File.Exists(poPath))
                        {
                            PrintStep($"  -> [Pipeline] PO file already exists at {Path.GetFileName(poPath)}. Skipping JSON to PO conversion (preserving translation).");
                            kept = true;
                        }
                        else
                        {
                            PrintStep("  -> [PoConverter] Generating PO file internally...");
                            try
                            {
                                int count = PoConverter.JsonToPo(targetJsonPath, poPath, ctx.DictFile, ctx.Language, ctx.CachedDict);
                                if (count > 0)
                                {
                                    PrintSuccess($"  [OK] Created PO file: {relPath}\\{Path.GetFileName(poPath)}");
                                    kept = true;

                                    // AUTOMATICALLY SPLIT SOUND_AUTH.PO
                                    if (ctx.SplitSoundAuth && filename.Equals("sound_auth.bin", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string splitDir = Path.Combine(currentWorkspaceDir, "sound_auth_split");
                                        PrintStep($"  -> [PoSplitter] Splitting sound_auth.po into smaller files...");
                                        Yakuza6LocalizationTool.PoSplitterMerger.SplitPoFile(poPath, splitDir);
                                        if (Directory.Exists(splitDir) && Directory.GetFiles(splitDir, "*.po").Length > 0)
                                        {
                                            File.Delete(poPath); // Delete the whole file to avoid confusing translators
                                            PrintSuccess($"  [OK] Split sound_auth.po into {GetRelativeWorkspacePath(splitDir, ctx.WorkspaceDir)}");
                                        }
                                        else
                                        {
                                            PrintError($"  [!] Error: sound_auth.po split failed. Original PO kept.");
                                        }
                                    }
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
                    }
                    else
                    {
                        PrintError($"  [!] JSON file not generated for {filename}. (Check if reARMP.exe is in the folder and works properly.)");
                    }
                }
            }

            if (!kept)
            {
                if (File.Exists(copiedBinPath)) File.Delete(copiedBinPath);
                
                if (Directory.Exists(currentOgFileDir) && !Directory.EnumerateFileSystemEntries(currentOgFileDir).Any()) Directory.Delete(currentOgFileDir);
                if (Directory.Exists(currentOgJsonDir) && !Directory.EnumerateFileSystemEntries(currentOgJsonDir).Any()) Directory.Delete(currentOgJsonDir);
            }

            return kept;
        }

        private static bool ProcessRecreationPoFile(string poPath, PipelineContext ctx)
        {
            string poFilename = Path.GetFileName(poPath);
            string relPath = GetRelativeWorkspacePath(poPath, ctx.WorkspaceDir);

            string currentOgJsonDir = Path.Combine(ctx.OgJsonDir, relPath);
            string currentOutputDir = Path.Combine(ctx.OutputDir, relPath);
            Directory.CreateDirectory(currentOutputDir);

            string baseName = Path.GetFileNameWithoutExtension(poFilename);
            string jsonFilename = baseName + ".bin.json";
            string ogJsonPath = Path.Combine(currentOgJsonDir, jsonFilename);
            string outputJsonPath = Path.Combine(currentOutputDir, jsonFilename);

            bool isCmn = IsFileAllowed(baseName + ".bin", ctx.AllowedCmnFiles);
            if (isCmn)
            {
                PrintStep($"  -> [BinaryScanner] Injecting translation from {poFilename} into .bin...");
                string originalCmnPath = Path.Combine(ctx.OgFileDir, relPath, baseName + ".bin");
                string outputCmnPath = Path.Combine(currentOutputDir, baseName + ".bin");

                if (!File.Exists(originalCmnPath))
                {
                    PrintError($"  [!] Original CMN file missing: {baseName}.bin in 'og file\\{relPath}'. (Did you delete the 'og file' folder? Try running Phase 1 Extraction again.)");
                    return false;
                }

                try
                {
                    var translatedTexts = PoConverter.PoToDict(poPath, ctx.ExtractSystemStrings);
                    Yakuza6LocalizationTool.CmnTextManager.InjectTextsAndSave(originalCmnPath, outputCmnPath, translatedTexts, ctx.WarningsFile);
                    PrintSuccess($"  [OK] CMN file updated successfully in {relPath}.");
                    return true;
                }
                catch (Exception ex)
                {
                    PrintError($"  [!] Error during PO injection into CMN for {poFilename}: {ex.Message}");
                    return false;
                }
            }

            if (!File.Exists(ogJsonPath))
            {
                PrintError($"  [!] Original JSON file missing: {jsonFilename} in 'og json\\{relPath}'. (Did you delete the 'og json' folder? Try running Phase 1 Extraction again.)");
                return false;
            }

            PrintStep($"  -> [PoConverter] Injecting translation from {poFilename} into JSON...");
            try
            {
                PoConverter.PoToJson(poPath, outputJsonPath, ogJsonPath, ctx.DictFile);
            }
            catch (Exception ex)
            {
                PrintError($"  [!] Error during PO injection into JSON for {poFilename}: {ex.Message}");
                return false;
            }

            PrintStep("  -> [Pipeline] Executing reARMP to recreate .bin...");
            string targetBinPath = Path.Combine(currentOutputDir, baseName + ".bin");
            DeleteFileIfExists(Path.Combine(currentOutputDir, jsonFilename + ".bin"));
            DeleteFileIfExists(Path.Combine(Environment.CurrentDirectory, jsonFilename + ".bin"));

            if (RunProcess(ctx.RearmpCmd, $"\"{outputJsonPath}\""))
            {
                string generatedBinPathInPlace = Path.Combine(currentOutputDir, jsonFilename + ".bin");
                string? actualGeneratedBin = FindGeneratedFile(jsonFilename + ".bin", generatedBinPathInPlace);

                if (MoveGeneratedFile(actualGeneratedBin, targetBinPath))
                {
                    PrintSuccess($"  [OK] BIN file updated successfully in {relPath}.");

                    if (File.Exists(outputJsonPath))
                    {
                        string targetConvertedJsonDir = Path.Combine(ctx.ConvertedJsonDir, relPath);
                        Directory.CreateDirectory(targetConvertedJsonDir);
                        string targetConvertedJsonPath = Path.Combine(targetConvertedJsonDir, jsonFilename);
                        if (File.Exists(targetConvertedJsonPath)) File.Delete(targetConvertedJsonPath);
                        File.Move(outputJsonPath, targetConvertedJsonPath);
                    }

                    return true;
                }
                else
                {
                    PrintError($"  [!] Error: {baseName}.bin was not generated by reARMP. (Check if reARMP.exe is in the folder and works properly.)");
                }
            }
            return false;
        }

        private static bool ProcessRecreationTextureFile(string texPath, string workspaceDir, string outputDir)
        {
            string texFilename = Path.GetFileName(texPath);
            string relPath = GetRelativeWorkspacePath(texPath, workspaceDir);

            string currentOutputDir = Path.Combine(outputDir, relPath);
            Directory.CreateDirectory(currentOutputDir);

            string targetPath = Path.Combine(currentOutputDir, texFilename);
            CopyFileUnrestricted(texPath, targetPath);
            PrintSuccess($"  [OK] Texture copied to output in {relPath}.");
            return true;
        }

        // ------------
        // CONSOLE LOGGING
        // ------------
        // PRINT HELPERS
        // ------------
        private static void WriteLogLine(string msg, ConsoleColor color, string defaultPrefix)
        {
            lock (consoleLock)
            {
                Console.ForegroundColor = color;
                if (msg.StartsWith("\n"))
                {
                    Console.Write("\n");
                    msg = msg.Substring(1);
                }
                Console.WriteLine(msg.StartsWith("  ") ? msg : defaultPrefix + " " + msg);
                Console.ResetColor();
            }
        }

        internal static void PrintError(string msg) 
        { 
            ErrorCount++; 
            WriteLogLine(msg, ConsoleColor.Red, "[ERR]");
            try
            {
                string patchDir = Path.Combine(Environment.CurrentDirectory, "Yakuza 6 - Patch");
                if (Directory.Exists(patchDir))
                {
                    File.AppendAllText(Path.Combine(patchDir, "errors.txt"), msg + Environment.NewLine);
                }
            }
            catch { }
        }
        internal static void PrintWarning(string msg) 
        { 
            WarningCount++; 
            WriteLogLine(msg, ConsoleColor.Yellow, "[WARN]");
            try
            {
                string patchDir = Path.Combine(Environment.CurrentDirectory, "Yakuza 6 - Patch");
                if (Directory.Exists(patchDir))
                {
                    File.AppendAllText(Path.Combine(patchDir, "warnings.txt"), msg + Environment.NewLine);
                }
            }
            catch { }
        }
        internal static void PrintSuccess(string msg) => WriteLogLine(msg, ConsoleColor.Green, "[OK]");
        internal static void PrintInfo(string msg) => WriteLogLine(msg, ConsoleColor.Cyan, "[INFO]");
        internal static void PrintStep(string msg) => WriteLogLine(msg, ConsoleColor.DarkGray, "  ->");
        internal static void PrintHeader(string msg) => WriteLogLine(msg, ConsoleColor.Magenta, "[PIPELINE]");

        private static bool MoveGeneratedFile(string? actualGeneratedPath, string targetPath)
        {
            if (actualGeneratedPath == null) return false;
            
            if (actualGeneratedPath != targetPath)
            {
                string targetDir = Path.GetDirectoryName(targetPath)!;
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                
                if (File.Exists(targetPath))
                {
                    File.SetAttributes(targetPath, File.GetAttributes(targetPath) & ~FileAttributes.ReadOnly);
                    File.Delete(targetPath);
                }
                File.Move(actualGeneratedPath, targetPath);
            }
            return true;
        }

        private static void DeleteFileIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                    File.Delete(path);
                }
            }
            catch { }
        }

        // ------------
        // FILE UTILITIES
        // ------------
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
                CopyFileUnrestricted(file, targetPath, overwrite);
            }
        }

        private static void CopyFileUnrestricted(string sourcePath, string targetPath, bool overwrite = true)
        {
            if (File.Exists(targetPath))
            {
                if (!overwrite) return;
                File.SetAttributes(targetPath, File.GetAttributes(targetPath) & ~FileAttributes.ReadOnly);
            }
            File.Copy(sourcePath, targetPath, overwrite);
            File.SetAttributes(targetPath, File.GetAttributes(targetPath) & ~FileAttributes.ReadOnly);
        }

        private static bool IsFileAllowed(string filename, HashSet<string> allowedFiles)
        {
            if (allowedFiles.Contains(filename)) return true;

            foreach (var f in allowedFiles)
            {
                if (f.Contains('*'))
                {
                    string start = f.Substring(0, f.IndexOf('*'));
                    string end = f.Substring(f.IndexOf('*') + 1);
                    if (filename.StartsWith(start, StringComparison.OrdinalIgnoreCase) && filename.EndsWith(end, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static string GetRelativeWorkspacePath(string fullPath, string workspaceDir)
        {
            string fileDir = Path.GetDirectoryName(fullPath) ?? "";
            if (fileDir.StartsWith(workspaceDir, StringComparison.OrdinalIgnoreCase))
            {
                return fileDir.Substring(workspaceDir.Length).TrimStart('\\', '/');
            }
            return "";
        }

        private static string? FindGeneratedFile(string cwdFilename, string inPlacePath)
        {
            string cwdPath = Path.Combine(Environment.CurrentDirectory, cwdFilename);
            if (File.Exists(cwdPath)) return cwdPath;
            if (File.Exists(inPlacePath)) return inPlacePath;
            return null;
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


        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                PrintStep($"  [!] Could not delete directory '{Path.GetFileName(path)}': {ex.Message}");
            }
        }

        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                PrintStep($"  [!] Could not delete file '{Path.GetFileName(path)}': {ex.Message}");
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private static bool ShowMessageBoxYesNo(string message, string title)
        {
            const uint MB_YESNO = 0x00000004;
            const uint MB_ICONWARNING = 0x00000030;
            const int IDYES = 6;
            try
            {
                return MessageBox(IntPtr.Zero, message, title, MB_YESNO | MB_ICONWARNING) == IDYES;
            }
            catch
            {
                PrintWarning($"\n[MessageBox Fallback] {message}");
                Console.Write("  La tua risposta (y/n) -> ");
                string? ans = Console.ReadLine()?.Trim().ToLower();
                return ans == "y" || ans == "yes";
            }
        }

        private static void CheckExternalTools()
        {
            bool rearmpExists = File.Exists("reARMP.exe");
            bool partoolExists = File.Exists("ParTool.exe");

            if (!rearmpExists || !partoolExists)
            {
                string missing = "";
                if (!rearmpExists && !partoolExists) missing = "reARMP.exe e ParTool.exe";
                else if (!rearmpExists) missing = "reARMP.exe";
                else missing = "ParTool.exe";

                string msg = $"I seguenti tool esterni sono mancanti: {missing}.\n\nVuoi scaricarli dai repository ufficiali? Verranno aperti i link nel browser e il programma verrà chiuso.";
                if (ShowMessageBoxYesNo(msg, "Tool Esterni Mancanti"))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("https://github.com/Ret-HZ/reARMP") { UseShellExecute = true });
                        Process.Start(new ProcessStartInfo("https://github.com/Kaplas80/ParTool") { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        PrintError($"Errore nell'apertura del browser: {ex.Message}");
                    }
                }
                Environment.Exit(0);
            }
        }

        private static void AutoDownloadConfigIfNeeded()
        {
            string configPath = "config.json";
            if (!File.Exists(configPath))
            {
                PrintStep("  -> [Pipeline] config.json non trovato. Download automatico da GitHub in corso...");
                DownloadFromGitHubRelease(configPath);
            }
        }

        private static void AutoDownloadDictionaryIfNeeded(string dictFile)
        {
            if (!File.Exists(dictFile))
            {
                PrintStep($"  -> [Pipeline] Dizionario '{dictFile}' non trovato. Download automatico da GitHub in corso...");
                DownloadFromGitHubRelease(dictFile);
            }
        }

        private static void DownloadFromGitHubRelease(string filename)
        {
            string currentReleaseUrl = $"https://github.com/zSavT/Yakuza-6-Localization-Tool/releases/download/v{ToolVersion}/{filename}";
            string fallbackReleaseUrl = $"https://github.com/zSavT/Yakuza-6-Localization-Tool/releases/download/v0.4/{filename}";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                try
                {
                    PrintInfo($"  Scaricamento di {filename} da: {currentReleaseUrl}");
                    var response = client.GetAsync(currentReleaseUrl).GetAwaiter().GetResult();
                    if (response.IsSuccessStatusCode)
                    {
                        using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                        }
                        PrintSuccess($"  [OK] Scaricato con successo {filename}!");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    PrintWarning($"  Tentativo con release v{ToolVersion} fallito: {ex.Message}");
                }

                // Fallback
                try
                {
                    PrintInfo($"  [Fallback] Scaricamento di {filename} da release v0.4: {fallbackReleaseUrl}");
                    var response = client.GetAsync(fallbackReleaseUrl).GetAwaiter().GetResult();
                    if (response.IsSuccessStatusCode)
                    {
                        using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                        }
                        PrintSuccess($"  [OK] Scaricato con successo {filename} (da release fallback v0.4)!");
                        return;
                    }
                    else
                    {
                        PrintError($"  [!] Download fallito per {filename}. Codice stato HTTP: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    PrintError($"  [!] Errore durante il download fallback: {ex.Message}");
                }
            }
        }
    }
}