namespace RobloxClientTracker
{
    public class DeployLog
    {
        public string VersionGuid { get; set; }
        public string BuildType   { get; set; }

        public int MajorRev   { get; set; }
        public int Version    { get; set; }
        public int Patch      { get; set; }
        public int Changelist { get; set; }

        public bool Is64Bit => BuildType.EndsWith("64", Program.InvariantString);
        public string VersionId => ToString();

        public override string ToString()
        {
            return string.Join(".", MajorRev, Version, Patch, Changelist);
        }
    }
}