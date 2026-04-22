using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using Nexus.Models;

namespace Nexus.Services;

public class SessionTokenStore(IDataProtectionProvider dpProvider, IJSRuntime js)
{
    private const string StorageKey = "nexus.linked-accounts";
    private const string Purpose = "Nexus.LinkedAccounts.v1";

    private readonly IDataProtector _protector = dpProvider.CreateProtector(Purpose);
    private LinkedAccounts? _cache;

    public event Action? AccountsChanged;

    /// <summary>
    /// Loads accounts from localStorage on first call, then returns the cached value.
    /// Must be called from OnAfterRenderAsync (JS interop is unavailable earlier).
    /// </summary>
    public async Task<LinkedAccounts> LoadAsync()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            var encrypted = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(encrypted))
            {
                var json = _protector.Unprotect(encrypted);
                _cache = JsonSerializer.Deserialize<LinkedAccounts>(json) ?? new();
                return _cache;
            }
        }
        catch
        {
            // Corrupt or tampered payload — start fresh.
        }

        _cache = new();
        return _cache;
    }

    /// <summary>Returns the in-memory cache. Returns an empty account list if LoadAsync has not been called yet.</summary>
    public LinkedAccounts GetLinkedAccounts() => _cache ?? new();

    public async Task LinkGitHubAccountAsync(GitHubAccountToken token)
    {
        LinkedAccounts accounts = await LoadAsync();
        accounts.GitHubAccounts.RemoveAll(a => a.Login == token.Login);
        accounts.GitHubAccounts.Add(token);
        await PersistAsync(accounts);
        AccountsChanged?.Invoke();
    }

    public async Task UnlinkGitHubAccountAsync(string login)
    {
        LinkedAccounts accounts = await LoadAsync();
        accounts.GitHubAccounts.RemoveAll(a => a.Login == login);
        await PersistAsync(accounts);
        AccountsChanged?.Invoke();
    }

    public async Task UpdateGitHubMonitoredReposAsync(string login, List<string> repos)
    {
        LinkedAccounts accounts = await LoadAsync();
        GitHubAccountToken? account = accounts.GitHubAccounts.FirstOrDefault(a => a.Login == login);
        if (account is null) return;
        if (account.MonitoredRepos.Order().SequenceEqual(repos.Order())) return;
        account.MonitoredRepos = repos;
        await PersistAsync(accounts);
        AccountsChanged?.Invoke();
    }

    public async Task LinkMicrosoftAccountAsync(MicrosoftAccountToken token)
    {
        LinkedAccounts accounts = await LoadAsync();
        accounts.MicrosoftAccounts.RemoveAll(a => a.Login == token.Login);
        accounts.MicrosoftAccounts.Add(token);
        await PersistAsync(accounts);
        AccountsChanged?.Invoke();
    }

    public async Task UnlinkMicrosoftAccountAsync(string login)
    {
        LinkedAccounts accounts = await LoadAsync();
        accounts.MicrosoftAccounts.RemoveAll(a => a.Login == login);
        await PersistAsync(accounts);
        AccountsChanged?.Invoke();
    }

    public async Task UpdateMicrosoftMonitoredProjectsAsync(string login, List<AdoMonitoredProject> projects)
    {
        LinkedAccounts accounts = await LoadAsync();
        MicrosoftAccountToken? account = accounts.MicrosoftAccounts.FirstOrDefault(a => a.Login == login);
        if (account is null) return;
        account.MonitoredProjects = projects;
        await PersistAsync(accounts);
        AccountsChanged?.Invoke();
    }

    private async Task PersistAsync(LinkedAccounts accounts)
    {
        var json = JsonSerializer.Serialize(accounts);
        var encrypted = _protector.Protect(json);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, encrypted);
    }
}
