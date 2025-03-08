using Aegis;
using System.Collections;
using System.Runtime.Serialization;

namespace AegisTester
{
    [AegisAgent]
    internal class ClassWithNamespace
    {
        [FieldSource(0)]
        public string Property1 { get; set; }

        [FieldSource(1)]
        public required string Property2 { get; set; }
    }
}

