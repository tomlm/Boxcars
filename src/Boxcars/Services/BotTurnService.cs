using System.Globalization;
using System.Text.Json;
using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.GameEngine;
using Microsoft.Extensions.Options;
using RailBaronGameEngine = global::Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Services;

public sealed class BotTurnService
{
    private readonly BotDefinitionService _botDefinitionService;
    private readonly BotDecisionPromptBuilder _promptBuilder;
    private readonly OpenAiBotClient _openAiBotClient;
    private readonly GamePresenceService _gamePresenceService;
    private readonly NetworkCoverageService _networkCoverageService;
    private readonly BotOptions _botOptions;
    private readonly PurchaseRulesOptions _purchaseRulesOptions;

    public BotTurnService(
        BotDefinitionService botDefinitionService,
        BotDecisionPromptBuilder promptBuilder,
        OpenAiBotClient openAiBotClient,
        GamePresenceService gamePresenceService,
        NetworkCoverageService networkCoverageService,
        IOptions<BotOptions> botOptions,
        IOptions<PurchaseRulesOptions> purchaseRulesOptions)
    {
        _botDefinitionService = botDefinitionService;
        _promptBuilder = promptBuilder;
        _openAiBotClient = openAiBotClient;
        _gamePresenceService = gamePresenceService;
        _networkCoverageService = networkCoverageService;
        _botOptions = botOptions.Value;
        _purchaseRulesOptions = purchaseRulesOptions.Value;
    }

    public IReadOnlyList<BotAssignment> GetAssignments(GameEntity game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return BotAssignmentSerialization.Deserialize(game.BotAssignmentsJson);
    }

    public BotAssignment? GetActiveAssignment(GameEntity game, string playerUserId)
    {
        return GetAssignments(game).FirstOrDefault(assignment =>
            string.Equals(assignment.PlayerUserId, playerUserId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(assignment.Status, BotAssignmentStatuses.Active, StringComparison.OrdinalIgnoreCase)
            && assignment.ClearedUtc is null);
    }

    public bool UpsertAssignment(
        GameEntity game,
        string playerUserId,
        string controllerUserId,
        string botDefinitionId,
        string? controllerMode = null)
    {
        ArgumentNullException.ThrowIfNull(game);

        controllerMode ??= string.IsNullOrWhiteSpace(controllerUserId)
            ? SeatControllerModes.AiBotSeat
            : SeatControllerModes.AiGhost;

        var assignments = GetAssignments(game).ToList();
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < assignments.Count; index++)
        {
            var assignment = assignments[index];
            if (!string.Equals(assignment.PlayerUserId, playerUserId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(assignment.Status, BotAssignmentStatuses.Active, StringComparison.OrdinalIgnoreCase)
                || assignment.ClearedUtc is not null)
            {
                continue;
            }

            assignments[index] = assignment with
            {
                Status = BotAssignmentStatuses.Cleared,
                ClearReason = "Reassigned",
                ClearedUtc = now
            };
        }

        assignments.Add(new BotAssignment
        {
            GameId = game.GameId,
            PlayerUserId = playerUserId,
            ControllerUserId = controllerUserId,
            ControllerMode = controllerMode,
            BotDefinitionId = botDefinitionId,
            AssignedUtc = now,
            Status = BotAssignmentStatuses.Active
        });

        PersistAssignments(game, assignments);
        return true;
    }

    public bool ClearAssignment(GameEntity game, string playerUserId, string clearReason, string clearedStatus = BotAssignmentStatuses.Cleared)
    {
        ArgumentNullException.ThrowIfNull(game);

        var assignments = GetAssignments(game).ToList();
        var changed = false;
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < assignments.Count; index++)
        {
            var assignment = assignments[index];
            if (!string.Equals(assignment.PlayerUserId, playerUserId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(assignment.Status, BotAssignmentStatuses.Active, StringComparison.OrdinalIgnoreCase)
                || assignment.ClearedUtc is not null)
            {
                continue;
            }

            assignments[index] = assignment with
            {
                Status = clearedStatus,
                ClearReason = clearReason,
                ClearedUtc = now
            };
            changed = true;
        }

        if (changed)
        {
            PersistAssignments(game, assignments);
        }

        return changed;
    }

    public async Task EnsureBotSeatAssignmentsAsync(
        GameEntity game,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        string controllerUserId,
        CancellationToken cancellationToken)
    {
        foreach (var selection in playerSelections)
        {
            if (string.IsNullOrWhiteSpace(selection.UserId))
            {
                continue;
            }

            var strategyProfile = await _botDefinitionService.GetAsync(selection.UserId, cancellationToken);
            if (strategyProfile is null || !strategyProfile.IsBotUser)
            {
                continue;
            }

            var existingAssignment = GetActiveAssignment(game, selection.UserId);
            if (existingAssignment is not null
                && string.Equals(PlayerControlRules.ResolveBotControllerMode(existingAssignment), SeatControllerModes.AiBotSeat, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existingAssignment.BotDefinitionId, selection.UserId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            UpsertAssignment(game, selection.UserId, string.Empty, selection.UserId, SeatControllerModes.AiBotSeat);
        }
    }

    public async Task<BotDecisionResolution?> ResolveDecisionAsync(
        GameEntity game,
        string playerUserId,
        string targetPlayerName,
        string phase,
        int turnNumber,
        string authoritativeStatePayload,
        IReadOnlyList<BotLegalOption> legalOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var assignmentContext = await ResolveAssignmentContextAsync(game, playerUserId, cancellationToken);
        if (assignmentContext is null)
        {
            return null;
        }

        var assignment = assignmentContext.Value.Assignment;
        var botDefinition = assignmentContext.Value.Definition;

        var context = new BotDecisionContext
        {
            GameId = game.GameId,
            PlayerUserId = playerUserId,
            TargetPlayerName = targetPlayerName,
            Phase = phase,
            TurnNumber = turnNumber,
            BotName = botDefinition.Name,
            StrategyText = botDefinition.StrategyText,
            GameStatePayload = authoritativeStatePayload,
            LegalOptions = legalOptions,
            TimeoutUtc = DateTimeOffset.UtcNow.AddSeconds(_botOptions.DecisionTimeoutSeconds)
        };

        if (legalOptions.Count == 0)
        {
            return null;
        }

        if (legalOptions.Count == 1)
        {
            return _promptBuilder.ResolveWithoutOpenAi(context, "Only one legal option was available.");
        }

        var systemPrompt = _promptBuilder.BuildSystemPrompt(context);
        var userPrompt = _promptBuilder.BuildUserPrompt(context);
        var openAiResult = await _openAiBotClient.SelectOptionAsync(systemPrompt, userPrompt, cancellationToken);

        if (!openAiResult.Succeeded)
        {
            return _promptBuilder.ResolveWithoutOpenAi(
                context,
                openAiResult.TimedOut ? "OpenAI request timed out." : openAiResult.FailureReason ?? "OpenAI request failed.");
        }

        var selectedOption = _promptBuilder.FindOption(context, openAiResult.SelectedOptionId);
        if (selectedOption is null)
        {
            return _promptBuilder.ResolveWithoutOpenAi(context, "OpenAI returned an invalid or stale option.");
        }

        return new BotDecisionResolution
        {
            GameId = context.GameId,
            PlayerUserId = context.PlayerUserId,
            Phase = context.Phase,
            SelectedOptionId = selectedOption.OptionId,
            Source = "OpenAI"
        };
    }

    public async Task<PlayerAction?> CreateBotActionAsync(
        GameEntity game,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(gameEngine);
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var snapshot = gameEngine.ToSnapshot();
        var playerSelections = GamePlayerSelectionSerialization.Deserialize(game.PlayersJson);

        if (gameEngine.CurrentTurn.AuctionState is not null)
        {
            return await CreateAuctionActionAsync(game, gameEngine, mapDefinition, snapshot, playerSelections, cancellationToken);
        }

        return gameEngine.CurrentTurn.Phase switch
        {
            TurnPhase.RegionChoice => await CreateRegionChoiceActionAsync(game, gameEngine, mapDefinition, snapshot, playerSelections, cancellationToken),
            TurnPhase.Move => await CreateMoveActionAsync(game, gameEngine, snapshot, playerSelections, cancellationToken),
            TurnPhase.Purchase => await CreatePurchaseActionAsync(game, gameEngine, mapDefinition, snapshot, playerSelections, cancellationToken),
            TurnPhase.UseFees => await CreateForcedSaleActionAsync(game, gameEngine, mapDefinition, playerSelections, cancellationToken),
            _ => null
        };
    }

    private async Task<PlayerAction?> CreateRegionChoiceActionAsync(
        GameEntity game,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var playerIndex = gameEngine.CurrentTurn.ActivePlayer.Index;
        var slotUserId = ResolveSlotUserId(playerSelections, playerIndex);
        if (string.IsNullOrWhiteSpace(slotUserId) || snapshot.Turn.PendingRegionChoice is null)
        {
            return null;
        }

        var assignmentContext = await ResolveAssignmentContextAsync(game, slotUserId, cancellationToken);
        if (assignmentContext is null)
        {
            return null;
        }

        var options = snapshot.Turn.PendingRegionChoice.EligibleRegionCodes
            .OrderBy(regionCode => regionCode, StringComparer.OrdinalIgnoreCase)
            .Select(regionCode => new BotLegalOption
            {
                OptionId = $"region:{regionCode}",
                OptionType = "Region",
                DisplayText = regionCode,
                Payload = regionCode
            })
            .ToList();

        var resolution = await ResolveDecisionAsync(
            game,
            slotUserId,
            gameEngine.CurrentTurn.ActivePlayer.Name,
            "PickRegion",
            snapshot.TurnNumber,
            BuildOpenAiStatePayload(
                game.GameId,
                gameEngine,
                snapshot,
                mapDefinition,
                playerSelections,
                playerIndex,
                slotUserId,
                BuildRegionChoicePhaseContext(mapDefinition, snapshot.Turn.PendingRegionChoice)),
            options,
            cancellationToken);

        if (resolution is null)
        {
            return null;
        }

        return new ChooseDestinationRegionAction
        {
            PlayerId = gameEngine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = playerIndex,
            ActorUserId = ResolveBotActorUserId(),
            SelectedRegionCode = resolution.SelectedOptionId["region:".Length..],
            BotMetadata = CreateBotMetadata(assignmentContext.Value.Assignment, assignmentContext.Value.Definition.Name, resolution.Source, resolution.FallbackReason)
        };
    }

    private async Task<PlayerAction?> CreateMoveActionAsync(
        GameEntity game,
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var player = gameEngine.CurrentTurn.ActivePlayer;
        var slotUserId = ResolveSlotUserId(playerSelections, player.Index);
        if (string.IsNullOrWhiteSpace(slotUserId))
        {
            return null;
        }

        var assignmentContext = await ResolveAssignmentContextAsync(game, slotUserId, cancellationToken);
        if (assignmentContext is null)
        {
            return null;
        }

        var snapshotPlayer = snapshot.Players[player.Index];
        var route = player.ActiveRoute;
        var routeProgressIndex = route is null
            ? 0
            : Math.Clamp(snapshotPlayer.RouteProgressIndex, 0, route.Segments.Count);

        if (route is null || routeProgressIndex >= route.Segments.Count)
        {
            route = gameEngine.SuggestRoute();
            routeProgressIndex = 0;
        }

        var remainingSegments = Math.Max(0, route.Segments.Count - routeProgressIndex);
        var steps = Math.Min(gameEngine.CurrentTurn.MovementRemaining, remainingSegments);
        if (steps <= 0)
        {
            return null;
        }

        var pointsTaken = route.NodeIds
            .Skip(routeProgressIndex)
            .Take(steps + 1)
            .ToList();
        var selectedSegmentKeys = route.Segments
            .Skip(routeProgressIndex)
            .Take(steps)
            .Select(segment => BuildSegmentKey(segment.FromNodeId, segment.ToNodeId, segment.RailroadIndex))
            .ToList();

        return new MoveAction
        {
            PlayerId = player.Name,
            PlayerIndex = player.Index,
            ActorUserId = ResolveBotActorUserId(),
            PointsTaken = pointsTaken,
            SelectedSegmentKeys = selectedSegmentKeys,
            BotMetadata = CreateBotMetadata(assignmentContext.Value.Assignment, assignmentContext.Value.Definition.Name, "SuggestedRoute")
        };
    }

    private async Task<PlayerAction?> CreatePurchaseActionAsync(
        GameEntity game,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var player = gameEngine.CurrentTurn.ActivePlayer;
        var slotUserId = ResolveSlotUserId(playerSelections, player.Index);
        if (string.IsNullOrWhiteSpace(slotUserId))
        {
            return null;
        }

        var assignmentContext = await ResolveAssignmentContextAsync(game, slotUserId, cancellationToken);
        if (assignmentContext is null)
        {
            return null;
        }

        var actionsByOptionId = new Dictionary<string, PlayerAction>(StringComparer.Ordinal);
        var legalOptions = new List<BotLegalOption>();
        var snapshotPlayer = snapshot.Players[player.Index];
        var pendingFeeAmount = CalculatePendingFeeAmount(gameEngine, player.Index, snapshotPlayer, snapshot.Turn.RailroadsRiddenThisTurn);

        foreach (var railroad in gameEngine.Railroads
                     .Where(railroad => railroad.Owner is null && !railroad.IsPublic && railroad.PurchasePrice <= player.Cash)
                     .OrderByDescending(railroad => railroad.PurchasePrice)
                     .ThenBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase))
        {
            var projectedCashAfterPurchase = player.Cash - railroad.PurchasePrice;
            var projectedFeeShortfall = Math.Max(0, pendingFeeAmount - projectedCashAfterPurchase);
            var optionId = $"buy-railroad:{railroad.Index}";
            legalOptions.Add(new BotLegalOption
            {
                OptionId = optionId,
                OptionType = "PurchaseRailroad",
                DisplayText = BuildPurchaseDisplayText(
                    $"Buy {railroad.Name}",
                    railroad.PurchasePrice,
                    projectedCashAfterPurchase,
                    pendingFeeAmount,
                    projectedFeeShortfall),
                Payload = railroad.Index.ToString(CultureInfo.InvariantCulture)
            });

            actionsByOptionId[optionId] = new PurchaseRailroadAction
            {
                PlayerId = player.Name,
                PlayerIndex = player.Index,
                ActorUserId = ResolveBotActorUserId(),
                RailroadIndex = railroad.Index,
                AmountPaid = railroad.PurchasePrice
            };
        }

        var currentEngineType = player.LocomotiveType;
        foreach (var engineType in Enum.GetValues<LocomotiveType>().Where(candidate => candidate > currentEngineType))
        {
            var amountPaid = RailBaronGameEngine.GetUpgradeCost(currentEngineType, engineType, _purchaseRulesOptions.SuperchiefPrice);
            if (amountPaid <= 0 || amountPaid > player.Cash)
            {
                continue;
            }

            var projectedCashAfterPurchase = player.Cash - amountPaid;
            var projectedFeeShortfall = Math.Max(0, pendingFeeAmount - projectedCashAfterPurchase);
            var optionId = $"buy-engine:{engineType}";
            legalOptions.Add(new BotLegalOption
            {
                OptionId = optionId,
                OptionType = "BuyEngine",
                DisplayText = BuildPurchaseDisplayText(
                    $"Buy {engineType}",
                    amountPaid,
                    projectedCashAfterPurchase,
                    pendingFeeAmount,
                    projectedFeeShortfall),
                Payload = engineType.ToString()
            });

            actionsByOptionId[optionId] = new BuyEngineAction
            {
                PlayerId = player.Name,
                PlayerIndex = player.Index,
                ActorUserId = ResolveBotActorUserId(),
                EngineType = engineType,
                AmountPaid = amountPaid
            };
        }

        const string declineOptionId = "decline-purchase";
        legalOptions.Add(new BotLegalOption
        {
            OptionId = declineOptionId,
            OptionType = "NoPurchase",
            DisplayText = "Buy nothing",
            Payload = string.Empty
        });
        actionsByOptionId[declineOptionId] = new DeclinePurchaseAction
        {
            PlayerId = player.Name,
            PlayerIndex = player.Index,
            ActorUserId = ResolveBotActorUserId()
        };

        var resolution = await ResolveDecisionAsync(
            game,
            slotUserId,
            player.Name,
            "Purchase",
            snapshot.TurnNumber,
            BuildOpenAiStatePayload(
                game.GameId,
                gameEngine,
                snapshot,
                mapDefinition,
                playerSelections,
                player.Index,
                slotUserId,
                BuildPurchasePhaseContext(gameEngine, snapshot, mapDefinition, player.Index, player)),
            legalOptions,
            cancellationToken);

        if (resolution is null || !actionsByOptionId.TryGetValue(resolution.SelectedOptionId, out var action))
        {
            return null;
        }

        return action with
        {
            BotMetadata = CreateBotMetadata(assignmentContext.Value.Assignment, assignmentContext.Value.Definition.Name, resolution.Source, resolution.FallbackReason)
        };
    }

    private async Task<PlayerAction?> CreateAuctionActionAsync(
        GameEntity game,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var auctionState = gameEngine.CurrentTurn.AuctionState;
        if (auctionState?.CurrentBidderPlayerIndex is not int bidderPlayerIndex)
        {
            return null;
        }

        var slotUserId = ResolveSlotUserId(playerSelections, bidderPlayerIndex);
        if (string.IsNullOrWhiteSpace(slotUserId))
        {
            return null;
        }

        var assignmentContext = await ResolveAssignmentContextAsync(game, slotUserId, cancellationToken);
        if (assignmentContext is null)
        {
            return null;
        }

        var bidder = gameEngine.Players[bidderPlayerIndex];
        var minimumBid = auctionState.CurrentBid > 0
            ? auctionState.CurrentBid + RailBaronGameEngine.AuctionBidIncrement
            : auctionState.StartingPrice;

        var legalOptions = new List<BotLegalOption>();
        var actionsByOptionId = new Dictionary<string, PlayerAction>(StringComparer.Ordinal);

        if (bidder.Cash >= minimumBid)
        {
            const string bidOptionId = "auction-bid:min";
            legalOptions.Add(new BotLegalOption
            {
                OptionId = bidOptionId,
                OptionType = "Bid",
                DisplayText = $"Bid {minimumBid.ToString("C0", CultureInfo.InvariantCulture)}",
                Payload = minimumBid.ToString(CultureInfo.InvariantCulture)
            });
            actionsByOptionId[bidOptionId] = new BidAction
            {
                PlayerId = bidder.Name,
                PlayerIndex = bidderPlayerIndex,
                ActorUserId = ResolveBotActorUserId(),
                RailroadIndex = auctionState.RailroadIndex,
                AmountBid = minimumBid
            };
        }

        const string passOptionId = "auction-pass";
        legalOptions.Add(new BotLegalOption
        {
            OptionId = passOptionId,
            OptionType = "Pass",
            DisplayText = "Pass",
            Payload = string.Empty
        });
        actionsByOptionId[passOptionId] = new AuctionPassAction
        {
            PlayerId = bidder.Name,
            PlayerIndex = bidderPlayerIndex,
            ActorUserId = ResolveBotActorUserId(),
            RailroadIndex = auctionState.RailroadIndex
        };

        var resolution = await ResolveDecisionAsync(
            game,
            slotUserId,
            bidder.Name,
            "Auction",
            snapshot.TurnNumber,
            BuildOpenAiStatePayload(
                game.GameId,
                gameEngine,
                snapshot,
                mapDefinition,
                playerSelections,
                bidderPlayerIndex,
                slotUserId,
                BuildAuctionPhaseContext(gameEngine, snapshot.Turn.Auction!, minimumBid)),
            legalOptions,
            cancellationToken);

        if (resolution is null || !actionsByOptionId.TryGetValue(resolution.SelectedOptionId, out var action))
        {
            return null;
        }

        return action with
        {
            BotMetadata = CreateBotMetadata(assignmentContext.Value.Assignment, assignmentContext.Value.Definition.Name, resolution.Source, resolution.FallbackReason)
        };
    }

    private async Task<PlayerAction?> CreateForcedSaleActionAsync(
        GameEntity game,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var player = gameEngine.CurrentTurn.ActivePlayer;
        var slotUserId = ResolveSlotUserId(playerSelections, player.Index);
        if (string.IsNullOrWhiteSpace(slotUserId) || gameEngine.CurrentTurn.ForcedSaleState is null)
        {
            return null;
        }

        var assignmentContext = await ResolveAssignmentContextAsync(game, slotUserId, cancellationToken);
        if (assignmentContext is null)
        {
            return null;
        }

        var currentCoverage = _networkCoverageService.BuildSnapshot(mapDefinition, player.OwnedRailroads.Select(railroad => railroad.Index));
        var bestCandidate = player.OwnedRailroads
            .Select(railroad =>
            {
                var projectedCoverage = _networkCoverageService.BuildProjectedSnapshotAfterSale(
                    mapDefinition,
                    player.OwnedRailroads.Select(ownedRailroad => ownedRailroad.Index),
                    railroad.Index);

                return new
                {
                    Railroad = railroad,
                    Evaluation = new SellImpactEvaluation
                    {
                        RailroadIndex = railroad.Index,
                        RailroadId = railroad.Name,
                        AccessDeltaScore = (int)Math.Round((projectedCoverage.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent) * 10m, MidpointRounding.AwayFromZero),
                        MonopolyDeltaScore = (int)Math.Round((projectedCoverage.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent) * 10m, MidpointRounding.AwayFromZero),
                        TieBreakerKey = railroad.Name,
                        CompositeRank = string.Concat(
                            projectedCoverage.AccessibleDestinationPercent.ToString("00000.0", CultureInfo.InvariantCulture),
                            ":",
                            projectedCoverage.MonopolyDestinationPercent.ToString("00000.0", CultureInfo.InvariantCulture),
                            ":",
                            railroad.Name)
                    }
                };
            })
            .OrderByDescending(candidate => candidate.Evaluation.AccessDeltaScore)
            .ThenByDescending(candidate => candidate.Evaluation.MonopolyDeltaScore)
            .ThenBy(candidate => candidate.Evaluation.TieBreakerKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (bestCandidate is null)
        {
            return null;
        }

        if (HasEligibleForcedSaleBidder(gameEngine, player, bestCandidate.Railroad))
        {
            return new StartAuctionAction
            {
                PlayerId = player.Name,
                PlayerIndex = player.Index,
                ActorUserId = ResolveBotActorUserId(),
                RailroadIndex = bestCandidate.Railroad.Index,
                BotMetadata = CreateBotMetadata(assignmentContext.Value.Assignment, assignmentContext.Value.Definition.Name, "DeterministicAuction")
            };
        }

        return new SellRailroadAction
        {
            PlayerId = player.Name,
            PlayerIndex = player.Index,
            ActorUserId = ResolveBotActorUserId(),
            RailroadIndex = bestCandidate.Railroad.Index,
            AmountReceived = bestCandidate.Railroad.PurchasePrice / 2,
            BotMetadata = CreateBotMetadata(assignmentContext.Value.Assignment, assignmentContext.Value.Definition.Name, "DeterministicSell")
        };
    }

    private static bool HasEligibleForcedSaleBidder(RailBaronGameEngine gameEngine, Player seller, Railroad railroad)
    {
        var startingPrice = railroad.PurchasePrice / 2;
        return gameEngine.Players.Any(player =>
            player != seller
            && player.IsActive
            && !player.IsBankrupt
            && player.Cash >= startingPrice);
    }

    private async Task<(BotAssignment Assignment, BotStrategyDefinitionEntity Definition)?> ResolveAssignmentContextAsync(
        GameEntity game,
        string playerUserId,
        CancellationToken cancellationToken)
    {
        var assignment = GetActiveAssignment(game, playerUserId);
        var delegatedControllerUserId = _gamePresenceService.GetDelegatedControllerUserId(game.GameId, playerUserId);
        var isConnected = _gamePresenceService.IsUserConnected(game.GameId, playerUserId);

        if (assignment is null)
        {
            if (isConnected || !string.IsNullOrWhiteSpace(delegatedControllerUserId))
            {
                return null;
            }

            var implicitDefinition = await _botDefinitionService.GetAsync(playerUserId, cancellationToken);
            if (implicitDefinition is null)
            {
                return null;
            }

            return (new BotAssignment
            {
                GameId = game.GameId,
                PlayerUserId = playerUserId,
                ControllerMode = SeatControllerModes.AiGhost,
                ControllerUserId = string.Empty,
                BotDefinitionId = playerUserId,
                Status = BotAssignmentStatuses.Active
            }, implicitDefinition);
        }

        var resolvedControllerMode = ResolveAssignmentControllerMode(assignment, playerUserId);
        var botDefinitionId = string.Equals(resolvedControllerMode, SeatControllerModes.AiGhost, StringComparison.OrdinalIgnoreCase)
            ? playerUserId
            : assignment.BotDefinitionId;
        var botDefinition = await _botDefinitionService.GetAsync(botDefinitionId, cancellationToken);
        if (botDefinition is null)
        {
            ClearAssignment(game, playerUserId, "The assigned bot definition no longer exists.", BotAssignmentStatuses.MissingDefinition);
            return null;
        }

        if (string.Equals(resolvedControllerMode, SeatControllerModes.AiGhost, StringComparison.OrdinalIgnoreCase)
            && isConnected)
        {
            ClearAssignment(game, playerUserId, "Player reconnected.", BotAssignmentStatuses.Cleared);
            return null;
        }

        if (string.Equals(resolvedControllerMode, SeatControllerModes.AiGhost, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(assignment.ControllerUserId)
            && !string.Equals(delegatedControllerUserId, assignment.ControllerUserId, StringComparison.OrdinalIgnoreCase))
        {
            ClearAssignment(game, playerUserId, "Delegated control is no longer active.", BotAssignmentStatuses.DisconnectedController);
            return null;
        }

        return (assignment with
        {
            ControllerMode = resolvedControllerMode,
            ControllerUserId = string.Equals(resolvedControllerMode, SeatControllerModes.AiBotSeat, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : assignment.ControllerUserId,
            BotDefinitionId = botDefinitionId
        }, botDefinition);
    }

    private string ResolveBotActorUserId()
    {
        return _botOptions.ServerActorUserId;
    }

    private static string ResolveAssignmentControllerMode(BotAssignment assignment, string playerUserId)
    {
        if (!string.IsNullOrWhiteSpace(assignment.ControllerMode))
        {
            return assignment.ControllerMode;
        }

        if (string.Equals(assignment.PlayerUserId, assignment.BotDefinitionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(assignment.PlayerUserId, playerUserId, StringComparison.OrdinalIgnoreCase))
        {
            return SeatControllerModes.AiBotSeat;
        }

        return string.IsNullOrWhiteSpace(assignment.ControllerUserId)
            ? SeatControllerModes.AiBotSeat
            : SeatControllerModes.AiGhost;
    }

    private static string? ResolveSlotUserId(IReadOnlyList<GamePlayerSelection> selections, int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < selections.Count
            ? selections[playerIndex].UserId
            : null;
    }

    private string BuildOpenAiStatePayload(
        string gameId,
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        MapDefinition mapDefinition,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        int targetPlayerIndex,
        string? targetPlayerUserId,
        object phaseContext)
    {
        var targetPlayerState = snapshot.Players[targetPlayerIndex];
        var regionProbabilityLookup = BuildRegionProbabilityLookup(mapDefinition);
        var otherOwnedRailroadIndices = snapshot.Players
            .Where((_, playerIndex) => playerIndex != targetPlayerIndex)
            .SelectMany(player => player.OwnedRailroadIndices)
            .Distinct()
            .ToArray();
        var targetOwnedRailroads = BuildRailroadSnapshots(gameEngine, targetPlayerState.OwnedRailroadIndices);
        var targetCoverage = _networkCoverageService.BuildSnapshot(mapDefinition, targetPlayerState.OwnedRailroadIndices);
        var targetReachableCoverage = _networkCoverageService.BuildSnapshotIncludingPublicRailroads(
            mapDefinition,
            targetPlayerState.OwnedRailroadIndices,
            otherOwnedRailroadIndices);

        return JsonSerializer.Serialize(new
        {
            GameId = gameId,
            snapshot.GameStatus,
            snapshot.TurnNumber,
            CurrentPhase = snapshot.Turn.Phase,
            ActivePlayer = new
            {
                snapshot.ActivePlayerIndex,
                PlayerName = snapshot.ActivePlayerIndex >= 0 && snapshot.ActivePlayerIndex < snapshot.Players.Count
                    ? snapshot.Players[snapshot.ActivePlayerIndex].Name
                    : string.Empty
            },
            TargetPlayer = new
            {
                PlayerIndex = targetPlayerIndex,
                PlayerName = targetPlayerState.Name,
                UserId = targetPlayerUserId,
                ExistingOwnedRailroads = targetOwnedRailroads,
                OwnedNetworkCoverage = targetCoverage,
                ReachableNetworkCoverageIncludingPublicRailroads = targetReachableCoverage
            },
            Players = snapshot.Players
                .Select((playerState, playerIndex) => BuildPlayerMetadata(gameEngine, mapDefinition, playerSelections, playerState, playerIndex, targetPlayerIndex))
                .ToList(),
            RailroadMarket = new
            {
                SoldRailroads = gameEngine.Railroads
                    .Where(railroad => !railroad.IsPublic && railroad.Owner is not null)
                    .OrderBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(railroad => new
                    {
                        railroad.Index,
                        railroad.Name,
                        railroad.ShortName,
                        railroad.PurchasePrice,
                        OwnerPlayerIndex = railroad.Owner!.Index,
                        OwnerPlayerName = railroad.Owner.Name
                    })
                    .ToList(),
                AvailableRailroads = gameEngine.Railroads
                    .Where(railroad => !railroad.IsPublic && railroad.Owner is null)
                    .OrderByDescending(railroad => railroad.PurchasePrice)
                    .ThenBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(railroad => new
                    {
                        railroad.Index,
                        railroad.Name,
                        railroad.ShortName,
                        railroad.PurchasePrice
                    })
                    .ToList(),
                PublicRailroads = gameEngine.Railroads
                    .Where(railroad => railroad.IsPublic)
                    .OrderBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(railroad => new
                    {
                        railroad.Index,
                        railroad.Name,
                        railroad.ShortName,
                        railroad.PurchasePrice
                    })
                    .ToList(),
                TargetPlayerExistingOwnedRailroads = targetOwnedRailroads,
                snapshot.AllRailroadsSold
            },
            MapPercentages = new
            {
                Regions = mapDefinition.Regions
                    .OrderBy(region => region.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(region => new
                    {
                        region.Index,
                        region.Code,
                        region.Name,
                        ProbabilityPercent = regionProbabilityLookup.TryGetValue(region.Code, out var probabilityPercent)
                            ? probabilityPercent
                            : 0m
                    })
                    .ToList(),
                Cities = BuildCityProbabilityRows(mapDefinition, regionProbabilityLookup)
            },
            TurnState = new
            {
                snapshot.Turn.Phase,
                snapshot.Turn.MovementAllowance,
                snapshot.Turn.MovementRemaining,
                snapshot.Turn.BonusRollAvailable,
                snapshot.Turn.PendingFeeAmount,
                snapshot.Turn.SelectedRailroadForSaleIndex,
                snapshot.Turn.RailroadsRiddenThisTurn,
                snapshot.Turn.DiceResult,
                snapshot.Turn.ArrivalResolution,
                snapshot.Turn.ForcedSale,
                snapshot.Turn.Auction,
                snapshot.Turn.PendingRegionChoice
            },
            PhaseContext = phaseContext
        });
    }

    private static object BuildRegionChoicePhaseContext(
        MapDefinition mapDefinition,
        global::Boxcars.Engine.Persistence.PendingRegionChoiceTurnState pendingRegionChoice)
    {
        var regionProbabilityLookup = BuildRegionProbabilityLookup(mapDefinition);

        return new
        {
            CurrentCity = BuildCityReference(mapDefinition, pendingRegionChoice.CurrentCityName),
            pendingRegionChoice.CurrentRegionCode,
            pendingRegionChoice.TriggeredByInitialRegionCode,
            EligibleRegions = pendingRegionChoice.EligibleRegionCodes
                .OrderBy(regionCode => regionCode, StringComparer.OrdinalIgnoreCase)
                .Select(regionCode => new
                {
                    RegionCode = regionCode,
                    EligibleCityCount = pendingRegionChoice.EligibleCityCountsByRegion.TryGetValue(regionCode, out var eligibleCityCount)
                        ? eligibleCityCount
                        : 0,
                    RegionProbabilityPercent = regionProbabilityLookup.TryGetValue(regionCode, out var probabilityPercent)
                        ? probabilityPercent
                        : 0m
                })
                .ToList()
        };
    }

    private object BuildPurchasePhaseContext(
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        MapDefinition mapDefinition,
        int playerIndex,
        Player player)
    {
        var ownedRailroadIndices = player.OwnedRailroads.Select(railroad => railroad.Index).ToArray();
        var currentCoverage = _networkCoverageService.BuildSnapshot(mapDefinition, ownedRailroadIndices);
        var snapshotPlayer = snapshot.Players[playerIndex];
        var pendingFeeAmount = CalculatePendingFeeAmount(gameEngine, playerIndex, snapshotPlayer, snapshot.Turn.RailroadsRiddenThisTurn);

        return new
        {
            CurrentCity = BuildCityReference(mapDefinition, player.CurrentCity.Name),
            DestinationCity = BuildCityReference(mapDefinition, player.Destination?.Name),
            player.Cash,
            Engine = player.LocomotiveType.ToString(),
            PendingFeeAmount = pendingFeeAmount,
            CashAfterFeesWithoutPurchase = player.Cash - pendingFeeAmount,
            ImmediateForcedSaleWithoutPurchase = player.Cash < pendingFeeAmount,
            AffordableRailroadOptions = gameEngine.Railroads
                .Where(railroad => railroad.Owner is null && !railroad.IsPublic && railroad.PurchasePrice <= player.Cash)
                .OrderByDescending(railroad => railroad.PurchasePrice)
                .ThenBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                .Select(railroad =>
                {
                    var projectedCoverage = _networkCoverageService.BuildProjectedSnapshot(mapDefinition, ownedRailroadIndices, railroad.Index);
                    var cashAfterPurchase = player.Cash - railroad.PurchasePrice;
                    var feeShortfall = Math.Max(0, pendingFeeAmount - cashAfterPurchase);
                    return new
                    {
                        railroad.Index,
                        railroad.Name,
                        railroad.ShortName,
                        railroad.PurchasePrice,
                        CashAfterPurchase = cashAfterPurchase,
                        CashAfterFees = cashAfterPurchase - pendingFeeAmount,
                        WouldTriggerForcedSale = feeShortfall > 0,
                        FeeShortfall = feeShortfall,
                        CurrentAccessibleDestinationPercent = currentCoverage.AccessibleDestinationPercent,
                        ProjectedAccessibleDestinationPercent = projectedCoverage.AccessibleDestinationPercent,
                        AccessDeltaPercent = Math.Round(projectedCoverage.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent, 1, MidpointRounding.AwayFromZero),
                        CurrentMonopolyDestinationPercent = currentCoverage.MonopolyDestinationPercent,
                        ProjectedMonopolyDestinationPercent = projectedCoverage.MonopolyDestinationPercent,
                        MonopolyDeltaPercent = Math.Round(projectedCoverage.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent, 1, MidpointRounding.AwayFromZero),
                        ProjectedRegionCoverage = projectedCoverage.RegionAccess
                    };
                })
                .ToList(),
            EngineUpgradeOptions = Enum.GetValues<LocomotiveType>()
                .Where(candidate => candidate > player.LocomotiveType)
                .Select(engineType =>
                {
                    var upgradeCost = RailBaronGameEngine.GetUpgradeCost(player.LocomotiveType, engineType, _purchaseRulesOptions.SuperchiefPrice);
                    var cashAfterPurchase = player.Cash - upgradeCost;
                    var feeShortfall = Math.Max(0, pendingFeeAmount - cashAfterPurchase);
                    return new
                    {
                        EngineType = engineType.ToString(),
                        UpgradeCost = upgradeCost,
                        IsAffordable = upgradeCost > 0 && upgradeCost <= player.Cash,
                        CashAfterPurchase = cashAfterPurchase,
                        CashAfterFees = cashAfterPurchase - pendingFeeAmount,
                        WouldTriggerForcedSale = feeShortfall > 0,
                        FeeShortfall = feeShortfall
                    };
                })
                .Where(option => option.UpgradeCost > 0)
                .ToList(),
            OwnedRailroads = player.OwnedRailroads
                .OrderBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                .Select(railroad =>
                {
                    var projectedCoverage = _networkCoverageService.BuildProjectedSnapshotAfterSale(mapDefinition, ownedRailroadIndices, railroad.Index);
                    return new
                    {
                        railroad.Index,
                        railroad.Name,
                        SaleValue = railroad.PurchasePrice / 2,
                        AccessDeltaPercentAfterSale = Math.Round(projectedCoverage.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent, 1, MidpointRounding.AwayFromZero),
                        MonopolyDeltaPercentAfterSale = Math.Round(projectedCoverage.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent, 1, MidpointRounding.AwayFromZero)
                    };
                })
                .ToList()
        };
    }

    private static string BuildPurchaseDisplayText(string label, int purchasePrice, int cashAfterPurchase, int pendingFeeAmount, int projectedFeeShortfall)
    {
        var currencyCulture = CultureInfo.InvariantCulture;

        if (pendingFeeAmount <= 0)
        {
            return $"{label} for {purchasePrice.ToString("C0", currencyCulture)} (cash after purchase {cashAfterPurchase.ToString("C0", currencyCulture)})";
        }

        if (projectedFeeShortfall > 0)
        {
            return $"{label} for {purchasePrice.ToString("C0", currencyCulture)} (cash after purchase {cashAfterPurchase.ToString("C0", currencyCulture)}, fees due {pendingFeeAmount.ToString("C0", currencyCulture)}, forced-sale shortfall {projectedFeeShortfall.ToString("C0", currencyCulture)})";
        }

        return $"{label} for {purchasePrice.ToString("C0", currencyCulture)} (cash after purchase {cashAfterPurchase.ToString("C0", currencyCulture)}, fees due {pendingFeeAmount.ToString("C0", currencyCulture)}, fees still covered)";
    }

    private static int CalculatePendingFeeAmount(
        RailBaronGameEngine gameEngine,
        int playerIndex,
        global::Boxcars.Engine.Persistence.PlayerState snapshotPlayer,
        IEnumerable<int> railroadsRiddenThisTurn)
    {
        var feeBuckets = new Dictionary<int, PendingFeeBucket>();

        foreach (var railroadIndex in railroadsRiddenThisTurn)
        {
            var railroad = gameEngine.Railroads.FirstOrDefault(candidate => candidate.Index == railroadIndex);
            if (railroad is null)
            {
                continue;
            }

            var usesBaseRate = railroad.Owner is null || railroad.Owner.Index == playerIndex;
            var ownerKey = usesBaseRate ? -1 : railroad.Owner!.Index;

            if (!feeBuckets.TryGetValue(ownerKey, out var bucket))
            {
                bucket = new PendingFeeBucket(usesBaseRate ? null : railroad.Owner);
                feeBuckets[ownerKey] = bucket;
            }

            if (!usesBaseRate && !snapshotPlayer.GrandfatheredRailroadIndices.Contains(railroad.Index))
            {
                bucket.RequiresFullOwnerRate = true;
            }
        }

        if (feeBuckets.Count == 0)
        {
            return 0;
        }

        var opponentRate = gameEngine.AllRailroadsSold ? 10000 : 5000;
        return feeBuckets.Values.Sum(bucket => bucket.Owner is null
            ? 1000
            : bucket.RequiresFullOwnerRate ? opponentRate : 1000);
    }

    private sealed class PendingFeeBucket(Player? owner)
    {
        public Player? Owner { get; } = owner;

        public bool RequiresFullOwnerRate { get; set; }
    }

    private static object BuildAuctionPhaseContext(
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.AuctionTurnState auctionState,
        int minimumBid)
    {
        return new
        {
            AuctionRailroad = BuildRailroadSnapshot(gameEngine, auctionState.RailroadIndex),
            auctionState.CurrentBid,
            auctionState.StartingPrice,
            MinimumBid = minimumBid,
            Seller = new
            {
                auctionState.SellerPlayerIndex,
                auctionState.SellerPlayerName
            },
            Participants = auctionState.Participants
                .OrderBy(participant => participant.PlayerIndex)
                .Select(participant => new
                {
                    participant.PlayerIndex,
                    participant.PlayerName,
                    participant.CashOnHand,
                    participant.LastBidAmount,
                    participant.IsEligible,
                    participant.HasDroppedOut,
                    participant.HasPassedThisRound,
                    participant.LastAction
                })
                .ToList()
        };
    }

    private static object BuildPlayerMetadata(
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        global::Boxcars.Engine.Persistence.PlayerState playerState,
        int playerIndex,
        int targetPlayerIndex)
    {
        return new
        {
            PlayerIndex = playerIndex,
            playerState.Name,
            UserId = ResolveSlotUserId(playerSelections, playerIndex),
            IsTargetPlayer = playerIndex == targetPlayerIndex,
            playerState.Cash,
            Engine = playerState.LocomotiveType,
            playerState.IsActive,
            playerState.IsBankrupt,
            playerState.HasDeclared,
            HomeCity = BuildCityReference(mapDefinition, playerState.HomeCityName),
            CurrentCity = BuildCityReference(mapDefinition, playerState.CurrentCityName),
            TripStartCity = BuildCityReference(mapDefinition, playerState.TripStartCityName),
            DestinationCity = BuildCityReference(mapDefinition, playerState.DestinationCityName),
            playerState.CurrentNodeId,
            playerState.RouteProgressIndex,
            ActiveRouteSegmentCount = playerState.ActiveRoute?.Segments.Count ?? 0,
            playerState.SelectedRouteNodeIds,
            playerState.SelectedRouteSegmentKeys,
            playerState.UsedSegments,
            OwnedRailroads = BuildRailroadSnapshots(gameEngine, playerState.OwnedRailroadIndices),
            GrandfatheredRailroads = BuildRailroadSnapshots(gameEngine, playerState.GrandfatheredRailroadIndices)
        };
    }

    private static List<object> BuildRailroadSnapshots(RailBaronGameEngine gameEngine, IEnumerable<int> railroadIndices)
    {
        var railroadLookup = gameEngine.Railroads.ToDictionary(railroad => railroad.Index);

        return railroadIndices
            .Distinct()
            .OrderBy(index => railroadLookup.TryGetValue(index, out var railroad) ? railroad.Name : index.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
            .Select(index => BuildRailroadSnapshot(railroadLookup, index))
            .Where(snapshot => snapshot is not null)
            .Cast<object>()
            .ToList();
    }

    private static object? BuildRailroadSnapshot(RailBaronGameEngine gameEngine, int railroadIndex)
    {
        return BuildRailroadSnapshot(gameEngine.Railroads.ToDictionary(railroad => railroad.Index), railroadIndex);
    }

    private static object? BuildRailroadSnapshot(Dictionary<int, Railroad> railroadLookup, int railroadIndex)
    {
        return railroadLookup.TryGetValue(railroadIndex, out var railroad)
            ? new
            {
                railroad.Index,
                railroad.Name,
                railroad.ShortName,
                railroad.PurchasePrice,
                railroad.IsPublic,
                OwnerPlayerIndex = railroad.Owner?.Index,
                OwnerPlayerName = railroad.Owner?.Name
            }
            : null;
    }

    private static object? BuildCityReference(MapDefinition mapDefinition, string? cityName)
    {
        if (string.IsNullOrWhiteSpace(cityName))
        {
            return null;
        }

        var city = mapDefinition.Cities.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, cityName, StringComparison.OrdinalIgnoreCase));

        return city is null
            ? new { Name = cityName }
            : new
            {
                city.Name,
                city.RegionCode,
                city.MapDotIndex,
                city.Probability,
                city.PayoutIndex
            };
    }

    private static Dictionary<string, decimal> BuildRegionProbabilityLookup(MapDefinition mapDefinition)
    {
        var regionIndexByCode = mapDefinition.Regions
            .ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);
        var weightedRegions = mapDefinition.Regions
            .Where(region => region.Probability.HasValue && region.Probability.Value > 0)
            .ToList();

        if (weightedRegions.Count > 0)
        {
            return weightedRegions.ToDictionary(
                region => region.Code,
                region => Math.Round((decimal)region.Probability!.Value, 3, MidpointRounding.AwayFromZero),
                StringComparer.OrdinalIgnoreCase);
        }

        var cityCountByRegion = mapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
            .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var totalCityCount = cityCountByRegion.Values.Sum();

        return cityCountByRegion.ToDictionary(
            entry => entry.Key,
            entry => totalCityCount == 0
                ? 0m
                : Math.Round(entry.Value * 100m / totalCityCount, 3, MidpointRounding.AwayFromZero),
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<object> BuildCityProbabilityRows(
        MapDefinition mapDefinition,
        Dictionary<string, decimal> regionProbabilityLookup)
    {
        var regionIndexByCode = mapDefinition.Regions
            .ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);
        var cityRows = new List<object>();

        foreach (var group in mapDefinition.Cities
                     .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
                     .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var cities = group.OrderBy(city => city.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var weightedCities = cities.Where(city => city.Probability.HasValue && city.Probability.Value > 0).ToList();
            var uniformWeight = cities.Count == 0
                ? 0m
                : Math.Round(100m / cities.Count, 2, MidpointRounding.AwayFromZero);

            foreach (var city in cities)
            {
                var withinRegionPercentage = weightedCities.Count > 0
                    ? city.Probability.HasValue
                        ? Math.Round((decimal)city.Probability.Value, 2, MidpointRounding.AwayFromZero)
                        : 0m
                    : uniformWeight;
                var globalAccessPercentage = regionProbabilityLookup.TryGetValue(city.RegionCode, out var regionProbability)
                    ? Math.Round(regionProbability * withinRegionPercentage / 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m;

                cityRows.Add(new
                {
                    city.RegionCode,
                    CityName = city.Name,
                    WithinRegionPercentage = withinRegionPercentage,
                    GlobalAccessPercentage = globalAccessPercentage
                });
            }
        }

        return cityRows;
    }

    private static string BuildSegmentKey(string fromNodeId, string toNodeId, int railroadIndex)
    {
        return string.Concat(fromNodeId, "|", toNodeId, "|", railroadIndex.ToString(CultureInfo.InvariantCulture));
    }

    private static BotRecordedActionMetadata CreateBotMetadata(BotAssignment assignment, string botName, string source, string? fallbackReason = null)
    {
        return new BotRecordedActionMetadata
        {
            BotDefinitionId = assignment.BotDefinitionId,
            BotName = botName,
            DecisionSource = source,
            FallbackReason = fallbackReason
        };
    }

    private static void PersistAssignments(GameEntity game, IReadOnlyList<BotAssignment> assignments)
    {
        game.BotAssignmentsJson = BotAssignmentSerialization.Serialize(assignments);
    }
}