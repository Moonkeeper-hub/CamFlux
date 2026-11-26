namespace CamFlux.Core;

public sealed record StartDecision(Camera Camera, string[]? Arguments, string? Error);

public static class StartPlanning
{
    public static IEnumerable<StartDecision> Plan(IEnumerable<Camera> requested, IEnumerable<Camera> active,
        IReadOnlySet<string> attachedVideo, IReadOnlySet<string> attachedAudio, Endpoint endpoint, Credentials publishing)
    {
        var occupied = active.ToList();
        foreach (var camera in requested)
        {
            var decision = Validate(camera);
            if (decision.Arguments is not null)
                occupied.Add(camera);
            yield return decision;
        }

        StartDecision Validate(Camera camera)
        {
            if (!attachedVideo.Contains(camera.DeviceId))
                return new(camera, null, "Disconnected");
            if (camera.AudioId is not null && !attachedAudio.Contains(camera.AudioId))
                return new(camera, null, "MissingAudio");
            if (occupied.Any(c => c.Path == camera.Path || c.DeviceId.Equals(camera.DeviceId, StringComparison.OrdinalIgnoreCase)))
                return new(camera, null, "DuplicateStream");
            if (camera.AudioId is not null && occupied.Any(c => c.AudioId is not null))
                return new(camera, null, "DuplicateAudio");
            try { return new(camera, Streaming.Arguments(camera, endpoint, publishing), null); }
            catch (ArgumentException error) { return new(camera, null, error.Message); }
        }
    }
}
