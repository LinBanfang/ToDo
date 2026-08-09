namespace ToDo.Models;

/// <summary>
/// Marker for entities whose position is a persisted <c>Order</c> int. Shared by
/// every drag-and-drop reorder site so <see cref="Services.ReorderService"/> can
/// re-stamp positions generically (ADR-004 half-zone insertion).
/// </summary>
public interface IOrdered
{
    int Order { get; set; }
}
