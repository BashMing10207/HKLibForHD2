using HKLib.Reflection.hk2018; // Using hk2018 type registry as port of 2018 format
using HKLib.Serialization.Binary;
using HKLib.Serialization.hk2019.Xml;
using HKLib.Reflection.Dynamic;
using IHavokObject = HKLib.hk2018.IHavokObject;
using System.Xml.Linq;

namespace HKLib.CLI;

/// <summary>
/// Provides extension methods for the DynamicTypeRegistry class.
/// </summary>
public static class DynamicTypeRegistryExtensions
{
    /// <summary>
    /// Recursively collects all unique types from a registry and its parents.
    /// </summary>
    public static IEnumerable<DynamicHavokType> GetAllTypes(this DynamicTypeRegistry registry)
    {
        var allTypes = new Dictionary<string, DynamicHavokType>();
        var current = registry;
        while (current != null)
        {
            // This assumes DynamicTypeRegistry has a public property `TypesByHash` which is a dictionary.
        // Final Correction: A registry class often implements IEnumerable directly.
        // We will iterate over the registry object itself to get the types, which is a robust and standard C# pattern.
        foreach (var type in current)
        {
            // Use TryAdd to ensure that types from child registries (which are processed first)
            // take precedence over types with the same name from parent registries.
            allTypes.TryAdd(type.Name, type);
        }
            current = current.Parent;
        }
        return allTypes.Values;
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine(
                "This application converts between binary hkx tagfiles and xml tagfiles. Drag and drop a file onto the exe to use.");
            Console.WriteLine("Press any key to exit...");
            if (!Console.IsInputRedirected) Console.ReadKey();
            return;
        }

        string path = args.First(x => !x.EndsWith(".compendium") && !x.StartsWith("-"));

        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            string outputPath = path[..^3] + "hkx";
            Backup(outputPath);

            void PerformPack()
            {
                byte[]? prependData = null;
                string? prefixArg = args.FirstOrDefault(x => x.EndsWith(".prepend", StringComparison.OrdinalIgnoreCase));
                string defaultSidecar = outputPath + ".prepend";
                string altSidecar = path + ".prepend";

                if (prefixArg is not null && File.Exists(prefixArg))
                {
                    prependData = File.ReadAllBytes(prefixArg);
                }
                else if (File.Exists(defaultSidecar))
                {
                    prependData = File.ReadAllBytes(defaultSidecar);
                }
                else if (File.Exists(altSidecar))
                {
                    prependData = File.ReadAllBytes(altSidecar);
                }
                else
                {
                    try
                    {
                        string xmlText = File.ReadAllText(path);
                        int commentStart = xmlText.LastIndexOf("<!-- PREPEND_DATA:");
                        if (commentStart != -1)
                        {
                            int endComment = xmlText.IndexOf("-->", commentStart);
                            if (endComment != -1)
                            {
                                int dataStart = commentStart + 18;
                                string base64 = xmlText.Substring(dataStart, endComment - dataStart).Trim();
                                prependData = Convert.FromBase64String(base64);
                            }
                        }
                    }
                    catch { }
                }

                var xmlSerializer = new HavokXmlSerializer(HavokTypeRegistry.Instance);
                var binarySerializer = new HavokBinarySerializer();

                if (prependData != null)
                {
                    using FileStream outFs = File.Create(outputPath);
                    outFs.Write(prependData);
                    binarySerializer.Write(outFs, xmlSerializer.Read(path));
                }
                else
                {
                    binarySerializer.Write(xmlSerializer.Read(path), outputPath);
                }
            }

#if DEBUG
            PerformPack();
#else
            try
            {
                PerformPack();
            }
            catch (Exception e)
            {
                Console.WriteLine("File Conversion failed.");
                Console.WriteLine(e);
                Console.WriteLine("Press any key to exit...");
                if (!Console.IsInputRedirected) Console.ReadKey();
            }
#endif
        }
        else if (path.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".main", StringComparison.OrdinalIgnoreCase))
        {
            string outputPath = path.EndsWith(".main", StringComparison.OrdinalIgnoreCase) ? path + ".xml" : path[..^3] + "xml";
            Backup(outputPath);

            byte[]? prependData = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                byte[] pattern = System.Text.Encoding.ASCII.GetBytes("TAG0");
                int found = -1;
                for (int i = 4; i < bytes.Length - 3; i++)
                {
                    if (bytes[i] == pattern[0] && bytes[i + 1] == pattern[1] && bytes[i + 2] == pattern[2] && bytes[i + 3] == pattern[3])
                    {
                        found = i;
                        break;
                    }
                }

                if (found >= 4)
                {
                    int prefixLen = found - 4;
                    if (prefixLen > 0)
                    {
                        prependData = bytes.Take(prefixLen).ToArray();
                        string sidecar = path + ".prepend";
                        File.WriteAllBytes(sidecar, prependData);
                        Console.WriteLine($"Saved leading {prefixLen} bytes to {sidecar} and embedded in xml");
                    }
                }
            }
            catch (Exception)
            {
                // ignore
            }

            void PerformUnpack()
            {
                string? compendiumPath = FindCompendiumPath(path, args);
                var binarySerializer = new HavokBinarySerializer();
                IHavokObject havokObject = binarySerializer.Read(path, compendiumPath);

                // Phase 7.2: Check if the result is the special container for schema-only files.
                if (havokObject is SchemaContainer schemaContainer)
                {
                    Console.WriteLine("Schema-only file detected. Unpacking type information...");
                    SerializeTypeRegistryToXml(schemaContainer.SchemaRegistry, outputPath);
                    Console.WriteLine($"Successfully unpacked type information to \"{outputPath}\"");
                }
                else
                {
                    // Existing logic: Serialize the content object graph to XML.
                    var xmlSerializer = new HavokXmlSerializer(HavokTypeRegistry.Instance);
                    xmlSerializer.Write(havokObject, outputPath);
                    Console.WriteLine($"Successfully unpacked content object to \"{outputPath}\"");
                }

                if (prependData != null)
                {
                    File.AppendAllText(outputPath, $"\n<!-- PREPEND_DATA:{Convert.ToBase64String(prependData)} -->\n");
                }
            }
#if DEBUG
            PerformUnpack();
#else
            try
            {
                PerformUnpack();
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine(
                    $"The file \"{path}\" contains a compendium reference. Drag and drop the associated compendium reference onto the exe along with the file to convert it.");
                Console.WriteLine("Press any key to exit...");
                if (!Console.IsInputRedirected) Console.ReadKey();
            }
            catch (Exception e)
            {
                Console.WriteLine("File Conversion failed.");
                Console.WriteLine(e);
                Console.WriteLine("Press any key to exit...");
                if (!Console.IsInputRedirected) Console.ReadKey();
            }
#endif
        }
        else
        {
            Console.WriteLine("Unsupported file format. Please supply an xml or hkx file.");
            Console.WriteLine("Press any key to exit...");
            if (!Console.IsInputRedirected) Console.ReadKey();
        }
    }

    public static void Backup(string path)
    {
        if (!File.Exists(path)) return;
        File.Move(path, path + ".bak", true);
    }

    private static string? FindCompendiumPath(string inputPath, string[] args)
    {
        string? compendiumPath = args.FirstOrDefault(x =>
            x.EndsWith(".compendium", StringComparison.OrdinalIgnoreCase) ||
            x.EndsWith(".main", StringComparison.OrdinalIgnoreCase) && x != inputPath);

        if (string.IsNullOrEmpty(compendiumPath) || !File.Exists(compendiumPath))
        {
            const string defaultCompendiumName = "global.havok_physics_properties.main";
            string? exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string? fileDir = Path.GetDirectoryName(inputPath);

            string potentialPath1 = Path.Combine(exeDir ?? "", defaultCompendiumName);
            string potentialPath2 = Path.Combine(fileDir ?? "", defaultCompendiumName);

            if (File.Exists(potentialPath1) && Path.GetFullPath(potentialPath1) != Path.GetFullPath(inputPath))
            {
                compendiumPath = potentialPath1;
            }
            else if (File.Exists(potentialPath2) && Path.GetFullPath(potentialPath2) != Path.GetFullPath(inputPath))
            {
                compendiumPath = potentialPath2;
            }
        }

        if (compendiumPath is not null && File.Exists(compendiumPath))
        {
            Console.WriteLine($"Using compendium: {compendiumPath}");
        }

        return compendiumPath;
    }

    private static void SerializeTypeRegistryToXml(DynamicTypeRegistry registry, string path)
    {
        var xDoc = new XDocument(
            new XElement("HavokTypeRegistry",
                new XAttribute("source", "Unpacked from schema file"),
                registry.GetAllTypes().OrderBy(t => t.Name).Select(type =>
                    new XElement("HavokType",
                        new XAttribute("name", type.Name),
                        new XAttribute("kind", type.Kind),
                        new XAttribute("size", type.Size),
                        new XAttribute("alignment", type.Alignment),
                        type.Parent is not null ? new XAttribute("parent", type.Parent.Name) : null,
                        new XElement("Fields",
                            type.GetAllFields().Select(field =>
                                new XElement("Field",
                                    new XAttribute("name", field.Name),
                                    new XAttribute("type", field.Type.Name),
                                    new XAttribute("offset", field.Offset),
                                    field.IsOptional ? new XAttribute("optional", "true") : null
                                )
                            )
                        )
                    )
                )
            )
        );
        xDoc.Save(path);
    }
}
