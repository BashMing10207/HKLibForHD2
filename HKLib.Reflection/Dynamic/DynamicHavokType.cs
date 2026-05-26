namespace HKLib.Reflection.Dynamic;

public class DynamicHavokType : IHavokType
{
    public string Name { get; set; } = null!;
    public global::HKLib.Reflection.HavokType.TypeKind Kind { get; set; }
    public int Size { get; set; }
    public int Alignment { get; set; }
    public DynamicHavokType? Parent { get; set; }
    public DynamicHavokType? SubType { get; set; }
    public List<DynamicHavokField> Fields { get; } = new();
    public List<DynamicHavokTemplateParameter> TemplateParameters { get; } = new();
    public List<DynamicHavokPreset> Presets { get; } = new();

    public List<DynamicHavokField> OptionalFields => GetAllFields().Where(f => f.IsOptional).ToList();

    public List<DynamicHavokField> GetAllFields()
    {
        List<DynamicHavokField> allFields = new();
        if (Parent is not null)
        {
            allFields.AddRange(Parent.GetAllFields());
        }
        allFields.AddRange(Fields);
        return allFields.OrderBy(f => f.Offset).ToList();
    }
}

public class DynamicHavokField
{
    public string Name { get; set; } = null!;
    public DynamicHavokType Type { get; set; } = null!;
    public int Offset { get; set; }
    public int Flags { get; set; }
    public bool IsOptional { get; set; }
}

public class DynamicHavokTemplateParameter
{
    public string Name { get; set; } = null!;
    public string Kind { get; set; } = null!; // "Type" or "Value"
    public DynamicHavokType? Type { get; set; }
    public string? Value { get; set; }
}

public class DynamicHavokPreset
{
    public string Name { get; set; } = null!;
    public string Value { get; set; } = null!;
}