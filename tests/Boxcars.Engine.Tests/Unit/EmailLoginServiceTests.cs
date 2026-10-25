using System.Linq.Expressions;
using System.Net;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Boxcars.Auth;
using Boxcars.Data;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class EmailLoginServiceTests
{
    [Fact]
    public async Task ResolveAsync_NewEmail_CreatesAnEmailOnlyAccount()
    {
        var users = new FakeUsersTableClient();
        var service = new EmailLoginService(new UserDirectoryService(users));

        var result = await service.ResolveAsync("  Friend@Example.com ", CancellationToken.None);

        var user = Assert.IsType<ApplicationUser>(result.User);
        Assert.Equal("friend@example.com", user.RowKey);
        Assert.Equal("friend", user.Nickname);
        Assert.Equal(EmailLoginService.EmailProvider, user.ExternalLoginProvider);
        Assert.Single(users.Users);
    }

    [Fact]
    public async Task ResolveAsync_ExistingEmailOnlyAccount_SignsIn()
    {
        var users = new FakeUsersTableClient(CreateUser("friend@example.com", EmailLoginService.EmailProvider, nickname: "Pat"));
        var service = new EmailLoginService(new UserDirectoryService(users));

        var result = await service.ResolveAsync("friend@example.com", CancellationToken.None);

        Assert.Equal("Pat", result.User?.Nickname);
        Assert.Single(users.Users);
    }

    [Theory]
    [InlineData("Google")]
    [InlineData("Microsoft")]
    [InlineData("")]
    public async Task ResolveAsync_AccountBackedByGoogleOrMicrosoft_IsRefused(string provider)
    {
        var users = new FakeUsersTableClient(CreateUser("owner@example.com", provider));
        var service = new EmailLoginService(new UserDirectoryService(users));

        var result = await service.ResolveAsync("owner@example.com", CancellationToken.None);

        Assert.Null(result.User);
        Assert.Contains("Use", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_Bot_IsRefused()
    {
        var bot = CreateUser("helper@example.com", string.Empty);
        bot.IsBot = true;
        var service = new EmailLoginService(new UserDirectoryService(new FakeUsersTableClient(bot)));

        var result = await service.ResolveAsync("helper@example.com", CancellationToken.None);

        Assert.Null(result.User);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("two@at@signs.com")]
    [InlineData("bot-123@boxcars.bot")]
    public async Task ResolveAsync_NotAnEmail_IsRefusedWithoutCreatingAnything(string email)
    {
        var users = new FakeUsersTableClient();
        var service = new EmailLoginService(new UserDirectoryService(users));

        var result = await service.ResolveAsync(email, CancellationToken.None);

        Assert.Null(result.User);
        Assert.Empty(users.Users);
    }

    [Fact]
    public async Task ListPeopleAsync_ListsEveryoneButBots_ByName()
    {
        var bot = CreateUser("bot-1@boxcars.bot", EmailLoginService.EmailProvider, nickname: "Bot");
        bot.IsBot = true;
        var users = new FakeUsersTableClient(
            CreateUser("zed@example.com", EmailLoginService.EmailProvider, nickname: "Zed"),
            CreateUser("owner@example.com", "Google", nickname: "Owner"),
            CreateUser("old@example.com", string.Empty, nickname: "Old"),
            bot,
            CreateUser("amy@example.com", EmailLoginService.EmailProvider, nickname: "Amy"));
        var service = new EmailLoginService(new UserDirectoryService(users));

        var players = await service.ListPeopleAsync(CancellationToken.None);

        Assert.Equal(["Amy", "Old", "Owner", "Zed"], players.Select(EmailLoginService.DisplayName));
    }

    [Fact]
    public async Task ResolvePlayerAsync_EmailOnlyPerson_SignsIn()
    {
        var pat = CreateUser("pat@example.com", EmailLoginService.EmailProvider, nickname: "Pat");
        var users = new FakeUsersTableClient(pat, CreateUser("owner@example.com", "Google", nickname: "Owner"));
        var service = new EmailLoginService(new UserDirectoryService(users));

        var result = await service.ResolvePlayerAsync(EmailLoginService.PlayerKey(pat), CancellationToken.None);

        Assert.Equal("pat@example.com", result.User?.RowKey);
    }

    [Fact]
    public async Task ResolvePlayerAsync_GoogleOrMicrosoftPersonOrBot_IsRefused()
    {
        var owner = CreateUser("owner@example.com", "Google", nickname: "Owner");
        var bot = CreateUser("bot-1@boxcars.bot", EmailLoginService.EmailProvider, nickname: "Bot");
        bot.IsBot = true;
        var users = new FakeUsersTableClient(owner, bot);
        var service = new EmailLoginService(new UserDirectoryService(users));

        Assert.Null((await service.ResolvePlayerAsync(EmailLoginService.PlayerKey(owner), CancellationToken.None)).User);
        Assert.Null((await service.ResolvePlayerAsync(EmailLoginService.PlayerKey(bot), CancellationToken.None)).User);
        Assert.Null((await service.ResolvePlayerAsync("not-a-key", CancellationToken.None)).User);
    }

    [Fact]
    public void PlayerKey_DoesNotContainTheEmail()
    {
        var key = EmailLoginService.PlayerKey(CreateUser("pat@example.com", EmailLoginService.EmailProvider));

        Assert.Equal(16, key.Length);
        Assert.DoesNotContain("pat", key, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationUser CreateUser(string email, string provider, string? nickname = null)
    {
        return new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = email,
            Email = email,
            Name = nickname ?? email,
            Nickname = nickname ?? string.Empty,
            ExternalLoginProvider = provider
        };
    }

    private sealed class FakeUsersTableClient(params ApplicationUser[] users) : TableClient
    {
        public List<ApplicationUser> Users { get; } = [.. users];

        public override Task<Response<T>> GetEntityAsync<T>(string partitionKey, string rowKey, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            var user = Users.SingleOrDefault(candidate => string.Equals(candidate.RowKey, rowKey, StringComparison.Ordinal));
            return user is null
                ? throw new RequestFailedException((int)HttpStatusCode.NotFound, "Not found")
                : Task.FromResult(Response.FromValue((T)(ITableEntity)user, new FakeResponse((int)HttpStatusCode.OK)));
        }

        public override AsyncPageable<T> QueryAsync<T>(Expression<Func<T, bool>> filter, int? maxPerPage = null, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            return AsyncPageable<T>.FromPages([Page<T>.FromValues(Users.Cast<T>().ToList(), null, new FakeResponse((int)HttpStatusCode.OK))]);
        }

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            var user = (ApplicationUser)(ITableEntity)entity;
            if (Users.Any(candidate => string.Equals(candidate.RowKey, user.RowKey, StringComparison.Ordinal)))
            {
                throw new RequestFailedException((int)HttpStatusCode.Conflict, "Conflict");
            }

            Users.Add(user);
            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
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
