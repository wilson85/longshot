using System.Runtime.InteropServices;

namespace LongShot.Rendering;

public sealed class RenderQueue
{
    readonly List<RenderItem> _items = new(256);

    public ReadOnlySpan<RenderItem> Items
        => CollectionsMarshal.AsSpan(_items);

    public void Clear()
    {
        _items.Clear();
    }

    public void Add(RenderItem item)
    {
        _items.Add(item);
    }
}