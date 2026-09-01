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
        private const byte MsgHello = 1, MsgCmd = 2, MsgAvatar = 3, MsgTime = 4, MsgNamed = 5,
                           MsgHazard = 6;
        private const byte MsgSnapshot = 20, MsgLedger = 21, MsgRoster = 22,
                           MsgFacility = 23, MsgKill = 24, MsgNews = 25, MsgStanding = 26,
                           MsgAvatars = 27, MsgHazardEvent = 28;
        private const string Channel = "GNP";
        private const double StandingLossNamed = 14.0;   // coop-griefing.md §4
        private const double StandingStart = 50.0;

        public bool Active { get; private set; }
        public bool IsHost { get; private set; }
        public bool IsClient { get; private set; }
        public string LocalPlayerName = "Operator";
        public TickReport RemoteReport;
        public long RemoteTick;
        public bool PanelOpen { get { return _panelOpen; } }

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
            // -= before += : a re-hosted session must not stack stale handlers.
            _nm.OnClientConnectedCallback -= OnClientConnected;
            _nm.OnClientConnectedCallback += OnClientConnected;
            _nm.OnClientDisconnectCallback -= OnClientDisconnected;
            _nm.OnClientDisconnectCallback += OnClientDisconnected;
            // Everything the host's game posts reaches every client verbatim.
            NewsFeed.OnPosted -= BroadcastNews;
            NewsFeed.OnPosted += BroadcastNews;
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
            _nm.OnClientConnectedCallback -= OnSelfConnected;
            _nm.OnClientConnectedCallback += OnSelfConnected;
            _nm.OnClientDisconnectCallback -= OnSelfDisconnected;
            _nm.OnClientDisconnectCallback += OnSelfDisconnected;
        }

        private void OnSelfConnected(ulong id)
        {
            if (!IsClient || id != _nm.LocalClientId) return;
            LocalPlayerName = "P" + id.ToString(CultureInfo.InvariantCulture);
            if (GameBootstrap.LocalPlayer != null)
                GameBootstrap.LocalPlayer.PlayerName = LocalPlayerName;
            using var w = BeginMsg(MsgHello);
            w.WriteValueSafe(LocalPlayerName);
            Send(w, NetworkManager.ServerClientId);
        }

        private void OnSelfDisconnected(ulong id)
        {
            if (!IsClient || id != _nm.LocalClientId) return;
            Active = false; IsClient = false;
            ClearRemoteState();
            // Frozen forever would be worse: the stale local sim resumes solo.
            if (GameBootstrap.Driver != null) GameBootstrap.Driver.Paused = false;
            NewsFeed.Post("Disconnected from the host — your local (stale) site resumes.");
        }

        public void Disconnect()
        {
            bool wasClient = IsClient;
            if (_nm != null) _nm.Shutdown();
            Active = false; IsHost = false; IsClient = false;
            NewsFeed.OnPosted -= BroadcastNews;
            ClearRemoteState();
            if (wasClient && GameBootstrap.Driver != null) GameBootstrap.Driver.Paused = false;
        }

        private void ClearRemoteState()
        {
            foreach (var kv in _avatars) if (kv.Value != null) Destroy(kv.Value);
            _avatars.Clear();
            _avatarPos.Clear();
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

        // TickReport lives in the Unity-free sim assembly, so it cannot carry
        // NGO's INetworkSerializeByMemcpy. It IS unmanaged, so it crosses the
        // wire as its raw managed bytes — valid because host and clients run
        // the same build (a length check drops mismatched versions).
        private static byte[] ReportToBytes(TickReport r)
        {
            return System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref r, 1)).ToArray();
        }

        private static readonly int ReportByteLen = ReportToBytes(default).Length;

        private static bool ReportFromBytes(byte[] bytes, out TickReport r)
        {
            r = default;
            if (bytes == null || bytes.Length != ReportByteLen) return false;
            r = System.Runtime.InteropServices.MemoryMarshal.Read<TickReport>(bytes);
            return true;
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
                    // the payload (multiplayer.md §4). The description is free
                    // text from the client's own build — it only ever appears
                    // under the sender's own name, but cap and flatten it so a
                    // modified client cannot fake multi-line ledger entries.
                    string actor = _roster.TryGetValue(sender, out string n)
                        ? n : "P" + sender.ToString(CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(desc))
                    {
                        desc = desc.Replace('\n', ' ').Replace('\r', ' ');
                        if (desc.Length > 120) desc = desc.Substring(0, 120);
                        GameBootstrap.AddLedger(actor, desc);
                    }
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
                    string actor = _roster.TryGetValue(sender, out string tn)
                        ? tn : "P" + sender.ToString(CultureInfo.InvariantCulture);
                    GameBootstrap.AddLedger(actor, paused ? "paused the clock"
                        : "set speed " + tps.ToString("0.#", CultureInfo.InvariantCulture) + " t/s");
                    break;
                }
                case MsgAvatars when IsClient:
                {
                    reader.ReadValueSafe(out int count);
                    for (int i = 0; i < count; i++)
                    {
                        reader.ReadValueSafe(out ulong id);
                        reader.ReadValueSafe(out Vector3 pos);
                        reader.ReadValueSafe(out float yaw);
                        if (id == _nm.LocalClientId) continue; // that one is me
                        _avatarPos[id] = pos;
                        UpdateAvatar(id, pos, yaw);
                    }
                    break;
                }
                case MsgNamed when IsHost:
                {
                    reader.ReadValueSafe(out string named);
                    ApplyNamingPenalty(named);
                    break;
                }
                case MsgHazard when IsHost:
                {
                    // A client wants to operate a pull station. The host's
                    // instance is authoritative; the state change echoes to
                    // everyone as a hazard event.
                    reader.ReadValueSafe(out int index);
                    reader.ReadValueSafe(out byte action);
                    if (index < 0 || index >= World.PullStation.All.Count) break;
                    string actor = _roster.TryGetValue(sender, out string hn)
                        ? hn : "P" + sender.ToString(CultureInfo.InvariantCulture);
                    World.PullStation.All[index].Apply(action, actor);
                    BroadcastHazard(index, action);
                    break;
                }
                case MsgHazardEvent when IsClient:
                {
                    reader.ReadValueSafe(out int index);
                    reader.ReadValueSafe(out byte action);
                    if (index < 0 || index >= World.PullStation.All.Count) break;
                    // Mirror only — the host already ledgered and posted news.
                    World.PullStation.All[index].Apply(action, null);
                    break;
                }
                case MsgSnapshot when IsClient:
                {
                    reader.ReadValueSafe(out long tick);
                    reader.ReadValueSafe(out int len);
                    var bytes = new byte[len];
                    reader.ReadBytesSafe(ref bytes, len);
                    if (ReportFromBytes(bytes, out TickReport report))
                    {
                        RemoteTick = tick;
                        RemoteReport = report;
                    }
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
                HostKillClient(kv.Key, cause + " in " + room.Name);
            }
        }

        public void HostKillClient(ulong clientId, string cause)
        {
            if (!IsHost || _nm == null) return;
            using var w = BeginMsg(MsgKill);
            w.WriteValueSafe(cause);
            Send(w, clientId);
        }

        // ------------------------------------------------------------------
        // Per-frame: broadcast + avatar sync + host/join panel
        // ------------------------------------------------------------------

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame)
            {
                _panelOpen = !_panelOpen;
                Cursor.lockState = _panelOpen ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = _panelOpen;
            }

            if (!Active || _nm == null) return;

            // Backstop: nothing on a client may ever tick the local sim — the
            // debug console's time buttons included. Intent goes via MsgTime.
            if (IsClient && GameBootstrap.Driver != null && !GameBootstrap.Driver.Paused)
                GameBootstrap.Driver.Paused = true;

            if (IsHost && Time.unscaledTime >= _nextBroadcast)
            {
                _nextBroadcast = Time.unscaledTime + 0.2f;
                var driver = GameBootstrap.Driver;
                if (driver != null && driver.Sim != null)
                {
                    using var w = BeginMsg(MsgSnapshot);
                    w.WriteValueSafe(driver.Sim.State.Tick);
                    byte[] bytes = ReportToBytes(driver.Latest);
                    w.WriteValueSafe(bytes.Length);
                    w.WriteBytesSafe(bytes);
                    Broadcast(w);
                }
                SendFacility(0, force: false);
            }

            if (IsHost && Time.unscaledTime >= _nextAvatarSend)
            {
                // Everyone sees everyone: the host mirrors all known positions
                // (its own included) back out at 10 Hz.
                _nextAvatarSend = Time.unscaledTime + 0.1f;
                var rig = GameBootstrap.LocalPlayer;
                using var w = BeginMsg(MsgAvatars);
                w.WriteValueSafe(_avatarPos.Count + (rig != null ? 1 : 0));
                if (rig != null)
                {
                    w.WriteValueSafe(_nm.LocalClientId);
                    w.WriteValueSafe(rig.transform.position);
                    w.WriteValueSafe(rig.transform.eulerAngles.y);
                }
                foreach (var kv in _avatarPos)
                {
                    w.WriteValueSafe(kv.Key);
                    w.WriteValueSafe(kv.Value);
                    w.WriteValueSafe(_avatars.TryGetValue(kv.Key, out GameObject go) && go != null
                        ? go.transform.eulerAngles.y : 0f);
                }
                Broadcast(w);
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

        public void SendHazard(int stationIndex, byte action)
        {
            if (!IsClient) return;
            using var w = BeginMsg(MsgHazard);
            w.WriteValueSafe(stationIndex);
            w.WriteValueSafe(action);
            Send(w, NetworkManager.ServerClientId);
        }

        public void BroadcastHazard(int stationIndex, byte action)
        {
            if (!IsHost || _nm == null) return;
            using var w = BeginMsg(MsgHazardEvent);
            w.WriteValueSafe(stationIndex);
            w.WriteValueSafe(action);
            Broadcast(w);
        }

        /// <summary>Client-side time intent: forwarded to the host, ledgered
        /// there under this player's name. The local sim stays paused.</summary>
        public void SendTimeControl(bool paused, float ticksPerSecond)
        {
            if (!IsClient) return;
            using var w = BeginMsg(MsgTime);
            w.WriteValueSafe(paused);
            w.WriteValueSafe(ticksPerSecond);
            Send(w, NetworkManager.ServerClientId);
        }

        private void BroadcastNews(string item)
        {
            if (!IsHost || _nm == null) return;
            using var w = BeginMsg(MsgNews);
            w.WriteValueSafe(item);
            Broadcast(w);
        }

        private void SendFacility(ulong specificClient, bool force)
        {
            if (GameBootstrap.Facility == null) return;
            string json = GameBootstrap.Facility.ToJson();
            if (!force && json == _lastFacilityJson) return;
            // WriteValueSafe throws past the writer's 64 KB ceiling; a site
            // with that much routing keeps its last replicated layout instead.
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 60000)
            {
                if (_lastFacilityJson != "OVERSIZE")
                {
                    _lastFacilityJson = "OVERSIZE";
                    Debug.LogWarning("[GNP] facility JSON exceeds the message ceiling; layout replication suspended");
                }
                return;
            }
            _lastFacilityJson = json;
            using var w = BeginMsg(MsgFacility);
            w.WriteValueSafe(json);
            if (force && specificClient != 0) Send(w, specificClient);
            else Broadcast(w);
        }

        private void BroadcastRoster()
        {
            var sb = new System.Text.StringBuilder();
            // The host is a player too — without this entry clients could
            // never publicly name the host in a press release.
            sb.Append(_nm.LocalClientId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(LocalPlayerName).Append('|');
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
