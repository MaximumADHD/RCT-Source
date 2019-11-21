using System.Net;
using System.IO;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobloxClientTracker
{
    public class ClientVersionInfo
    {
        public string Version;
        public string Guid;

        public static async Task<ClientVersionInfo> Get(string buildType = "WindowsStudio", string branch = "roblox")
        {
            string jsonUrl = $"https://clientsettings.{branch}.com/v1/client-version/{buildType}";
            var versionInfo = new ClientVersionInfo();
            
            using (WebClient http = new WebClient())
            {
                string json = await http.DownloadStringTaskAsync(jsonUrl);

                using (StringReader jsonText = new StringReader(json))
                {
                    var reader = new JsonTextReader(jsonText);
                    JObject jsonData = JObject.Load(reader);

                    versionInfo.Version = jsonData.Value<string>("version");
                    versionInfo.Guid = jsonData.Value<string>("clientVersionUpload");

                    return versionInfo;
                }
            }
        }
    }
}
