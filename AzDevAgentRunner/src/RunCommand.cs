using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Azure.Pipelines.WebApi;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using TaskResult = Microsoft.TeamFoundation.DistributedTask.WebApi.TaskResult;
using TimelineRecord = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecord;

namespace AzDevAgentRunner;

public class RunCommand(SubProcessRunner? agentRunner = null)
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

        async Task setTaskResult(TaskResult result)
        {
            if (result == TaskResult.Succeeded)
            {
                //var orch = await taskClient.GetPlanAsync(
                //    scopeIdentifier: build.Project.Id,
                //    hubName: taskInfo.HubName,
                //    planId: taskInfo.PlanId
                //    );

                //var pools = await agentClient.GetAgentPoolsAsync("RunnerPool");
                //var pool = pools[0];

                //var reqs1 = await agentClient.GetAgentRequestsAsync(pool.Id, 100);

                //var reqs = await agentClient.GetAgentRequestsForPlanAsync(pool.Id, planId: Guid.Parse("b71b1ff9-6954-4dc3-9cbf-9d7a94b5a462"));// taskInfo.PlanId);

                //var records = await taskClient.GetRecordsAsync(
                //    scopeIdentifier: build.Project.Id,
                //    hubName: taskInfo.HubName,
                //    planId: taskInfo.PlanId,
                //    timelineId: taskInfo.TimelineId);

                await taskClient.UpdateTimelineRecordsAsync(
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
            }

            await taskClient.RaisePlanEventAsync(
                scopeIdentifier: build.Project.Id,
                planType: taskInfo.HubName,
                planId: taskInfo.PlanId,
                eventData: new TaskCompletedEvent(
                    taskInfo.JobId,
                    taskInfo.TaskId,
                    result));
        }

        try
        {
            var qualifier = $"ghagents.{taskInfo.TaskId}";
            string getQualifiedPropertyKey(string runnerId) => $"{qualifier}{runnerId}";

            async Task<PropertiesCollection> setProperty(string key)
            {
                var props = new PropertiesCollection()
                {
                    [getQualifiedPropertyKey(key)] = "1"
                };

                return await client.UpdateBuildPropertiesAsync(props, build.Project.Id, build.Id);
            }

            async Task<bool> hasProperty(string key)
            {
                var response = await client.GetBuildPropertiesAsync(build.Project.Id, build.Id);
                return response.ContainsKey(getQualifiedPropertyKey(key));
            }

            if (!await hasProperty(IsMarkedKey))
            {
                Console.WriteLine("Machine is master");
                await setTaskResult(TaskResult.Succeeded);
                await setProperty(IsMarkedKey);
            }

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

    public record TaskInfo(Guid JobId, Guid PlanId, Guid TaskId, Guid TimelineId, string HubName = "build");
}
