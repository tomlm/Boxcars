using Boxcars.Data;

namespace Boxcars.Engine.Tests.Unit;

public class SlangNamesTests
{
    [Theory]
    [InlineData("Butte", "Butt")]
    [InlineData("Portland OR", "Portlandia")]
    [InlineData("Portland ME", "Portland ME")]
    [InlineData("San Francisco/Oakland", "Frisco")]
    [InlineData("Kansas City", "KanSassCity")]
    [InlineData("Superchief", "Pooperchief")]
    [InlineData("T&P", "Toilet Paper")]
    [InlineData("Boston and Maine RR", "Bowel Movement")]
    [InlineData("Chicago", "Chicago")]
    public void Apply_Name_GivesItsNickname(string name, string expected)
    {
        Assert.Equal(expected, SlangNames.Apply(name));
    }

    [Fact]
    public void Apply_Sentence_RenamesRailroadsBeforeTheCitiesTheyAreNamedFor()
    {
        Assert.Equal(
            "Thermous rode the Dirgwa and Norfucker from Danvey to Norfuck in a Pooperchief.",
            SlangNames.Apply("Thermous rode the Denver and Rio Grande Western and Norfolk and Western from Denver to Norfolk in a Superchief."));
    }

    [Fact]
    public void Apply_NameInsideAnotherWord_IsLeftAlone()
    {
        Assert.Equal("Buttes and Omahans", SlangNames.Apply("Buttes and Omahans"));
        Assert.Equal("NYNH&HX", SlangNames.Apply("NYNH&HX"));
    }

    [Fact]
    public void Apply_SlangModeOff_LeavesTextAlone()
    {
        Assert.Equal("Butte", SlangNames.Apply("Butte", slangMode: false));
    }
}
