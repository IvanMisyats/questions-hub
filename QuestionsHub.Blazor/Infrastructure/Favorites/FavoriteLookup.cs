using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Favorites;

/// <summary>
/// Which items on a page the current user has favorited — what the heart buttons render from.
/// <see cref="Anonymous"/> renders hearts that lead to the login page.
/// </summary>
public sealed class FavoriteLookup
{
    public static readonly FavoriteLookup Anonymous = new(isAnonymous: true);

    private readonly HashSet<int> _questionIds = [];
    private readonly HashSet<int> _tourIds = [];
    private readonly HashSet<int> _blockIds = [];
    private readonly HashSet<int> _packageIds = [];

    public FavoriteLookup() : this(isAnonymous: false)
    {
    }

    private FavoriteLookup(bool isAnonymous)
    {
        IsAnonymous = isAnonymous;
    }

    /// <summary>The viewer is not logged in: hearts lead to the login page.</summary>
    public bool IsAnonymous { get; }

    public bool Has(FavoriteKind kind, int id) => SetFor(kind).Contains(id);

    /// <summary>Records one favorite; used when building the lookup.</summary>
    public void Add(FavoriteKind kind, int id)
    {
        if (IsAnonymous)
            throw new InvalidOperationException("The anonymous lookup is shared and read-only.");

        SetFor(kind).Add(id);
    }

    private HashSet<int> SetFor(FavoriteKind kind) => kind switch
    {
        FavoriteKind.Question => _questionIds,
        FavoriteKind.Tour => _tourIds,
        FavoriteKind.Block => _blockIds,
        FavoriteKind.Package => _packageIds,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
