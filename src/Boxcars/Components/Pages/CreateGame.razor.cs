using Boxcars.Data;
using Boxcars.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Boxcars.Components.Pages;

public partial class CreateGame
{
    private readonly List<ApplicationUser> _players = [];
    private readonly List<CreateGameSlotState> _slots = [
        new(),
        new(),
        new(),
        new()
    ];

    private readonly string[] _colors = ["Blue", "Red", "Green", "Yellow", "Purple", "Orange"];

    private bool _loading = true;
    private bool _creating;
    private string? _errorMessage;
    private string? _currentUserId;

    [Inject]
    private GameService GameService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IMessageService MessageService { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null)
        {
            var state = await AuthState;
            _currentUserId = state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        var players = await GameService.GetAvailablePlayersAsync(CancellationToken.None);
        _players.AddRange(players);

        if (!string.IsNullOrWhiteSpace(_currentUserId))
        {
            var creatorSlot = _slots[0];
            creatorSlot.UserId = _currentUserId;

            var creator = _players.FirstOrDefault(player => string.Equals(player.RowKey, _currentUserId, StringComparison.OrdinalIgnoreCase));
            if (creator is not null)
            {
                creatorSlot.DisplayName = string.IsNullOrWhiteSpace(creator.Nickname) ? creator.Email : creator.Nickname;
                creatorSlot.Color = _colors[0];
            }
        }

        _loading = false;
    }

    private void OnUserChanged(int slotIndex, string? userId)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Count)
        {
            return;
        }

        var slot = _slots[slotIndex];
        slot.UserId = userId ?? string.Empty;

        var selected = _players.FirstOrDefault(player => string.Equals(player.RowKey, slot.UserId, StringComparison.OrdinalIgnoreCase));
        slot.DisplayName = selected is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(selected.Nickname) ? selected.Email : selected.Nickname;
    }

    private void OnColorChanged(int slotIndex, string? color)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Count)
        {
            return;
        }

        _slots[slotIndex].Color = color ?? string.Empty;
    }

    private async Task CreateGameAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentUserId))
        {
            _errorMessage = "Unable to identify current user.";
            return;
        }

        _errorMessage = null;
        _creating = true;

        try
        {
            var activeSlots = _slots.Where(slot => !string.IsNullOrWhiteSpace(slot.UserId) || !string.IsNullOrWhiteSpace(slot.Color)).ToList();
            if (activeSlots.Count < 2)
            {
                _errorMessage = "Assign at least two player slots.";
                return;
            }

            if (activeSlots.Any(slot => string.IsNullOrWhiteSpace(slot.UserId) || string.IsNullOrWhiteSpace(slot.Color)))
            {
                _errorMessage = "Every used slot must include both a player and a color.";
                return;
            }

            var duplicateUser = activeSlots
                .GroupBy(slot => slot.UserId, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);

            if (duplicateUser)
            {
                _errorMessage = "Each player can only be selected once.";
                return;
            }

            var duplicateColor = activeSlots
                .GroupBy(slot => slot.Color, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);

            if (duplicateColor)
            {
                _errorMessage = "Each color can only be selected once.";
                return;
            }

            var request = new CreateGameRequest
            {
                CreatorUserId = _currentUserId,
                Players = activeSlots
                    .Select(slot => new GamePlayerSelection
                    {
                        UserId = slot.UserId,
                        DisplayName = slot.DisplayName,
                        Color = slot.Color
                    })
                    .ToList()
            };

            var result = await GameService.CreateGameAsync(request, CancellationToken.None);
            if (!result.Success || string.IsNullOrWhiteSpace(result.GameId))
            {
                _errorMessage = result.Reason ?? "Failed to create game.";
                return;
            }

            Navigation.NavigateTo($"/game/{result.GameId}");
        }
        catch
        {
            await MessageService.ShowMessageBarAsync("Failed to create game.", MessageIntent.Error, "CREATE-GAME");
        }
        finally
        {
            _creating = false;
        }
    }

    private void BackToDashboard()
    {
        Navigation.NavigateTo("/dashboard");
    }

    private sealed class CreateGameSlotState
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
    }
}
