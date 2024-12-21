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

        var agentRunner = remainingArgs.Count > 0
            ? new SubProcessRunner(remainingArgs[0], remainingArgs.Skip(1), cts.Token)
            : null;

        var rootCommand = new RootCommand("Run multiple self-hosted agents");
        CliModel.Bind<RunCommand>(
            rootCommand,
            m =>
            {
                var result = new RunCommand(agentRunner)
                {
                    AdoBuildUri = m.Option(c => ref c.AdoBuildUri, name: "uri", required: true,
                        description: "annotated build uri (e.g. $(System.CollectionUri)$(System.TeamProject)?buildId=$(Build.BuildId)&jobId=$(System.JobId)&planId=$(System.PlanId)&taskId=$(System.TaskInstanceId)&timelineId=$(System.TimelineId) )"),
                    AdoToken = m.Option(c => ref c.AdoToken, name: "pat", description: "The access token (e.g. $(System.AccessToken) )", required: true),
                };

                m.Option(c => ref c.PollSeconds, name: "pollSeconds");
                m.Option(c => ref c.AgentTimeoutSeconds, name: "timeoutSeconds");

                return result;
            },
            r => r.RunAsync(cts));

        return await rootCommand.InvokeAsync(precedingArgs.ToArray());
    }
}