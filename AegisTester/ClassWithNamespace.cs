using Aegis;
using System.Collections;
using System.Runtime.Serialization;

namespace AegisTester
{
    [AegisAgent]
    internal class ClassWithNamespace
    {
        public string Property1 { get; set; }
        public required string Property2 { get; set; }
    }
}

