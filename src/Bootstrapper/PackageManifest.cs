using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace RobloxClientTracker
{
    public struct Package
    {
        public string Name      { get; set; }
        public string Signature { get; set; }
        public int PackedSize   { get; set; }
        public int Size         { get; set; }
    }

    public class PackageManifest : List<Package>
    {
        public string RawData { get; private set; }

        private PackageManifest(string data)
        {
            RawData = data;

            using StringReader reader = new StringReader(data);
            string version = reader.ReadLine();

            if (version != "v0")
            {
                string errorMsg = $"Unexpected package manifest version: {version} (expected v0!)\n" +
                                   "Please contact CloneTrooper1019 if you see this error.";

                throw new NotSupportedException(errorMsg);
            }

            while (true)
            {
                try
                {
                    string fileName = reader.ReadLine();
                    string signature = reader.ReadLine();

                    int packedSize = int.Parse(reader.ReadLine(), Program.InvariantNumber);
                    int size = int.Parse(reader.ReadLine(), Program.InvariantNumber);

                    if (fileName.EndsWith(".zip", Program.InvariantString))
                    {
                        var package = new Package()
                        {
                            Name = fileName,
                            Signature = signature,
                            PackedSize = packedSize,
                            Size = size
                        };

                        Add(package);
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        public static async Task<PackageManifest> Get(string branch, string versionGuid)
        {
            string pkgManifestUrl = $"https://s3.amazonaws.com/setup.{branch}.com/{versionGuid}-rbxPkgManifest.txt";
            string pkgManifestData;

            using (WebClient http = new WebClient())
                pkgManifestData = await http.DownloadStringTaskAsync(pkgManifestUrl);

            return new PackageManifest(pkgManifestData);
        }
    }
}
