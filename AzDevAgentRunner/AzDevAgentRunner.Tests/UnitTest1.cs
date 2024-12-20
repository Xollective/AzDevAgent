using System.Text.Json;

namespace AzDevAgentRunner.Tests;

public class UnitTest1
{
    public string Data = """

    """;

    public record BuildData(string authtoken, string builduri);

    private BuildData ReadData()
    {
        return JsonSerializer.Deserialize<BuildData>(Data)!;
    }

    public Task RunAsync(string runnerId, string runnerIds)
    {
        var data = ReadData();

        var runCommand = new RunCommand()
        {
            AdoBuildUri = data.builduri,
            AdoToken = data.authtoken,
            RunnerId = runnerId,
            RunnerIds = runnerIds
        };

        return runCommand.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SingleRunner()
    {
        var data = ReadData();

        await RunAsync("0", "0");
    }
}
