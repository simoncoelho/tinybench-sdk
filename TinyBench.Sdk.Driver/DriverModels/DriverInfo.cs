using System;
using System.Collections.Generic;
using System.Text;

namespace TinyBench.Sdk.Driver.DriverModels
{
    public struct DriverInfo
    {
        public DriverInfo(string name) 
        {
            Name = name;
            Version = "1.0.0";
            Description = "No description provided.";
            Category = DeviceCategory.NotSpecified;
        }

        public DriverInfo(string name, string version, string description, DeviceCategory category)
        {
            Name = name;
            Version = version;
            Description = description;
            Category = category;
        }

        public string Name { get; init; }
        public string Version { get; init; }
        public string Description { get; init; }
        public DeviceCategory Category { get; init; }
    }

    public enum DeviceCategory
    {
        Centrifuge,
        LiquidHandler,
        Reader,
        IncubatorShaker,
        SampleStorage,
        DispenserWasher,
        CapperDecapper,
        Other,
        NotSpecified,
    }
}
