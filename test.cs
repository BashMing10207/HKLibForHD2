using System;
using System.IO;
using System.Text;

class Program
{
    static void Main()
    {
        byte[] bytes = File.ReadAllBytes("global.havok_physics_properties.main");
        int start = -1;
        for (int i = 0; i < bytes.Length - 4; i++)
        {
            if (bytes[i] == 'T' && bytes[i+1] == 'A' && bytes[i+2] == 'G' && bytes[i+3] == '0')
            {
                start = i;
                break;
            }
        }
        if (start != -1)
        {
            Console.WriteLine("Found TAG0 at " + start);
            uint numSec = BitConverter.ToUInt32(bytes, start + 24);
            Console.WriteLine("NumSec: " + numSec);
            for (int s = 0; s < numSec; s++)
            {
                int secStart = start + 40 + (s * 32);
                string name = Encoding.ASCII.GetString(bytes, secStart, 16);
                Console.WriteLine("Section " + s + ": " + name.Replace("\0", "\\0"));
            }
        }
        else
        {
            Console.WriteLine("TAG0 not found.");
        }
    }
}