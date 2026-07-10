using System;
using System.Collections.Generic;
using System.Text;
using TinyBench.Sdk.Driver.DriverModels;

namespace TinyBench.Sdk.Driver
{
    public class TinyDriver
    {
        public TinyDriver(DriverInfo driverInfo, TinyMethod connect, TinyMethod disconnect, HashSet<TinyMethod> methods)
        {
            DriverInfo = driverInfo;
            Connect = connect;
            Disconnect = disconnect;
            Methods = methods;
        }

        public DriverInfo DriverInfo { get; init; }
        public TinyMethod Connect { get; init; }
        public TinyMethod Disconnect { get; init; }
        public HashSet<TinyMethod> Methods { get; init; }
    }
}
