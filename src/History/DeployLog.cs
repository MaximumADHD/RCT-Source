namespace RobloxClientTracker
{
    public class DeployLog
    {
        public string BuildType;
        public string VersionGuid;

        public int MajorRev;
        public int Version;
        public int Patch;
        public int Changelist;

        public bool Is64Bit => (BuildType?.EndsWith("64") ?? false);

        public override string ToString()
        {
            return string.Join(".", MajorRev, Version, Patch, Changelist);
        }
    }
}