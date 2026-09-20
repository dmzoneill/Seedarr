using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace NzbDrone.Core.Indexers.Torznab;

public static class TorznabCapsParser
{
    public static TorznabCapabilities Parse(string xml)
    {
        var caps = new TorznabCapabilities();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return caps;
        }

        try
        {
            var doc = new XmlDocument();
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 1024
            };

            using var stringReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(stringReader, settings);
            doc.Load(xmlReader);

            return Parse(doc);
        }
        catch (XmlException)
        {
            return caps;
        }
    }

    public static TorznabCapabilities Parse(XmlDocument doc)
    {
        var caps = new TorznabCapabilities();
        if (doc == null)
        {
            return caps;
        }

        // Server info
        var serverNode = doc.SelectSingleNode("//server") ?? doc.SelectSingleNode("//*[local-name()='server']");
        if (serverNode?.Attributes != null)
        {
            caps.ServerTitle = serverNode.Attributes["title"]?.Value;
            caps.ServerVersion = serverNode.Attributes["version"]?.Value;
        }

        // Searching capabilities
        var searchingNode = doc.SelectSingleNode("//searching") ?? doc.SelectSingleNode("//*[local-name()='searching']");
        if (searchingNode != null)
        {
            foreach (XmlNode child in searchingNode.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                var mode = child.LocalName.ToLowerInvariant();
                var availableAttr = child.Attributes?["available"]?.Value;
                var isAvailable = string.Equals(availableAttr, "yes", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(availableAttr, "1", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(availableAttr, "true", StringComparison.OrdinalIgnoreCase);

                var supportedParamsStr = child.Attributes?["supportedParams"]?.Value;
                var supportedParams = new List<string>();
                if (!string.IsNullOrWhiteSpace(supportedParamsStr))
                {
                    supportedParams = supportedParamsStr
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                }

                caps.Searching[mode] = new TorznabSearchCapability
                {
                    Available = isAvailable,
                    SupportedParams = supportedParams
                };
            }
        }

        // Categories
        var categoriesNode = doc.SelectSingleNode("//categories") ?? doc.SelectSingleNode("//*[local-name()='categories']");
        if (categoriesNode != null)
        {
            var catNodes = categoriesNode.SelectNodes("./*[local-name()='category']") ?? categoriesNode.SelectNodes(".//*[local-name()='category']");
            if (catNodes != null)
            {
                foreach (XmlNode catNode in catNodes)
                {
                    var idAttr = catNode.Attributes?["id"]?.Value;
                    if (!int.TryParse(idAttr, out var catId))
                    {
                        continue;
                    }

                    // Avoid duplicate category entries
                    var existing = caps.Categories.FirstOrDefault(c => c.Id == catId);
                    var category = existing ?? new TorznabCategory
                    {
                        Id = catId,
                        Name = catNode.Attributes?["name"]?.Value ?? string.Empty,
                        Description = catNode.Attributes?["description"]?.Value
                    };

                    if (existing == null)
                    {
                        caps.Categories.Add(category);
                    }

                    // Subcategories
                    var subNodes = catNode.SelectNodes(".//*[local-name()='subcat']");
                    if (subNodes != null)
                    {
                        foreach (XmlNode subNode in subNodes)
                        {
                            var subIdAttr = subNode.Attributes?["id"]?.Value;
                            if (!int.TryParse(subIdAttr, out var subId))
                            {
                                continue;
                            }

                            if (!category.Subcategories.Any(s => s.Id == subId))
                            {
                                category.Subcategories.Add(new TorznabSubcategory
                                {
                                    Id = subId,
                                    Name = subNode.Attributes?["name"]?.Value ?? string.Empty,
                                    Description = subNode.Attributes?["description"]?.Value
                                });
                            }
                        }
                    }
                }
            }
        }

        return caps;
    }
}
