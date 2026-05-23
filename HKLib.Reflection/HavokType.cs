namespace HKLib.Reflection;

/// <summary>
/// Stores Havok reflection type info
/// </summary>
public abstract class HavokType : IEquatable<HavokType>
{
    public enum TypeKind
    {
        Void,
        Opaque,
        Bool,
        Char,
        Int8,
        UInt8,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Real,
        Vector4,
        Quaternion,
        Matrix3,
        Rotation,
        QsTransform,
        Matrix4,
        Transform,
        Pointer,
        FunctionPointer,
        Array,
        InplaceArray,
        Enum,
        Record,
        SimpleArray,
        HomogeneousArray,
        Variant,
        CString,
        String,
        Flags,
        Half,
        Float,
        Double
    }

    /// <summary>
    /// Base name of the type, does not include template arguments
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Full name of the type, includes template arguments
    /// </summary>
    public required string Identity { get; init; }

    /// <summary>
    /// The C# class which represents this type.
    /// </summary>
    public Type? Type { get; init; }

    /// <summary>
    /// Denotes whether this type is serializable
    /// </summary>
    public abstract bool Serializable { get; }

    /// <summary>
    /// Size of the type in bytes
    /// </summary>
    public int Size { get; init; }

    /// <summary>
    /// Byte alignment of the type
    /// </summary>
    public int Alignment { get; init; }

    /// <summary>
    /// Parent class of the type
    /// </summary>
    public abstract HavokType? Parent { get; }

    public bool Equals(HavokType? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.GetType() != this.GetType()) return false;
        return Identity == other.Identity;
    }

    public override bool Equals(object? obj)
    {
        return Equals((HavokType?)obj);
    }

    public override int GetHashCode()
    {
        return Identity.GetHashCode();
    }

    public static bool operator ==(HavokType? left, HavokType? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(HavokType? left, HavokType? right)
    {
        return !Equals(left, right);
    }

    public override string ToString()
    {
        return Identity;
    }

    /// <summary>
    /// A member of a <see cref="HavokType" />
    /// </summary>
    public abstract record Member(string Name, Member.MemberFlags Flags, int Offset)
    {
        [Flags]
        public enum MemberFlags
        {
            None = 0,
            NonSerializable = 1,
            Protected = 1 << 1,
            Private = 1 << 2,
            Hidden = 1 << 3,
            Property = 1 << 4,
            Field = 1 << 5,
            CustomSetter = 1 << 6
        }

        public abstract HavokType Type { get; }

        public bool NonSerializable => Flags.HasFlag(MemberFlags.NonSerializable);
    }
}