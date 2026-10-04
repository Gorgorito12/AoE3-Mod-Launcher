using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Renders design handoffs 57, 59 and 60 off-screen for comparison with the mockups: the Ranking
/// page at 59's three widths (1366, 1920, 2560) and its Highlights view at 1366 and 1920, and the
/// Rooms page on a laptop (1440 × 810) and at Full HD — the community block open (60a) and folded
/// (60b), the chat folded, an empty list, and the activity cards laid over the list. Does nothing
/// unless <c>AOE3ML_ROOMS_SNAPSHOTS</c> names a folder, like the other snapshot harnesses.
/// </summary>
[Collection("wpf-and-language")]
public class RoomsAndRankingSnapshotTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Snapshots()
    {
        var folder = Environment.GetEnvironmentVariable("AOE3ML_ROOMS_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);

        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var window = EloPreviewTests.OffscreenWindow(1440, 810);
            window.Show();
            var previous = Strings.Language;
            try
            {
                foreach (var language in new[] { "es", "en" })
                {
                    Strings.SetLanguage(language);

                    // 59: the ranking at the handoff's three frames.
                    foreach (var width in new[] { 1366.0, 1920.0, 2560.0 })
                    {
                        window.Width = width;
                        window.Height = width >= 2560 ? 1100 : 820;
                        var tab = new MultiplayerTab();
                        window.Content = tab;
                        tab.ShowDemoElo("ranking");
                        // The preview's payload carries no matches; 59 draws seven beside the table.
                        var stats = (CommunityStats)typeof(MultiplayerTab).GetField("_communityStats", Private)!.GetValue(tab)!;
                        stats.RecentMatches = SampleMatches();
                        typeof(MultiplayerTab).GetMethod("RenderRanking", Private)!.Invoke(tab, null);
                        Settle(window, 3);
                        EloPreviewTests.Save(window, Path.Combine(folder, $"{language}-59-ranking-{width:0}.png"));
                    }

                    // Ranking › Highlights: the month's top five of every highlight.
                    foreach (var width in new[] { 1366.0, 1920.0 })
                    {
                        window.Width = width;
                        window.Height = 900;
                        var tab = new MultiplayerTab();
                        window.Content = tab;
                        tab.ShowDemoElo("rankinghighlights");
                        Settle(window, 4);
                        EloPreviewTests.Save(window, Path.Combine(folder, $"{language}-ranking-highlights-{width:0}.png"));
                    }

                    // 57: the rooms.
                    // 61a is the 1300 × 800 laptop with three rooms; 61b the 2560 × 1392 screen with none.
                    foreach (var (w, h, compact) in new[] { (1300.0, 800.0, true), (1366.0, 768.0, true), (1440.0, 810.0, true), (1920.0, 1080.0, false), (2560.0, 1392.0, false) })
                    {
                        foreach (var scene in new[] { "rooms", "block-folded", "chat-folded", "empty", "overlay" })
                        {
                            if (w is 1300 or 1366 or 2560 && scene is "chat-folded" or "overlay") continue;
                            // The tab's own height in a window that size: the launcher's title
                            // bar and nav row take 76 compact, 90 wide.
                            window.Width = w;
                            window.Height = h - (compact ? 76 : 90)
                                // The overlay only happens when the cards do not fit: the height a
                                // Radmin banner and a shorter laptop screen leave.
                                - (scene == "overlay" ? 110 : 0);
                            var config = new LauncherConfig
                            {
                                RoomsChatFolded = scene == "chat-folded",
                                RoomsActivityChoice = scene switch
                                {
                                    "overlay" => true,
                                    "block-folded" => false,
                                    _ => null,
                                },
                            };
                            var rooms = scene == "empty" ? new List<LobbySummary>()
                                : w == 1300 ? SampleRooms().Take(3).ToList()
                                : SampleRooms();
                            var tab = RoomsTab(config, compact, rooms);
                            window.Content = tab;
                            Settle(window, 4);
                            tab.ApplyActivityLayout();
                            Settle(window, 2);
                            EloPreviewTests.Save(window, Path.Combine(folder, $"{language}-61-{scene}-{w:0}.png"));
                        }
                    }
                }
            }
            finally
            {
                Strings.SetLanguage(previous);
                window.Content = null;
                EloPreviewTests.Settle(window);
                window.Close();
            }
        });
        Assert.Null(error);
    }

    private static void Settle(Window window, int passes)
    {
        for (var i = 0; i < passes; i++) EloPreviewTests.Settle(window);
    }

    /// <summary>The Rooms page with the preview's community data and these rooms.</summary>
    private static MultiplayerTab RoomsTab(LauncherConfig config, bool compact, List<LobbySummary> rooms)
    {
        var tab = new MultiplayerTab();
        typeof(MultiplayerTab).GetField("_config", Private)!.SetValue(tab, config);
        tab.ShowDemoElo("highlights");
        // 60a draws the three cards; the preview's payload carries no matches for the middle one.
        var stats = (CommunityStats)typeof(MultiplayerTab).GetField("_communityStats", Private)!.GetValue(tab)!;
        stats.RecentMatches = SampleMatches();
        typeof(MultiplayerTab).GetMethod("RenderActivityStrip", Private)!.Invoke(tab, null);
        tab.SetCompactLayout(compact);
        typeof(MultiplayerTab).GetMethod("ApplyChatFold", Private)!.Invoke(tab, null);
        typeof(MultiplayerTab).GetField("_lastBrowserList", Private)!.SetValue(tab, rooms);
        typeof(MultiplayerTab).GetMethod("RenderRoomRows", Private)!.Invoke(tab, new object?[] { rooms });
        return tab;
    }

    private static List<CommunityMatch> SampleMatches()
    {
        CommunityMatch M(int i, string a, string b, string map, int minutes) => new()
        {
            Id = "m" + i,
            ModId = "wol",
            MapName = map,
            DurationSeconds = minutes * 60,
            Competitive = true,
            ReportedAt = DateTime.UtcNow.AddHours(-(20 + i * 3)).ToString("o"),
            Participants = new List<MatchHistoryParticipant>
            {
                new() { UserId = a, DisplayName = a, Result = 1 },
                new() { UserId = b, DisplayName = b, Result = 0 },
            },
        };
        return new List<CommunityMatch>
        {
            M(0, "Kaiser", "UnstoppableStreletsy", "ESOC_Florida", 24),
            M(1, "Aluclown", "Geaf_Argento", "ESOC_Indonesia", 19),
            M(2, "UnstoppableStreletsy", "Kaiser", "ESOC_Hudson Bay", 31),
            M(3, "Geaf_Argento", "Aluclown", "ESOC_Baja California", 33),
            M(4, "Kaiser", "UnstoppableStreletsy", "ESOC_Parallel Rivers", 12),
            M(5, "UnstoppableStreletsy", "Kaiser", "ESOC_Hudson Bay", 49),
            M(6, "UnstoppableStreletsy", "El Taita", "northwest territory", 5),
        };
    }

    private static List<LobbySummary> SampleRooms()
    {
        var now = DateTime.UtcNow;
        LobbySummary Room(string id, string title, string mod, bool competitive, int players, int max,
            string host, double rating, int minutes, string status = "open") => new()
        {
            Id = id,
            Title = title,
            ModId = mod,
            Competitive = competitive,
            CurrentPlayers = players,
            MaxPlayers = max,
            Status = status,
            CreatedAt = now.AddMinutes(-minutes).ToString("yyyy-MM-dd HH:mm:ss"),
            Host = new LobbyHost { Id = "h-" + id, DiscordUsername = host, DisplayName = host, Rating = rating, Rd = 90 },
        };

        return new List<LobbySummary>
        {
            Room("SJMD9J6W", "Wars of Liberty · Ranked 1v1", "wol", true, 1, 2, "Gorgorito12", 1383, 1),
            Room("K4T2Q8PZ", "Napoleonic Era · chill 2v2", "napoleonic-era", false, 3, 4, "Kaiser", 1630, 4),
            Room("A7XN2M4R", "Wars of Liberty · Treaty 40", "wol", true, 1, 2, "Aluclown", 1590, 9),
            Room("B3VQ8D1K", "Wars of Liberty · 3v3 night", "wol", true, 4, 6, "Comandante_Supremo_de_la_Gran_Armada", 1702, 2),
            Room("C9HW5T2E", "Improvement Mod · FFA", "improvement-mod", false, 5, 8, "Geaf_Argento", 1569, 21),
            Room("D1FR7Y3S", "Wars of Liberty · Ranked 1v1", "wol", true, 2, 2, "UnstoppableStreletsy", 1538, 31, "in_game"),
            Room("E6PL4N8Q", "Wars of Liberty · casual 1v1", "wol", false, 1, 2, "El Taita", 1258, 6),
        };
    }
}
