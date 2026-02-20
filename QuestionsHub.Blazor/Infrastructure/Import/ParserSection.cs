namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Parser sections for state machine.
/// </summary>
public enum ParserSection
{
    PackageHeader,
    TourHeader,
    BlockHeader,
    QuestionText,
    HostInstructions,
    Handout,
    Answer,
    AcceptedAnswers,
    RejectedAnswers,
    Comment,
    Source,
    Authors
}
