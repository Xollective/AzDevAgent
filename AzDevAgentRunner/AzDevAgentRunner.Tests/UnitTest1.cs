using System.CommandLine.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using static AzDevAgentRunner.RunOperation;
using TaskResult = Microsoft.TeamFoundation.DistributedTask.WebApi.TaskResult;
using TimelineRecord = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecord;

namespace AzDevAgentRunner.Tests;

public class UnitTest1
{

    public record BuildData(string authtoken, string builduri);

    private BuildData ReadData([CallerFilePath]string path = null)
    {
        path = Path.Combine(Path.GetDirectoryName(path), "data.json");
        var dataString = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BuildData>(dataString)!;
    }

    public Task RunAsync(string runnerId, string runnerIds)
    {
        var data = ReadData();

        var runCommand = new RunOperation()
        {
            AdoBuildUri = data.builduri,
            AdoToken = data.authtoken,
        };

        return runCommand.RunAsync(new CancellationTokenSource());
    }

    [Fact]
    public async Task SingleRunner()
    {
        var data = ReadData();

        await RunAsync("0", "0");
    }

    [Fact]
    public async Task TestReserve()
    {
        var data = ReadData();
        var prefix = Guid.NewGuid().ToString("N");

        var results = await SelectAsync(parallel: true, 10,
            async index =>
            {
                var reserveCommand = new ReserveOperation(new SystemConsole())
                {
                    AdoBuildUri = data.builduri,
                    AdoToken = data.authtoken,
                    AgentName = index.ToString(),
                    SlotCount = 5,
                    ReservationPrefix = prefix,
                    Verbose = true
                };

                return await reserveCommand.RunAsync();
            });

        Console.WriteLine($"Results: {JsonSerializer.Serialize(results)}");
    }

    public static async Task<TResult[]> SelectAsync<TResult>(bool parallel, int count, Func<int, Task<TResult>> selectAsync)
    {
        var results = new TResult[count];

        if (parallel)
        {
            await Parallel.ForAsync(0, count, async (i, token) =>
            {
                results[i] = await selectAsync(i);
            });
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                results[i] = await selectAsync(i);
            }
        }

        return results;
    }

    [Fact]
    public async Task TestApi()
    {
        var data = ReadData();

        var adoBuildUri = BuildUri.ParseBuildUri(data.builduri);
        var taskInfo = adoBuildUri.DeserializeFromParameters<TaskInfo>();

        var connection = new VssConnection(adoBuildUri.OrganizationUri, new VssBasicCredential(data.authtoken, string.Empty));
        var client = connection.GetClient<BuildHttpClient>();
        var taskClient = connection.GetClient<TaskHttpClient>();
        var agentClient = connection.GetClient<TaskAgentHttpClient>();

        var build = await client.GetBuildAsync(adoBuildUri.Project, adoBuildUri.BuildId);

        var orch = await taskClient.GetPlanAsync(
            scopeIdentifier: build.Project.Id,
            hubName: taskInfo.HubName,
            planId: taskInfo.PlanId
            );

        var records = await taskClient.GetRecordsAsync(
            scopeIdentifier: build.Project.Id,
            hubName: taskInfo.HubName,
            planId: taskInfo.PlanId,
            timelineId: taskInfo.TimelineId);

        var record = records.First(t => t.Id == taskInfo.TaskId);

        await taskClient.AppendTimelineRecordFeedAsync(
            scopeIdentifier: build.Project.Id,
            planType: taskInfo.HubName,
            planId: taskInfo.PlanId,
            timelineId: taskInfo.TimelineId,
            recordId: taskInfo.TaskId,
            new[]
            {
                "hello"
            });

        //var s = new StreamWriter(new MemoryStream());


        var log = await taskClient.AppendLogContentAsync(
            scopeIdentifier: build.Project.Id,
            hubName: taskInfo.HubName,
            planId: taskInfo.PlanId,
            logId: record.Log.Id,
            new MemoryStream(Encoding.UTF8.GetBytes("hello"))

            );

        var logLines = await taskClient.GetLogAsync(
            scopeIdentifier: build.Project.Id,
            hubName: taskInfo.HubName,
            planId: taskInfo.PlanId,
            logId: record.Log.Id
            );

        

        //var newId = Guid.Parse("a71373fd-f761-49fe-a74f-c9fe544178d8");

        //var records2 = await taskClient.UpdateTimelineRecordsAsync(
        //            scopeIdentifier: build.Project.Id,
        //            planType: taskInfo.HubName,
        //            planId: taskInfo.PlanId,
        //            timelineId: taskInfo.TimelineId,
        //            new[]
        //            {
        //                new TimelineRecord()
        //                {
        //                    Name = "test1",
        //                    RecordType = "Task",
        //                    ParentId = taskInfo.TaskId,
        //                    Id = newId,
        //                    Result = TaskResult.SucceededWithIssues
        //                }
        //            });


    }
}
