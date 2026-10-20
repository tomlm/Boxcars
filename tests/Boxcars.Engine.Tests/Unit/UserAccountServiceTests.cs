using System.Linq.Expressions;
using System.Net;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Boxcars.Auth;
using Boxcars.Data;
using Boxcars.Services;
using Microsoft.Extensions.Configuration;

namespace Boxcars.Engine.Tests.Unit;

public class UserAccountServiceTests
{
    [Theory]
    [InlineData("Admin@Example.com; other@example.com")]
    [InlineData("admin@example.com,other@example.com")]
    public void AdminAccess_ReadsAListOfUserIds(string setting)
    {
        var access = new AdminAccess(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:UserIds"] = setting })
            .Build());

        Assert.True(access.IsAdmin("admin@example.com"));
        Assert.True(access.IsAdmin("OTHER@example.com"));
        Assert.False(access.IsAdmin("player@example.com"));
        Assert.False(access.IsAdmin((string?)null));
    }

    [Fact]
    public void AdminAccess_NoSetting_HasNoAdmins()
    {
        var access = new AdminAccess(new ConfigurationBuilder().Build());

        Assert.False(access.IsAdmin("admin@example.com"));
    }

    [Fact]
    public async Task SaveAsync_RenamesAndRecolorsThePlayerInTheirUnfinishedGames()
    {
        var users = new FakeUsersTable(CreateUser("pat@example.com", "Pat"));
        var games = new FakeGamesTable(CreateGame("game-1", "pat@example.com", "sam@example.com"));
        var service = CreateService(users, games);

        var result = await service.SaveAsync("admin@example.com", "pat@example.com", ETag.All, " Patty ", "green", " https://example.com/patty.png ", "Buy the B&O.", CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var user = users.Users.Single(candidate => candidate.RowKey == "pat@example.com");
        Assert.Equal("Patty", user.Nickname);
        Assert.Equal("PATTY", user.NormalizedNickname);
        Assert.Equal("green", user.PreferredColor);
        Assert.Equal("https://example.com/patty.png", user.ThumbnailUrl);
        Assert.Equal("Buy the B&O.", user.StrategyText);
        var seat = games.Games.Single().Seats.Single(candidate => candidate.PlayerUserId == "pat@example.com");
        Assert.Equal("Patty", seat.DisplayName);
        Assert.Equal("green", seat.Color);
    }

    [Fact]
    public async Task SaveAsync_NameAnotherAccountUses_IsRefused()
    {
        var users = new FakeUsersTable(CreateUser("pat@example.com", "Pat"), CreateUser("sam@example.com", "Sam"));
        var service = CreateService(users, new FakeGamesTable());

        var result = await service.SaveAsync("admin@example.com", "pat@example.com", ETag.All, "sam", "green", string.Empty, string.Empty, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Pat", users.Users.Single(candidate => candidate.RowKey == "pat@example.com").Nickname);
    }

    [Fact]
    public async Task SaveAsync_Bot_RenamesItsBotNameToo()
    {
        var bot = CreateUser("bot-1@boxcars.bot", "Pooper");
        bot.IsBot = true;
        var users = new FakeUsersTable(bot);
        var service = CreateService(users, new FakeGamesTable());

        var result = await service.SaveAsync("admin@example.com", "bot-1@boxcars.bot", ETag.All, "Balenco", null, "/user-photo/balenco.png", "Rover the leader.", CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Balenco", users.Users.Single().Name);
        Assert.Equal("Balenco", users.Users.Single().Nickname);
    }

    [Fact]
    public async Task SaveAsync_PictureThatIsNotAWebAddress_IsRefused()
    {
        var users = new FakeUsersTable(CreateUser("pat@example.com", "Pat"));
        var service = CreateService(users, new FakeGamesTable());

        var result = await service.SaveAsync("admin@example.com", "pat@example.com", ETag.All, "Pat", "green", "javascript:alert(1)", string.Empty, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, users.Users.Single().ThumbnailUrl);
    }

    [Fact]
    public async Task CreateAsync_EmailPerson_SignsInWithTheirEmail()
    {
        var users = new FakeUsersTable();
        var service = CreateService(users, new FakeGamesTable());

        var result = await service.CreateAsync("admin@example.com", AccountType.Email, " Pat@Example.com ", "Pat", "green", string.Empty, "Buy the B&O.", CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var user = users.Users.Single();
        Assert.Equal("pat@example.com", user.RowKey);
        Assert.Equal("Pat", user.Nickname);
        Assert.Equal("green", user.PreferredColor);
        Assert.False(user.IsBot);
        Assert.Equal(AccountType.Email, UserAccountService.GetAccountType(user));
    }

    [Fact]
    public async Task CreateAsync_AuthPerson_CantSignInByEmailAlone()
    {
        var users = new FakeUsersTable();
        var service = CreateService(users, new FakeGamesTable());

        var result = await service.CreateAsync("admin@example.com", AccountType.Auth, "sam@example.com", "Sam", null, string.Empty, string.Empty, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var user = users.Users.Single();
        Assert.Equal(AccountType.Auth, UserAccountService.GetAccountType(user));
        Assert.False(EmailLoginService.IsEmailOnly(user));
    }

    [Fact]
    public async Task CreateAsync_Bot_GetsAMadeUpId()
    {
        var users = new FakeUsersTable();
        var service = CreateService(users, new FakeGamesTable());

        var result = await service.CreateAsync("admin@example.com", AccountType.Bot, null, "Balenco", "orange", "https://example.com/b.png", "Rover the leader.", CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var bot = users.Users.Single();
        Assert.True(bot.IsBot);
        Assert.EndsWith("@boxcars.bot", bot.RowKey, StringComparison.Ordinal);
        Assert.Equal("orange", bot.PreferredColor);
        Assert.Equal("https://example.com/b.png", bot.ThumbnailUrl);
    }

    [Fact]
    public async Task CreateAsync_EmailAlreadyInUse_IsRefused()
    {
        var users = new FakeUsersTable(CreateUser("pat@example.com", "Pat"));
        var service = CreateService(users, new FakeGamesTable());

        var result = await service.CreateAsync("admin@example.com", AccountType.Auth, "pat@example.com", "Patty", null, string.Empty, string.Empty, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Pat", users.Users.Single().Nickname);
    }

    [Fact]
    public async Task DeleteAsync_PlayerInAnUnfinishedGame_IsRefused()
    {
        var users = new FakeUsersTable(CreateUser("pat@example.com", "Pat"));
        var service = CreateService(users, new FakeGamesTable(CreateGame("game-1", "pat@example.com", "sam@example.com")));

        var result = await service.DeleteAsync("admin@example.com", "pat@example.com", ETag.All, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("game-1", result.Error, StringComparison.Ordinal);
        Assert.Single(users.Users);
    }

    [Fact]
    public async Task DeleteAsync_YourOwnAccount_IsRefused()
    {
        var users = new FakeUsersTable(CreateUser("admin@example.com", "Admin"));
        var service = CreateService(users, new FakeGamesTable());

        var result = await service.DeleteAsync("admin@example.com", "admin@example.com", ETag.All, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Single(users.Users);
    }

    [Fact]
    public async Task DeleteAsync_PlayerWithNoUnfinishedGames_IsDeleted()
    {
        var users = new FakeUsersTable(CreateUser("pat@example.com", "Pat"), CreateUser("sam@example.com", "Sam"));
        var service = CreateService(users, new FakeGamesTable(CreateGame("game-1", "sam@example.com", "admin@example.com")));

        var result = await service.DeleteAsync("admin@example.com", "pat@example.com", ETag.All, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["sam@example.com"], users.Users.Select(user => user.RowKey));
    }

    private static UserAccountService CreateService(FakeUsersTable users, FakeGamesTable games)
    {
        // The hub and engine aren't used by anything these tests reach.
        var gameService = new GameService(games, users, null!, null!);
        return new UserAccountService(new UserDirectoryService(users), gameService, new GamePresenceService());
    }

    private static ApplicationUser CreateUser(string email, string nickname)
    {
        return new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = email,
            Email = email,
            Name = nickname,
            Nickname = nickname,
            NormalizedNickname = nickname.ToUpperInvariant(),
            ExternalLoginProvider = "Google"
        };
    }

    private static GameEntity CreateGame(string gameId, params string[] playerUserIds)
    {
        return new GameEntity
        {
            PartitionKey = gameId,
            RowKey = "GAME",
            GameId = gameId,
            Name = gameId,
            CreatorId = playerUserIds[0],
            State = PersistedGameStates.Playing,
            Seats = playerUserIds
                .Select((userId, index) => new GameSeatDefinition { SeatIndex = index, PlayerUserId = userId, DisplayName = userId, Color = index == 0 ? "red" : "blue" })
                .ToList()
        };
    }

    private sealed class FakeUsersTable(params ApplicationUser[] users) : TableClient
    {
        public List<ApplicationUser> Users { get; } = [.. users];

        public override Task<Response<T>> GetEntityAsync<T>(string partitionKey, string rowKey, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            var user = Users.SingleOrDefault(candidate => string.Equals(candidate.RowKey, rowKey, StringComparison.Ordinal));
            return user is null
                ? throw new RequestFailedException((int)HttpStatusCode.NotFound, "Not found")
                : Task.FromResult(Response.FromValue((T)(ITableEntity)Copy(user), new FakeResponse((int)HttpStatusCode.OK)));
        }

        public override AsyncPageable<T> QueryAsync<T>(Expression<Func<T, bool>> filter, int? maxPerPage = null, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            var matches = Users.Select(Copy).Cast<T>().Where(filter.Compile()).ToList();
            return AsyncPageable<T>.FromPages([Page<T>.FromValues(matches, null, new FakeResponse((int)HttpStatusCode.OK))]);
        }

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            var user = (ApplicationUser)(ITableEntity)entity;
            if (Users.Any(candidate => candidate.RowKey == user.RowKey))
            {
                throw new RequestFailedException((int)HttpStatusCode.Conflict, "Conflict");
            }

            Users.Add(Copy(user));
            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }

        public override Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            var user = (ApplicationUser)(ITableEntity)entity;
            Users[Users.FindIndex(candidate => candidate.RowKey == user.RowKey)] = Copy(user);
            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }

        public override Task<Response> DeleteEntityAsync(string partitionKey, string rowKey, ETag ifMatch = default, CancellationToken cancellationToken = default)
        {
            Users.RemoveAll(candidate => candidate.RowKey == rowKey);
            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }

        private static ApplicationUser Copy(ApplicationUser user)
        {
            return new ApplicationUser
            {
                PartitionKey = user.PartitionKey,
                RowKey = user.RowKey,
                Email = user.Email,
                Name = user.Name,
                Nickname = user.Nickname,
                NormalizedNickname = user.NormalizedNickname,
                PreferredColor = user.PreferredColor,
                ThumbnailUrl = user.ThumbnailUrl,
                StrategyText = user.StrategyText,
                IsBot = user.IsBot,
                ExternalLoginProvider = user.ExternalLoginProvider
            };
        }
    }

    private sealed class FakeGamesTable(params GameEntity[] games) : TableClient
    {
        public List<GameEntity> Games { get; } = [.. games];

        public override Task<Response<T>> GetEntityAsync<T>(string partitionKey, string rowKey, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            var game = Games.SingleOrDefault(candidate => candidate.PartitionKey == partitionKey);
            return game is null
                ? throw new RequestFailedException((int)HttpStatusCode.NotFound, "Not found")
                : Task.FromResult(Response.FromValue((T)(ITableEntity)Copy(game), new FakeResponse((int)HttpStatusCode.OK)));
        }

        public override AsyncPageable<T> QueryAsync<T>(Expression<Func<T, bool>> filter, int? maxPerPage = null, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            var values = typeof(T) == typeof(GameEntity) ? Games.Select(Copy).Cast<T>().ToList() : [];
            return AsyncPageable<T>.FromPages([Page<T>.FromValues(values, null, new FakeResponse((int)HttpStatusCode.OK))]);
        }

        public override AsyncPageable<T> QueryAsync<T>(string? filter = null, int? maxPerPage = null, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            return AsyncPageable<T>.FromPages([Page<T>.FromValues([], null, new FakeResponse((int)HttpStatusCode.OK))]);
        }

        public override Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            var game = (GameEntity)(ITableEntity)entity;
            Games[Games.FindIndex(candidate => candidate.GameId == game.GameId)] = Copy(game);
            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }

        private static GameEntity Copy(GameEntity game)
        {
            return new GameEntity
            {
                PartitionKey = game.PartitionKey,
                RowKey = game.RowKey,
                GameId = game.GameId,
                Name = game.Name,
                CreatorId = game.CreatorId,
                State = game.State,
                SeatsJson = game.SeatsJson
            };
        }
    }

    private sealed class FakeResponse(int status) : Response
    {
        public override int Status => status;
        public override string ReasonPhrase => string.Empty;
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");

        public override void Dispose()
        {
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];

        protected override bool TryGetHeader(string name, out string? value)
        {
            value = null;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }
    }
}
