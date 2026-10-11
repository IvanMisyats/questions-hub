namespace QuestionsHub.Blazor.Infrastructure.Favorites;

/// <summary>Authorization policies of the favorites endpoints.</summary>
public static class FavoritePolicies
{
    /// <summary>The site cookie only (never agent tokens or API keys), signed in, verified email.</summary>
    public const string SignedInBrowser = "FavoritesSignedInBrowser";
}
