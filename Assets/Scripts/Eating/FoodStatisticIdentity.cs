using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public static class FoodStatisticIdentity
{
    public static readonly string[] KnownKeys =
    {
        "wheat", "rice", "corn", "sunflower", "watermelon", "tomato",
        "pumpkin", "apple", "cookie", "bread", "croissant", "donut", "strawberry", "blackberry", "watermelon_flesh",
        "tomato_fruit", "lettuce"
    };

    public static string ResolveKey(EdibleObject edible, string explicitKey = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitKey)) return Normalize(explicitKey);
        if (edible == null) return "other";

        EdibleFoodSource source = edible.GetComponentInParent<EdibleFoodSource>(true);
        string normalized = Normalize(source != null ? source.name : edible.name);
        if (normalized.Contains("wheat")) return "wheat";
        if (normalized.Contains("rice")) return "rice";
        if (normalized.Contains("corn")) return "corn";
        if (normalized.Contains("sunflower")) return "sunflower";
        if (normalized.Contains("watermelon")) return "watermelon";
        if (normalized.Contains("tomato")) return "tomato";
        if (normalized.Contains("pumpkin")) return "pumpkin";
        if (normalized.Contains("apple")) return "apple";
        if (normalized.Contains("cookie")) return "cookie";
        if (normalized.Contains("bread")) return "bread";
        if (normalized.Contains("croissant")) return "croissant";
        if (normalized.Contains("donut")) return "donut";
        if (normalized.Contains("strawberry")) return "strawberry";
        if (normalized.Contains("blackberry")) return "blackberry";
        normalized = Regex.Replace(normalized, @"(?:_|-)?\d+$", string.Empty);
        return string.IsNullOrWhiteSpace(normalized) ? "other" : normalized;
    }

    public static string ResolveDisplayName(string key, string explicitName = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName.Trim();
        switch (Normalize(key))
        {
            case "wheat": return "Buğday";
            case "rice": return "Pirinç";
            case "corn": return "Mısır";
            case "sunflower": return "Ay çekirdeği";
            case "watermelon": return "Karpuz çekirdeği";
            case "tomato": return "Domates çekirdeği";
            case "pumpkin": return "Kabak çekirdeği";
            case "apple": return "Elma";
            case "cookie": return "Kurabiye";
            case "bread": return "Ekmek";
            case "croissant": return "Kruvasan";
            case "donut": return "Donut";
            case "strawberry": return "Çilek";
            case "blackberry": return "Böğürtlen";
            case "watermelon_flesh": return "Karpuz";
            case "tomato_fruit": return "Domates";
            case "lettuce": return "Marul";
            case "other": return "Diğer";
            default: return Humanize(key);
        }
    }

    public static string ResolveBadge(string key)
    {
        switch (Normalize(key))
        {
            case "wheat": return "BU"; case "rice": return "Pİ"; case "corn": return "MI";
            case "sunflower": return "AY"; case "watermelon": return "KA"; case "tomato": return "DO";
            case "pumpkin": return "KB"; case "apple": return "EL"; case "cookie": return "KU";
            case "bread": return "EK"; case "croissant": return "KR"; case "donut": return "DN";
            case "strawberry": return "Çİ"; case "blackberry": return "BÖ"; case "watermelon_flesh": return "KP";
            case "tomato_fruit": return "DM";
            case "lettuce": return "MR";
            default: return "YE";
        }
    }

    public static Color ResolveColor(string key)
    {
        switch (Normalize(key))
        {
            case "wheat": return new Color(0.91f, 0.67f, 0.24f); case "rice": return new Color(0.90f, 0.86f, 0.70f);
            case "corn": return new Color(0.98f, 0.77f, 0.10f); case "sunflower": return new Color(0.20f, 0.17f, 0.13f);
            case "watermelon": return new Color(0.27f, 0.66f, 0.35f); case "tomato": return new Color(0.88f, 0.24f, 0.17f);
            case "pumpkin": return new Color(0.94f, 0.43f, 0.10f); case "apple": return new Color(0.72f, 0.13f, 0.16f);
            case "cookie": return new Color(0.67f, 0.42f, 0.22f); case "bread": return new Color(0.82f, 0.57f, 0.29f);
            case "croissant": return new Color(0.94f, 0.62f, 0.18f); case "donut": return new Color(0.84f, 0.36f, 0.53f);
            case "strawberry": return new Color(0.86f, 0.18f, 0.24f);
            case "blackberry": return new Color(0.29f, 0.16f, 0.38f);
            case "watermelon_flesh": return new Color(0.94f, 0.33f, 0.31f);
            case "tomato_fruit": return new Color(0.90f, 0.22f, 0.15f);
            case "lettuce": return new Color(0.52f, 0.78f, 0.22f);
            default: return new Color(0.45f, 0.58f, 0.32f);
        }
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Replace("(Clone)", string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Diğer";
        string normalized = value.Replace('_', ' ').Replace('-', ' ').Trim();
        StringBuilder result = new StringBuilder(normalized.Length);
        bool upper = true;
        CultureInfo culture = CultureInfo.GetCultureInfo("tr-TR");
        foreach (char c in normalized)
        {
            result.Append(upper ? char.ToUpper(c, culture) : c);
            upper = c == ' ';
        }
        return result.ToString();
    }
}
