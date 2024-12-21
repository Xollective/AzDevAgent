﻿using System;
using System.CommandLine;
using System.Diagnostics;

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

        var rootCommand = new RootCommand
        {
            CliModel.Bind<RunOperation>(
                new Command("runagent", "Run command until it completes or build finishes. Also completes agent invocation task."),
                m =>
                {
                    var result = new RunOperation(agentRunner)
                    {
                        AdoBuildUri = m.Option(c => ref c.AdoBuildUri, name: "uri", required: true,
                            description: "annotated build uri (e.g. $(System.CollectionUri)$(System.TeamProject)?buildId=$(Build.BuildId)&jobId=$(System.JobId)&planId=$(System.PlanId)&taskId=$(System.TaskInstanceId)&timelineId=$(System.TimelineId) )"),
                        AdoToken = m.Option(c => ref c.AdoToken, name: "pat", description: "The access token (e.g. $(System.AccessToken) )", required: true),
                    };

                    m.Option(c => ref c.PollSeconds, name: "pollSeconds");
                    m.Option(c => ref c.AgentTimeoutSeconds, name: "timeoutSeconds");

                    return result;
                },
                r => r.RunAsync(cts)),

            CliModel.Bind<ReserveOperation>(
                new Command("reserve", "Reserve a slot for a github runner Azure Devops agent"),
                m =>
                {
                    var result = new ReserveOperation(m.Console)
                    {
                        AdoBuildUri = m.Option(c => ref c.AdoBuildUri, name: "uri", required: true,
                            description: "annotated build uri (e.g. $(System.CollectionUri)$(System.TeamProject)?buildId=$(Build.BuildId)&jobId=$(System.JobId)&planId=$(System.PlanId)&taskId=$(System.TaskInstanceId)&timelineId=$(System.TimelineId) )"),
                        AdoToken = m.Option(c => ref c.AdoToken, name: "pat", description: "The access token (e.g. $(System.AccessToken) )", required: true),
                        AgentName = m.Option(c => ref c.AgentName, name: "agentName", description: "The name of the agent", required: false),
                        SlotCount = m.Option(c => ref c.SlotCount, name: "slotCount", description: "The number of slots available for reservation", required: true),
                    };

                    m.Option(c => ref c.PollSeconds, name: "pollSeconds");

                    return result;
                },
                r => r.RunAsync())
        };

        return await rootCommand.InvokeAsync(precedingArgs.ToArray());
    }
}