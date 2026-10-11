using System.ComponentModel.DataAnnotations.Schema;

namespace QuestionsHub.Blazor.Domain;

/// <summary>What a favorite points at.</summary>
public enum FavoriteKind
{
    Question = 0,
    Tour = 1,
    Block = 2,
    Package = 3
}

/// <summary>
/// A question, tour, block or package a user marked with a heart. Private to the user.
/// <see cref="PackageId"/> is always set (the package that contains the item); at most one of
/// <see cref="QuestionId"/>, <see cref="TourId"/>, <see cref="BlockId"/> is set, and none set means
/// the package itself is the favorite. Every FK cascades, so deleting the item deletes the favorite.
/// </summary>
public class Favorite
{
    public int Id { get; set; }

    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>The package that contains the item (denormalized: items never move between packages).</summary>
    public int PackageId { get; set; }
    public Package Package { get; set; } = null!;

    public int? QuestionId { get; set; }
    public Question? Question { get; set; }

    public int? TourId { get; set; }
    public Tour? Tour { get; set; }

    public int? BlockId { get; set; }
    public Block? Block { get; set; }

    /// <summary>When the heart was pressed (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    [NotMapped]
    public FavoriteKind Kind =>
        QuestionId != null ? FavoriteKind.Question
        : TourId != null ? FavoriteKind.Tour
        : BlockId != null ? FavoriteKind.Block
        : FavoriteKind.Package;
}
