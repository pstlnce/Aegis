using Aegis;

namespace AegisTester
{
    internal class InnerClass
    {
        public int Faf { get; set; }

        public DateTime SosiSuchara { get; set; }
    }

    [AegisAgent]
    internal class ClassWithNamespace
    {
        [FieldSource(0)]
        public string Property1 { get; set; }

        [FieldSource(1)]
        public required string Property2 { get; set; }

        [FieldSource("bb")]
        public required bool RequiredBoolean { get; set; }

        public required InnerClass Inner { get; set; }

        [Parser]
        public static bool Parse(System.Object f)
        {
            return f switch
            {
                bool boolVal => boolVal,
                
                int int32 => int32 != 0,
                
                long int64 => int64 != 0,

                char charVal => charVal switch
                {
                    '0' => false,
                    '1' => true,
                    'Y' => true,
                    'N' => false,
                    _ => throw new InvalidCastException(),
                },

                string strVal => strVal switch
                {
                    "true" => true,
                    "false" => false,
                    "0" => false,
                    "1" => true,
                    "Y" => true,
                    "N" => false,
                    "Yes" => true,
                    "No" => false,
                    _ => throw new InvalidCastException(),
                },

                _ => throw new InvalidCastException(),
            };
        }
    }
}

