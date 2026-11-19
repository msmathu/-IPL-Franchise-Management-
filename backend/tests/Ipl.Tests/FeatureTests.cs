using System.Net;
using System.Text.Json;
using Ipl.Infrastructure.Services;

namespace Ipl.Tests;

/// <summary>Cricsheet parsing: name matching and scorecards, on small hand-written matches.</summary>
public class CricsheetTests
{
    [Theory]
    [InlineData("V Kohli", "Virat Kohli", true)]
    [InlineData("SA Yadav", "Suryakumar Yadav", true)]
    [InlineData("Shubman Gill", "Shubman Gill", true)]
    [InlineData("B Sai Sudharsan", "Sai Sudharsan", true)]
    [InlineData("P Simran Singh", "Prabhsimran Singh", true)]
    [InlineData("Nithish Kumar Reddy", "Nitish Kumar Reddy", true)]
    [InlineData("RG Sharma", "Abhishek Sharma", false)]
    [InlineData("Ishan Kishan", "Aman Khan", false)]
    public void Scorecard_names_match_full_names(string cricsheet, string full, bool expected) =>
        Assert.Equal(expected, CricsheetNameMatcher.NameFits(cricsheet, full));

    [Fact]
    public void Ambiguous_names_are_left_unlinked_but_team_history_decides()
    {
        var a = new CricsheetPerson("aaaaaaaa"); a.Names.Add("RK Singh"); a.FranchiseYears["KKR"] = 2026; a.LastYear = 2026;
        var b = new CricsheetPerson("bbbbbbbb"); b.Names.Add("RK Singh"); b.FranchiseYears["MI"] = 2026; b.LastYear = 2026;
        var players = new[] { new SquadPlayerRef(Guid.NewGuid(), "Rinku Singh", "KKR"), new SquadPlayerRef(Guid.NewGuid(), "Rahul Kumar Singh", null) };
        var result = CricsheetNameMatcher.Match(players, [a, b], 2026);
        Assert.Equal("aaaaaaaa", result[players[0].Id]);          // the KKR player is the KKR person
        Assert.False(result.ContainsKey(players[1].Id));          // no team and two candidates: not guessed
    }

    private const string Match = """
    {"info":{"dates":["2026-04-01"],"gender":"male","teams":["Alpha","Beta"],"venue":"Test Ground","city":"Chennai",
      "toss":{"winner":"Alpha","decision":"bat"},"outcome":{"winner":"Alpha","by":{"runs":3}},"player_of_match":["A One"],
      "players":{"Alpha":["A One","A Two","A Three"],"Beta":["B One","B Two"]},
      "registry":{"people":{"A One":"a1","A Two":"a2","A Three":"a3","B One":"b1","B Two":"b2"}}},
     "innings":[
      {"team":"Alpha","overs":[{"over":0,"deliveries":[
        {"batter":"A One","bowler":"B One","non_striker":"A Two","runs":{"batter":4,"extras":0,"total":4}},
        {"batter":"A One","bowler":"B One","non_striker":"A Two","runs":{"batter":0,"extras":1,"total":1},"extras":{"wides":1}},
        {"batter":"A One","bowler":"B One","non_striker":"A Two","runs":{"batter":6,"extras":0,"total":6}},
        {"batter":"A One","bowler":"B One","non_striker":"A Two","runs":{"batter":0,"extras":0,"total":0},
          "wickets":[{"kind":"caught","player_out":"A One","fielders":[{"name":"B Two"}]}]}]}]},
      {"team":"Beta","overs":[{"over":0,"deliveries":[
        {"batter":"B One","bowler":"A Three","non_striker":"B Two","runs":{"batter":1,"extras":0,"total":1}},
        {"batter":"B Two","bowler":"A Three","non_striker":"B One","runs":{"batter":0,"extras":0,"total":0},
          "wickets":[{"kind":"bowled","player_out":"B Two"}]}]}]}]}
    """;

    [Fact]
    public void Scorecard_has_batting_bowling_extras_and_fall_of_wickets()
    {
        using var doc = JsonDocument.Parse(Match);
        var sc = CricsheetScorecard.Build(doc.RootElement);
        using var card = JsonDocument.Parse(sc.Json);
        var first = card.RootElement.GetProperty("innings")[0];
        Assert.Equal(11, first.GetProperty("runs").GetInt32());
        Assert.Equal("0.3", first.GetProperty("overs").GetString());                     // the wide is not a legal ball
        var one = first.GetProperty("batting")[0];
        Assert.Equal(10, one.GetProperty("runs").GetInt32());
        Assert.Equal(3, one.GetProperty("balls").GetInt32());
        Assert.Equal("c B Two b B One", one.GetProperty("how").GetString());
        Assert.Equal(1, first.GetProperty("extras").GetProperty("wides").GetInt32());
        var bowler = first.GetProperty("bowling")[0];
        Assert.Equal(11, bowler.GetProperty("runs").GetInt32());                        // wides count against the bowler
        Assert.Equal(1, bowler.GetProperty("wickets").GetInt32());
        Assert.Equal("1-11 (A One, 0.3 ov)", first.GetProperty("fallOfWickets")[0].GetString());
        Assert.Contains("A Three", first.GetProperty("didNotBat").EnumerateArray().Select(x => x.GetString()));

        Assert.Equal(1, sc.People.Single(p => p.Id == "b2").Catches);
        var duel = sc.Matchups[("a1", "b1")];
        Assert.Equal((3, 10, 1, 1, 1), (duel.Balls, duel.Runs, duel.Dismissals, duel.Fours, duel.Sixes));
        Assert.Equal(1, sc.Matchups[("b2", "a3")].Dismissals);
    }
}

/// <summary>New workflows through the API: replacement approvals, scorecards and the auction assistant.</summary>
[Collection("api")]
public class FeatureWorkflowTests(ApiFactory api)
{
    [Fact]
    public async Task Injury_replacement_needs_an_injured_player_and_league_approval()
    {
        var medical = await api.LoginAsync("medical.staff.mi@demo.local");
        var director = await api.LoginAsync("cricket.director.mi@demo.local");
        var league = await api.LoginAsync("league@demo.local");
        var owner = await api.LoginAsync("owner.mi@demo.local");

        var squad = await director.OkAsync(director.GetAsync("squad"));
        var injured = squad.GetProperty("players").EnumerateArray().First(p => !p.GetProperty("isOverseas").GetBoolean()).GetProperty("playerId").GetString()!;
        var pool = await director.OkAsync(director.GetAsync("players?status=AvailableForAuction&pageSize=20"));
        var replacement = pool.GetProperty("items").EnumerateArray().First(p => !p.GetProperty("isOverseas").GetBoolean()).GetProperty("id").GetString()!;

        // Not injured yet: refused.
        var early = await director.PostAsync("replacements", new { injuredPlayerId = injured, replacementPlayerId = replacement, fee = 2_000_000, reason = "Test" });
        Assert.Equal(HttpStatusCode.BadRequest, early.Status);

        await medical.OkAsync(medical.PutAsync($"players/{injured}/availability", new { status = "Injured", publicNote = "Hamstring", expectedReturn = (string?)null }));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync("replacements", new { injuredPlayerId = injured, replacementPlayerId = replacement, fee = 0, reason = "x" })).Status);
        var created = await director.OkAsync(director.PostAsync("replacements", new { injuredPlayerId = injured, replacementPlayerId = replacement, fee = 2_000_000, reason = "Hamstring tear" }));
        var id = created.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.Forbidden, (await director.PostAsync($"replacements/{id}/approve", new { note = (string?)null })).Status);
        await league.OkAsync(league.PostAsync($"replacements/{id}/approve", new { note = "OK" }));

        var after = await director.OkAsync(director.GetAsync("squad"));
        var players = after.GetProperty("players").EnumerateArray().ToList();
        Assert.Contains(players, p => p.GetProperty("playerId").GetString() == replacement && p.GetProperty("acquisition").GetString() == "Replacement");
        Assert.DoesNotContain(players, p => p.GetProperty("playerId").GetString() == injured);
        Assert.Equal(HttpStatusCode.Conflict, (await league.PostAsync($"replacements/{id}/approve", new { note = (string?)null })).Status); // only once
    }

    [Fact]
    public async Task Season_centre_and_records_endpoints_answer_without_match_data()
    {
        var analyst = await api.LoginAsync("analyst.mi@demo.local");
        await analyst.OkAsync(analyst.GetAsync("season-centre/years"));
        await analyst.OkAsync(analyst.GetAsync("season-centre/records"));
        await analyst.OkAsync(analyst.GetAsync("season-centre/2026/leaders"));
        var h2h = await analyst.OkAsync(analyst.GetAsync("season-centre/head-to-head?team1=CSK&team2=MI"));
        Assert.Equal(0, h2h.GetProperty("played").GetInt32());   // the test database has no imported matches
        Assert.Equal(HttpStatusCode.BadRequest, (await analyst.GetAsync("season-centre/head-to-head?team1=CSK&team2=CSK")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await analyst.GetAsync($"season-centre/matches/{Guid.NewGuid()}/scorecard")).Status);
    }
}
