namespace AzDevAgentRunner;

public record TaskInfo(Guid JobId, Guid PlanId, Guid TaskId, Guid TimelineId, string HubName = "build");
