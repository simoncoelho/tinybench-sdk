using System;
using System.Collections.Generic;
using System.Text;
using System.Transactions;

namespace TinyBench.Sdk
{
    public class TinyMethod : IEquatable<TinyMethod>
    {
        public TinyMethod(string methodName, HashSet<TinyMethodArg>? inputs = null, HashSet<TinyMethodArg>? outputs = null)
        {
            MethodName = methodName;
            MethodIO = new TinyMethodIO(inputs, outputs);
            MethodMetadata = new TinyMethodMetadata(inputs, outputs);
        }

        public string MethodName { get; }

        public TinyMethodIO MethodIO { get; }

        public TinyMethodMetadata MethodMetadata {get; }

        public bool Equals(TinyMethod? other) => MethodName == other?.MethodName;
    }

    public class TinyMethodIO
    {
        public TinyMethodIO(HashSet<TinyMethodArg>? inputs = null, HashSet<TinyMethodArg>? outputs = null)
        {
            Inputs = inputs;
            Outputs = outputs;
        }
        public HashSet<TinyMethodArg>? Inputs { get; }
        public HashSet<TinyMethodArg>? Outputs { get; }
    }

    public class TinyMethodArg
    {
        public TinyMethodArg(string name, Type type, bool? isRequired = null, object? defaultValue = null)
        {
            Name = name;
            Type = type;
            IsRequired = isRequired;
            DefaultValue = defaultValue;
        }
        public string Name { get; }
        public Type Type { get; }
        public bool? IsRequired { get; }
        public object? DefaultValue { get; }
    }

    public class TinyMethodMetadata
    {
        public TinyMethodMetadata(HashSet<TinyMethodArg>? inputs = null, HashSet<TinyMethodArg>? outputs = null)
        {
            Inputs = inputs?.ToDictionary(i => i, _ => new TinyMethodArgMetadata());

            Outputs = outputs?.ToDictionary(o => o, _ => new TinyMethodArgMetadata());
        }

        public string? Description { get; }

        public string? DisplayName { get; }

        public Dictionary<TinyMethodArg, TinyMethodArgMetadata>? Inputs { get; }

        public Dictionary<TinyMethodArg, TinyMethodArgMetadata>? Outputs { get; }
    }

    public class TinyMethodArgMetadata
    {
        public TinyMethodArgMetadata() { }

        public string? DisplayName { get; }

        public string? Description { get; }

        public Tuple<double, double>? AcceptedRange { get; }

        public TinyMethod? OptionsMethod { get; }
    }
}
