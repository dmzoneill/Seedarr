using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Indexers.Torznab;

public class TorznabSubcategory
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}

public class TorznabCategory
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public List<TorznabSubcategory> Subcategories { get; set; } = new();
}

public class TorznabSearchCapability
{
    public bool Available { get; set; }
    public List<string> SupportedParams { get; set; } = new();
}

public class TorznabCapabilities
{
    public string ServerTitle { get; set; }
    public string ServerVersion { get; set; }
    public List<TorznabCategory> Categories { get; set; } = new();
    public Dictionary<string, TorznabSearchCapability> Searching { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool SupportsSearch => Searching.TryGetValue("search", out var s) && s.Available;
    public bool SupportsTvSearch => Searching.TryGetValue("tv-search", out var s) && s.Available;
    public bool SupportsMovieSearch => Searching.TryGetValue("movie-search", out var s) && s.Available;
    public bool SupportsMusicSearch => Searching.TryGetValue("music-search", out var s) && s.Available;
    public bool SupportsBookSearch => Searching.TryGetValue("book-search", out var s) && s.Available;

    public List<int> GetAllCategoryIds()
    {
        var ids = new List<int>();
        foreach (var cat in Categories)
        {
            if (!ids.Contains(cat.Id))
            {
                ids.Add(cat.Id);
            }

            if (cat.Subcategories != null)
            {
                foreach (var sub in cat.Subcategories)
                {
                    if (!ids.Contains(sub.Id))
                    {
                        ids.Add(sub.Id);
                    }
                }
            }
        }

        return ids;
    }

    public TorznabCategory FindCategory(int id)
    {
        return Categories.FirstOrDefault(c => c.Id == id || (c.Subcategories != null && c.Subcategories.Any(s => s.Id == id)));
    }
}
