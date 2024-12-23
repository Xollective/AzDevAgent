using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;

public class Functions
{
    public static string GetEnvironmentVars(string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        return string.Join(",", Environment.GetEnvironmentVariables().Keys.OfType<string>().Where(name => regex.IsMatch(name)));
    }

    public static int RunWithRetry(string workingDirectory, string suffix)
    {
        Environment.CurrentDirectory = workingDirectory;
        Console.WriteLine(workingDirectory);
        int exitCode = -1;

        int maxRetryCount = 3;
        for (int i = 1; i <= maxRetryCount; i++)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(workingDirectory, $"run.{suffix}"), "--once")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            };

            processStartInfo.EnvironmentVariables.Remove("PSModulePath");

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