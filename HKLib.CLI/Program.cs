using System;
using System.IO;
using System.Linq;
using System.Text;
using HavokBinarySerializer2018 = HKLib.Serialization.hk2018.Binary.HavokBinarySerializer;
using HavokXmlSerializer2018 = HKLib.Serialization.hk2018.Xml.HavokXmlSerializer;
using HavokBinarySerializer2019 = HKLib.Serialization.hk2019.Binary.HavokBinarySerializer;
using HavokXmlSerializer2019 = HKLib.Serialization.hk2019.Xml.HavokXmlSerializer;
using HKLib.Serialization.hk2018;

namespace HKLib.CLI;

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
        bool is2019 = true; // default to 2019 for HD2

        if (args.Contains("-2018")) is2019 = false;
        if (args.Contains("-2019")) is2019 = true;

        if (path.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase))
        {
            byte[] fileBytes = File.ReadAllBytes(path);
            string fileString = Encoding.ASCII.GetString(fileBytes);
            if (fileString.Contains("20180100")) is2019 = false;
            else if (fileString.Contains("20190100")) is2019 = true;
        }
        else if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            string fileString = File.ReadAllText(path);
            if (fileString.Contains("20180100") || fileString.Contains("hk2018")) is2019 = false;
            else if (fileString.Contains("20190100") || fileString.Contains("hk2019")) is2019 = true;
        }

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

                if (prependData != null)
                {
                    using FileStream outFs = File.Create(outputPath);
                    outFs.Write(prependData);
                    if (is2019)
                    {
                        var xmlSerializer = new HavokXmlSerializer2019();
                        var binarySerializer = new HavokBinarySerializer2019();
                        binarySerializer.Write(outFs, xmlSerializer.Read(path));
                    }
                    else
                    {
                        var xmlSerializer = new HavokXmlSerializer2018(HKLib.Reflection.hk2018.HavokTypeRegistry.Instance);
                        var binarySerializer = new HavokBinarySerializer2018(HKLib.Reflection.hk2018.HavokTypeRegistry.Instance);
                        binarySerializer.Write(xmlSerializer.Read(path), outFs);
                    }
                }
                else
                {
                    if (is2019)
                    {
                        var xmlSerializer = new HavokXmlSerializer2019();
                        var binarySerializer = new HavokBinarySerializer2019();
                        binarySerializer.Write(xmlSerializer.Read(path), outputPath);
                    }
                    else
                    {
                        var xmlSerializer = new HavokXmlSerializer2018(HKLib.Reflection.hk2018.HavokTypeRegistry.Instance);
                        var binarySerializer = new HavokBinarySerializer2018(HKLib.Reflection.hk2018.HavokTypeRegistry.Instance);
                        binarySerializer.Write(xmlSerializer.Read(path), outputPath);
                    }
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
        else if (path.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase))
        {
            string outputPath = path[..^3] + "xml";
            Backup(outputPath);

            // If file contains leading non-TAG0 bytes, save them to a sidecar so they can be
            // restored when re-packing. Non-fatal: if this fails, conversion still proceeds.
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
                if (is2019)
                {
                    var xmlSerializer = new HavokXmlSerializer2019();
                    var binarySerializer = new HavokBinarySerializer2019();

                    // Try to find compendium argument first
                    string? compendiumPath = args.FirstOrDefault(x =>
                        x.EndsWith(".compendium", StringComparison.OrdinalIgnoreCase) ||
                        x.EndsWith(".main", StringComparison.OrdinalIgnoreCase));

                    // If not provided, try to find it automatically
                    if (string.IsNullOrEmpty(compendiumPath) || !File.Exists(compendiumPath))
                    {
                        const string defaultCompendiumName = "global.havok_physics_properties.main";
                        string? exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                        string? fileDir = Path.GetDirectoryName(path);

                        string potentialPath1 = Path.Combine(exeDir ?? "", defaultCompendiumName);
                        string potentialPath2 = Path.Combine(fileDir ?? "", defaultCompendiumName);

                        if (File.Exists(potentialPath1))
                        {
                            compendiumPath = potentialPath1;
                        }
                        else if (File.Exists(potentialPath2))
                        {
                            compendiumPath = potentialPath2;
                        }
                    }

                    if (compendiumPath is not null && File.Exists(compendiumPath))
                    {
                        Console.WriteLine($"Using compendium: {compendiumPath}");
                        binarySerializer.LoadCompendium(compendiumPath);
                    }
                    xmlSerializer.Write(binarySerializer.Read(path), outputPath);
                }
                else
                {
                    var xmlSerializer = new HavokXmlSerializer2018(HKLib.Reflection.hk2018.HavokTypeRegistry.Instance);
                    var binarySerializer = new HavokBinarySerializer2018(HKLib.Reflection.hk2018.HavokTypeRegistry.Instance);
                    if (args.FirstOrDefault(x => x.EndsWith(".compendium")) is { } compendiumPath)
                    {
                        binarySerializer.LoadCompendium(compendiumPath);
                    }
                    xmlSerializer.Write(binarySerializer.Read(path), outputPath);
                }

                if (prependData != null)
                {
                    File.AppendAllText(outputPath, $"\n<!-- PREPEND_DATA:{Convert.ToBase64String(prependData)} -->\n");
                }
            }

#if DEBUG
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

    /// <summary>
    /// Creates a backup of the file at the given path if it exists to avoid it being overwritten by appending ".bak" to its
    /// name.
    /// </summary>
    public static void Backup(string path)
    {
        if (!File.Exists(path)) return;
        File.Move(path, path + ".bak", true);
    }

    private static long FindSignatureOffset(Stream stream, byte[] pattern)
    {
        const int bufferSize = 8192;
        var buffer = new byte[bufferSize];

        long originalPosition = stream.Position;
        stream.Seek(0, SeekOrigin.Begin);

        long streamPosition = 0;
        int bytesRead;

        try
        {
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i <= bytesRead - pattern.Length; i++)
                {
                    if (buffer[i] != pattern[0]) continue;

                    var found = true;
                    for (var j = 1; j < pattern.Length; j++)
                    {
                        if (buffer[i + j] != pattern[j])
                        {
                            found = false;
                            break;
                        }
                    }

                    if (found)
                    {
                        long tag0Position = streamPosition + i;
                        if (tag0Position >= 4)
                        {
                            return tag0Position - 4; // Return position of the length field
                        }
                    }
                }

                streamPosition += bytesRead;

                // Seek back to handle patterns spanning buffer boundaries
                if (stream.Position < stream.Length)
                {
                    long seekBack = Math.Min(stream.Position, pattern.Length - 1);
                    stream.Seek(-seekBack, SeekOrigin.Current);
                    streamPosition -= seekBack;
                }
            }
        }
        finally
        {
            stream.Seek(originalPosition, SeekOrigin.Begin);
        }

        return -1;
    }
}
