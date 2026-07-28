using System.Text.RegularExpressions;

namespace TrainPlanner.Models;

public enum TrainCategoryTier { Express, InterCity, Regional, Commuter, Other }

public record TrainCategory(string DisplayName, TrainCategoryTier Tier) : IComparable<TrainCategory>
{
    public int CompareTo(TrainCategory? other)
    {
        if (other is null)
            return 1;
        
        return other.Tier.CompareTo(Tier);
    }
    
    public static bool operator <(TrainCategory left, TrainCategory right)
        => left.CompareTo(right) < 0;

    public static bool operator >(TrainCategory left, TrainCategory right)
        => left.CompareTo(right) > 0;

    public static bool operator <=(TrainCategory left, TrainCategory right)
        => left.CompareTo(right) <= 0;

    public static bool operator >=(TrainCategory left, TrainCategory right)
        => left.CompareTo(right) >= 0;
}

public static class TrainCategories
{
    private static readonly Dictionary<string, TrainCategory> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EIP"] = new("InterCity Premium", TrainCategoryTier.Express),
        ["EIC"] = new("Express InterCity", TrainCategoryTier.Express),
        ["IC"]  = new("InterCity",         TrainCategoryTier.InterCity),
        ["EC"]  = new("InterCity",         TrainCategoryTier.InterCity),
        ["EN"]  = new("InterCity",         TrainCategoryTier.InterCity),
        ["TLK"] = new("Regional",          TrainCategoryTier.Regional),
        ["REG"] = new("Regional",          TrainCategoryTier.Regional),
        ["IR"]  = new("Regional",          TrainCategoryTier.Regional),
        ["R"]   = new("Regional",          TrainCategoryTier.Regional),
    };

    // Commuter pattern: letter(s) followed by digits, e.g. S1, S2, SKM3
    private static readonly Regex CommuterPattern =
        new(@"^[A-Z]{1,3}\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static TrainCategory Resolve(string? plkSymbol)
    {
        if (string.IsNullOrWhiteSpace(plkSymbol))
            return new("Unknown", TrainCategoryTier.Other);

        foreach (var symbol in plkSymbol.Split('/'))
        {
            var category = ResolveSingle(symbol.Trim());
            if (category.Tier != TrainCategoryTier.Other)
                return category;
        }

        return ResolveSingle(plkSymbol);
    }
    
    private static TrainCategory ResolveSingle(string symbol)
    {
        if (Map.TryGetValue(symbol, out var known))
            return known;

        if (CommuterPattern.IsMatch(symbol))
            return new(symbol, TrainCategoryTier.Commuter);

        return new($"Other ({symbol})", TrainCategoryTier.Other);
    }
}
