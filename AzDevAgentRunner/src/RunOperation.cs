using System.CommandLine;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using TaskResult = Microsoft.TeamFoundation.DistributedTask.WebApi.TaskResult;
using TimelineRecord = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecord;

namespace AzDevAgentRunner;

public class RunOperation(IConsole Console, CancellationTokenSource agentCancellation, SubProcessRunner? agentRunner = null) : TaskOperationBase(Console)
{
    public double AgentTimeoutSeconds = 30;

    public const string IsMarkedKey = "Marked";

    protected override async Task<int> RunCoreAsync()
    {
        var runTask = default(ValueTask<int>?);
        runTask = agentRunner?.RunAsync();

        runTask?.AsTask().ContinueWith(t =>
        {
            agentCancellation.Cancel();
        });

        await RunHelperAsync(agentCancellation);

        return await runTask.GetValueOrDefault();
    }

    private async Task RunHelperAsync(CancellationTokenSource agentCancellation)
    {
        var records = await taskClient.UpdateTimelineRecordsAsync(
            scopeIdentifier: build.Project.Id,
            planType: taskInfo.HubName,
            planId: taskInfo.PlanId,
            timelineId: taskInfo.TimelineId,
            new[]
            {
                new TimelineRecord()
                {
                    Id = taskInfo.TaskId,
                    Variables =
                    {
                        ["TaskId"] = taskInfo.TaskId.ToString()
                    }
                }
            });

        var record = records[0];

        async Task setTaskResult(TaskResult result)
        {
            if (record.Result != null)
            {
                Console.WriteLine($"Setting result to {result}");
                await taskClient.RaisePlanEventAsync(
                    scopeIdentifier: build.Project.Id,
                    planType: taskInfo.HubName,
                    planId: taskInfo.PlanId,
                    eventData: new TaskCompletedEvent(
                        taskInfo.JobId,
                        taskInfo.TaskId,
                        result));
            }
            else
            {
                Console.WriteLine($"Skipping due to exit result: {result}");
            }
        }

        try
        {
            await setTaskResult(TaskResult.Succeeded);

            while (!IsCompleted(build) && !agentCancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(PollSeconds));

                build = await client.GetBuildAsync(adoBuildUri.Project, adoBuildUri.BuildId);
            }

            if (!IsCompleted(build) && !(await GetBuildProperties()).ContainsKey(taskInfo.AllJobsReservedKey()))
            {
                AppendLinesToEnvFile(FileEnvVar.GITHUB_OUTPUT,
                    $"{OutputNames.hasMoreJobs}=true");
            }

            agentCancellation.CancelAfter(TimeSpan.FromSeconds(AgentTimeoutSeconds));
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || ex is TaskCanceledException)
            {
                await setTaskResult(TaskResult.Canceled);
            }
        }
    }
}