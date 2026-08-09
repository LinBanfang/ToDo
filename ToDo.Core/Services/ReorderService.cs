using ToDo.Models;

namespace ToDo.Services;

/// <summary>
/// Half-zone insertion reorder (ADR-004), shared by every drag-and-drop site in the
/// UI: move <typeparamref name="T"/> <c>dragged</c> to just before/after <c>target</c>
/// in <c>siblings</c> (upper half of the target inserts before, lower half after),
/// then re-stamp each item's <c>Order</c> with its new index. Persistence stays with
/// the caller — this service only decides the new order.
/// </summary>
public static class ReorderService
{
    /// <param name="siblings">The ordered siblings to mutate (a List or ObservableCollection).</param>
    /// <param name="dragged">The item being dragged, already a member of <paramref name="siblings"/>.</param>
    /// <param name="target">The drop-target item; the dragged item lands before or after it.</param>
    /// <param name="lowerHalf">True = insert after the target (drop on its lower half).</param>
    /// <returns>False when the drag can't be placed (dragged not in siblings / target missing) — callers skip persisting then.</returns>
    public static bool Reorder<T>(IList<T> siblings, T dragged, T target, bool lowerHalf) where T : IOrdered
    {
        if (!siblings.Contains(dragged)) return false;

        // Resolve the target BEFORE removing the dragged item, so a missing target
        // leaves the collection untouched (the caller skips persisting on false).
        var targetIdx = siblings.IndexOf(target);
        if (targetIdx < 0) return false;

        var draggedIdx = siblings.IndexOf(dragged);
        siblings.Remove(dragged);
        if (draggedIdx < targetIdx) targetIdx--;   // removal shifted the target down one

        siblings.Insert(lowerHalf ? targetIdx + 1 : targetIdx, dragged);
        for (int i = 0; i < siblings.Count; i++)
            siblings[i].Order = i;
        return true;
    }
}
