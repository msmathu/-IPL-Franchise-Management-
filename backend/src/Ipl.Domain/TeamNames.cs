namespace Ipl.Domain;

/// <summary>Historical and current IPL team names → current franchise code (data, not season rules).</summary>
public static class TeamNames
{
    public static readonly IReadOnlyDictionary<string, string> FranchiseCodeByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Chennai Super Kings"] = "CSK", ["Mumbai Indians"] = "MI", ["Royal Challengers Bangalore"] = "RCB",
        ["Royal Challengers Bengaluru"] = "RCB", ["Kolkata Knight Riders"] = "KKR", ["Delhi Daredevils"] = "DC",
        ["Delhi Capitals"] = "DC", ["Rajasthan Royals"] = "RR", ["Kings XI Punjab"] = "PBKS", ["Punjab Kings"] = "PBKS",
        ["Sunrisers Hyderabad"] = "SRH", ["Lucknow Super Giants"] = "LSG", ["Gujarat Titans"] = "GT",
    };

    /// <summary>Short code for any team name: the franchise code, or initials for defunct/national teams.</summary>
    public static string ShortCode(string name) =>
        FranchiseCodeByName.TryGetValue(name, out var code) ? code
        : string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]))) is { Length: >= 2 } s ? s
        : name.Length >= 3 ? name[..3].ToUpperInvariant() : name.ToUpperInvariant();
}
