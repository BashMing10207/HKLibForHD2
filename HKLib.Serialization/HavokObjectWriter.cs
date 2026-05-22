using System;
using System.IO;
using System.Numerics;
using HKLib.hk2018;
using HKLib.Reflection.hk2018;

namespace HKLib.Serialization;

/// <summary>
/// Handles writing a single Havok object's fields to a binary stream, respecting special Havok data types.
/// </summary>
public static class HavokObjectWriter
{
    public static void Write(BinaryWriter writer, IHavokObject obj, PackerData data)
    {
        HavokType type = HavokTypeRegistry.Instance.GetType(obj.GetType())!;
        long objectStartOffset = data.AddressMap[obj];
        var havokData = HavokData.Of(obj, type);

        foreach (HavokType.Member field in type.Fields)
        {
            // Position the writer at the start of the field
            writer.BaseStream.Position = objectStartOffset + field.Offset;

            havokData.TryGetField<object>(field.Name, out object? value);
            WriteField(writer, value, field, data);
        }
    }

    private static void WriteField(BinaryWriter writer, object? value, HavokType.Member field, PackerData data)
    {
        // Handle special types first
        if (field.Type.Identity != null && field.Type.Identity.StartsWith("hkRelArray"))
        {
            WriteRelArray(writer, value as IHavokObject, data);
            return;
        }

        switch (field.Type.Kind)
        {
            case HavokType.TypeKind.Void:
            case HavokType.TypeKind.Opaque:
                // These types are just placeholders for size; we don't write anything.
                // The stream is already positioned correctly for the next field.
                break;

            case HavokType.TypeKind.Pointer:
                WritePointer(writer, value as IHavokObject, data);
                break;

            case HavokType.TypeKind.Bool:
                writer.Write((bool)(value ?? false));
                break;

            case HavokType.TypeKind.Int:
                switch (field.Type.Size)
                {
                    case 1: writer.Write((sbyte)(value ?? 0)); break;
                    case 2: writer.Write((short)(value ?? 0)); break;
                    case 4: writer.Write((int)(value ?? 0)); break;
                    case 8: writer.Write((long)(value ?? 0)); break;
                    default: writer.Write((int)(value ?? 0)); break;
                }
                break;

            case HavokType.TypeKind.Float:
                switch (field.Type.Size)
                {
                    case 2: writer.Write((Half)(value ?? Half.Zero)); break;
                    case 4: writer.Write((float)(value ?? 0f)); break;
                    case 16: // Vector4 or Matrix
                        if (value is Vector4 v4)
                        {
                            writer.Write(v4.X);
                            writer.Write(v4.Y);
                            writer.Write(v4.Z);
                            writer.Write(v4.W);
                        }
                        else if (value is Quaternion q)
                        {
                            writer.Write(q.X);
                            writer.Write(q.Y);
                            writer.Write(q.Z);
                            writer.Write(q.W);
                        }
                        break;
                    default: writer.Write((float)(value ?? 0f)); break;
                }
                break;

            case HavokType.TypeKind.String:
                // Placeholder
                break;

            case HavokType.TypeKind.Record:
            case HavokType.TypeKind.Array:
                // For inline structs and array containers (like hkArray), their members are individual fields
                // in the parent's type definition and are handled by the main loop. We don't need to do anything here.
                break;

            default:
                throw new NotImplementedException($"Writing for type kind {field.Type.Kind} is not implemented.");
        }
    }

    private static void WritePointer(BinaryWriter writer, IHavokObject? target, PackerData data)
    {
        long sourceOffset = writer.BaseStream.Position;
        if (target is null)
        {
            writer.Write((long)0);
            return;
        }

        long destinationOffset = data.AddressMap[target];
        data.Patches.Add(new PointerPatch(sourceOffset, destinationOffset));
        writer.Write((long)0); // Write placeholder, to be patched by the loader
    }

    private static void WriteRelArray(BinaryWriter writer, IHavokObject? arrayObj, PackerData data)
    {
        // A hkRelArray32 field points to an hkArray object.
        int count = 0;
        IHavokObject? arrayData = null;

        if (arrayObj is not null)
        {
            var arrayDataWrapper = HavokData.Of(arrayObj);
            if (arrayDataWrapper.TryGetField<int>("Count", out int c)) count = c;
            if (arrayDataWrapper.TryGetField<object>("m_data", out object? dataObj)) arrayData = dataObj as IHavokObject;
        }

        writer.Write(count);

        long fieldAddress = writer.BaseStream.Position; // Address of the offset field itself

        int relativeOffset = 0;
        if (arrayData is not null)
        {
            long targetAddress = data.AddressMap[arrayData];
            relativeOffset = (int)(targetAddress - fieldAddress);
        }

        writer.Write(relativeOffset);
    }
}
