using HKLib.hk2018;
using HKLib.Reflection.hk2018;

namespace HKLib.Serialization;

/// <summary>
/// A utility for traversing a Havok object graph, ensuring that each object is visited only once.
/// </summary>
public static class HavokObjectWalker
{
    public static void Walk(IHavokObject? root, Action<IHavokObject> action)
    {
        if (root is null) return;

        var queue = new Queue<IHavokObject>();
        var visited = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);

        queue.Enqueue(root);
        visited.Add(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            action(current);

            // Use the reflection system to find all referenced child objects.
            foreach (var child in GetReferencedObjects(current).Where(child => child is not null))
            {
                if (visited.Add(child!))
                {
                    queue.Enqueue(child!);
                }
            }
        }
    }

    private static IEnumerable<IHavokObject?> GetReferencedObjects(IHavokObject obj)
    {
        var type = HavokTypeRegistry.Instance.GetType(obj.GetType());
        if (type == null) yield break;

        var data = HavokData.Of(obj, type);
        foreach (var field in type.Fields)
        {
            if (field.Type.Kind == HavokType.TypeKind.Pointer || field.Type.Kind == HavokType.TypeKind.Array)
            {
                if (data.TryGetField<object>(field.Name, out var value))
                {
                    if (value is IHavokObject childObj)
                    {
                        yield return childObj;
                    }
                    else if (value is System.Collections.IEnumerable list)
                    {
                        foreach (var item in list)
                        {
                            if (item is IHavokObject listChild)
                                yield return listChild;
                        }
                    }
                }
            }
            else if (field.Type.Kind == HavokType.TypeKind.Record)
            {
                if (data.TryGetField<object>(field.Name, out var value) && value is IHavokObject childObj)
                {
                    yield return childObj;
                }
            }
        }
    }
}
