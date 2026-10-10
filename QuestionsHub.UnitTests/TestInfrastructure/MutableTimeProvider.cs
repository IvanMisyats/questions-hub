namespace QuestionsHub.UnitTests.TestInfrastructure;

/// <summary>A clock tests can set and advance.</summary>
public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
