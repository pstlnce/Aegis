namespace Aegis.Options;

internal class NotEnoughReaderFieldsException
{
    public const string Name = "NotEnoughFieldsForRequiredException";
    public const string Namespace = AegisAttributeGenerator.Namespace;
    public const string FullName = Namespace + "." + Name;

    public const string Source =
  @$"[Serializable]
    internal sealed class {Name} : Exception
    {{
        public NotEnoughFieldsForRequiredException(int expected, int actual)
            : base($""Required field/properties count: {{expected}}, actual reader's fields count: {{actual}}"")
        {{
            Expected = expected;
            Actual = actual;
        }}
        
        public int Expected {{ get; init; }}
        public int Actual {{ get; init; }}
    }}";
}


public class MissingRequiredFieldOrPropertyException
{
    public const string Name = "MissingRequiredFieldOrPropertyException";
    public const string Namespace = AegisAttributeGenerator.Namespace;
    public const string FullName = Namespace + "." + Name;

    public const string Source =
  @$"[Serializable]
    public class {Name} : System.Exception
    {{
        public MissingRequiredFieldOrPropertyException(string[] propertiesOrFields)
            : base(""There is no matched data for required properties or fields"")
        {{
            PropertiesOrFields = propertiesOrFields;
        }}

        public string[] PropertiesOrFields {{ get; init; }}
    }}";
}
