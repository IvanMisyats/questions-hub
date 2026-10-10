using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

/// <summary>
/// Loading and fingerprinting the editable graph of a package (package → tours → blocks/questions
/// with their authors/editors, package editors and tags) for the agent API.
/// </summary>
public static class PackageGraph
{
    private static readonly JsonSerializerOptions FingerprintJson = new() { WriteIndented = false };

    /// <summary>
    /// Includes everything an agent can read or change. Split query: one statement per collection
    /// instead of a cartesian product (tours × blocks × questions × authors).
    /// Every question is reached through <see cref="Tour.Questions"/>; questions inside a block also
    /// carry <see cref="Question.BlockId"/>.
    /// </summary>
    public static IQueryable<Package> WithEditableGraph(this IQueryable<Package> packages) =>
        packages
            .Include(p => p.PackageEditors)
            .Include(p => p.Tags)
            .Include(p => p.Tours).ThenInclude(t => t.Editors)
            .Include(p => p.Tours).ThenInclude(t => t.Blocks).ThenInclude(b => b.Editors)
            .Include(p => p.Tours).ThenInclude(t => t.Questions).ThenInclude(q => q.Authors)
            .AsSplitQuery();

    /// <summary>
    /// A content version of the package: SHA-256 (hex) over a canonical form of everything the
    /// agent API exposes for editing — package fields, editors, tags, tour/block/question ids,
    /// order, numbers, texts, media URLs and author ids. Any edit (through the API or the editor
    /// UI) changes it; publication status and other fields the API does not edit do not.
    /// Collections are sorted, so the in-memory order of navigation lists does not matter.
    /// </summary>
    public static string Fingerprint(Package package)
    {
        static int[] Ids(IEnumerable<Author> authors) => authors.Select(a => a.Id).Order().ToArray();

        var canonical = new
        {
            package.Id,
            package.Title,
            package.Description,
            package.Preamble,
            package.PlayedFrom,
            package.PlayedTo,
            package.NumberingMode,
            package.SharedEditors,
            PackageEditors = Ids(package.PackageEditors),
            Tags = package.Tags.Select(t => t.Id).Order().ToArray(),
            Tours = package.Tours.OrderBy(t => t.OrderIndex).ThenBy(t => t.Id).Select(t => new
            {
                t.Id,
                t.OrderIndex,
                t.Type,
                t.Number,
                t.Title,
                t.Preamble,
                t.Comment,
                Editors = Ids(t.Editors),
                Blocks = t.Blocks.OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).Select(b => new
                {
                    b.Id,
                    b.OrderIndex,
                    b.Name,
                    b.Preamble,
                    Editors = Ids(b.Editors)
                }),
                Questions = t.Questions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).Select(q => new
                {
                    q.Id,
                    q.BlockId,
                    q.OrderIndex,
                    q.Number,
                    q.HostInstructions,
                    q.Text,
                    q.HandoutText,
                    q.HandoutUrl,
                    q.Answer,
                    q.AcceptedAnswers,
                    q.RejectedAnswers,
                    q.AnswerForm,
                    q.Comment,
                    q.CommentAttachmentUrl,
                    q.Source,
                    Authors = Ids(q.Authors)
                })
            })
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, FingerprintJson);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Questions of <paramref name="tour"/> that are not in a block, in order.</summary>
    public static IEnumerable<Question> RootQuestions(Tour tour) =>
        tour.Questions.Where(q => q.BlockId == null).OrderBy(q => q.OrderIndex);

    /// <summary>Questions of <paramref name="block"/> (taken from its tour), in order.</summary>
    public static IEnumerable<Question> BlockQuestions(Tour tour, Block block) =>
        tour.Questions.Where(q => q.BlockId == block.Id).OrderBy(q => q.OrderIndex);
}
