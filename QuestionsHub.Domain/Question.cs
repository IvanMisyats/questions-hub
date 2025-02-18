namespace QuestionsHub.Domain;

public class Question
{
    public int Id { get; set; }
    public string Number { get; set; }
    public string Text { get; set; }
    public string? QuestionAttachmentText { get; set; }
    public string? QuestionAttachmentUrl { get; set; }
    public string Answer { get; set; }
    public string? Matches { get; set; }
    public string? Unmatches { get; set; }
    public string? Comment { get; set; }
    public string? CommentAttachmentText { get; set; }
    public string? CommentAttachmentUrl { get; set; }
    public string? Source { get; set; }
    public string[]? Authors { get; set; }
}

public class Tour
{
    public int Id { get; set; }
    public string Number { get; set; }
    public string[]? Editors { get; set; }
    public string? Comment { get; set; }

    public Question[] Questions { get; set; }
}

public class Package
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string[]? Editors { get; set; }

    public Tour[] Tours { get; set; }
}