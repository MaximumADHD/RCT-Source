using System.Collections.Generic;

namespace RobloxClientTracker
{
    #pragma warning disable CA1051 // Do not declare visible instance fields

    public struct CsvLocalizationEntry
    {
        public string Key;
        public string Source;
        public string Context;
        public string Example;

        public Dictionary<string, string> Values;
    }
}
