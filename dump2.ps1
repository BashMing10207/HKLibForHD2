$code = @"
using System;
using System.IO;

public class Dumper
{
    public static void Run()
    {
        byte[] bytes = File.ReadAllBytes(""global.havok_physics_properties.main"");
        byte[] pattern = System.Text.Encoding.ASCII.GetBytes(""TBDY"");
        int tbdyIdx = -1;
        for (int i = 0; i < bytes.Length - 3; i++)
        {
            if (bytes[i] == pattern[0] && bytes[i + 1] == pattern[1] && bytes[i + 2] == pattern[2] && bytes[i + 3] == pattern[3])
            {
                tbdyIdx = i;
                break;
            }
        }

        if (tbdyIdx >= 0)
        {
            Console.WriteLine(""TBDY found at: "" + tbdyIdx);
            int start = tbdyIdx - 4;
            int len = BitConverter.ToInt32(bytes, start);
            Console.WriteLine(""Length: "" + len);
            byte[] data = new byte[Math.Min(len, 100)];
            Array.Copy(bytes, tbdyIdx + 4, data, 0, data.Length);
            Console.WriteLine(""Data: "" + BitConverter.ToString(data));
        }
    }
}
"@
Add-Type -TypeDefinition $code
[Dumper]::Run()
