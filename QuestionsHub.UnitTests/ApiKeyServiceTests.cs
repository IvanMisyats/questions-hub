using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using QuestionsHub.Blazor.Infrastructure.Api;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

public class ApiKeyServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly MemoryCache _cache;
    private readonly ApiKeyService _service;

    public ApiKeyServiceTests()
    {
        _dbFactory = new InMemoryDbContextFactory();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _service = new ApiKeyService(_dbFactory, _cache);
    }

    public void Dispose()
    {
        _cache.Dispose();
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }

    [Fact]
    public void HashKey_ProducesDeterministicResult()
    {
        var key = "qh_live_abc123";
        var hash1 = ApiKeyService.HashKey(key);
        var hash2 = ApiKeyService.HashKey(key);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // SHA-256 hex
    }

    [Fact]
    public void HashKey_DifferentKeys_ProduceDifferentHashes()
    {
        var hash1 = ApiKeyService.HashKey("qh_live_key1");
        var hash2 = ApiKeyService.HashKey("qh_live_key2");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public async Task Create_ReturnsClientAndRawKey()
    {
        var (client, rawKey) = await _service.Create("Test Client", "dev@example.com");

        client.Name.Should().Be("Test Client");
        client.ContactEmail.Should().Be("dev@example.com");
        client.IsActive.Should().BeTrue();
        client.KeyHash.Should().HaveLength(64);

        rawKey.Should().StartWith("qh_live_");
        rawKey.Should().HaveLength(40); // "qh_live_" (8) + 32 hex chars
        client.KeyPrefix.Should().Be(rawKey[..16]);
    }

    [Fact]
    public async Task Validate_ValidKey_ReturnsClient()
    {
        var (_, rawKey) = await _service.Create("Valid Client");

        var result = await _service.Validate(rawKey);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Valid Client");
    }

    [Fact]
    public async Task Validate_InvalidKey_ReturnsNull()
    {
        await _service.Create("Some Client");

        var result = await _service.Validate("qh_live_invalid_key_12345678901");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Validate_RevokedKey_ReturnsNull()
    {
        var (client, rawKey) = await _service.Create("Revoked Client");
        await _service.Revoke(client.Id);

        var result = await _service.Validate(rawKey);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Revoke_ExistingClient_ReturnsTrue()
    {
        var (client, _) = await _service.Create("To Revoke");

        var result = await _service.Revoke(client.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_NonExistentClient_ReturnsFalse()
    {
        var result = await _service.Revoke(9999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAll_ReturnsAllClients()
    {
        await _service.Create("Client A");
        await _service.Create("Client B");

        var all = await _service.GetAll();

        all.Should().HaveCount(2);
    }
}
