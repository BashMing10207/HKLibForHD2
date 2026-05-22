using System.Reflection;
using HKLib.Reflection.hk2018;
using HKLib.hk2018;
using HKLib.Reflection.Dynamic;

namespace HKLib.Serialization.hk2019.Binary;

/// <summary>
/// Performs the first pass of the serialization process, calculating the layout of all objects and sections.
/// This is the core of Phase 3, enabling dynamic asset editing.
/// </summary>
internal class LayoutCalculator
{
    private readonly DynamicTypeRegistry _typeRegistry;
    private readonly LayoutInfo _layoutInfo = new(); // The dictionary inside uses ReferenceEqualityComparer
    private readonly Queue<IHavokObject> _objectQueue = new();
    private long _currentOffset;

    public LayoutCalculator(DynamicTypeRegistry typeRegistry)
    {
        _typeRegistry = typeRegistry;
    }

    /// <summary>
    /// Calculates the complete file layout for the given root object.
    /// </summary>
    public LayoutInfo Calculate(IHavokObject rootObject)
    {
        // Start with the header size
        _currentOffset = 0x80;

        // Pass 1.1: Traverse the object graph to find all unique objects and their types.
        AddToQueue(rootObject);
        while (_objectQueue.Count > 0)
        {
            IHavokObject obj = _objectQueue.Dequeue();
            ProcessObject(obj);
        }

        // Pass 1.2: Calculate the starting offsets of the main sections
        // __classnames__ section
        AlignmentHelper.Align(ref _currentOffset, 16);
        _layoutInfo.ClassNamesSectionOffset = _currentOffset;
        // _currentOffset += CalculateClassNamesSize(); // TODO

        // __types__ section
        AlignmentHelper.Align(ref _currentOffset, 16);
        _layoutInfo.TypesSectionOffset = _currentOffset;
        // _currentOffset += CalculateTypesSize(); // TODO

        // __data__ section
        AlignmentHelper.Align(ref _currentOffset, 16);
        _layoutInfo.DataSectionOffset = _currentOffset;

        // Pass 1.3: Calculate the final offsets for all data objects
        foreach (IHavokObject obj in _layoutInfo.ObjectLayouts.Keys)
        {
            ObjectLayout objectLayout = _layoutInfo.ObjectLayouts[obj];
            AlignmentHelper.Align(ref _currentOffset, objectLayout.Size); // Simplified alignment for now
            objectLayout.Offset = _currentOffset;
            _currentOffset += objectLayout.Size;
        }

        _layoutInfo.FileSize = _currentOffset;
        return _layoutInfo;
    }

    /// <summary>
    /// Adds an object to the processing queue if it's a reference type and hasn't been seen before.
    /// </summary>
    private void AddToQueue(IHavokObject? obj)
    {
        if (obj is null || _layoutInfo.ObjectLayouts.ContainsKey(obj))
        {
            return;
        }

        _layoutInfo.ObjectLayouts.Add(obj, new ObjectLayout());
        _objectQueue.Enqueue(obj);
    }

    /// <summary>
    /// Processes a single object to discover its type, fields, and any referenced objects.
    /// </summary>
    private void ProcessObject(IHavokObject obj)
    {
        DynamicHavokType type = _typeRegistry.GetType(obj.GetType().Name)
                         ?? throw new InvalidDataException($"Type \"{obj.GetType().Name}\" not found in the type registry.");

        ObjectLayout objectLayout = _layoutInfo.ObjectLayouts[obj];
        objectLayout.Size = type.Size; // This is where dynamic size calculation would happen (e.g., for arrays)

        if (!_layoutInfo.ClassNames.Contains(type.Name))
        {
            _layoutInfo.ClassNames.Add(type.Name);
        }

        // Recurse into fields
        foreach (FieldInfo field in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? fieldValue = field.GetValue(obj);
            if (fieldValue is null) continue;

            // TODO: Differentiate between value types, pointers, and arrays.
            // For now, we just queue up any non-value type.
            if (!field.FieldType.IsValueType)
            {
                AddToQueue(fieldValue as IHavokObject);
                // TODO: Create PointerPatch for this field if it's a pointer.
            }
        }
    }
}