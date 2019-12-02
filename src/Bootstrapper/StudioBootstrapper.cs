using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Microsoft.Win32;

namespace RobloxClientTracker
{
    public class StudioBootstrapper
    {
        private string branch;
        private string buildVersion;
        private FileManifest fileManifest;

        private const string appSettingsXml =
            "<Settings>\n" +
            "   <ContentFolder>content</ContentFolder>\n" +
            "   <BaseUrl>http://www.roblox.com</BaseUrl>\n" +
            "</Settings>";

        private static WebClient http = new WebClient();

        private static RegistryKey root = Program.BranchRegistry;

        private static RegistryKey versionRegistry;
        private static RegistryKey pkgRegistry;

        private static RegistryKey fileRegistry;
        private static RegistryKey fileRepairs;

        public StudioBootstrapper(string studioBranch)
        {
            branch = studioBranch;
            root = Program.BranchRegistry;

            versionRegistry = root.Open("VersionData");
            pkgRegistry = root.Open("PackageManifest");

            fileRegistry = root.Open("FileManifest");
            fileRepairs = fileRegistry.Open("Repairs");

            http.Headers.Set(HttpRequestHeader.UserAgent, "Roblox");
        }

        private static string computeSignature(Stream source)
        {
            string result;

            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(source);
                result = BitConverter.ToString(hash)
                    .Replace("-", "")
                    .ToLower();
            }

            return result;
        }

        private static string computeSignature(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            {
                return computeSignature(stream);
            }
        }

        private static void tryToKillProcess(Process process)
        {
            try
            {
                process.Kill();
            }
            catch
            {
                Console.WriteLine($"Cannot terminate process {process.Id}!");
            }
        }

        private void echo(string text, ConsoleColor color = ConsoleColor.Yellow)
        {
            Program.print(text, color);
        }
        
        private static string getDirectory(params string[] paths)
        {
            string basePath = Path.Combine(paths);

            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);

            return basePath;
        }

        private Task deleteUnusedFiles()
        {
            var taskQueue = new List<Task>();
            string studioDir = GetStudioDirectory();

            foreach (string rawFileName in fileRegistry.GetValueNames())
            {
                // Temporary variable so we can change what file we are testing,
                // without breaking the foreach loop.
                string fileName = rawFileName;

                // A few hacky exemptions to the rules, but necessary because older versions of the
                // mod manager do not record which files are outside of the manifest, but valid otherwise.
                // TODO: Need a more proper way of handling this

                if (fileName.Contains("/") || fileName.EndsWith(".dll") && !fileName.Contains("\\"))
                    continue;

                string filePath = Path.Combine(studioDir, fileName);

                if (!File.Exists(filePath))
                {
                    // Check if we recorded this file as an error in the manifest.
                    string fixedFile = fileRepairs.GetString(fileName);

                    if (fixedFile.Length > 0)
                    {
                        // Use this path instead.
                        filePath = Path.Combine(studioDir, fixedFile);
                        fileName = fixedFile;
                    }
                }

                if (!fileManifest.ContainsKey(fileName))
                {
                    if (File.Exists(filePath))
                    {
                        Task verify = Task.Run(() =>
                        {
                            try
                            {
                                // Confirm that this file no longer exists in the manifest.
                                string sig;

                                using (FileStream file = File.OpenRead(filePath))
                                    sig = computeSignature(file);

                                if (!fileManifest.ContainsValue(sig))
                                {
                                    echo($"Deleting unused file {fileName}");
                                    fileRegistry.DeleteValue(fileName);
                                    File.Delete(filePath);
                                }
                                else if (!fileName.StartsWith("content"))
                                {
                                    // The path may have been labeled incorrectly in the manifest.
                                    // Record it for future reference so we don't have to
                                    // waste time computing the signature later on.

                                    string fixedName = fileManifest
                                        .Where(pair => pair.Value == sig)
                                        .Select(pair => pair.Key)
                                        .First();

                                    fileRepairs.SetValue(fileName, fixedName);
                                }
                            }
                            catch
                            {
                                Console.WriteLine("FAILED TO VERIFY OR DELETE " + fileName);
                            }
                        });

                        taskQueue.Add(verify);
                    }
                    else
                    {
                        fileRegistry.DeleteValue(fileName);
                    }
                }
            }

            return Task.WhenAll(taskQueue);
        }
        
        public static string GetStudioBinaryType()
        {
            return "WindowsStudio64";
        }

        // This does a quick check of /versionQTStudio without resolving
        // if its the proper version-guid for gametest builds. This should
        // make gametest update checks faster... at least for 64-bit users.
        public static async Task<string> GetFastVersionGuid(string branch)
        {
            if (branch == "roblox")
            {
                string binaryType = GetStudioBinaryType();
                var info = await ClientVersionInfo.Get(binaryType);

                return info.Guid;
            }
            else
            {
                string fastUrl = $"https://s3.amazonaws.com/setup.{branch}.com/versionQTStudio";
                return await http.DownloadStringTaskAsync(fastUrl);
            }
        }

        public static async Task<ClientVersionInfo> GetCurrentVersionInfo(string branch, string fastGuid = "")
        {
            string binaryType = GetStudioBinaryType();


            if (fastGuid == "")
                fastGuid = await GetFastVersionGuid(branch);

            var info = new ClientVersionInfo();
            string latestFastGuid = versionRegistry.GetString("LatestFastGuid");

            bool refresh = (latestFastGuid != fastGuid);
            var logData = await StudioDeployLogs.Get(branch, refresh);

            DeployLog build_x86 = logData.CurrentLogs_x86.Last();
            DeployLog build_x64 = logData.CurrentLogs_x64.Last();

            if (binaryType == "WindowsStudio64")
            {
                info.Version = build_x64.VersionId;
                info.Guid = build_x64.VersionGuid;
            }
            else
            {
                info.Version = build_x86.VersionId;
                info.Guid = build_x86.VersionGuid;
            }

            versionRegistry.SetValue("LatestFastGuid", fastGuid);
            versionRegistry.SetValue("LatestGuid_x86", build_x86.VersionGuid);
            versionRegistry.SetValue("LatestGuid_x64", build_x64.VersionGuid);

            return info;
        }

        // YOU WERE SO CLOSE ROBLOX, AGHHHH
        private static string fixFilePath(string pkgName, string filePath)
        {
            string pkgDir = pkgName.Replace(".zip", "");

            if ((pkgDir == "Plugins" || pkgDir == "Qml") && !filePath.StartsWith(pkgDir))
                filePath = pkgDir + '\\' + filePath;

            return filePath;
        }

        private async Task installPackage(Package package)
        {
            string pkgName = package.Name;

            string studioDir = GetStudioDirectory();
            string downloads = getDirectory(studioDir, "downloads");

            string oldSig = pkgRegistry.GetString(pkgName);
            string newSig = package.Signature;

            if (oldSig == newSig)
            {
                echo($"Package '{pkgName}' hasn't changed between builds, skipping.");
                return;
            }

            string zipFileUrl = $"https://s3.amazonaws.com/setup.{branch}.com/{buildVersion}-{pkgName}";
            string zipExtractPath = Path.Combine(downloads, package.Name);

            echo($"Installing package {zipFileUrl}");

            var localHttp = new WebClient();
            localHttp.Headers.Set("UserAgent", "Roblox");

            // Download the zip file package.
            byte[] fileContents = await localHttp.DownloadDataTaskAsync(zipFileUrl);

            // If the size of the file we downloaded does not match the packed size specified
            // in the manifest, then this file has been tampered with.

            if (fileContents.Length != package.PackedSize)
                throw new InvalidDataException($"{package.Name} expected packed size: {package.PackedSize} but got: {fileContents.Length}");

            using (MemoryStream fileBuffer = new MemoryStream(fileContents))
            {
                // Compute the MD5 signature of this zip file, and make sure it matches with the
                // signature specified in the package manifest.
                string checkSig = computeSignature(fileBuffer);

                if (checkSig != newSig)
                    throw new InvalidDataException($"{package.Name} expected signature: {newSig} but got: {checkSig}");

                // Write the zip file.
                File.WriteAllBytes(zipExtractPath, fileContents);
            }

            ZipArchive archive = ZipFile.OpenRead(zipExtractPath);
            string localRootDir = null;

            var reprocess = new Dictionary<ZipArchiveEntry, string>();

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.Length > 0)
                {
                    string newFileSig = null;

                    // If we have figured out what our root directory is, try to resolve
                    // what the signature of this file is.

                    if (localRootDir != null)
                    {
                        string filePath = entry.FullName.Replace('/', '\\');
                        bool hasFilePath = fileManifest.ContainsKey(filePath);

                        // If we can't find this file in the signature lookup table,
                        // try appending the local directory to it. This resolves some
                        // edge cases relating to the fixFilePath function above.

                        if (!hasFilePath)
                        {
                            filePath = localRootDir + filePath;
                            hasFilePath = fileManifest.ContainsKey(filePath);
                        }

                        // If we can find this file path in the file manifest, then we will
                        // use its pre-computed signature to check if the file has changed.

                        newFileSig = hasFilePath ? fileManifest[filePath] : null;
                    }

                    // If we couldn't pre-determine the file signature from the manifest,
                    // then we have to compute it manually. This is slower.

                    if (newFileSig == null)
                        newFileSig = computeSignature(entry);

                    // Now check what files this signature corresponds with.
                    var files = fileManifest
                        .Where(pair => pair.Value == newFileSig)
                        .Select(pair => pair.Key);

                    if (files.Count() > 0)
                    {
                        foreach (string file in files)
                        {
                            // Write the file from this signature.
                            writePackageFile(studioDir, pkgName, file, newFileSig, entry);

                            if (localRootDir == null)
                            {
                                string filePath = fixFilePath(pkgName, file);
                                string entryPath = entry.FullName.Replace('/', '\\');

                                if (filePath.EndsWith(entryPath))
                                {
                                    // We can infer what the root extraction  
                                    // directory is for the files in this package!                                 
                                    localRootDir = filePath.Replace(entryPath, "");
                                }
                            }
                        }
                    }
                    else
                    {
                        string file = entry.FullName;

                        if (localRootDir == null)
                        {
                            // Check back on this file after we extract the regular files,
                            // so we can make sure this is extracted to the correct directory.
                            reprocess.Add(entry, newFileSig);
                        }
                        else
                        {
                            // Append the local root directory.
                            file = localRootDir + file;
                            writePackageFile(studioDir, pkgName, file, newFileSig, entry);
                        }
                    }
                }
            }

            // Process any files that we deferred from writing immediately.
            foreach (ZipArchiveEntry entry in reprocess.Keys)
            {
                string file = entry.FullName;
                string newFileSig = reprocess[entry];

                if (localRootDir != null)
                    file = localRootDir + file;

                writePackageFile(studioDir, pkgName, file, newFileSig, entry);
            }

            // Update the signature in the package registry so we can check
            // if this zip file needs to be updated in future versions.
            pkgRegistry.SetValue(pkgName, package.Signature);
        }

        private void writePackageFile(string studioDir, string pkgName, string file, string newFileSig, ZipArchiveEntry entry)
        {
            string filePath = fixFilePath(pkgName, file);
            string oldFileSig = fileRegistry.GetString(filePath);

            if (oldFileSig == newFileSig)
                return;

            string extractPath = Path.Combine(studioDir, filePath);
            string extractDir = Path.GetDirectoryName(extractPath);

            getDirectory(extractDir);

            try
            {
                if (File.Exists(extractPath))
                    File.Delete(extractPath);

                echo($"Writing {filePath}...");
                entry.ExtractToFile(extractPath);

                fileRegistry.SetValue(filePath, newFileSig);
            }
            catch
            {
                echo($"FILE WRITE FAILED: {filePath} (This build may not run as expected!)", ConsoleColor.Red);
            }
        }

        public string GetStudioDirectory()
        {
            return getDirectory(Program.trunk, "builds", branch);
        }

        public string GetStudioPath()
        {
            string studioDir = GetStudioDirectory();
            return Path.Combine(studioDir, "RobloxStudioBeta.exe");
        }

        public static List<Process> GetRunningStudioProcesses()
        {
            var studioProcs = new List<Process>();

            foreach (Process process in Process.GetProcessesByName("RobloxStudioBeta"))
            {
                Action<Process> action;

                if (process.MainWindowHandle != IntPtr.Zero)
                    action = studioProcs.Add;
                else
                    action = tryToKillProcess;

                action(process);
            }

            return studioProcs;
        }

        public async Task UpdateStudio()
        {
            echo("Checking build installation...");

            string currentVersion = versionRegistry.GetString("VersionGuid");
            string fastVersion = await GetFastVersionGuid(branch);

            ClientVersionInfo versionInfo = null;

            if (fastVersion != currentVersion)
            {
                if (branch != "roblox")
                    echo("Possible update detected, verifying...");

                versionInfo = await GetCurrentVersionInfo(branch);
                buildVersion = versionInfo.Guid;
            }
            else
            {
                buildVersion = fastVersion;
            }

            if (currentVersion != buildVersion)
            {
                echo("This build needs to be installed!");

                string binaryType = GetStudioBinaryType();
                string studioDir = GetStudioDirectory();
                string versionId = versionInfo.Version;

                string guidPath = Path.Combine(studioDir, "version-guid.txt");
                File.WriteAllText(guidPath, buildVersion);
                
                string versionPath = Path.Combine(studioDir, "version.txt");
                File.WriteAllText(versionPath, versionId);

                echo($"Installing Version {versionId} of Roblox Studio...");

                var taskQueue = new List<Task>();

                echo("Grabbing package manifest...");
                var pkgManifest = await PackageManifest.Get(branch, buildVersion);

                echo("Grabbing file manifest...");
                fileManifest = await FileManifest.Get(branch, buildVersion);

                string pkgManifestPath = Path.Combine(studioDir, "rbxPkgManifest.txt");
                File.WriteAllText(pkgManifestPath, pkgManifest.RawData);

                string fileManifestPath = Path.Combine(studioDir, "rbxManifest.txt");
                File.WriteAllText(fileManifestPath, fileManifest.RawData);
                
                foreach (var package in pkgManifest)
                {
                    Task installer = Task.Run(() => installPackage(package));
                    taskQueue.Add(installer);
                }

                await Task.WhenAll(taskQueue);

                echo("Writing AppSettings.xml...");

                string appSettings = Path.Combine(studioDir, "AppSettings.xml");
                File.WriteAllText(appSettings, appSettingsXml);

                echo("Deleting unused files...");
                await deleteUnusedFiles();

                echo("Dumping API...");

                string studioPath = GetStudioPath();
                string apiPath = Path.Combine(studioDir, "API-Dump.json");

                Process dumpApi = Process.Start(studioPath, $"-API \"{apiPath}\"");
                dumpApi.WaitForExit();
                
                versionRegistry.SetValue("Version", versionId);
                versionRegistry.SetValue("VersionGuid", buildVersion);
            }
        }
    }
}
