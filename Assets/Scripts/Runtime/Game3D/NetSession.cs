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
    public sealed class NetSession : MonoBehaviour, IUiWindow
    {
        private const byte MsgHello = 1, MsgCmd = 2, MsgAvatar = 3, MsgTime = 4, MsgNamed = 5,
                           MsgHazard = 6, MsgPanel = 7, MsgRoute = 8, MsgVehicle = 9;
        private const byte MsgSnapshot = 20, MsgLedger = 21, MsgRoster = 22,
                           MsgFacility = 23, MsgKill = 24, MsgNews = 25, MsgStanding = 26,
                           MsgAvatars = 27, MsgHazardEvent = 28, MsgHazardState = 29,
                           MsgVehicleState = 30, MsgVehicleOrder = 31;
        private const string Channel = "GNP";
        private const double StandingLossNamed = 14.0;   // coop-griefing.md §4
        private const double StandingStart = 50.0;

        public bool Active { get; private set; }
        public bool IsHost { get; private set; }
        public bool IsClient { get; private set; }
        public string LocalPlayerName = "Operator";
        public TickReport RemoteReport;
        public long RemoteTick;
        public bool RemoteTimePaused = true;
        public float RemoteTps = 24f;

        // --- forklift: one driver at a time, whoever asked first ---
        private const ulong NoDriver = ulong.MaxValue;
        private ulong _vehicleDriver = NoDriver;
        private float _nextVehicleSend;
        private VehicleState _vehicle;

        private struct VehicleState
        {
            public Vector3 Pos; public float Yaw, Roll, Fork;
            public bool PalletExists, PalletCarried;
            public Vector3 PalletPos; public float PalletYaw;
        }

        /// <summary>True when the forklift is occupied by somebody else — used
        /// for the prompt and to decide who simulates it.</summary>
        public bool SomeoneElseDriving
        {
            get
            {
                return Active && _nm != null && _vehicleDriver != NoDriver &&
                       _vehicleDriver != _nm.LocalClientId;
            }
        }
        public bool PanelOpen { get { return UiWindows.IsOpen(this); } }

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

        private void Awake()
        {
            UiWindows.Register(this);
        }

        private void OnDestroy()
        {
            UiWindows.Unregister(this);
        }

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
            if (GameBootstrap.LocalPlayer != null)
                GameBootstrap.LocalPlayer.PlayerName = LocalPlayerName;
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
            _standings.Clear();
            _vehicleDriver = NoDriver;
            RemoteReport = default;
            RemoteTick = 0;
            RemoteTimePaused = true;
            RemoteTps = 24f;
            _lastFacilityJson = "";
        }

        private void OnClientConnected(ulong id)
        {
            if (!IsHost || id == _nm.LocalClientId) return;
            // Late joiner: full state — roster, facility, standings arrive with
            // the next broadcast; the ledger tail says where they walked in.
            SendFacility(id, force: true);
            // Armed or counting pull stations, with the REAL remaining clock:
            // a joiner walking into a 5 s countdown deserves the siren.
            for (int i = 0; i < World.PullStation.All.Count; i++)
            {
                var st = World.PullStation.All[i];
                if (!st.SealCut && !st.CountingDown) continue;
                using var w = BeginMsg(MsgHazardState);
                w.WriteValueSafe(i);
                w.WriteValueSafe(st.SealCut);
                w.WriteValueSafe(st.CountingDown);
                w.WriteValueSafe(st.Remaining);
                Send(w, id);
            }
            GameBootstrap.AddLedger("system", "player " + id + " connected");
        }

        private void OnClientDisconnected(ulong id)
        {
            if (!IsHost) return;
            if (_roster.TryGetValue(id, out string name))
                GameBootstrap.AddLedger("system", name + " disconnected");
            _roster.Remove(id);
            if (_vehicleDriver == id) _vehicleDriver = NoDriver;   // the forklift is free again
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
                    // The list is a COMPLETE mirror: anyone absent from it has
                    // left, so their avatar is torn down (no ghost bodies).
                    reader.ReadValueSafe(out int count);
                    var seen = new HashSet<ulong>();
                    for (int i = 0; i < count; i++)
                    {
                        reader.ReadValueSafe(out ulong id);
                        reader.ReadValueSafe(out Vector3 pos);
                        reader.ReadValueSafe(out float yaw);
                        seen.Add(id);
                        if (id == _nm.LocalClientId) continue; // that one is me
                        _avatarPos[id] = pos;
                        UpdateAvatar(id, pos, yaw);
                    }
                    var stale = new List<ulong>();
                    foreach (var id in _avatars.Keys) if (!seen.Contains(id)) stale.Add(id);
                    foreach (ulong id in stale)
                    {
                        if (_avatars[id] != null) Destroy(_avatars[id]);
                        _avatars.Remove(id);
                        _avatarPos.Remove(id);
                    }
                    break;
                }
                case MsgVehicle when IsHost:
                {
                    reader.ReadValueSafe(out byte kind);
                    string vactor = _roster.TryGetValue(sender, out string vn)
                        ? vn : "P" + sender.ToString(CultureInfo.InvariantCulture);
                    if (kind == 0)
                    {
                        if (_vehicleDriver == NoDriver)
                        {
                            _vehicleDriver = sender;
                            GameBootstrap.AddLedger(vactor, "took the forklift");
                        }
                    }
                    else if (kind == 1)
                    {
                        if (_vehicleDriver == sender) _vehicleDriver = NoDriver;
                    }
                    else if (kind == 2)
                    {
                        var st = ReadVehicle(ref reader);
                        if (_vehicleDriver == sender) _vehicle = st;   // the driver owns it
                    }
                    else if (kind == 3)
                    {
                        // Only ledger an order that actually produces a pallet.
                        if (World.Pallet.Current != null) break;
                        GameBootstrap.AddLedger(vactor, "ordered a pallet of rack hardware");
                        if (_vehicleDriver == NoDriver || _vehicleDriver == _nm.LocalClientId)
                        {
                            World.Forklift.SpawnDelivery();
                        }
                        else
                        {
                            using var fw = BeginMsg(MsgVehicleOrder);
                            Send(fw, _vehicleDriver);   // the authority spawns it
                        }
                    }
                    else if (kind == 4)
                    {
                        reader.ReadValueSafe(out ulong target);
                        // Only the actual driver may run people over with it.
                        if (_vehicleDriver != sender) break;
                        if (target == _nm.LocalClientId)
                        {
                            var rig = GameBootstrap.LocalPlayer;
                            if (rig != null && !rig.IsDead) rig.Die("was struck by the forklift");
                        }
                        else HostKillClient(target, "was struck by the forklift");
                    }
                    else if (kind == 5)
                    {
                        var flr = World.Forklift.Instance;
                        if (flr != null && flr.IsAuthority()) flr.Right(vactor);
                    }
                    break;
                }
                case MsgVehicleOrder when IsClient:
                {
                    World.Forklift.SpawnDelivery();
                    break;
                }
                case MsgVehicleState when IsClient:
                {
                    reader.ReadValueSafe(out ulong driver);
                    _vehicleDriver = driver;
                    var st = ReadVehicle(ref reader);
                    var fl = World.Forklift.Instance;
                    // Seat or evict: a claim is only real once the host says so.
                    if (fl != null) fl.OnVehicleGranted(driver == _nm.LocalClientId);
                    if (fl != null && !fl.IsAuthority())
                    {
                        fl.ApplyState(st.Pos, st.Yaw, st.Roll, st.Fork);
                        fl.ApplyPalletState(st.PalletExists, st.PalletPos, st.PalletYaw, st.PalletCarried);
                    }
                    break;
                }
                case MsgHazardState when IsClient:
                {
                    reader.ReadValueSafe(out int index);
                    reader.ReadValueSafe(out bool sealCut);
                    reader.ReadValueSafe(out bool counting);
                    reader.ReadValueSafe(out float remaining);
                    if (index >= 0 && index < World.PullStation.All.Count)
                        World.PullStation.All[index].ForceState(sealCut, counting, remaining);
                    break;
                }
                case MsgPanel when IsHost:
                {
                    reader.ReadValueSafe(out int delta);
                    if (delta != 1 && delta != -1) break;
                    string actor = _roster.TryGetValue(sender, out string pn)
                        ? pn : "P" + sender.ToString(CultureInfo.InvariantCulture);
                    if (GameBootstrap.Facility != null)
                        GameBootstrap.Facility.ApplyPanelDelta(delta, actor);
                    break;
                }
                case MsgRoute when IsHost:
                {
                    reader.ReadValueSafe(out string json);
                    World.RoutePath route = null;
                    try { route = JsonUtility.FromJson<World.RoutePath>(json); } catch { }
                    string actor2 = _roster.TryGetValue(sender, out string rn)
                        ? rn : "P" + sender.ToString(CultureInfo.InvariantCulture);
                    if (route != null && GameBootstrap.Facility != null)
                        GameBootstrap.Facility.AddRouteFromNet(route, actor2);
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
                    reader.ReadValueSafe(out bool hostPaused);
                    reader.ReadValueSafe(out float hostTps);
                    if (ReportFromBytes(bytes, out TickReport report))
                    {
                        RemoteTick = tick;
                        RemoteReport = report;
                        RemoteTimePaused = hostPaused;
                        RemoteTps = hostTps;
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
            if (kb != null && kb.f2Key.wasPressedThisFrame) UiWindows.Toggle(this);

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
                    w.WriteValueSafe(driver.Paused);
                    w.WriteValueSafe(driver.TicksPerSecond);
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

            // --- forklift: the authority publishes, everyone else follows ---
            if (Time.unscaledTime >= _nextVehicleSend)
            {
                _nextVehicleSend = Time.unscaledTime + 0.1f;
                var fl = World.Forklift.Instance;
                if (fl != null && fl.IsAuthority()) _vehicle = LocalVehicle();
                if (IsHost)
                {
                    using var w = BeginMsg(MsgVehicleState);
                    w.WriteValueSafe(_vehicleDriver);
                    WriteVehicle(w, _vehicle);
                    Broadcast(w);
                    // The host follows a client-driven forklift like anyone else.
                    if (fl != null && !fl.IsAuthority())
                    {
                        fl.ApplyState(_vehicle.Pos, _vehicle.Yaw, _vehicle.Roll, _vehicle.Fork);
                        fl.ApplyPalletState(_vehicle.PalletExists, _vehicle.PalletPos,
                            _vehicle.PalletYaw, _vehicle.PalletCarried);
                    }
                }
                else if (fl != null && fl.IsAuthority())
                {
                    using var w = BeginMsg(MsgVehicle);
                    w.WriteValueSafe((byte)2);
                    WriteVehicle(w, _vehicle);
                    Send(w, NetworkManager.ServerClientId);
                }
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

        public void SendPanelDelta(int delta)
        {
            if (!IsClient) return;
            using var w = BeginMsg(MsgPanel);
            w.WriteValueSafe(delta);
            Send(w, NetworkManager.ServerClientId);
        }

        public void SendRoute(string routeJson)
        {
            if (!IsClient) return;
            using var w = BeginMsg(MsgRoute);
            w.WriteValueSafe(routeJson);
            Send(w, NetworkManager.ServerClientId);
        }

        /// <summary>Take or release the forklift. The host arbitrates; a
        /// client's claim is a request, not a fact.</summary>
        public void ClaimVehicle(bool claim)
        {
            if (!Active || _nm == null) return;
            if (IsHost)
            {
                if (claim && _vehicleDriver != NoDriver && _vehicleDriver != _nm.LocalClientId) return;
                _vehicleDriver = claim ? _nm.LocalClientId : NoDriver;
                return;
            }
            using var w = BeginMsg(MsgVehicle);
            w.WriteValueSafe((byte)(claim ? 0 : 1));
            Send(w, NetworkManager.ServerClientId);
        }

        /// <summary>Ask whoever owns the vehicle to put a delivery on the dock.
        /// A client asks the host; the host forwards to the driving client.</summary>
        public void RequestDelivery()
        {
            if (!Active || _nm == null) return;
            if (IsHost)
            {
                if (_vehicleDriver == NoDriver || _vehicleDriver == _nm.LocalClientId)
                {
                    World.Forklift.SpawnDelivery();
                    return;
                }
                using var fw = BeginMsg(MsgVehicleOrder);
                Send(fw, _vehicleDriver);
                return;
            }
            using var w = BeginMsg(MsgVehicle);
            w.WriteValueSafe((byte)3);
            Send(w, NetworkManager.ServerClientId);
        }

        /// <summary>A driving client asks the host to run somebody over.</summary>
        public void RequestVehicleKill(ulong target)
        {
            if (!Active || _nm == null || IsHost) return;
            using var w = BeginMsg(MsgVehicle);
            w.WriteValueSafe((byte)4);
            w.WriteValueSafe(target);
            Send(w, NetworkManager.ServerClientId);
        }

        /// <summary>Ask the vehicle's owner to put it back on its wheels.</summary>
        public void RequestRight()
        {
            if (!Active || _nm == null || IsHost) return;
            using var w = BeginMsg(MsgVehicle);
            w.WriteValueSafe((byte)5);
            Send(w, NetworkManager.ServerClientId);
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

        private static VehicleState ReadVehicle(ref FastBufferReader reader)
        {
            var s = new VehicleState();
            reader.ReadValueSafe(out s.Pos);
            reader.ReadValueSafe(out s.Yaw);
            reader.ReadValueSafe(out s.Roll);
            reader.ReadValueSafe(out s.Fork);
            reader.ReadValueSafe(out s.PalletExists);
            reader.ReadValueSafe(out s.PalletPos);
            reader.ReadValueSafe(out s.PalletYaw);
            reader.ReadValueSafe(out s.PalletCarried);
            return s;
        }

        private static void WriteVehicle(FastBufferWriter w, VehicleState s)
        {
            w.WriteValueSafe(s.Pos);
            w.WriteValueSafe(s.Yaw);
            w.WriteValueSafe(s.Roll);
            w.WriteValueSafe(s.Fork);
            w.WriteValueSafe(s.PalletExists);
            w.WriteValueSafe(s.PalletPos);
            w.WriteValueSafe(s.PalletYaw);
            w.WriteValueSafe(s.PalletCarried);
        }

        /// <summary>Read the local forklift, whoever owns it right now.</summary>
        private static VehicleState LocalVehicle()
        {
            var s = new VehicleState();
            var fl = World.Forklift.Instance;
            if (fl == null) return s;
            fl.WriteState(out s.Pos, out s.Yaw, out s.Roll, out s.Fork);
            var p = World.Pallet.Current;
            s.PalletExists = p != null;
            if (p != null)
            {
                s.PalletPos = p.transform.position;
                s.PalletYaw = p.transform.eulerAngles.y;
                s.PalletCarried = p.Carried;
            }
            return s;
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
            // Guard with NGO's OWN wire size (4-byte length + 2 bytes/char) --
            // WriteValueSafe throws past the writer's 64 KB ceiling, and a
            // UTF-8 count disagrees with it by a factor of two for ASCII.
            if (FastBufferWriter.GetWriteSize(json) > 60000)
            {
                if (_lastFacilityJson != "OVERSIZE")
                {
                    _lastFacilityJson = "OVERSIZE";
                    Debug.LogWarning("[GNP] facility JSON exceeds the message ceiling; layout replication suspended");
                }
                return;
            }
            using var w = BeginMsg(MsgFacility);
            w.WriteValueSafe(json);
            if (force && specificClient != 0)
            {
                // Targeted late-joiner send: do NOT mark broadcast, or the
                // clients who were already here never receive this change.
                Send(w, specificClient);
            }
            else
            {
                _lastFacilityJson = json;
                Broadcast(w);
            }
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

        /// <summary>The passive corner label only; the window itself is
        /// drawn by UiWindows.</summary>
        private void OnGUI()
        {
            if (PanelOpen || PauseMenu.IsOpen) return;
            // Above the ticker, not on it — and not under the cab read-out,
            // which owns that corner while someone drives.
            var local = GameBootstrap.LocalPlayer;
            if (local != null && local.Driving != null) return;
            GUI.depth = 10;
            World.UiScaler.Begin();
            try
            {
                GUI.Label(new Rect(World.UiScaler.W - 200, World.UiScaler.H - 54, 196, 22),
                    Active ? (IsHost ? "hosting (F2)" : "client (F2)") : "F2: multiplayer");
            }
            finally { World.UiScaler.End(); }
        }

        // --- IUiWindow ----------------------------------------------------
        public int Id { get { return 912; } }
        public string Title { get { return "MULTIPLAYER (F2)"; } }
        public UiWindowFlags Flags { get { return UiWindowFlags.None; } }

        public Rect DefaultRect(float w, float h)
        {
            // Bottom right, clear of the ticker; height 0 lets the layout
            // size the window to its contents.
            return new Rect(w - 320f, h - UiScaler.BottomReserve - 150f, 300f, 0f);
        }

        public void OnOpened() { }
        public void OnClosed() { }

        private const int RosterShown = 6;

        public void DrawContents(int id)
        {
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
                // An auto-sized window must not grow past the screen with a
                // big roster: the last few entries and a count.
                int skip = Mathf.Max(0, _standings.Count - RosterShown);
                if (skip > 0) GUILayout.Label("+" + skip + " more");
                int i = 0;
                foreach (var kv in _standings)
                {
                    if (i++ < skip) continue;
                    GUILayout.Label(kv.Key + "  standing " +
                        kv.Value.ToString("0", CultureInfo.InvariantCulture));
                }
                if (GUILayout.Button("Disconnect")) Disconnect();
            }
        }
    }
}
