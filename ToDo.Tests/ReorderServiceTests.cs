using System.Collections.ObjectModel;
using System.Linq;
using ToDo.Models;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises ReorderService — the shared half-zone insertion logic every drag-and-drop
/// site in the UI delegates to (ADR-004). Pins the before/after placement, the Order
/// re-stamping and the no-op guards.
/// </summary>
public sealed class ReorderServiceTests
{
    private sealed class OrderedItem : IOrdered
    {
        public string Name { get; init; } = "";
        public int Order { get; set; }
    }

    private static List<OrderedItem> Seq(params string[] names)
    {
        var list = new List<OrderedItem>();
        for (int i = 0; i < names.Length; i++)
            list.Add(new OrderedItem { Name = names[i], Order = i });
        return list;
    }

    private static OrderedItem Of(List<OrderedItem> items, string name) => items.First(i => i.Name == name);

    private static string Names(List<OrderedItem> items) => string.Join(",", items.Select(i => i.Name));

    private static string Orders(List<OrderedItem> items) => string.Join(",", items.Select(i => i.Order));

    [Fact]
    public void DropOnUpperHalf_InsertsDraggedBeforeTarget()
    {
        var items = Seq("A", "B", "C", "D");

        Assert.True(ReorderService.Reorder(items, Of(items, "D"), Of(items, "B"), lowerHalf: false));

        Assert.Equal("A,D,B,C", Names(items));
        Assert.Equal("0,1,2,3", Orders(items));   // Order re-stamped as the new index
    }

    [Fact]
    public void DropOnLowerHalf_InsertsDraggedAfterTarget()
    {
        var items = Seq("A", "B", "C", "D");

        Assert.True(ReorderService.Reorder(items, Of(items, "D"), Of(items, "B"), lowerHalf: true));

        Assert.Equal("A,B,D,C", Names(items));
        Assert.Equal("0,1,2,3", Orders(items));
    }

    [Fact]
    public void DropOnFirstItem_UpperHalf_MovesDraggedToFront()
    {
        var items = Seq("A", "B", "C", "D");

        Assert.True(ReorderService.Reorder(items, Of(items, "C"), Of(items, "A"), lowerHalf: false));

        Assert.Equal("C,A,B,D", Names(items));
    }

    [Fact]
    public void DropOnLastItem_LowerHalf_MovesDraggedToBack()
    {
        var items = Seq("A", "B", "C", "D");

        Assert.True(ReorderService.Reorder(items, Of(items, "A"), Of(items, "D"), lowerHalf: true));

        Assert.Equal("B,C,D,A", Names(items));
    }

    [Fact]
    public void DraggedBeforeTarget_LowerHalf_InsertsAfterTarget()
    {
        var items = Seq("A", "B", "C", "D");

        // Dragged is removed before the target index is resolved, so the insert
        // position is stable regardless of which side the dragged was on.
        Assert.True(ReorderService.Reorder(items, Of(items, "A"), Of(items, "B"), lowerHalf: true));

        Assert.Equal("B,A,C,D", Names(items));
    }

    [Fact]
    public void DraggedNotInSiblings_IsNoOp_AndReportsFailure()
    {
        var items = Seq("A", "B", "C");
        var foreign = new OrderedItem { Name = "X", Order = 9 };

        Assert.False(ReorderService.Reorder(items, foreign, Of(items, "B"), lowerHalf: false));

        Assert.Equal("A,B,C", Names(items));
        Assert.Equal("0,1,2", Orders(items));   // untouched — caller must skip persisting
    }

    [Fact]
    public void TargetMissing_IsNoOp_AndReportsFailure()
    {
        var items = Seq("A", "B", "C");
        var foreign = new OrderedItem { Name = "Z", Order = 9 };

        Assert.False(ReorderService.Reorder(items, Of(items, "A"), foreign, lowerHalf: false));

        Assert.Equal("A,B,C", Names(items));
        Assert.Equal("0,1,2", Orders(items));
    }

    [Fact]
    public void ObservableCollection_WorksAsSiblings()
    {
        // Step drag-reorder operates on a live ObservableCollection<TaskStep>.
        var steps = new ObservableCollection<TaskStep>
        {
            new() { Id = "s1", Order = 0 },
            new() { Id = "s2", Order = 1 },
            new() { Id = "s3", Order = 2 },
        };

        Assert.True(ReorderService.Reorder(steps, steps[2], steps[0], lowerHalf: false));

        Assert.Equal(new[] { "s3", "s1", "s2" }, steps.Select(s => s.Id));
        Assert.Equal(new[] { 0, 1, 2 }, steps.Select(s => s.Order));
    }
}
