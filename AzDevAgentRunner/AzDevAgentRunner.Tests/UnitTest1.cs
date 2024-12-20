using System.Runtime.CompilerServices;
using System.Text.Json;

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

        var runCommand = new RunCommand()
        {
            AdoBuildUri = data.builduri,
            AdoToken = data.authtoken,
            RunnerId = runnerId,
            RunnerIds = runnerIds
        };

        return runCommand.RunAsync(new CancellationTokenSource());
    }

    [Fact]
    public async Task SingleRunner()
    {
        var data = ReadData();

        await RunAsync("0", "0");
    }
}
