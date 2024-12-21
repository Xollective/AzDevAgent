using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using TaskResult = Microsoft.TeamFoundation.DistributedTask.WebApi.TaskResult;
using TimelineRecord = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecord;

namespace AzDevAgentRunner;

public class RunOperation(SubProcessRunner? agentRunner = null)
{
    public required string AdoBuildUri;
    public required string AdoToken;

    public double PollSeconds = 2;
    public double AgentTimeoutSeconds = 30;

    public const string IsMarkedKey = "Marked";

    public async Task<int> RunAsync(CancellationTokenSource agentCancellation)
    {
        var runTask = default(ValueTask<int>?);
        runTask = agentRunner?.RunAsync();

        runTask?.AsTask().ContinueWith(t =>
        {
            agentCancellation.Cancel();
        });

        await RunAsyncCore(agentCancellation);

        return await runTask.GetValueOrDefault();
    }

    private async Task RunAsyncCore(CancellationTokenSource agentCancellation)
    {
        var token = agentCancellation.Token;
        var adoBuildUri = BuildUri.ParseBuildUri(AdoBuildUri);
        var taskInfo = adoBuildUri.DeserializeFromParameters<TaskInfo>();

        var connection = new VssConnection(adoBuildUri.OrganizationUri, new VssBasicCredential(AdoToken, string.Empty));
        var client = connection.GetClient<BuildHttpClient>();
        var taskClient = connection.GetClient<TaskHttpClient>();
        var agentClient = connection.GetClient<TaskAgentHttpClient>();

        var build = await client.GetBuildAsync(adoBuildUri.Project, adoBuildUri.BuildId);

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

    private bool IsCompleted(Build build)
    {
        var result = build.Result;
        return result != null && result != BuildResult.None;
    }
}