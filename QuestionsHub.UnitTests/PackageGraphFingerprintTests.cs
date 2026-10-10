using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using Xunit;

namespace QuestionsHub.UnitTests;

public class PackageGraphFingerprintTests
{
    private static Package Sample()
    {
        var anna = new Author { Id = 1, FirstName = "Анна", LastName = "Коваль" };
        var petro = new Author { Id = 2, FirstName = "Петро", LastName = "Мельник" };

        var block = new Block { Id = 30, OrderIndex = 0, Name = "Блок А", Editors = [anna, petro] };
        var block2 = new Block { Id = 31, OrderIndex = 1, Name = "Блок Б" };
        var tour = new Tour
        {
            Id = 10,
            OrderIndex = 0,
            Number = "1",
            Editors = [anna, petro],
            Blocks = [block, block2],
            Questions =
            [
                new Question { Id = 100, OrderIndex = 0, Number = "1", Text = "Перше", Answer = "А", BlockId = 30, Authors = [anna] },
                new Question { Id = 101, OrderIndex = 1, Number = "2", Text = "Друге", Answer = "Б", Authors = [petro, anna] },
            ]
        };

        return new Package
        {
            Id = 5,
            Title = "Пакет",
            Status = PackageStatus.Draft,
            PackageEditors = [petro, anna],
            Tags = [new Tag { Id = 7, Name = "2026" }, new Tag { Id = 8, Name = "Кубок" }],
            Tours = [tour, new Tour { Id = 11, OrderIndex = 1, Number = "2" }]
        };
    }

    [Fact]
    public void SameContent_SameFingerprint_RegardlessOfListOrder()
    {
        var first = Sample();
        var second = Sample();
        var tour = second.Tours.First(t => t.Id == 10);
        second.Tours.Reverse();
        tour.Questions.Reverse();
        tour.Questions.Single(q => q.Id == 101).Authors.Reverse(); // two authors
        tour.Editors.Reverse();
        tour.Blocks.Reverse();
        tour.Blocks.Single(b => b.Id == 30).Editors.Reverse();
        second.PackageEditors.Reverse();
        second.Tags.Reverse();

        PackageGraph.Fingerprint(first).Should().Be(PackageGraph.Fingerprint(second))
            .And.MatchRegex("^[0-9a-f]{64}$");
    }

    public static TheoryData<string, Action<Package>> ContentChanges => new()
    {
        { "question text", p => p.Tours[0].Questions[1].Text = "Змінене" },
        { "question number", p => p.Tours[0].Questions[1].Number = "3" },
        { "question order", p => p.Tours[0].Questions[1].OrderIndex = 5 },
        { "question moved out of block", p => p.Tours[0].Questions[0].BlockId = null },
        { "question authors", p => p.Tours[0].Questions[0].Authors.Clear() },
        { "question handout", p => p.Tours[0].Questions[0].HandoutUrl = "/media/x.png" },
        { "tour title", p => p.Tours[0].Title = "Тема" },
        { "tour editors", p => p.Tours[0].Editors.RemoveAt(0) },
        { "tour type", p => p.Tours[1].Type = TourType.Shootout },
        { "block preamble", p => p.Tours[0].Blocks[0].Preamble = "Преамбула" },
        { "package title", p => p.Title = "Інший" },
        { "package editors", p => p.PackageEditors.Clear() },
        { "tags", p => p.Tags.Clear() },
        { "numbering mode", p => p.NumberingMode = QuestionNumberingMode.Manual },
        { "shared editors", p => p.SharedEditors = true },
        { "question added", p => p.Tours[1].Questions.Add(new Question { Id = 200, Number = "1", Text = "", Answer = "" }) },
        { "tour removed", p => p.Tours.RemoveAt(1) },
    };

    [Theory]
    [MemberData(nameof(ContentChanges))]
    public void ContentChange_ChangesFingerprint(string what, Action<Package> change)
    {
        var package = Sample();
        var before = PackageGraph.Fingerprint(package);

        change(package);

        PackageGraph.Fingerprint(package).Should().NotBe(before, what);
    }

    [Fact]
    public void PublicationState_DoesNotChangeFingerprint()
    {
        var package = Sample();
        var before = PackageGraph.Fingerprint(package);

        package.Status = PackageStatus.Published;
        package.AccessLevel = PackageAccessLevel.RegisteredOnly;
        package.PublicationDate = DateTime.UtcNow;
        package.TotalQuestions = 99;

        PackageGraph.Fingerprint(package).Should().Be(before);
    }
}
