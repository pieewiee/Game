using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Runtime.Media;
using Game.Runtime.World;
using Game.Sim;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.Net
{
    /// <summary>
    /// Milestone 4, per docs/multiplayer.md: HOST-AUTHORITATIVE. The simulation
    /// runs on exactly one machine; clients send intents and render snapshots.
    /// Implemented entirely over NGO's CustomMessagingManager — no
    /// NetworkObjects, no prefabs, no scene objects — because the whole game is
    /// runtime-generated and NGO's prefab-hash pipeline assumes editor assets.
    ///
    /// Shared account, no permissions: every client may send every command.
    /// The HOST stamps the actor (never the client — a modified client cannot
    /// blame someone else), and the ledger line is broadcast to everyone.
    ///
    /// Time control (decision, documented in the PR): any player may change
    /// speed or pause; the act itself is ledgered with their name. The docs'
    /// idle-gated clock is deferred — with snapshots at 5 Hz there is no
    /// per-player "touching" concept yet for it to key on.
    /// </summary>
    public sealed class NetSession : MonoBehaviour
    {
        private const byte MsgHello = 1, MsgCmd = 2, MsgAvatar = 3, MsgTime = 4, MsgNamed = 5;
        private const byte MsgSnapshot = 20, MsgLedger = 21, MsgRoster = 22,
                           MsgFacility = 23, MsgKill = 24, MsgNews = 25, MsgStanding = 26;
        private const string Channel = "GNP";
        private const double StandingLossNamed = 14.0;   // coop-griefing.md §4
        private const double StandingStart = 50.0;

        public bool Active { get; private set; }
        public bool IsHost { get; private set; }
        public bool IsClient { get; private set; }
        public string LocalPlayerName = "Operator";
        public TickReport RemoteReport;
        public long RemoteTick;
        public int RemoteNodes;

        private NetworkManager _nm;
        private UnityTransport _transport;
        private string _address = "127.0.0.1";
        private readonly Dictionary<ulong, string> _roster = new Dictionary<ulong, string>();
        private readonly Dictionary<string, double> _standings = new Dictionary<string, double>();
        private readonly Dictionary<ulong, GameObject> _avatars = new Dictionary<ulong, GameObject>();
        private readonly Dictionary<ulong, Vector3> _avatarPos = new Dictionary<ulong, Vector3>();
        private float _nextBroadcast;
        private float _nextAvatarSend;
        private string _lastFacilityJson = "";
        private bool _panelOpen;

        public List<string> RosterSnapshot()
        {
            var list = new List<string>(_roster.Values);
            if (IsHost && !list.Contains(LocalPlayerName)) list.Insert(0, LocalPlayerName);
            return list;
        }

        // ------------------------------------------------------------------
        // Session lifecycle
        // ------------------------------------------------------------------

        private void EnsureManager()
        {
            if (_nm != null) return;
            var go = new GameObject("[GNP NetworkManager]");
            DontDestroyOnLoad(go);
            _nm = go.AddComponent<NetworkManager>();
            _transport = go.AddComponent<UnityTransport>();
            _nm.NetworkConfig = new NetworkConfig { NetworkTransport = _transport };
        }

        public void StartHost()
        {
            EnsureManager();
            _transport.SetConnectionData("0.0.0.0", 7777);
            if (!_nm.StartHost()) { NewsFeed.Post("Hosting failed — see log."); return; }
            Active = true; IsHost = true; IsClient = false;
            LocalPlayerName = "Host";
            _standings[LocalPlayerName] = StandingStart;
            _nm.CustomMessagingManager.RegisterNamedMessageHandler(Channel, OnMessage);
            _nm.OnClientConnectedCallback += OnClientConnected;
            _nm.OnClientDisconnectCallback += OnClientDisconnected;
            NewsFeed.Post("Session hosted on port 7777. Shared account, no permissions — the ledger records names.");
        }

        public void StartClient(string address)
        {
            EnsureManager();
            _address = address;
            _transport.SetConnectionData(address, 7777);
            if (!_nm.StartClient()) { NewsFeed.Post("Join failed — see log."); return; }
            Active = true; IsHost = false; IsClient = true;
            // The client renders snapshots; its local sim must not tick.
            if (GameBootstrap.Driver != null) GameBootstrap.Driver.Paused = true;
            _nm.CustomMessagingManager.RegisterNamedMessageHandler(Channel, OnMessage);
            _nm.OnClientConnectedCallback += id =>
            {
                if (id == _nm.LocalClientId)
                {
                    LocalPlayerName = "P" + id.ToString(CultureInfo.InvariantCulture);
                    if (GameBootstrap.LocalPlayer != null)
                        GameBootstrap.LocalPlayer.PlayerName = LocalPlayerName;
                    using var w = BeginMsg(MsgHello);
                    w.WriteValueSafe(LocalPlayerName);
                    Send(w, NetworkManager.ServerClientId);
                }
            };
            _nm.OnClientDisconnectCallback += id =>
            {
                if (id == _nm.LocalClientId)
                {
                    Active = false; IsClient = false;
                    NewsFeed.Post("Disconnected from the host.");
                }
            };
        }

        public void Disconnect()
        {
            if (_nm != null) _nm.Shutdown();
            Active = false; IsHost = false; IsClient = false;
            foreach (var kv in _avatars) if (kv.Value != null) Destroy(kv.Value);
            _avatars.Clear();
            _roster.Clear();
        }

        private void OnClientConnected(ulong id)
        {
            if (!IsHost || id == _nm.LocalClientId) return;
            // Late joiner: full state — roster, facility, standings arrive with
            // the next broadcast; the ledger tail says where they walked in.
            SendFacility(id, force: true);
            GameBootstrap.AddLedger("system", "player " + id + " connected");
        }

        private void OnClientDisconnected(ulong id)
        {
            if (!IsHost) return;
            if (_roster.TryGetValue(id, out string name))
                GameBootstrap.AddLedger("system", name + " disconnected");
            _roster.Remove(id);
            if (_avatars.TryGetValue(id, out GameObject go) && go != null) Destroy(go);
            _avatars.Remove(id);
            _avatarPos.Remove(id);
            BroadcastRoster();
        }

        // ------------------------------------------------------------------
        // Message plumbing
        // ------------------------------------------------------------------

        private static FastBufferWriter BeginMsg(byte type)
        {
            var w = new FastBufferWriter(1400, Allocator.Temp, 65536);
            w.WriteValueSafe(type);
            return w;
        }

        private void Send(FastBufferWriter w, ulong to)
        {
            _nm.CustomMessagingManager.SendNamedMessage(Channel, to, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        private void Broadcast(FastBufferWriter w)
        {
            _nm.CustomMessagingManager.SendNamedMessageToAll(Channel, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        private void OnMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte type);
            switch (type)
            {
                case MsgHello when IsHost:
                {
                    reader.ReadValueSafe(out string name);
                    _roster[sender] = name;
                    if (!_standings.ContainsKey(name)) _standings[name] = StandingStart;
                    BroadcastRoster();
                    GameBootstrap.AddLedger("system", name + " joined the site");
                    break;
                }
                case MsgCmd when IsHost:
                {
                    reader.ReadValueSafe(out int kind);
                    reader.ReadValueSafe(out double a);
                    reader.ReadValueSafe(out double b);
                    reader.ReadValueSafe(out string desc);
                    var cmd = new SimCommand { Kind = (CommandKind)kind, A = a, B = b };
                    GameBootstrap.Driver.EnqueueRecorded(cmd);
                    // The HOST stamps the actor from the connection, never from
                    // the payload (multiplayer.md §4).
                    string actor = _roster.TryGetValue(sender, out string n)
                        ? n : "P" + sender.ToString(CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(desc)) GameBootstrap.AddLedger(actor, desc);
                    break;
                }
                case MsgAvatar when IsHost:
                {
                    reader.ReadValueSafe(out Vector3 pos);
                    reader.ReadValueSafe(out float yaw);
                    _avatarPos[sender] = pos;
                    UpdateAvatar(sender, pos, yaw);
                    break;
                }
                case MsgTime when IsHost:
                {
                    reader.ReadValueSafe(out bool paused);
                    reader.ReadValueSafe(out float tps);
                    GameBootstrap.Driver.Paused = paused;
                    GameBootstrap.Driver.TicksPerSecond = tps;
                    string actor = _roster.TryGetValue(sender, out string tn) ? tn : "P" + sender;
                    GameBootstrap.AddLedger(actor, paused ? "paused the clock" : "set speed " + tps + " t/s");
                    break;
                }
                case MsgNamed when IsHost:
                {
                    reader.ReadValueSafe(out string named);
                    ApplyNamingPenalty(named);
                    break;
                }
                case MsgSnapshot when IsClient:
                {
                    reader.ReadValueSafe(out long tick);
                    reader.ReadValueSafe(out TickReport report);
                    reader.ReadValueSafe(out int nodes);
                    RemoteTick = tick;
                    RemoteReport = report;
                    RemoteNodes = nodes;
                    break;
                }
                case MsgLedger when IsClient:
                {
                    reader.ReadValueSafe(out string line);
                    GameBootstrap.Ledger.Add(line);
                    break;
                }
                case MsgNews when IsClient:
                {
                    reader.ReadValueSafe(out string item);
                    NewsFeed.Post(item);
                    break;
                }
                case MsgRoster:
                {
                    reader.ReadValueSafe(out string csv);
                    if (IsClient)
                    {
                        _roster.Clear();
                        string[] parts = csv.Split('|');
                        for (int i = 0; i + 1 < parts.Length; i += 2)
                            if (ulong.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong cid))
                                _roster[cid] = parts[i + 1];
                    }
                    break;
                }
                case MsgStanding when IsClient:
                {
                    reader.ReadValueSafe(out string csv);
                    _standings.Clear();
                    string[] parts = csv.Split('|');
                    for (int i = 0; i + 1 < parts.Length; i += 2)
                        if (double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                            _standings[parts[i]] = v;
                    break;
                }
                case MsgFacility when IsClient:
                {
                    reader.ReadValueSafe(out string json);
                    if (GameBootstrap.Facility != null) GameBootstrap.Facility.ApplyJson(json);
                    break;
                }
                case MsgKill when IsClient:
                {
                    reader.ReadValueSafe(out string cause);
                    var rig = GameBootstrap.LocalPlayer;
                    if (rig != null && !rig.IsDead) rig.Die(cause);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Outbound API used by the rest of the game
        // ------------------------------------------------------------------

        public void SendCommandToHost(SimCommand cmd, string desc)
        {
            if (!IsClient) return;
            using var w = BeginMsg(MsgCmd);
            w.WriteValueSafe((int)cmd.Kind);
            w.WriteValueSafe(cmd.A);
            w.WriteValueSafe(cmd.B);
            w.WriteValueSafe(desc ?? "");
            Send(w, NetworkManager.ServerClientId);
        }

        public void BroadcastLedger(string line)
        {
            if (!IsHost || _nm == null) return;
            using var w = BeginMsg(MsgLedger);
            w.WriteValueSafe(line);
            Broadcast(w);
        }

        public void ApplyNamingPenalty(string playerName)
        {
            if (IsClient)
            {
                using var w = BeginMsg(MsgNamed);
                w.WriteValueSafe(playerName);
                Send(w, NetworkManager.ServerClientId);
                return;
            }
            if (!_standings.ContainsKey(playerName)) _standings[playerName] = StandingStart;
            _standings[playerName] = Math.Max(0.0, _standings[playerName] - StandingLossNamed);
            GameBootstrap.AddLedger("the Program", playerName + " was publicly named (standing " +
                _standings[playerName].ToString("0", CultureInfo.InvariantCulture) + ")");
            BroadcastStandings();
        }

        public void HostKillPlayersInRoom(Room room, string cause)
        {
            if (!IsHost || _nm == null) return;
            foreach (var kv in _avatarPos)
            {
                if (!room.Contains(kv.Value)) continue;
                using var w = BeginMsg(MsgKill);
                w.WriteValueSafe(cause + " in " + room.Name);
                Send(w, kv.Key);
            }
        }

        // ------------------------------------------------------------------
        // Per-frame: broadcast + avatar sync + host/join panel
        // ------------------------------------------------------------------

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame) _panelOpen = !_panelOpen;

            if (!Active || _nm == null) return;

            if (IsHost && Time.unscaledTime >= _nextBroadcast)
            {
                _nextBroadcast = Time.unscaledTime + 0.2f;
                var driver = GameBootstrap.Driver;
                if (driver != null && driver.Sim != null)
                {
                    using var w = BeginMsg(MsgSnapshot);
                    w.WriteValueSafe(driver.Sim.State.Tick);
                    TickReport r = driver.Latest;
                    w.WriteValueSafe(r);
                    w.WriteValueSafe(driver.Sim.State.NodesInstalled);
                    Broadcast(w);
                }
                SendFacility(0, force: false);
                // Also feed clients the news the host generated this interval.
            }

            if (IsClient && Time.unscaledTime >= _nextAvatarSend)
            {
                _nextAvatarSend = Time.unscaledTime + 0.1f;
                var rig = GameBootstrap.LocalPlayer;
                if (rig != null)
                {
                    using var w = BeginMsg(MsgAvatar);
                    w.WriteValueSafe(rig.transform.position);
                    w.WriteValueSafe(rig.transform.eulerAngles.y);
                    Send(w, NetworkManager.ServerClientId);
                }
            }
        }

        private void SendFacility(ulong specificClient, bool force)
        {
            if (GameBootstrap.Facility == null) return;
            string json = GameBootstrap.Facility.ToJson();
            if (!force && json == _lastFacilityJson) return;
            _lastFacilityJson = json;
            using var w = BeginMsg(MsgFacility);
            w.WriteValueSafe(json);
            if (force && specificClient != 0) Send(w, specificClient);
            else Broadcast(w);
        }

        private void BroadcastRoster()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _roster)
                sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(kv.Value).Append('|');
            using var w = BeginMsg(MsgRoster);
            w.WriteValueSafe(sb.ToString());
            Broadcast(w);
            BroadcastStandings();
        }

        private void BroadcastStandings()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _standings)
                sb.Append(kv.Key).Append('|')
                  .Append(kv.Value.ToString("0.##", CultureInfo.InvariantCulture)).Append('|');
            using var w = BeginMsg(MsgStanding);
            w.WriteValueSafe(sb.ToString());
            Broadcast(w);
        }

        private void UpdateAvatar(ulong id, Vector3 pos, float yaw)
        {
            if (!_avatars.TryGetValue(id, out GameObject go) || go == null)
            {
                var pm = new ProcMesh();
                pm.Cylinder(new Vector3(0, 0.9f, 0), 0.35f, 1.5f, 8, Palette.Render);
                pm.Cylinder(new Vector3(0, 1.78f, 0), 0.3f, 0.22f, 8, Palette.ProgramBlue); // hard hat
                pm.Box(new Vector3(0, 1.2f, 0), new Vector3(0.75f, 0.18f, 0.4f), Palette.Amber); // hi-vis band
                go = MatLib.Spawn("Avatar " + id, pm.Build("avatar"), null, pos);
                _avatars[id] = go;
            }
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        }

        // ------------------------------------------------------------------
        // The F2 session panel
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!_panelOpen)
            {
                GUI.Label(new Rect(Screen.width - 200, Screen.height - 24, 196, 22),
                    Active ? (IsHost ? "hosting (F2)" : "client (F2)") : "F2: multiplayer");
                return;
            }
            GUILayout.BeginArea(new Rect(Screen.width - 280, Screen.height - 220, 272, 212), GUI.skin.box);
            GUILayout.Label("— session —");
            if (!Active)
            {
                if (GUILayout.Button("Host (port 7777)")) StartHost();
                GUILayout.BeginHorizontal();
                _address = GUILayout.TextField(_address, GUILayout.Width(150));
                if (GUILayout.Button("Join")) StartClient(_address);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(IsHost ? "hosting — sim authority here" : "client of " + _address);
                foreach (var kv in _standings)
                    GUILayout.Label(kv.Key + "  standing " +
                        kv.Value.ToString("0", CultureInfo.InvariantCulture));
                if (GUILayout.Button("Disconnect")) Disconnect();
            }
            GUILayout.EndArea();
        }
    }
}
