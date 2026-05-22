using System;
using System.Threading.Tasks;

namespace HKLib.Cli;

public static class CliOrchestrator
{
    public static Task<int> Run(string[] args)
    {
        Console.WriteLine("CLI functionality is currently disabled pending updates.");
        return Task.FromResult(0);
    }
}
