﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HKLib.Reflection;
using HKLib.Reflection.Dynamic;

namespace HKLib.Serialization.hk2019.Xml
{
    public class HavokXmlSerializer : IXmlSerializer
    {
        private readonly DynamicTypeRegistry _registry;
        private readonly Dictionary<IHavokObject, string> _objectIds = new();
        private int _nextId;

        public HavokXmlSerializer(DynamicTypeRegistry registry)
        {
            _registry = registry;
        }

        public object Read(string path)
        {
            XDocument doc = XDocument.Load(path);
            XElement? dataSection = doc.Root?.Element("hksection");
            if (dataSection is null) throw new InvalidDataException("hksection not found in XML.");

            var objects = new Dictionary<string, DynamicHavokObject>();
            var objectElements = new Dictionary<string, XElement>();

            // Pass 1: Create all objects
            foreach (XElement objectElement in dataSection.Elements("hkobject"))
            {
                string? name = objectElement.Attribute("name")?.Value;
                string? className = objectElement.Attribute("class")?.Value;
                if (name is null || className is null) continue;

                DynamicHavokType? type = _registry.GetType(className);
                if (type is null) throw new KeyNotFoundException($"Type '{className}' not found in registry.");

                var havokObject = new DynamicHavokObject(type);
                objects.Add(name, havokObject);
                objectElements.Add(name, objectElement);
            }

            // Pass 2: Populate fields
            foreach (var (id, havokObject) in objects)
            {
                XElement objectElement = objectElements[id];
                PopulateFields(havokObject, objectElement, objects);
            }

            string? rootId = dataSection.Elements("hkobject").FirstOrDefault()?.Attribute("name")?.Value;
            if (rootId is null || !objects.TryGetValue(rootId, out DynamicHavokObject? rootObject))
            {
                throw new InvalidDataException("Root object not found in XML.");
            }

            return rootObject;
        }

        private void PopulateFields(DynamicHavokObject havokObject, XElement element,
            IReadOnlyDictionary<string, DynamicHavokObject> objects)
        {
            var fields = havokObject.Type.GetAllFields();
            var fieldDict = fields.ToDictionary(f => f.Name);

            foreach (XElement paramElement in element.Elements("hkparam"))
            {
                string? fieldName = paramElement.Attribute("name")?.Value;
                if (fieldName is null) continue;

                if (fieldDict.TryGetValue(fieldName, out DynamicHavokField? field))
                {
                    havokObject.Fields[fieldName] = ParseFieldValue(paramElement, field, objects);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Warning: Field '{fieldName}' not found in type '{havokObject.Type.Name}'.");
                }
            }
        }

        public void Write(object root, string path, byte[]? prependData = null)
        {
            if (root is not IHavokObject havokObject)
            {
                throw new ArgumentException("Root object must be an IHavokObject.", nameof(root));
            }

            _objectIds.Clear();
            _nextId = 1;

            // First pass: find all objects and assign IDs
            AssignObjectIds(havokObject);

            XDocument doc = new();
            XElement packfile = new("hkpackfile",
                new XAttribute("classversion", "11"),
                new XAttribute("contentsversion", "hk_2019.1.0-r1"));
            doc.Add(packfile);

            XElement dataSection = new("hksection", new XAttribute("name", "__data__"));
            packfile.Add(dataSection);

            // Second pass: serialize objects, ordered by their ID for readability
            foreach (var (obj, id) in _objectIds.OrderBy(kvp => int.Parse(kvp.Value[1..])))
            {
                dataSection.Add(SerializeObject(obj, id));
            }

            if (prependData != null)
            {
                doc.Root?.Add(new XComment($" PREPEND_DATA:{Convert.ToBase64String(prependData)} "));
            }

            doc.Save(path);
        }

        private void AssignObjectIds(IHavokObject? obj)
        {
            if (obj is null || _objectIds.ContainsKey(obj))
            {
                return;
            }

            _objectIds.Add(obj, $"#{_nextId++}");

            if (obj is not DynamicHavokObject dho) return;

            // Recursively find all referenced objects
            foreach (object? fieldValue in dho.Fields.Values)
            {
                if (fieldValue is IHavokObject childObj)
                {
                    AssignObjectIds(childObj);
                }
                else if (fieldValue is object[] array)
                {
                    foreach (object? item in array)
                    {
                        if (item is IHavokObject arrayObj)
                        {
                            AssignObjectIds(arrayObj);
                        }
                    }
                }
            }
        }

        private XElement SerializeObject(IHavokObject obj, string id)
        {
            if (obj is not DynamicHavokObject dho)
            {
                throw new NotSupportedException("Can only serialize DynamicHavokObject.");
            }

            XElement objectElement = new("hkobject",
                new XAttribute("name", id),
                new XAttribute("class", dho.Type.Name),
                new XAttribute("signature", $"0x{dho.GetHashCode():x8}"));

            foreach (var field in dho.Type.GetAllFields())
            {
                if (dho.Fields.TryGetValue(field.Name, out object? fieldValue))
                {
                    objectElement.Add(SerializeField(field, fieldValue));
                }
            }

            return objectElement;
        }

        private XElement SerializeField(DynamicHavokField field, object? value)
        {
            XElement paramElement = new("hkparam", new XAttribute("name", field.Name));

            if (value is null)
            {
                paramElement.Value = "null";
                return paramElement;
            }

            switch (value)
            {
                case IHavokObject havokObject:
                    paramElement.Value = _objectIds.TryGetValue(havokObject, out string? id) ? id : "null";
                    break;
                case object[] array:
                    paramElement.Add(new XAttribute("numelements", array.Length));
                    if (array.Length > 0 && array.FirstOrDefault(x => x != null) is IHavokObject)
                    {
                        paramElement.Value = string.Join(" ",
                            array.Select(o => o is null ? "null" : _objectIds[(IHavokObject)o]));
                    }
                    else
                    {
                        paramElement.Value = $"({string.Join(" ", array.Select(FormatPrimitive))})";
                    }

                    break;
                default:
                    paramElement.Value = FormatPrimitive(value);
                    break;
            }

            return paramElement;
        }

        private object? ParseFieldValue(XElement paramElement, DynamicHavokField field,
            IReadOnlyDictionary<string, DynamicHavokObject> objects)
        {
            string valueStr = paramElement.Value.Trim();
            if (valueStr == "null") return null;

            if (field.Type.Kind == HavokType.TypeKind.Pointer || field.Type.Name == "hkcstring" ||
                field.Type.Name == "hkStringPtr")
            {
                if (valueStr.StartsWith("#"))
                {
                    return objects.TryGetValue(valueStr, out var obj)
                        ? obj
                        : throw new InvalidDataException($"Object reference '{valueStr}' not found.");
                }

                // It's a string for hkStringPtr
                return valueStr;
            }

            if (paramElement.Attribute("numelements") is { } numElementsAttr)
            {
                int numElements = int.Parse(numElementsAttr.Value);
                if (numElements == 0) return Array.CreateInstance(typeof(object), 0);

                DynamicHavokType? elementType = field.Type.SubType ?? field.Type.TemplateParameters.FirstOrDefault()?.Type;
                if (elementType is null)
                {
                    var tParam = field.Type.TemplateParameters.FirstOrDefault(p => p.Name == "T" && p.Kind == "Type");
                    elementType = tParam?.Type;
                }

                if (elementType is null)
                    throw new InvalidDataException($"Could not determine element type for array field '{field.Name}'.");

                if (elementType.Kind == HavokType.TypeKind.Pointer)
                {
                    string[] refs = valueStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var objArray = new IHavokObject?[numElements];
                    for (int i = 0; i < numElements; i++)
                    {
                        if (refs[i] == "null") objArray[i] = null;
                        else if (objects.TryGetValue(refs[i], out var obj)) objArray[i] = obj;
                        else throw new InvalidDataException($"Object reference '{refs[i]}' not found in pointer array.");
                    }

                    return objArray;
                }
                else
                {
                    valueStr = valueStr.Trim('(', ')').Trim();
                    string[] values = valueStr.Split(new[] { ' ', '\t', '\n', '\r' },
                        StringSplitOptions.RemoveEmptyEntries);
                    var array = new object?[numElements];
                    for (int i = 0; i < numElements; i++)
                    {
                        array[i] = ParsePrimitive(values[i], elementType);
                    }

                    return array;
                }
            }

            // C-style array or tuple
            if (valueStr.StartsWith("("))
            {
                valueStr = valueStr.Trim('(', ')');
                string[] values = valueStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                var nParam = field.Type.TemplateParameters.FirstOrDefault(p => p.Name == "N" && p.Kind == "Value");
                if (nParam != null && int.TryParse(nParam.Value, out int arraySize))
                {
                    var tParam = field.Type.TemplateParameters.FirstOrDefault(p => p.Name == "T" && p.Kind == "Type");
                    DynamicHavokType? elementType = tParam?.Type ?? _registry.GetType(field.Type.Name.Split('[')[0])!;
                    var cStyleArray = new object?[arraySize];
                    for (int i = 0; i < arraySize; i++)
                    {
                        cStyleArray[i] = ParsePrimitive(values[i], elementType);
                    }

                    return cStyleArray;
                }

                return ParseTuple(values, field.Type);
            }

            return ParsePrimitive(valueStr, field.Type);
        }

        private object ParsePrimitive(string value, DynamicHavokType type)
        {
            return type.Kind switch
            {
                HavokType.TypeKind.Bool => bool.Parse(value),
                HavokType.TypeKind.Char or HavokType.TypeKind.Int8 => sbyte.Parse(value, CultureInfo.InvariantCulture),
                HavokType.TypeKind.UInt8 => byte.Parse(value, CultureInfo.InvariantCulture),
                HavokType.TypeKind.Int16 => short.Parse(value, CultureInfo.InvariantCulture),
                HavokType.TypeKind.UInt16 => ushort.Parse(value, CultureInfo.InvariantCulture),
                HavokType.TypeKind.Int32 or HavokType.TypeKind.Enum or HavokType.TypeKind.Flags => int.Parse(value,
                    CultureInfo.InvariantCulture),
                HavokType.TypeKind.UInt32 => uint.Parse(value, CultureInfo.InvariantCulture),
                HavokType.TypeKind.Int64 => long.Parse(value, CultureInfo.InvariantCulture),
                HavokType.TypeKind.UInt64 => ulong.Parse(value, CultureInfo.InvariantCulture),
                HavokType.TypeKind.Half or HavokType.TypeKind.Real or HavokType.TypeKind.Float => float.Parse(value,
                    CultureInfo.InvariantCulture),
                HavokType.TypeKind.Double => double.Parse(value, CultureInfo.InvariantCulture),
                HavokType.TypeKind.CString or HavokType.TypeKind.String => value,
                _ => throw new NotImplementedException(
                    $"Parsing for primitive type kind '{type.Kind}' ('{type.Name}') is not implemented.")
            };
        }

        private object ParseTuple(string[] values, DynamicHavokType type)
        {
            return type.Name switch
            {
                "hkVector4" => new Vector4(
                    float.Parse(values[0], CultureInfo.InvariantCulture),
                    float.Parse(values[1], CultureInfo.InvariantCulture),
                    float.Parse(values[2], CultureInfo.InvariantCulture),
                    float.Parse(values[3], CultureInfo.InvariantCulture)),
                "hkQuaternion" => new Quaternion(
                    float.Parse(values[0], CultureInfo.InvariantCulture),
                    float.Parse(values[1], CultureInfo.InvariantCulture),
                    float.Parse(values[2], CultureInfo.InvariantCulture),
                    float.Parse(values[3], CultureInfo.InvariantCulture)),
                _ => throw new NotImplementedException($"Parsing for tuple type '{type.Name}' is not implemented.")
            };
        }

        private static string FormatPrimitive(object? value)
        {
            return value switch
            {
                null => "null",
                bool b => b ? "true" : "false",
                float f => f.ToString("G9", CultureInfo.InvariantCulture),
                double d => d.ToString("G17", CultureInfo.InvariantCulture),
                Vector3 v3 => $"({v3.X:G9} {v3.Y:G9} {v3.Z:G9})",
                Vector4 v4 => $"({v4.X:G9} {v4.Y:G9} {v4.Z:G9} {v4.W:G9})",
                Quaternion q => $"({q.X:G9} {q.Y:G9} {q.Z:G9} {q.W:G9})",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }
    }
}