using System.Net;

namespace WarsOfLibertyLauncher.Services.Multiplayer.P2P;

/// <summary>
/// One AoE3 datagram in transit between the in-game DLL hook (running
/// inside age3y.exe) and <see cref="PeerMesh"/>. The hook captures
/// what the game just wrote to its DirectPlay socket, ships it to the
/// launcher over the local IPC bridge, the launcher hands it to the
/// mesh for fan-out, and the receiving launcher reverses the journey
/// — IPC back into its own age3y.exe so the game sees an inbound LAN
/// frame.
///
/// We carry both <see cref="SrcIp"/> and <see cref="DstIp"/> instead
/// of just ports so the receiving side can replay the exact addressing
/// the sender used: broadcasts stay broadcasts, unicast peer chatter
/// stays unicast. The payload is the raw UDP body the game produced —
/// the bridge does not interpret DirectPlay framing, it just moves
/// bytes across the mesh.
/// </summary>
public sealed record GamePacket(
    ushort SrcPort,
    ushort DstPort,
    IPAddress SrcIp,
    IPAddress DstIp,
    byte[] Payload);
