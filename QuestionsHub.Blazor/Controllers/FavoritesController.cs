using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Favorites;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Controllers;

/// <summary>Response of the favorites endpoints: the item's state after the call.</summary>
public record FavoriteStateResponse(bool IsFavorite);

/// <summary>
/// Heart buttons on the site: add/remove a favorite question, tour, block or package.
/// Called by <c>wwwroot/favorites.js</c> with the site cookie and the antiforgery header; agent tokens
/// and API keys are never accepted (see <see cref="FavoritePolicies.SignedInBrowser"/>). Both calls are idempotent.
/// </summary>
[ApiController]
[Route("api/favorites")]
[Authorize(Policy = FavoritePolicies.SignedInBrowser)]
[ClientRateLimit(RateLimitBudgets.UserFavorites)]
public class FavoritesController(FavoriteService favorites) : ControllerBase
{
    [HttpPut("{kind}/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string kind, int id)
    {
        if (FavoriteKindSegments.Parse(kind) is not { } favoriteKind)
            return NotFound();

        var outcome = await favorites.Add(User.ToAccessContext(), favoriteKind, id);
        return outcome switch
        {
            AddFavoriteOutcome.Added => Ok(new FavoriteStateResponse(true)),
            AddFavoriteOutcome.LimitReached => Conflict(new
            {
                error = $"Можна мати не більше {FavoriteService.MaxFavoritesPerUser} обраних. Приберіть непотрібні."
            }),
            _ => NotFound()
        };
    }

    [HttpDelete("{kind}/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(string kind, int id)
    {
        if (FavoriteKindSegments.Parse(kind) is not { } favoriteKind)
            return NotFound();

        await favorites.Remove(User.GetUserId()!, favoriteKind, id);
        return Ok(new FavoriteStateResponse(false));
    }
}
