using System.Linq.Expressions;
using System.Net;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class UserDirectoryServiceTests
{
    [Fact]
    public async Task ListAsync_ReturnsBotUsersSortedByName()
    {
        var usersTable = new FakeUsersTableClient(
        [
            CreateBotUser("bot-b", "Zulu Bot"),
            CreateBotUser("bot-a", "Alpha Bot"),
            CreateHumanUser("person@example.com", "Regular User")
        ]);
        var service = new UserDirectoryService(usersTable);

        var results = await service.ListBotDefinitionsAsync(CancellationToken.None);

        Assert.Collection(
            results,
            bot => Assert.Equal("Alpha Bot", bot.Name),
            bot => Assert.Equal("Zulu Bot", bot.Name));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForNonBotUser()
    {
        var service = new UserDirectoryService(new FakeUsersTableClient([CreateHumanUser("person@example.com", "Regular User")]));

        var result = await service.GetBotDefinitionAsync("person@example.com", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsBotUserFromUsersTable()
    {
        var usersTable = new FakeUsersTableClient(
        [
            CreateBotUser("legacy-bot", "Legacy Ghost", strategyText: "Always buy the cheapest railroad.")
        ]);
        var service = new UserDirectoryService(usersTable);

        var result = await service.GetBotDefinitionAsync("legacy-bot", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("legacy-bot", result.BotDefinitionId);
        Assert.Equal("Legacy Ghost", result.Name);
        Assert.Equal("Always buy the cheapest railroad.", result.StrategyText);
        Assert.True(result.IsBotUser);
        Assert.Equal(1, usersTable.GetEntityCallCount);
    }

    [Fact]
    public async Task GetAsync_ReusesCachedBotDefinition()
    {
        var usersTable = new FakeUsersTableClient(
        [
            CreateBotUser("legacy-bot", "Legacy Ghost", strategyText: "Always buy the cheapest railroad.")
        ]);
        var service = new UserDirectoryService(usersTable);

        var firstResult = await service.GetBotDefinitionAsync("legacy-bot", CancellationToken.None);
        var secondResult = await service.GetBotDefinitionAsync("legacy-bot", CancellationToken.None);

        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.Equal(1, usersTable.GetEntityCallCount);
        Assert.NotSame(firstResult, secondResult);
    }

    [Fact]
    public async Task CreateUpdateDeleteAsync_UsesUsersTableAndRespectsEtags()
    {
        var usersTable = new FakeUsersTableClient();
        var service = new UserDirectoryService(usersTable);

        var createResult = await service.CreateBotDefinitionAsync(
            "controller@example.com",
            "Route Shark",
            "Prefer high-value trips.",
            CancellationToken.None);

        Assert.True(createResult.Succeeded);
        var createdBot = Assert.IsType<BotStrategyDefinitionEntity>(createResult.Bot);
        var createdUser = Assert.Single(usersTable.Entities);
        Assert.True(createdUser.IsBot);
        Assert.Equal("USER", createdBot.PartitionKey);
        Assert.Equal("Route Shark", createdBot.Name);

        var updateResult = await service.UpdateBotDefinitionAsync(
            "editor@example.com",
            createdBot.BotDefinitionId,
            createdBot.ETag,
            "Route Shark Prime",
            "Prefer monopoly-building routes.",
            CancellationToken.None);

        Assert.True(updateResult.Succeeded);
        Assert.Equal("Route Shark Prime", updateResult.Bot!.Name);
        Assert.Equal("editor@example.com", updateResult.Bot.ModifiedByUserId);

        var conflictResult = await service.UpdateBotDefinitionAsync(
            "editor@example.com",
            createdBot.BotDefinitionId,
            new ETag("\"stale\""),
            "Should Fail",
            "Prefer short trips.",
            CancellationToken.None);

        Assert.True(conflictResult.IsConflict);

        var deleteResult = await service.DeleteBotDefinitionAsync(
            createdBot.BotDefinitionId,
            updateResult.Bot.ETag,
            CancellationToken.None);

        Assert.Equal(BotDefinitionDeleteResult.Success, deleteResult);
        Assert.Empty(usersTable.Entities);
    }

    private static ApplicationUser CreateBotUser(string id, string name, string strategyText = "Always choose a legal option.")
    {
        return new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = id,
            ETag = new ETag("\"seed\""),
            Email = id,
            NormalizedEmail = id.ToUpperInvariant(),
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            Name = name,
            Nickname = name,
            NormalizedNickname = name.ToUpperInvariant(),
            StrategyText = strategyText,
            IsBot = true,
            CreatedByUserId = "creator@example.com",
            CreatedUtc = new DateTimeOffset(2026, 3, 17, 0, 0, 0, TimeSpan.Zero),
            ModifiedByUserId = "creator@example.com",
            ModifiedUtc = new DateTimeOffset(2026, 3, 17, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private static ApplicationUser CreateHumanUser(string id, string name)
    {
        return new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = id,
            ETag = new ETag("\"legacy\""),
            Email = id,
            NormalizedEmail = id.ToUpperInvariant(),
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            Name = name,
            Nickname = name,
            NormalizedNickname = name.ToUpperInvariant(),
            StrategyText = string.Empty,
            IsBot = false,
            CreatedByUserId = "creator@example.com",
            CreatedUtc = new DateTimeOffset(2026, 3, 17, 0, 0, 0, TimeSpan.Zero),
            ModifiedByUserId = "creator@example.com",
            ModifiedUtc = new DateTimeOffset(2026, 3, 17, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private sealed class FakeUsersTableClient(params ApplicationUser[] entities) : TableClient
    {
        private readonly Dictionary<(string PartitionKey, string RowKey), ApplicationUser> _entities = entities.ToDictionary(
            entity => (entity.PartitionKey, entity.RowKey),
            Clone);

        private int _etagVersion = entities.Length;

        public IReadOnlyCollection<ApplicationUser> Entities => _entities.Values.Select(Clone).ToList();

        public int GetEntityCallCount { get; private set; }

        public override AsyncPageable<T> QueryAsync<T>(
            Expression<Func<T, bool>> filter,
            int? maxPerPage = null,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) != typeof(ApplicationUser))
            {
                return AsyncPageable<T>.FromPages([]);
            }

            var values = _entities.Values
                .Where(entity => entity.IsBot)
                .Select(entity => (T)(ITableEntity)Clone(entity))
                .ToList();

            return AsyncPageable<T>.FromPages(
            [
                Page<T>.FromValues(values, null, new FakeResponse((int)HttpStatusCode.OK))
            ]);
        }

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            GetEntityCallCount++;

            if (!_entities.TryGetValue((partitionKey, rowKey), out var entity))
            {
                throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
            }

            return Task.FromResult(Response.FromValue((T)(ITableEntity)Clone(entity), new FakeResponse((int)HttpStatusCode.OK)));
        }

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            var user = Clone((entity as ApplicationUser) ?? throw new InvalidOperationException("Expected user entity."));
            user.ETag = NextEtag();
            _entities[(user.PartitionKey, user.RowKey)] = user;

            if (entity is ApplicationUser source)
            {
                source.ETag = user.ETag;
            }

            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
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

            if (!Matches(ifMatch, existing.ETag))
            {
                throw new RequestFailedException((int)HttpStatusCode.PreconditionFailed, "ETag mismatch.");
            }

            user.ETag = NextEtag();
            _entities[(user.PartitionKey, user.RowKey)] = user;

            if (entity is ApplicationUser source)
            {
                source.ETag = user.ETag;
            }

            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }

        public override Task<Response> DeleteEntityAsync(
            string partitionKey,
            string rowKey,
            ETag ifMatch = default,
            CancellationToken cancellationToken = default)
        {
            if (!_entities.TryGetValue((partitionKey, rowKey), out var existing))
            {
                throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
            }

            if (!Matches(ifMatch, existing.ETag))
            {
                throw new RequestFailedException((int)HttpStatusCode.PreconditionFailed, "ETag mismatch.");
            }

            _entities.Remove((partitionKey, rowKey));
            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }

        private ETag NextEtag()
        {
            _etagVersion++;
            return new ETag($"\"v{_etagVersion}\"");
        }
    }

    private static bool Matches(ETag expected, ETag actual)
    {
        return expected == ETag.All || string.Equals(expected.ToString(), actual.ToString(), StringComparison.Ordinal);
    }

    private static BotStrategyDefinitionEntity Clone(BotStrategyDefinitionEntity entity)
    {
        return new BotStrategyDefinitionEntity
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            Name = entity.Name,
            StrategyText = entity.StrategyText,
            IsBotUser = entity.IsBotUser,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedUtc = entity.CreatedUtc,
            ModifiedByUserId = entity.ModifiedByUserId,
            ModifiedUtc = entity.ModifiedUtc
        };
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
            Nickname = entity.Nickname,
            NormalizedNickname = entity.NormalizedNickname,
            StrategyText = entity.StrategyText,
            IsBot = entity.IsBot,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedUtc = entity.CreatedUtc,
            ModifiedByUserId = entity.ModifiedByUserId,
            ModifiedUtc = entity.ModifiedUtc
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