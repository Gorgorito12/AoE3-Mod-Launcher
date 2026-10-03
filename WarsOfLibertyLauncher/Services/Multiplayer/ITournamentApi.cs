using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Every tournament call the Multiplayer tab makes, as one swappable server.
///
/// <para><b>Why the tab talks to this and not to <see cref="LobbyApiClient"/> directly.</b> The
/// tournament preview has to answer the same eighteen calls with the same shapes and the same
/// refusals, and putting the swap at the SERVER is what lets every confirmation, error notice and
/// refresh in the preview be the production code path rather than an imitation of it. In
/// production it is <see cref="LiveTournamentApi"/>, which only forwards; under
/// <c>--demo-tournaments</c> it is <see cref="PreviewTournamentApi"/>.</para>
///
/// <para>The signatures are <see cref="LobbyApiClient"/>'s, defaults included, so a call site
/// reads the same against either. A tournament call that goes around this — straight to the
/// session's client — would reach the real server from inside the preview, which is why
/// <c>TournamentPreviewTests</c> scans the tab for exactly that.</para>
/// </summary>
internal interface ITournamentApi
{
    Task<TournamentListResponse> ListTournamentsAsync(CancellationToken ct = default);

    Task<TournamentDetail> GetTournamentAsync(string id, CancellationToken ct = default);

    Task<TournamentSummary> CreateTournamentAsync(object req, CancellationToken ct = default);

    Task<TournamentEntryResponse> EnterTournamentAsync(
        string id, object? req = null, CancellationToken ct = default);

    Task<object> WithdrawFromTournamentAsync(string id, string entrantId, CancellationToken ct = default);

    Task<TournamentLobbyResponse> OpenTournamentMatchLobbyAsync(
        string id, string matchId, TournamentLobbyRequest req, CancellationToken ct = default);

    Task<object> OpenTournamentRegistrationAsync(string id, CancellationToken ct = default);

    Task<object> CloseTournamentRegistrationAsync(string id, CancellationToken ct = default);

    Task<object> SeedTournamentAsync(string id, object? req = null, CancellationToken ct = default);

    Task<object> StartTournamentAsync(string id, CancellationToken ct = default);

    Task<object> CancelTournamentAsync(string id, CancellationToken ct = default);

    Task<object> AcceptEntrantAsync(string id, string entrantId, CancellationToken ct = default);

    Task<object> RejectEntrantAsync(string id, string entrantId, CancellationToken ct = default);

    Task<object> DisqualifyEntrantAsync(string id, string entrantId, CancellationToken ct = default);

    Task<object> ReplayMatchAsync(string id, string matchId, CancellationToken ct = default);

    Task<object> AwardWalkoverAsync(
        string id, string matchId, string winnerEntrantId, CancellationToken ct = default);

    Task<object> AddTournamentManagerAsync(string id, string userId, CancellationToken ct = default);

    Task<object> RemoveTournamentManagerAsync(string id, string userId, CancellationToken ct = default);
}

/// <summary>The real server: forwards every call to the session's client and adds nothing.</summary>
internal sealed class LiveTournamentApi : ITournamentApi
{
    internal LiveTournamentApi(LobbyApiClient client) => Client = client;

    /// <summary>The client this forwards to, so the tab can tell when the session swapped it.</summary>
    internal LobbyApiClient Client { get; }

    public Task<TournamentListResponse> ListTournamentsAsync(CancellationToken ct = default)
        => Client.ListTournamentsAsync(ct);

    public Task<TournamentDetail> GetTournamentAsync(string id, CancellationToken ct = default)
        => Client.GetTournamentAsync(id, ct);

    public Task<TournamentSummary> CreateTournamentAsync(object req, CancellationToken ct = default)
        => Client.CreateTournamentAsync(req, ct);

    public Task<TournamentEntryResponse> EnterTournamentAsync(
        string id, object? req = null, CancellationToken ct = default)
        => Client.EnterTournamentAsync(id, req, ct);

    public Task<object> WithdrawFromTournamentAsync(string id, string entrantId, CancellationToken ct = default)
        => Client.WithdrawFromTournamentAsync(id, entrantId, ct);

    public Task<TournamentLobbyResponse> OpenTournamentMatchLobbyAsync(
        string id, string matchId, TournamentLobbyRequest req, CancellationToken ct = default)
        => Client.OpenTournamentMatchLobbyAsync(id, matchId, req, ct);

    public Task<object> OpenTournamentRegistrationAsync(string id, CancellationToken ct = default)
        => Client.OpenTournamentRegistrationAsync(id, ct);

    public Task<object> CloseTournamentRegistrationAsync(string id, CancellationToken ct = default)
        => Client.CloseTournamentRegistrationAsync(id, ct);

    public Task<object> SeedTournamentAsync(string id, object? req = null, CancellationToken ct = default)
        => Client.SeedTournamentAsync(id, req, ct);

    public Task<object> StartTournamentAsync(string id, CancellationToken ct = default)
        => Client.StartTournamentAsync(id, ct);

    public Task<object> CancelTournamentAsync(string id, CancellationToken ct = default)
        => Client.CancelTournamentAsync(id, ct);

    public Task<object> AcceptEntrantAsync(string id, string entrantId, CancellationToken ct = default)
        => Client.AcceptEntrantAsync(id, entrantId, ct);

    public Task<object> RejectEntrantAsync(string id, string entrantId, CancellationToken ct = default)
        => Client.RejectEntrantAsync(id, entrantId, ct);

    public Task<object> DisqualifyEntrantAsync(string id, string entrantId, CancellationToken ct = default)
        => Client.DisqualifyEntrantAsync(id, entrantId, ct);

    public Task<object> ReplayMatchAsync(string id, string matchId, CancellationToken ct = default)
        => Client.ReplayMatchAsync(id, matchId, ct);

    public Task<object> AwardWalkoverAsync(
        string id, string matchId, string winnerEntrantId, CancellationToken ct = default)
        => Client.AwardWalkoverAsync(id, matchId, winnerEntrantId, ct);

    public Task<object> AddTournamentManagerAsync(string id, string userId, CancellationToken ct = default)
        => Client.AddTournamentManagerAsync(id, userId, ct);

    public Task<object> RemoveTournamentManagerAsync(string id, string userId, CancellationToken ct = default)
        => Client.RemoveTournamentManagerAsync(id, userId, ct);
}

/// <summary>
/// The preview's server: <see cref="TournamentSimulator"/> behind the same eighteen calls.
///
/// <para>Answers with completed tasks, so a click in the preview is settled before the tab's
/// <c>await</c> resumes — nothing about it depends on a dispatcher running, which is what lets the
/// tests drive it. A refusal comes back as the same <see cref="LobbyApiException"/>, code and
/// sentence the server would send.</para>
///
/// <para><b>Its own bugs are made LOUD.</b> The tab's action wrapper swallows anything that is not
/// a <see cref="LobbyApiException"/> with a bare <c>catch</c>, which is right for a network fault
/// and wrong here: a simulator that broke would look like a button that does nothing. So any other
/// exception is logged and handed back as a <c>preview_error</c> refusal, which the tab shows.</para>
///
/// <para>What the simulated server would PUSH to the person looking goes out through
/// <paramref name="push"/> — in the tab, the real frame handler, so the toast is the real one.</para>
/// </summary>
internal sealed class PreviewTournamentApi : ITournamentApi
{
    private readonly Func<string?> _selectedId;
    private readonly Action<TournamentUpdateNotice>? _push;

    /// <param name="simulator">The server.</param>
    /// <param name="selectedId">The tournament on screen: the list's drafts are whoever is looking
    /// at it's.</param>
    /// <param name="push">Where a notice for the person looking goes.</param>
    internal PreviewTournamentApi(
        TournamentSimulator simulator, Func<string?> selectedId, Action<TournamentUpdateNotice>? push)
    {
        Simulator = simulator;
        _selectedId = selectedId;
        _push = push;
    }

    internal TournamentSimulator Simulator { get; }

    /// <summary>Run one operation and push what it says to the person looking. The console's
    /// buttons and the routes below both come through here.</summary>
    internal PreviewOutcome Simulate(Func<TournamentSimulator, PreviewOutcome> operation)
    {
        var outcome = operation(Simulator);
        foreach (var notice in outcome.ToViewer) _push?.Invoke(notice);
        return outcome;
    }

    public Task<TournamentListResponse> ListTournamentsAsync(CancellationToken ct = default)
        => Answer(() => Simulator.List(_selectedId()));

    public Task<TournamentDetail> GetTournamentAsync(string id, CancellationToken ct = default)
        => Answer(() => Simulator.Detail(id)
                        ?? throw new LobbyApiException(404, "not_found", "Tournament not found.", null));

    public Task<TournamentSummary> CreateTournamentAsync(object req, CancellationToken ct = default)
        => Answer(() => Simulator.Create(JsonSerializer.SerializeToElement(req)));

    public Task<TournamentEntryResponse> EnterTournamentAsync(
        string id, object? req = null, CancellationToken ct = default)
        => Answer(() => Simulator.Enter(id));

    public Task<object> WithdrawFromTournamentAsync(string id, string entrantId, CancellationToken ct = default)
        => Route(s => s.Withdraw(id, entrantId));

    public Task<TournamentLobbyResponse> OpenTournamentMatchLobbyAsync(
        string id, string matchId, TournamentLobbyRequest req, CancellationToken ct = default)
        => Answer(() =>
        {
            var (response, outcome) = Simulator.OpenRoom(id, matchId);
            foreach (var notice in outcome.ToViewer) _push?.Invoke(notice);
            return response;
        });

    public Task<object> OpenTournamentRegistrationAsync(string id, CancellationToken ct = default)
        => Route(s => s.OpenRegistration(id));

    public Task<object> CloseTournamentRegistrationAsync(string id, CancellationToken ct = default)
        => Route(s => s.CloseRegistration(id));

    public Task<object> SeedTournamentAsync(string id, object? req = null, CancellationToken ct = default)
        => Route(s => s.Seed(id));

    public Task<object> StartTournamentAsync(string id, CancellationToken ct = default)
        => Route(s => s.Start(id));

    public Task<object> CancelTournamentAsync(string id, CancellationToken ct = default)
        => Route(s => s.Cancel(id));

    public Task<object> AcceptEntrantAsync(string id, string entrantId, CancellationToken ct = default)
        => Route(s => s.Accept(id, entrantId));

    public Task<object> RejectEntrantAsync(string id, string entrantId, CancellationToken ct = default)
        => Route(s => s.Reject(id, entrantId));

    public Task<object> DisqualifyEntrantAsync(string id, string entrantId, CancellationToken ct = default)
        => Route(s => s.Disqualify(id, entrantId));

    public Task<object> ReplayMatchAsync(string id, string matchId, CancellationToken ct = default)
        => Route(s => s.Replay(id, matchId));

    public Task<object> AwardWalkoverAsync(
        string id, string matchId, string winnerEntrantId, CancellationToken ct = default)
        => Route(s => s.Walkover(id, matchId, winnerEntrantId));

    public Task<object> AddTournamentManagerAsync(string id, string userId, CancellationToken ct = default)
        => Route(s => s.AddManager(id, userId));

    public Task<object> RemoveTournamentManagerAsync(string id, string userId, CancellationToken ct = default)
        => Route(s => s.RemoveManager(id, userId));

    private Task<object> Route(Func<TournamentSimulator, PreviewOutcome> operation)
        => Answer<object>(() =>
        {
            Simulate(operation);
            return new Dictionary<string, object?> { ["ok"] = true };
        });

    private static Task<T> Answer<T>(Func<T> operation)
    {
        try
        {
            return Task.FromResult(operation());
        }
        catch (LobbyApiException ex)
        {
            return Task.FromException<T>(ex);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"Tournament preview: the simulated server failed ({ex.GetType().Name}: {ex.Message}).");
            return Task.FromException<T>(new LobbyApiException(500, "preview_error", ex.Message, null));
        }
    }
}
