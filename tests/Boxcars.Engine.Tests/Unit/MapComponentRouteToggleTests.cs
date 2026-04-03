using System.Reflection;
using Boxcars.Components.Map;

namespace Boxcars.Engine.Tests.Unit;

public class MapComponentRouteToggleTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(3, false, true)]
    [InlineData(3, true, false)]
    public void CanAutoApplySuggestedRouteSelection_RespectsDismissedState(int movementCapacity, bool suggestionSelectionDismissed, bool expected)
    {
        var method = typeof(MapComponent).GetMethod(
            "CanAutoApplySuggestedRouteSelection",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Expected helper method was not found.");

        var result = (bool)(method.Invoke(null, [movementCapacity, suggestionSelectionDismissed])
            ?? throw new InvalidOperationException("Expected a boolean result."));

        Assert.Equal(expected, result);
    }
}
