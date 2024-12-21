using System.CommandLine;
using System.Text;
using System.Text.Json;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using TimelineRecord = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecord;

namespace AzDevAgentRunner;

public class ReserveOperation(IConsole Console)
{
    public required string AdoBuildUri;
    public required string AdoToken;
    public string AgentName = "unknown";
    public required int SlotCount;

    public double PollSeconds = 1;

    public bool Verbose = false;

    public string ReservationPrefix = $"****reservations:";

    public async Task<int> RunAsync()
    {
        try
        {
            var adoBuildUri = BuildUri.ParseBuildUri(AdoBuildUri);
            var taskInfo = adoBuildUri.DeserializeFromParameters<TaskInfo>();

            var connection = new VssConnection(adoBuildUri.OrganizationUri, new VssBasicCredential(AdoToken, string.Empty));
            var client = connection.GetClient<BuildHttpClient>();
            var taskClient = connection.GetClient<TaskHttpClient>();
            var agentClient = connection.GetClient<TaskAgentHttpClient>();

            var build = await client.GetBuildAsync(adoBuildUri.Project, adoBuildUri.BuildId);

            if (build.Result is BuildResult result && result != BuildResult.None)
            {
                // Build is completed, can't reserve
                return -100001;
            }

            var updatedRecord = await taskClient.UpdateTimelineRecordsAsync(
                        scopeIdentifier: build.Project.Id,
                        planType: taskInfo.HubName,
                        planId: taskInfo.PlanId,
                        timelineId: taskInfo.TimelineId,
                        [
                            new TimelineRecord()
                        {
                            Id = taskInfo.TaskId
                        }
                        ]);

            var record = updatedRecord[0];

            var entry = new ReservationEntry(AgentName, Guid.NewGuid());

            await taskClient.AppendLogContentAsync(
                scopeIdentifier: build.Project.Id,
                hubName: taskInfo.HubName,
                planId: taskInfo.PlanId,
                record.Log.Id,
                new MemoryStream(Encoding.UTF8.GetBytes(ReservationPrefix + JsonSerializer.Serialize(entry))));

            // Wait some time for log to propagate
            // Without wait we see insertions from other threads may be inserted between entries
            // which breaks consistency
            await Task.Delay(TimeSpan.FromSeconds(PollSeconds));

            var logLines = await taskClient.GetLogAsync(
                scopeIdentifier: build.Project.Id,
                hubName: taskInfo.HubName,
                planId: taskInfo.PlanId,
                logId: record.Log.Id);

            using var writer = new StringWriter();
            writer.WriteLine();
            writer.WriteLine("[");
            foreach (var line in logLines)
            {
                if (line.StartsWith(ReservationPrefix))
                {
                    writer.Write(line.AsSpan().Slice(ReservationPrefix.Length));
                    writer.WriteLine(",");
                }
            }
            writer.WriteLine("]");

            writer.Flush();

            var reservations = JsonSerializer.Deserialize<List<ReservationEntry>>(writer.ToString(), BuildUri.SerializerOptions)!;

            int reservationIndex = reservations.IndexOf(entry);

            var verboseOutput = "";
            if (Verbose)
            {
                logLines.ForEach(l => writer.WriteLine(l));
                verboseOutput = writer.ToString();
            }

            Console.WriteLine($"AgentName: '{AgentName}', Reservations: {reservations.Count}, ReservationIndex: {reservationIndex}{verboseOutput}");

            return reservationIndex < SlotCount ? reservationIndex : -reservationIndex;
        }
        catch
        {
            // Return large negative number to indicate failure
            return -10000;
        }
    }

    private record ReservationEntry(string AgentName, Guid Id);
}
