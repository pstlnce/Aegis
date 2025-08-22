using Aegis;

namespace AegisTester
{
    internal class InnerClass2
    {
        public int Ttt1 { get; set; }
        public int Ttt2 { get; set; }
        public int Ttt3 { get; set; }
        public string Ttt4 { get; set; }

        public InnerClass Complex { get; set; }
        public InnerClass Com_____Plex { get; set; }
    }

    internal class InnerClass3
    {
        public required int RequiredTest { get; set; }

        public required int RequiredTest2 { get; set; }

        //public InnerClass Optional { get; set; }
    }

    internal class InnerClass
    {
        public required int RequiredTest { get; set; }

        public required int RequiredTest2 { get; set; }

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

        public required InnerClass3 _3 { get; set; }

        public required InnerClass Inner { get; set; }

        public required InnerClass Req { get; set; }

        public InnerClass NotRequired { get; set; }

        public InnerClass2 OptionalWithZeroRequired { get; set; }

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

