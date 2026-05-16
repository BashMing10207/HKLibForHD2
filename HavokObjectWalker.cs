using System;
using System.Collections.Generic;
using System.Linq;
using HKLib.hk2018;
using HKLib.Reflection;

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
            foreach (var child in HavokData.GetReferencedObjects(current).Where(child => child is not null))
            {
                if (visited.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }
    }
}