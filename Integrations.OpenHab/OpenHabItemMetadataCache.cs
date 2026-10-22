using SRF.Network.OpenHab.Items;
using System.Collections.Concurrent;

namespace HomeCompanion.Integrations.OpenHab;

public sealed class OpenHabItemMetadataCache
{
    private readonly ConcurrentDictionary<string, Item> _itemsByName = new(StringComparer.OrdinalIgnoreCase);

    public void Update(IEnumerable<Item> items)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                continue;

            seenNames.Add(item.Name);
            _itemsByName[item.Name] = item;
        }

        foreach (var existingName in _itemsByName.Keys)
        {
            if (seenNames.Contains(existingName))
                continue;

            _itemsByName.TryRemove(existingName, out _);
        }
    }

    public bool TryGetItem(string itemName, out Item? item)
    {
        if (_itemsByName.TryGetValue(itemName, out var cachedItem))
        {
            item = cachedItem;
            return true;
        }

        item = null;
        return false;
    }
}