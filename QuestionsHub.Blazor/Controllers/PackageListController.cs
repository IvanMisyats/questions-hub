using Microsoft.AspNetCore.Mvc;
using QuestionsHub.Blazor.Infrastructure;
using System.Security.Claims;

namespace QuestionsHub.Blazor.Controllers;

/// <summary>
/// API controller for package list filtering.
/// Provides endpoints for the home page filter dropdowns.
/// </summary>
[ApiController]
[Route("api/packages")]
public class PackageListController : ControllerBase
{
    private readonly PackageListService _packageListService;
    private readonly AccessControlService _accessControlService;

    public PackageListController(
        PackageListService packageListService,
        AccessControlService accessControlService)
    {
        _packageListService = packageListService;
        _accessControlService = accessControlService;
    }

    /// <summary>
    /// Searches packages with filters and sorting.
    /// Returns paginated package cards data for rendering.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<PackageListResult>> SearchPackages(
        [FromQuery] string? search = null,
        [FromQuery] int? editor = null,
        [FromQuery] PackageSortField sort = PackageSortField.PublicationDate,
        [FromQuery] SortDirection dir = SortDirection.Desc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 21)
    {
        // Limit page size to prevent abuse
        pageSize = Math.Clamp(pageSize, 1, 100);

        var filter = new PackageListFilter(
            TitleSearch: search,
            EditorId: editor,
            SortField: sort,
            SortDir: dir,
            Page: page,
            PageSize: pageSize
        );

        var accessContext = await _accessControlService.GetPackageAccessContext(User);
        var result = await _packageListService.SearchPackages(filter, accessContext);

        return Ok(result);
    }

    /// <summary>
    /// Gets editors for the filter dropdown.
    /// Returns editors who are associated with published packages.
    /// </summary>
    /// <param name="search">Optional search term to filter by name.</param>
    [HttpGet("editors")]
    public async Task<ActionResult<List<EditorFilterDto>>> GetEditors([FromQuery] string? search = null)
    {
        var editors = await _packageListService.GetEditorsForFilter(search);
        return Ok(editors);
    }
}
