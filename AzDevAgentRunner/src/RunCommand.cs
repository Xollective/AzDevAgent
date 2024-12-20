using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using TaskResult = Microsoft.TeamFoundation.DistributedTask.WebApi.TaskResult;
using TimelineRecord = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecord;

namespace AzDevAgentRunner;

public class RunCommand
{
    public required string RunnerIds;
    public required string RunnerId;
    public required string AdoBuildUri;
    public required string AdoToken;

    public double PollSeconds = 2;
    public double AgentTimeoutSeconds = 30;

    public const string ReadyRunnerId = "*ready*";

    public async Task RunAsync(CancellationToken token)
    {
        var adoBuildUri = BuildUri.ParseBuildUri(AdoBuildUri);
        var taskInfo = adoBuildUri.DeserializeFromParameters<TaskInfo>();

        var connection = new VssConnection(adoBuildUri.OrganizationUri, new VssBasicCredential(AdoToken, string.Empty));
        var client = connection.GetClient<BuildHttpClient>();
        var taskClient = connection.GetClient<TaskHttpClient>();

        var build = await client.GetBuildAsync(adoBuildUri.Project, adoBuildUri.BuildId);

        async Task setTaskResult(TaskResult result)
        {
            if (result == TaskResult.Succeeded)
            {
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
            var runnerIds = RunnerIds.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            bool isMaster = runnerIds[0] == RunnerId;

            var qualifier = $"ghagents.{taskInfo.TaskId}";
            string getQualifiedPropertyKey(string runnerId) => $"{qualifier}{runnerId}";
            var properties = await markAgent(RunnerId);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(AgentTimeoutSeconds));

            // Register this machine
            async Task<PropertiesCollection> markAgent(string runnerId)
            {
                var props = new PropertiesCollection()
                {
                    [getQualifiedPropertyKey(runnerId)] = "1"
                };

                return await client.UpdateBuildPropertiesAsync(props, adoBuildUri.Project, adoBuildUri.BuildId);
            }

            bool hasAllAgents(PropertiesCollection response)
            {
                cts.Token.ThrowIfCancellationRequested();

                foreach (var runnerId in runnerIds)
                {
                    if (!response.ContainsKey(getQualifiedPropertyKey(runnerId)))
                    {
                        Console.WriteLine($"Waiting for runner: {runnerId}");
                        return false;
                    }
                }

                Console.WriteLine($"All machines ready.");
                return true;
            }

            if (isMaster)
            {
                Console.WriteLine("Machine is master");

                // Wait for all agents to be registered
                while (!hasAllAgents(properties))
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds), cts.Token);

                    properties = await client.GetBuildPropertiesAsync(adoBuildUri.Project, adoBuildUri.BuildId);
                }

                await setTaskResult(TaskResult.Succeeded);
                properties = await markAgent(ReadyRunnerId);
            }
            else
            {
                runnerIds.Add(ReadyRunnerId);

                while (!hasAllAgents(properties))
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds), cts.Token);

                    properties = await client.GetBuildPropertiesAsync(adoBuildUri.Project, adoBuildUri.BuildId);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
        {
            await setTaskResult(TaskResult.Canceled);
        }
    }

    public record TaskInfo(Guid JobId, Guid PlanId, Guid TaskId, Guid TimelineId, string HubName = "build");
}
