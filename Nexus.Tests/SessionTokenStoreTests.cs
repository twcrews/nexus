using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using Nexus.Models;
using Nexus.Services;

namespace Nexus.Tests;

public class SessionTokenStoreTests
{
    private static SessionTokenStore MakeStore(out FakeJsRuntime js)
    {
        js = new FakeJsRuntime();
        var dp = new FakeDataProtectionProvider();
        return new SessionTokenStore(dp, js);
    }

    [Fact]
    public async Task LoadAsync_NoStoredData_ReturnsEmptyAccounts()
    {
        var store = MakeStore(out _);
        var accounts = await store.LoadAsync();
        Assert.Empty(accounts.GitHubAccounts);
        Assert.Empty(accounts.MicrosoftAccounts);
    }

    [Fact]
    public async Task LoadAsync_CalledTwice_ReturnsSameInstance()
    {
        var store = MakeStore(out _);
        var first = await store.LoadAsync();
        var second = await store.LoadAsync();
        Assert.Same(first, second);
    }

    [Fact]
    public void GetLinkedAccounts_BeforeLoad_ReturnsEmptyAccounts()
    {
        var store = MakeStore(out _);
        var accounts = store.GetLinkedAccounts();
        Assert.Empty(accounts.GitHubAccounts);
        Assert.Empty(accounts.MicrosoftAccounts);
    }

    [Fact]
    public async Task LinkGitHubAccount_AccountIsStoredAndReturned()
    {
        var store = MakeStore(out _);
        var token = new GitHubAccountToken
        {
            Login = "alice",
            DisplayName = "Alice",
            PersonalAccessToken = "ghp_test",
            MonitoredRepos = ["owner/repo"]
        };

        await store.LinkGitHubAccountAsync(token);

        var accounts = store.GetLinkedAccounts();
        Assert.Single(accounts.GitHubAccounts);
        Assert.Equal("alice", accounts.GitHubAccounts[0].Login);
    }

    [Fact]
    public async Task LinkGitHubAccount_SameLoginTwice_ReplacesExisting()
    {
        var store = MakeStore(out _);
        var token1 = new GitHubAccountToken { Login = "alice", PersonalAccessToken = "old" };
        var token2 = new GitHubAccountToken { Login = "alice", PersonalAccessToken = "new" };

        await store.LinkGitHubAccountAsync(token1);
        await store.LinkGitHubAccountAsync(token2);

        var accounts = store.GetLinkedAccounts();
        Assert.Single(accounts.GitHubAccounts);
        Assert.Equal("new", accounts.GitHubAccounts[0].PersonalAccessToken);
    }

    [Fact]
    public async Task LinkGitHubAccount_FiresAccountsChangedEvent()
    {
        var store = MakeStore(out _);
        var fired = false;
        store.AccountsChanged += () => fired = true;

        await store.LinkGitHubAccountAsync(new GitHubAccountToken { Login = "alice" });

        Assert.True(fired);
    }

    [Fact]
    public async Task UnlinkGitHubAccount_RemovesAccount()
    {
        var store = MakeStore(out _);
        await store.LinkGitHubAccountAsync(new GitHubAccountToken { Login = "alice" });
        await store.UnlinkGitHubAccountAsync("alice");

        Assert.Empty(store.GetLinkedAccounts().GitHubAccounts);
    }

    [Fact]
    public async Task UnlinkGitHubAccount_NonExistentLogin_DoesNotThrow()
    {
        var store = MakeStore(out _);
        var ex = await Record.ExceptionAsync(() => store.UnlinkGitHubAccountAsync("nobody"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task UnlinkGitHubAccount_FiresAccountsChangedEvent()
    {
        var store = MakeStore(out _);
        await store.LinkGitHubAccountAsync(new GitHubAccountToken { Login = "alice" });
        var fired = false;
        store.AccountsChanged += () => fired = true;

        await store.UnlinkGitHubAccountAsync("alice");

        Assert.True(fired);
    }

    [Fact]
    public async Task UpdateGitHubMonitoredRepos_UpdatesRepoList()
    {
        var store = MakeStore(out _);
        await store.LinkGitHubAccountAsync(new GitHubAccountToken { Login = "alice", MonitoredRepos = [] });

        await store.UpdateGitHubMonitoredReposAsync("alice", ["owner/repo1", "owner/repo2"]);

        var account = store.GetLinkedAccounts().GitHubAccounts[0];
        Assert.Contains("owner/repo1", account.MonitoredRepos);
        Assert.Contains("owner/repo2", account.MonitoredRepos);
    }

    [Fact]
    public async Task UpdateGitHubMonitoredRepos_SameRepos_DoesNotFireEvent()
    {
        var store = MakeStore(out _);
        await store.LinkGitHubAccountAsync(new GitHubAccountToken
        {
            Login = "alice",
            MonitoredRepos = ["owner/repo"]
        });

        var fired = false;
        store.AccountsChanged += () => fired = true;

        // Same repos in different order — SequenceEqual with Order() means same set = no change
        await store.UpdateGitHubMonitoredReposAsync("alice", ["owner/repo"]);

        Assert.False(fired);
    }

    [Fact]
    public async Task UpdateGitHubMonitoredRepos_UnknownLogin_DoesNotThrow()
    {
        var store = MakeStore(out _);
        var ex = await Record.ExceptionAsync(
            () => store.UpdateGitHubMonitoredReposAsync("nobody", ["owner/repo"]));
        Assert.Null(ex);
    }

    [Fact]
    public async Task LinkMicrosoftAccount_AccountIsStoredAndReturned()
    {
        var store = MakeStore(out _);
        var token = new MicrosoftAccountToken
        {
            Login = "user@example.com",
            DisplayName = "User",
            PersonalAccessToken = "pat",
            OrgUrl = "https://dev.azure.com/myorg"
        };

        await store.LinkMicrosoftAccountAsync(token);

        var accounts = store.GetLinkedAccounts();
        Assert.Single(accounts.MicrosoftAccounts);
        Assert.Equal("user@example.com", accounts.MicrosoftAccounts[0].Login);
    }

    [Fact]
    public async Task LinkMicrosoftAccount_SameLoginTwice_ReplacesExisting()
    {
        var store = MakeStore(out _);
        await store.LinkMicrosoftAccountAsync(new MicrosoftAccountToken { Login = "user@example.com", PersonalAccessToken = "old" });
        await store.LinkMicrosoftAccountAsync(new MicrosoftAccountToken { Login = "user@example.com", PersonalAccessToken = "new" });

        var accounts = store.GetLinkedAccounts();
        Assert.Single(accounts.MicrosoftAccounts);
        Assert.Equal("new", accounts.MicrosoftAccounts[0].PersonalAccessToken);
    }

    [Fact]
    public async Task UnlinkMicrosoftAccount_RemovesAccount()
    {
        var store = MakeStore(out _);
        await store.LinkMicrosoftAccountAsync(new MicrosoftAccountToken { Login = "user@example.com" });
        await store.UnlinkMicrosoftAccountAsync("user@example.com");

        Assert.Empty(store.GetLinkedAccounts().MicrosoftAccounts);
    }

    [Fact]
    public async Task UpdateMicrosoftMonitoredProjects_UpdatesProjectList()
    {
        var store = MakeStore(out _);
        await store.LinkMicrosoftAccountAsync(new MicrosoftAccountToken
        {
            Login = "user@example.com",
            MonitoredProjects = []
        });

        var projects = new List<AdoMonitoredProject>
        {
            new() { ProjectName = "Project1", OrgUrl = "https://dev.azure.com/myorg" }
        };

        await store.UpdateMicrosoftMonitoredProjectsAsync("user@example.com", projects);

        var account = store.GetLinkedAccounts().MicrosoftAccounts[0];
        Assert.Single(account.MonitoredProjects);
        Assert.Equal("Project1", account.MonitoredProjects[0].ProjectName);
    }

    [Fact]
    public async Task Persist_DataSurvivesReload()
    {
        // Use the same FakeJsRuntime instance to simulate browser storage surviving a new store instance
        var js = new FakeJsRuntime();
        var dp = new FakeDataProtectionProvider();

        var store1 = new SessionTokenStore(dp, js);
        await store1.LinkGitHubAccountAsync(new GitHubAccountToken { Login = "alice", PersonalAccessToken = "ghp_test" });

        // New store instance with same backing storage
        var store2 = new SessionTokenStore(dp, js);
        var accounts = await store2.LoadAsync();

        Assert.Single(accounts.GitHubAccounts);
        Assert.Equal("alice", accounts.GitHubAccounts[0].Login);
    }

    [Fact]
    public async Task LoadAsync_CorruptStorage_ReturnsEmptyAndDoesNotThrow()
    {
        var js = new FakeJsRuntime();
        js.SetStorageValue("nexus.linked-accounts", "not-valid-encrypted-data");
        var dp = new FakeDataProtectionProvider(throwOnUnprotect: true);

        var store = new SessionTokenStore(dp, js);
        var accounts = await store.LoadAsync();

        Assert.Empty(accounts.GitHubAccounts);
        Assert.Empty(accounts.MicrosoftAccounts);
    }

    // --- Fakes ---

    internal class FakeJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string?> _storage = new();

        public void SetStorageValue(string key, string? value) => _storage[key] = value;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => Handle<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => Handle<TValue>(identifier, args);

        private ValueTask<TValue> Handle<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "localStorage.getItem" && args?.Length > 0 && args[0] is string key)
            {
                var val = _storage.TryGetValue(key, out var v) ? v : null;
                return ValueTask.FromResult((TValue)(object?)val!);
            }
            if (identifier == "localStorage.setItem" && args?.Length >= 2 && args[0] is string setKey)
            {
                _storage[setKey] = args[1]?.ToString();
            }
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    internal class FakeDataProtector(bool throwOnUnprotect = false) : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        // String Protect/Unprotect are extension methods that call these byte[] overloads.
        public byte[] Protect(byte[] plaintext) => plaintext;

        public byte[] Unprotect(byte[] protectedData)
        {
            if (throwOnUnprotect)
                throw new InvalidOperationException("Simulated corrupt data");
            return protectedData;
        }
    }

    internal class FakeDataProtectionProvider(bool throwOnUnprotect = false) : IDataProtectionProvider
    {
        private readonly FakeDataProtector _protector = new(throwOnUnprotect);
        public IDataProtector CreateProtector(string purpose) => _protector;
    }
}
