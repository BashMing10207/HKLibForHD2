using HKLib.Serialization.hk2018.Binary;
using HKLib.Serialization.hk2018.Xml;

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

        HavokBinarySerializer binarySerializer = new();
        HavokXmlSerializer xmlSerializer = new();

        string path = args.First(x => !x.EndsWith(".compendium"));
        if (path.EndsWith(".xml"))
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
                    binarySerializer.Write(xmlSerializer.Read(path), outFs);
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
        else if (path.EndsWith(".hkx"))
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

            if (args.FirstOrDefault(x => x.EndsWith(".compendium")) is { } compendiumPath)
            {
                binarySerializer.LoadCompendium(compendiumPath);
            }

            void PerformUnpack()
            {
                xmlSerializer.Write(binarySerializer.Read(path), outputPath);
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
}