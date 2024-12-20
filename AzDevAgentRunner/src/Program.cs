using System;
using System.CommandLine;
using System.Diagnostics;
using Microsoft.VisualStudio.Services.CircuitBreaker;

namespace AzDevAgentRunner;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var precedingArgs = new List<string>();
        var remainingArgs = new List<string>();

        var list = precedingArgs;
        foreach (var arg in args)
        {
            if (arg == "--" && list != remainingArgs)
            {
                list = remainingArgs;
            }
            else
            {
                list.Add(arg);
            }
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var agentRunTask = remainingArgs.Count == 0 
            ? Task.FromResult(0)
            : RunAgentAsync(remainingArgs[0], remainingArgs.Skip(1), cts.Token);

        if (remainingArgs.Count > 0)
        {
            agentRunTask.ContinueWith(t =>
            {
                cts.Cancel();
            }).GetAwaiter();

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        var rootCommand = new RootCommand("Run multiple self-hosted agents");
        CliModel.Bind<RunCommand>(
            rootCommand,
            m =>
            {
                var result = new RunCommand()
                {
                    AdoBuildUri = m.Option(c => ref c.AdoBuildUri, name: "uri", required: true,
                        description: "annotated build uri (e.g. $(System.CollectionUri)$(System.TeamProject)?buildId=$(Build.BuildId)&jobId=$(System.JobId)&planId=$(System.PlanId)&taskId=$(System.TaskInstanceId)&timelineId=$(System.TimelineId) )"),
                    RunnerId = m.Option(c => ref c.RunnerId, name: "runnerId", required: true),
                    RunnerIds = m.Option(c => ref c.RunnerIds, name: "runnerIds", required: true),
                    AdoToken = m.Option(c => ref c.AdoToken, name: "pat", description: "The access token (e.g. $(System.AccessToken) )", required: true),

                };

                m.Option(c => ref c.PollSeconds, name: "pollSeconds");
                m.Option(c => ref c.AgentTimeoutSeconds, name: "timeoutSeconds");

                return result;
            },
            r => r.RunAsync(cts.Token));

        await rootCommand.InvokeAsync(precedingArgs.ToArray());

        return await agentRunTask;
    }

    public static async Task<int> RunAgentAsync(string executable, IEnumerable<string> args, CancellationToken token)
    {
        Console.WriteLine($"Executable: {executable}");
        Console.WriteLine($"Arguments: {string.Join(" ", args)}");
        int exitCode = -1;

        int maxRetryCount = 3;
        for (int i = 1; i <= maxRetryCount; i++)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo(executable, args)
            {
                UseShellExecute = false,
            };

            processStartInfo.EnvironmentVariables.Remove("PSModulePath");

            Process process = new Process();
            process.StartInfo = processStartInfo;

            process.Start();
            using var r = token.Register(() =>
            {
                try
                {
                    process.Kill();
                }
                catch { }
            });
            await process.WaitForExitAsync();

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