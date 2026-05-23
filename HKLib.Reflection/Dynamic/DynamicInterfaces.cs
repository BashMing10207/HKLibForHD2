namespace HKLib.Reflection.Dynamic;

/// <summary>
/// A marker interface for all dynamically reflected Havok objects.
/// </summary>
public interface IHavokObject
{
    IHavokType GetType();
}

/// <summary>
/// A marker interface for all dynamically reflected Havok type definitions.
/// </summary>
public interface IHavokType { }