using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

public class Program
{
    public static int Run(string workingDirectory)
    {
        Environment.CurrentDirectory = workingDirectory;
        Console.WriteLine(workingDirectory);
        int exitCode = -1;

        int maxRetryCount = 3;
        for (int i = 1; i <= maxRetryCount; i++)
        {

            ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(workingDirectory, "run.cmd"), "--once")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            };
            Process process = new Process();
            process.StartInfo = processStartInfo;

            process.Start();
            process.WaitForExit();

            exitCode = process.ExitCode;
            if (exitCode == 0)
            {
                Console.WriteLine("SUCCESS: Process completed with code '{0}'", exitCode);
                return exitCode;
            }

            Thread.Sleep(TimeSpan.FromSeconds(5));
            Console.WriteLine("::warning::Process exited with code '{0}'." + ((i != maxRetryCount) ? " Retrying..." : " Reached max retry count. Failing."),
                exitCode);
        }

        return exitCode;
    }
}