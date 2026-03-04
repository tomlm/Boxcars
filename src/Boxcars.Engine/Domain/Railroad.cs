using Boxcars.Engine.Data.Maps;

namespace Boxcars.Engine.Domain;

/// <summary>
/// Observable railroad entity.
/// </summary>
public sealed class Railroad : ObservableBase
{
    private Player? _owner;

    /// <summary>Railroad index from map definition (immutable).</summary>
    public int Index { get; }

    /// <summary>Full railroad name (immutable).</summary>
    public string Name { get; }

    /// <summary>Abbreviated name (immutable).</summary>
    public string ShortName { get; }

    /// <summary>Current owner (null = bank-held).</summary>
    public Player? Owner
    {
        get => _owner;
        internal set => SetField(ref _owner, value);
    }

    /// <summary>Cost to purchase.</summary>
    public int PurchasePrice { get; }

    /// <summary>Public railroads cannot be purchased.</summary>
    public bool IsPublic { get; }

    /// <summary>Reference to the map definition data.</summary>
    public RailroadDefinition Definition { get; }

    public Railroad(RailroadDefinition definition, int purchasePrice, bool isPublic)
    {
        Definition = definition;
        Index = definition.Index;
        Name = definition.Name;
        ShortName = definition.ShortName ?? definition.Name;
        PurchasePrice = purchasePrice;
        IsPublic = isPublic;
    }
}
