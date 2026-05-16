using System;
using System.IO;
using System.Numerics;
using HKLib.hk2018;
using HKLib.Reflection;

namespace HKLib.Serialization;

/// <summary>
/// Handles writing a single Havok object's fields to a binary stream, respecting special Havok data types.
/// </summary>
public static class HavokObjectWriter
{
    public static void Write(BinaryWriter writer, IHavokObject obj, PackerData data)
    {
        HavokType type = HavokTypeRegistry.GetType(obj.GetType())!;
        long objectStartOffset = data.AddressMap[obj];

        foreach (HavokType.Member field in type.Fields)
        {
            // Position the writer at the start of the field
            writer.BaseStream.Position = objectStartOffset + field.Offset;

            object? value = HavokData.GetField(obj, field.Name);
            WriteField(writer, value, field, data);
        }
    }

    private static void WriteField(BinaryWriter writer, object? value, HavokType.Member field, PackerData data)
    {
        // Handle special types first
        if (field.Type.SubKind == HavokType.TypeSubKind.RelArray)
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
            case HavokType.TypeKind.RefPtr:
                WritePointer(writer, value as IHavokObject, data);
                break;

            case HavokType.TypeKind.Bool:
                writer.Write((bool)(value ?? false));
                break;
            case HavokType.TypeKind.Int8:
                writer.Write((sbyte)(value ?? 0));
                break;
            case HavokType.TypeKind.UInt8:
                writer.Write((byte)(value ?? 0));
                break;
            case HavokType.TypeKind.Int16:
                writer.Write((short)(value ?? 0));
                break;
            case HavokType.TypeKind.UInt16:
                writer.Write((ushort)(value ?? 0));
                break;
            case HavokType.TypeKind.Int32:
                writer.Write((int)(value ?? 0));
                break;
            case HavokType.TypeKind.UInt32:
                writer.Write((uint)(value ?? 0));
                break;
            case HavokType.TypeKind.Int64:
                writer.Write((long)(value ?? 0));
                break;
            case HavokType.TypeKind.UInt64:
                writer.Write((ulong)(value ?? 0));
                break;

            case HavokType.TypeKind.Real:
                writer.Write((float)(value ?? 0f));
                break;
            case HavokType.TypeKind.Half:
                writer.Write((Half)(value ?? Half.Zero));
                break;

            case HavokType.TypeKind.Vector4:
                writer.Write((Vector4)(value ?? Vector4.Zero));
                break;
            case HavokType.TypeKind.Quaternion:
                writer.Write((Quaternion)(value ?? Quaternion.Identity));
                break;

            case HavokType.TypeKind.Struct:
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
        var array = arrayObj as hkArray;
        writer.Write(array?.Count ?? 0);

        long fieldAddress = writer.BaseStream.Position; // Address of the offset field itself
        IHavokObject? arrayData = array is null ? null : (IHavokObject?)HavokData.GetField(array, "m_data");

        int relativeOffset = 0;
        if (arrayData is not null)
        {
            long targetAddress = data.AddressMap[arrayData];
            relativeOffset = (int)(targetAddress - fieldAddress);
        }

        writer.Write(relativeOffset);
    }
}