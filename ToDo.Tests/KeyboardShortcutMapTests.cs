using ToDo;
using Xunit;

namespace ToDo.Tests;

public sealed class KeyboardShortcutMapTests
{
    [Theory]
    [InlineData(1, "list-myday")]
    [InlineData(2, "list-important")]
    [InlineData(3, "list-planned")]
    [InlineData(4, "list-tasks")]
    public void SystemListId_MapsFirstFourSystemLists(int digit, string expected) =>
        Assert.Equal(expected, KeyboardShortcutMap.SystemListId(digit));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(-1)]
    public void SystemListId_OtherDigits_ReturnNull(int digit) =>
        Assert.Null(KeyboardShortcutMap.SystemListId(digit));
}
