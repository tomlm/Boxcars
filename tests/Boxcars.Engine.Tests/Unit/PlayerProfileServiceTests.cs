using System.Net;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class PlayerProfileServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeStrategyText_BlankInput_ReturnsEmptyString(string? strategyText)
    {
        var normalized = PlayerProfileService.NormalizeStrategyText(strategyText);

        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void ResolveStrategyTextOrDefault_BlankInput_ReturnsDefaultStrategyText()
    {
        var resolved = PlayerProfileService.ResolveStrategyTextOrDefault("   ");

        Assert.Equal(PlayerProfileService.DefaultStrategyText, resolved);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Play for monopolies", true)]
    [InlineData("  Save cash early  ", true)]
    public void HasRequiredStrategyText_ReflectsWhetherMeaningfulTextExists(string? strategyText, bool expected)
    {
        var hasRequiredStrategy = PlayerProfileService.HasRequiredStrategyText(strategyText);

        Assert.Equal(expected, hasRequiredStrategy);
    }

    [Fact]
    public async Task GetProfileAsync_ReusesCachedProfileWithinServiceLifetime()
    {
        var usersTable = new FakeUsersTableClient(CreateUser("alice@example.com", "Alice"));
        var service = new PlayerProfileService(new UserDirectoryService(usersTable));

        var firstProfile = await service.GetProfileAsync("alice@example.com", CancellationToken.None);
        var secondProfile = await service.GetProfileAsync("alice@example.com", CancellationToken.None);

        Assert.NotNull(firstProfile);
        Assert.NotNull(secondProfile);
        Assert.Equal(1, usersTable.GetEntityCallCount);
        Assert.NotSame(firstProfile, secondProfile);
        Assert.Equal(firstProfile!.Nickname, secondProfile!.Nickname);
    }

    [Fact]
    public async Task GetProfileAsync_CoalescesConcurrentRequestsForSameUser()
    {
        var usersTable = new FakeUsersTableClient([CreateUser("alice@example.com", "Alice")], TimeSpan.FromMilliseconds(50));
        var service = new PlayerProfileService(new UserDirectoryService(usersTable));

        var firstTask = service.GetProfileAsync("alice@example.com", CancellationToken.None);
        var secondTask = service.GetProfileAsync("alice@example.com", CancellationToken.None);

        var profiles = await Task.WhenAll(firstTask, secondTask);

        Assert.All(profiles, Assert.NotNull);
        Assert.Equal(1, usersTable.GetEntityCallCount);
        Assert.NotSame(profiles[0], profiles[1]);
    }

    [Fact]
    public async Task UpdateStrategyTextAsync_RefreshesCachedProfile()
    {
        var usersTable = new FakeUsersTableClient(CreateUser("alice@example.com", "Alice"));
        var service = new PlayerProfileService(new UserDirectoryService(usersTable));

        var originalProfile = await service.GetProfileAsync("alice@example.com", CancellationToken.None);
        var updated = await service.UpdateStrategyTextAsync("alice@example.com", "Prefer monopolies", CancellationToken.None);
        var refreshedProfile = await service.GetProfileAsync("alice@example.com", CancellationToken.None);

        Assert.True(updated);
        Assert.NotNull(originalProfile);
        Assert.NotNull(refreshedProfile);
        Assert.Equal(2, usersTable.GetEntityCallCount);
        Assert.Equal("Prefer monopolies", refreshedProfile!.StrategyText);
    }

    private static ApplicationUser CreateUser(string userId, string nickname)
    {
        return new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = userId,
            ETag = new ETag("\"seed\""),
            Email = userId,
            NormalizedEmail = userId.ToUpperInvariant(),
            UserName = userId,
            NormalizedUserName = userId.ToUpperInvariant(),
            Name = nickname,
            Nickname = nickname,
            NormalizedNickname = nickname.ToUpperInvariant(),
            StrategyText = "Initial strategy",
            CreatedByUserId = "tester",
            CreatedUtc = new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero),
            ModifiedByUserId = "tester",
            ModifiedUtc = new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private sealed class FakeUsersTableClient : TableClient
    {
        private readonly TimeSpan? _getEntityDelay;
        private readonly Dictionary<(string PartitionKey, string RowKey), ApplicationUser> _entities;

        private int _etagVersion;

        public int GetEntityCallCount { get; private set; }

        public FakeUsersTableClient(params ApplicationUser[] entities)
            : this(entities, null)
        {
        }

        public FakeUsersTableClient(ApplicationUser[] entities, TimeSpan? getEntityDelay)
        {
            _entities = entities.ToDictionary(entity => (entity.PartitionKey, entity.RowKey), Clone);
            _etagVersion = entities.Length;
            _getEntityDelay = getEntityDelay;
        }

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            return GetEntityCoreAsync<T>(partitionKey, rowKey, cancellationToken);
        }

        private async Task<Response<T>> GetEntityCoreAsync<T>(
            string partitionKey,
            string rowKey,
            CancellationToken cancellationToken)
        {
            GetEntityCallCount++;

            if (_getEntityDelay is { } delay)
            {
                await Task.Delay(delay, cancellationToken);
            }

            if (!_entities.TryGetValue((partitionKey, rowKey), out var entity))
            {
                throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
            }

            return Response.FromValue((T)(ITableEntity)Clone(entity), new FakeResponse((int)HttpStatusCode.OK));
        }

        public override Task<Response> UpdateEntityAsync<T>(
            T entity,
            ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            var user = Clone((entity as ApplicationUser) ?? throw new InvalidOperationException("Expected user entity."));
            if (!_entities.TryGetValue((user.PartitionKey, user.RowKey), out var existing))
            {
                throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
            }

            if (ifMatch != ETag.All && !string.Equals(ifMatch.ToString(), existing.ETag.ToString(), StringComparison.Ordinal))
            {
                throw new RequestFailedException((int)HttpStatusCode.PreconditionFailed, "ETag mismatch.");
            }

            user.ETag = new ETag($"\"v{++_etagVersion}\"");
            _entities[(user.PartitionKey, user.RowKey)] = user;

            if (entity is ApplicationUser source)
            {
                source.ETag = user.ETag;
            }

            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }
    }

    private static ApplicationUser Clone(ApplicationUser entity)
    {
        return new ApplicationUser
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            Name = entity.Name,
            Email = entity.Email,
            NormalizedEmail = entity.NormalizedEmail,
            UserName = entity.UserName,
            NormalizedUserName = entity.NormalizedUserName,
            PasswordHash = entity.PasswordHash,
            SecurityStamp = entity.SecurityStamp,
            EmailConfirmed = entity.EmailConfirmed,
            LockoutEnd = entity.LockoutEnd,
            LockoutEnabled = entity.LockoutEnabled,
            AccessFailedCount = entity.AccessFailedCount,
            Nickname = entity.Nickname,
            NormalizedNickname = entity.NormalizedNickname,
            ThumbnailUrl = entity.ThumbnailUrl,
            PreferredColor = entity.PreferredColor,
            StrategyText = entity.StrategyText,
            IsBot = entity.IsBot,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedUtc = entity.CreatedUtc,
            ModifiedByUserId = entity.ModifiedByUserId,
            ModifiedUtc = entity.ModifiedUtc,
            ConcurrencyStamp = entity.ConcurrencyStamp,
            PhoneNumber = entity.PhoneNumber,
            PhoneNumberConfirmed = entity.PhoneNumberConfirmed,
            TwoFactorEnabled = entity.TwoFactorEnabled
        };
    }

    private sealed class FakeResponse(int status) : Response
    {
        public override int Status { get; } = status;

        public override string ReasonPhrase => string.Empty;

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose()
        {
        }

        protected override bool ContainsHeader(string name)
        {
            return false;
        }

        protected override IEnumerable<HttpHeader> EnumerateHeaders()
        {
            yield break;
        }

        protected override bool TryGetHeader(string name, out string value)
        {
            value = string.Empty;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = [];
            return false;
        }
    }
}