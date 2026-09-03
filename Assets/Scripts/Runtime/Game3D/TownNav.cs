using System.Collections.Generic;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// Where a resident may walk. A 4 m lattice over the town outside the
    /// perimeter, built once at load from the fence rectangle and the
    /// footprints CityBuilder registered on SiteRefs. Nodes on the
    /// carriageway cost more than pavement or verge, so a walk across town
    /// keeps off the road where it can and still crosses one where it must.
    ///
    /// Deliberately not a NavMesh: that is a package and a bake step for a
    /// world that is flat and whose obstacles are already a list of boxes.
    /// The site itself is walled off — residents never come inside the wire,
    /// which is the one part of the old presence rules that still holds.
    /// </summary>
    public sealed class TownNav
    {
        public const float Spacing = 4f;          // lattice pitch
        private const float FineCell = 1f;        // blocked-bitmap resolution
        private const float Reach = 46f;          // how far past the fence the town runs
        private const float ObstacleClear = 1.2f; // a shoulder's width off a wall
        private const float FenceClear = 2.5f;
        // What a step costs. People keep to the streets: a node beside the
        // ring road is cheap, the open ground behind the houses is dearer,
        // and the carriageway itself is dearest of all — crossed, not walked.
        private const int StreetCost = 2, BackCost = 5, RoadCost = 14;
        private const float StreetBand = 14f;
        private const int MaxSkip = 12;           // string-pulling look-ahead

        // The carriageway: a band around the fence, centre line RingRoadOffset
        // out and 3.5 m of asphalt either side of it (CityBuilder's slabs).
        private const float RoadCentre = 8.5f, RoadHalf = 3.5f;

        private float _x0, _z0;
        private int _fnx, _fnz;
        private bool[] _blocked;

        private int _nx, _nz;
        private int[] _cell;        // lattice cell -> node index, or -1
        private Vector3[] _pos;
        private int[] _cost;
        private int[] _street;      // the nodes an errand may aim at
        private int[] _linkOff, _linkTo;

        private int[] _dist, _prev;
        private readonly List<long> _pq = new List<long>();
        private readonly List<int> _nodePath = new List<int>();

        public int NodeCount { get { return _pos.Length; } }

        // ------------------------------------------------------------------
        // Building
        // ------------------------------------------------------------------

        public static TownNav Build(SiteRefs site)
        {
            var nav = new TownNav();
            nav.Rasterise(site);
            nav.Lattice(site);
            return nav;
        }

        /// <summary>The 1 m bitmap every clearance test reads: the site plus
        /// its wire, and every town footprint grown by a shoulder's width.
        /// Rasterised per obstacle rather than tested per cell — 80 boxes
        /// against 35 000 cells the other way round is a visible hitch.</summary>
        private void Rasterise(SiteRefs site)
        {
            _x0 = site.FenceX0 - Reach;
            _z0 = site.FenceZ0 - Reach;
            float x1 = site.FenceX1 + Reach, z1 = site.FenceZ1 + Reach;
            _fnx = Mathf.CeilToInt((x1 - _x0) / FineCell);
            _fnz = Mathf.CeilToInt((z1 - _z0) / FineCell);
            _blocked = new bool[_fnx * _fnz];

            Block(site.FenceX0 - FenceClear, site.FenceX1 + FenceClear,
                  site.FenceZ0 - FenceClear, site.FenceZ1 + FenceClear);
            var obstacles = site.TownObstacles;
            for (int i = 0; i < obstacles.Count; i++)
            {
                Bounds b = obstacles[i];
                Block(b.min.x - ObstacleClear, b.max.x + ObstacleClear,
                      b.min.z - ObstacleClear, b.max.z + ObstacleClear);
            }
        }

        private void Block(float xa, float xb, float za, float zb)
        {
            int ia = Mathf.Max(0, Mathf.FloorToInt((xa - _x0) / FineCell));
            int ib = Mathf.Min(_fnx - 1, Mathf.CeilToInt((xb - _x0) / FineCell));
            int ja = Mathf.Max(0, Mathf.FloorToInt((za - _z0) / FineCell));
            int jb = Mathf.Min(_fnz - 1, Mathf.CeilToInt((zb - _z0) / FineCell));
            for (int j = ja; j <= jb; j++)
                for (int i = ia; i <= ib; i++)
                    _blocked[j * _fnx + i] = true;
        }

        /// <summary>True where a figure may stand.</summary>
        public bool Walkable(Vector3 p)
        {
            int i = Mathf.FloorToInt((p.x - _x0) / FineCell);
            int j = Mathf.FloorToInt((p.z - _z0) / FineCell);
            if (i < 0 || j < 0 || i >= _fnx || j >= _fnz) return false;
            return !_blocked[j * _fnx + i];
        }

        /// <summary>Both ends and every metre between them are walkable.</summary>
        public bool Clear(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            int steps = Mathf.CeilToInt(Mathf.Sqrt(dx * dx + dz * dz) / FineCell);
            for (int k = 0; k <= steps; k++)
            {
                float t = steps == 0 ? 0f : (float)k / steps;
                if (!Walkable(new Vector3(a.x + dx * t, 0f, a.z + dz * t))) return false;
            }
            return true;
        }

        private void Lattice(SiteRefs site)
        {
            float x1 = site.FenceX1 + Reach, z1 = site.FenceZ1 + Reach;
            _nx = Mathf.FloorToInt((x1 - _x0) / Spacing);
            _nz = Mathf.FloorToInt((z1 - _z0) / Spacing);
            _cell = new int[_nx * _nz];
            var pos = new List<Vector3>(_nx * _nz / 2);
            var cost = new List<int>(_nx * _nz / 2);
            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    var p = new Vector3(_x0 + (i + 0.5f) * Spacing, 0f, _z0 + (j + 0.5f) * Spacing);
                    if (!Walkable(p)) { _cell[j * _nx + i] = -1; continue; }
                    _cell[j * _nx + i] = pos.Count;
                    pos.Add(p);
                    float ring = RingDistance(site, p);
                    cost.Add(ring <= RoadHalf ? RoadCost : ring <= StreetBand ? StreetCost : BackCost);
                }
            _pos = pos.ToArray();
            _cost = cost.ToArray();
            var street = new List<int>();
            for (int n = 0; n < _cost.Length; n++) if (_cost[n] == StreetCost) street.Add(n);
            _street = street.ToArray();

            // Eight-way adjacency; a diagonal needs both of its orthogonal
            // neighbours, or a figure clips the corner of a house.
            var to = new List<int>(_pos.Length * 8);
            _linkOff = new int[_pos.Length + 1];
            for (int j = 0; j < _nz; j++)
                for (int i = 0; i < _nx; i++)
                {
                    int n = _cell[j * _nx + i];
                    if (n < 0) continue;
                    _linkOff[n] = to.Count;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            if (di == 0 && dj == 0) continue;
                            int m = At(i + di, j + dj);
                            if (m < 0) continue;
                            if (di != 0 && dj != 0 && (At(i + di, j) < 0 || At(i, j + dj) < 0)) continue;
                            to.Add(m);
                        }
                }
            _linkOff[_pos.Length] = to.Count;
            // The offsets were written in lattice order, which is node order,
            // so the array is already a valid CSR index.
            _linkTo = to.ToArray();
            _dist = new int[_pos.Length];
            _prev = new int[_pos.Length];
        }

        private int At(int i, int j)
        {
            if (i < 0 || j < 0 || i >= _nx || j >= _nz) return -1;
            return _cell[j * _nx + i];
        }

        /// <summary>Plan distance from p to the ring road's centre line — the
        /// rectangle RoadCentre metres outside the fence. Zero on the white
        /// line, RoadHalf at the kerb, and it grows both into town and back
        /// toward the wire.</summary>
        private static float RingDistance(SiteRefs site, Vector3 p)
        {
            float x0 = site.FenceX0 - RoadCentre, x1 = site.FenceX1 + RoadCentre;
            float z0 = site.FenceZ0 - RoadCentre, z1 = site.FenceZ1 + RoadCentre;
            float dx = Mathf.Min(p.x - x0, x1 - p.x);
            float dz = Mathf.Min(p.z - z0, z1 - p.z);
            if (dx > 0f && dz > 0f) return Mathf.Min(dx, dz);      // inside the ring
            float ox = Mathf.Max(0f, -dx), oz = Mathf.Max(0f, -dz);
            return Mathf.Sqrt(ox * ox + oz * oz);
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        public Vector3 NodePos(int node) { return _pos[node]; }

        /// <summary>Somewhere to set out for, from a hash: always a place on
        /// a street rather than the middle of a field, so an errand reads as
        /// going somewhere. The route may still cut across the green.</summary>
        public int NodeFrom(uint hash)
        {
            if (_street.Length > 0) return _street[hash % (uint)_street.Length];
            return (int)(hash % (uint)_pos.Length);
        }

        /// <summary>The walkable node nearest p, searching outward through
        /// the lattice; -1 when the town has none within 40 m.</summary>
        public int Nearest(Vector3 p)
        {
            int ci = Mathf.FloorToInt((p.x - _x0) / Spacing);
            int cj = Mathf.FloorToInt((p.z - _z0) / Spacing);
            for (int ring = 0; ring < 10; ring++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int dj = -ring; dj <= ring; dj++)
                    for (int di = -ring; di <= ring; di++)
                    {
                        if (ring > 0 && Mathf.Abs(di) != ring && Mathf.Abs(dj) != ring) continue;
                        int n = At(ci + di, cj + dj);
                        if (n < 0) continue;
                        float d = (_pos[n] - p).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = n; }
                    }
                if (best >= 0) return best;
            }
            return -1;
        }

        /// <summary>Shortest walk from `from` to the node `goal`, written into
        /// `path` as world points with the redundant corners pulled out.
        /// False when there is no way through.</summary>
        public bool TryPath(Vector3 from, int goal, List<Vector3> path)
        {
            path.Clear();
            int start = Nearest(from);
            if (start < 0 || goal < 0 || goal >= _pos.Length) return false;
            if (start == goal) { path.Add(_pos[goal]); return true; }
            if (!Dijkstra(start, goal)) return false;

            _nodePath.Clear();
            for (int n = goal; n != -1; n = _prev[n]) _nodePath.Add(n);
            _nodePath.Reverse();

            // String-pulling: keep only the corners a straight line cannot
            // reach past, so the walk reads as a person crossing a green
            // rather than a token stepping through a grid.
            int i = 0;
            path.Add(_pos[_nodePath[0]]);
            while (i < _nodePath.Count - 1)
            {
                int limit = Mathf.Min(_nodePath.Count - 1, i + MaxSkip);
                int j = limit;
                while (j > i + 1 && !Clear(_pos[_nodePath[i]], _pos[_nodePath[j]])) j--;
                path.Add(_pos[_nodePath[j]]);
                i = j;
            }
            return true;
        }

        private bool Dijkstra(int start, int goal)
        {
            for (int i = 0; i < _dist.Length; i++) { _dist[i] = int.MaxValue; _prev[i] = -1; }
            _pq.Clear();
            _dist[start] = 0;
            Push(0, start);
            while (_pq.Count > 0)
            {
                long top = Pop();
                int d = (int)(top >> 20);
                int n = (int)(top & 0xFFFFF);
                if (d > _dist[n]) continue;          // a stale copy; the node is done
                if (n == goal) return true;
                for (int k = _linkOff[n]; k < _linkOff[n + 1]; k++)
                {
                    int m = _linkTo[k];
                    bool diagonal = !Mathf.Approximately(_pos[m].x, _pos[n].x) && !Mathf.Approximately(_pos[m].z, _pos[n].z);
                    int step = (_cost[n] + _cost[m]) * (diagonal ? 7 : 5);
                    int nd = d + step;
                    if (nd >= _dist[m]) continue;
                    _dist[m] = nd;
                    _prev[m] = n;
                    Push(nd, m);
                }
            }
            return false;
        }

        // A binary heap of (distance, node) packed into a long. Stale entries
        // are pushed rather than decreased in place and skipped on the way out.
        private void Push(int dist, int node)
        {
            long v = ((long)dist << 20) | (uint)node;
            _pq.Add(v);
            int i = _pq.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_pq[p] <= _pq[i]) break;
                long t = _pq[p]; _pq[p] = _pq[i]; _pq[i] = t;
                i = p;
            }
        }

        private long Pop()
        {
            long top = _pq[0];
            int last = _pq.Count - 1;
            _pq[0] = _pq[last];
            _pq.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < _pq.Count && _pq[l] < _pq[m]) m = l;
                if (r < _pq.Count && _pq[r] < _pq[m]) m = r;
                if (m == i) break;
                long t = _pq[m]; _pq[m] = _pq[i]; _pq[i] = t;
                i = m;
            }
            return top;
        }
    }
}
