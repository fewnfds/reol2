using System;
using System.Collections.Generic;
using System.Numerics;
using static Reol2.World.LandformProfile;

namespace Reol2.World
{
    internal readonly struct RiverSection
    {
        internal readonly Vector2 Center, Tangent;
        internal readonly float Level, Depth, HalfWidth, Valley, LeftBank, RightBank;

        internal RiverSection(Vector2 center, float ground, float level, float width, float depth,
            Vector2 tangent = default, float leftBank = 0f, float rightBank = 0f)
        {
            Center = center;
            Tangent = tangent;
            Level = level;
            HalfWidth = width;
            Depth = depth;
            float bank = width + MathF.Min(width * 0.25f, 8f);
            LeftBank = leftBank > 0f ? leftBank : bank;
            RightBank = rightBank > 0f ? rightBank : bank;
            Valley = width == 0f ? 0f : MathF.Max(MathF.Max(LeftBank, RightBank) + RiverSample.Margin,
                MathF.Min(300f, width + MathF.Max(24f, (ground - level) * 1.2f)));
        }
    }

    internal readonly struct RiverSample
    {
        internal const float Margin = 12f;
        internal readonly float DistanceSquared, Level, Depth, HalfWidth, Valley, Bank;

        internal RiverSample(float distanceSquared, float level, float depth, float halfWidth, float valley, float bank)
        {
            DistanceSquared = distanceSquared;
            Level = level;
            Depth = depth;
            HalfWidth = halfWidth;
            Valley = valley;
            Bank = bank;
        }

        internal float Carve(float initial)
        {
            float distance = MathF.Sqrt(DistanceSquared);
            float lateral = distance / HalfWidth;
            float channel = Level + Depth * (lateral * lateral - 1f);
            float shoulder = Ramp((distance - HalfWidth) / (Valley - HalfWidth));
            return Mix(initial, MathF.Min(channel, initial), (1f - shoulder) * Ramp(HalfWidth / 5f));
        }

        internal float Water(float ground, out bool tidal)
        {
            float distance = MathF.Sqrt(DistanceSquared);
            tidal = Level == 0f && distance < Bank;
            return distance >= Bank + Margin ? 0f : Math.Clamp(ground + (Bank - distance) * 0.75f, 0f, Level);
        }
    }

    internal readonly struct RiverReach
    {
        internal readonly RiverSection Upstream, Downstream;
        private readonly Vector2 axis, startTangent, endTangent;
        private readonly float reciprocalLengthSquared, startScale, endScale;
        private readonly bool endCap;

        internal RiverReach(RiverSection upstream, RiverSection downstream, bool endCap = false)
        {
            axis = downstream.Center - upstream.Center;
            float lengthSquared = axis.LengthSquared();
            Upstream = upstream;
            Downstream = downstream;
            reciprocalLengthSquared = lengthSquared > 0f ? 1f / lengthSquared : 0f;
            Vector2 direction = axis * MathF.Sqrt(reciprocalLengthSquared);
            startTangent = SectionTangent(upstream.Tangent, direction);
            endTangent = SectionTangent(downstream.Tangent, direction);
            startScale = Vector2.Dot(direction, startTangent);
            endScale = Vector2.Dot(direction, endTangent);
            this.endCap = endCap;
        }
        internal Vector2 Minimum => Vector2.Min(Upstream.Center, Downstream.Center) - new Vector2(Padding);
        internal Vector2 Maximum => Vector2.Max(Upstream.Center, Downstream.Center) + new Vector2(Padding);
        private float Padding => MathF.Max(Upstream.Valley, Downstream.Valley);

        internal float Carve(Vector2 point, float initial)
        {
            return TrySample(point, out RiverSample section) ? section.Carve(initial) : initial;
        }

        internal bool TryWaterAt(Vector2 point, float ground, out float level, out float distanceSquared, ref bool tidal)
        {
            level = 0f;
            distanceSquared = float.PositiveInfinity;
            if (!TrySample(point, out RiverSample section))
                return false;
            distanceSquared = section.DistanceSquared;
            level = section.Water(ground, out bool atSea);
            tidal |= atSea;
            return distanceSquared < (section.Bank + RiverSample.Margin) * (section.Bank + RiverSample.Margin);
        }

        internal bool TrySample(Vector2 point, out RiverSample sample)
        {
            sample = default;
            Vector2 fromStart = point - Upstream.Center;
            float from = Vector2.Dot(fromStart, startTangent) * endScale;
            float to = Vector2.Dot(Downstream.Center - point, endTangent) * startScale;
            if (from < 0f || to < 0f && !endCap || from + to == 0f)
                return false;

            // The join plane belongs to both reaches with exactly the same end section.
            float t = to <= 0f ? 1f : from / (from + to);
            Vector2 lateral = fromStart - t * axis;
            float distanceSquared = lateral.LengthSquared();
            float width = Mix(Upstream.HalfWidth, Downstream.HalfWidth, t);
            float valley = Mix(Upstream.Valley, Downstream.Valley, t);
            if (width <= 0f || distanceSquared >= valley * valley)
                return false;
            Vector2 tangent = Vector2.Lerp(startTangent * endScale, endTangent * startScale, t);
            float bank = tangent.X * lateral.Y >= tangent.Y * lateral.X
                ? Mix(Upstream.LeftBank, Downstream.LeftBank, t) : Mix(Upstream.RightBank, Downstream.RightBank, t);
            if (to < 0f && distanceSquared >= (bank + RiverSample.Margin) * (bank + RiverSample.Margin))
                return false;
            sample = new RiverSample(distanceSquared, Mix(Upstream.Level, Downstream.Level, t),
                Mix(Upstream.Depth, Downstream.Depth, t), width, valley, bank);
            return true;
        }

        internal static Vector2 SectionTangent(Vector2 tangent, Vector2 direction) =>
            Vector2.Dot(tangent, direction) >= 0.15f ? tangent : direction;

        internal float DistanceSquared(Vector2 point, out float t)
        {
            Vector2 offset = point - Upstream.Center;
            t = Math.Clamp(Vector2.Dot(offset, axis) * reciprocalLengthSquared, 0f, 1f);
            return (offset - t * axis).LengthSquared();
        }

        internal bool ClipAxis(Vector2 min, Vector2 max, out float enter, out float exit)
        {
            enter = 0f;
            exit = 1f;
            return ClipDimension(min.X - Upstream.Center.X, max.X - Upstream.Center.X, axis.X, ref enter, ref exit)
                && ClipDimension(min.Y - Upstream.Center.Y, max.Y - Upstream.Center.Y, axis.Y, ref enter, ref exit);
        }

        private static bool ClipDimension(float min, float max, float delta, ref float enter, ref float exit)
        {
            if (delta == 0f)
                return min <= 0f && max >= 0f;
            float a = min / delta, b = max / delta;
            enter = MathF.Max(enter, MathF.Min(a, b));
            exit = MathF.Min(exit, MathF.Max(a, b));
            return enter <= exit;
        }

    }

    internal sealed class RegionalRivers
    {
        private const float MinimumRunoff = 29f;
        private const float MaximumDepth = 9f;
        internal const int BucketWidth = 500;
        private readonly int half, buckets;
        private readonly int[] offsets, candidates;
        internal readonly RiverReach[] Reaches;
        internal int IndexBytes => (offsets.Length + candidates.Length) * sizeof(int);

        internal RegionalRivers(RegionalDrainage drainage, int size, int step, bool hasLake,
            Func<float, float, float> heightAt = null)
            : this(BuildReaches(drainage, size / step + 1, step, size / 2, hasLake, heightAt), size)
        {
        }

        internal RegionalRivers(RiverReach[] reaches, int size)
        {
            half = size / 2;
            Reaches = reaches;
            buckets = size / BucketWidth;
            offsets = new int[buckets * buckets + 1];
            for (int i = 0; i < Reaches.Length; i++)
                AddToBuckets(i, null);
            for (int i = 1; i < offsets.Length; i++)
                offsets[i] += offsets[i - 1];
            candidates = new int[offsets[^1]];
            var write = (int[])offsets.Clone();
            for (int i = 0; i < Reaches.Length; i++)
                AddToBuckets(i, write);
        }

        internal float Sculpt(Vector2 point, float initial)
        {
            // The tidal bed continues below the old shoreline. Deep seabed can
            // never be reached by the channel cut and needs no river lookup.
            if (initial <= -MaximumDepth)
                return initial;
            return TrySection(point, out RiverSample section) ? section.Carve(initial) : initial;
        }

        internal float WaterAt(Vector2 point, float ground, out bool tidal)
        {
            if (TrySection(point, out RiverSample section))
                return section.Water(ground, out tidal);
            tidal = false;
            return 0f;
        }

        internal void Sample(Vector2 point, ref float ground, out float water, out bool tidal)
        {
            water = 0f;
            tidal = false;
            if (!TrySection(point, out RiverSample section))
                return;
            if (ground > -MaximumDepth)
                ground = section.Carve(ground);
            water = section.Water(ground, out tidal);
        }

        private bool TrySection(Vector2 point, out RiverSample closest)
        {
            int bucket = Bucket(point.X) + Bucket(point.Y) * buckets;
            float best = float.PositiveInfinity;
            closest = default;
            for (int i = offsets[bucket]; i < offsets[bucket + 1]; i++)
            {
                if (!Reaches[candidates[i]].TrySample(point, out RiverSample section))
                    continue;
                // A channel's dry shoulder must not take ownership from an adjacent wet section.
                float bankSquared = section.Bank * section.Bank;
                float score = section.DistanceSquared < bankSquared
                    ? section.DistanceSquared / bankSquared - 1f : section.DistanceSquared;
                if (score < best)
                {
                    best = score;
                    closest = section;
                }
            }
            return best < float.PositiveInfinity;
        }

        internal bool IntersectsChannel(Vector2 center, float radius)
        {
            return Intersects(center, radius, valley: false);
        }

        internal bool IntersectsValley(Vector2 center, float radius)
        {
            return Intersects(center, radius, valley: true);
        }

        internal bool TrySpine(Vector2 min, Vector2 max, out Vector2 point, out float halfWidth, float margin = 0f)
        {
            point = default;
            halfWidth = 0f;
            min -= new Vector2(margin);
            max += new Vector2(margin);
            int lastX = Bucket(max.X), lastZ = Bucket(max.Y);
            for (int z = Bucket(min.Y); z <= lastZ; z++)
            for (int x = Bucket(min.X); x <= lastX; x++)
            {
                int bucket = x + z * buckets;
                for (int i = offsets[bucket]; i < offsets[bucket + 1]; i++)
                {
                    RiverReach reach = Reaches[candidates[i]];
                    if (!reach.ClipAxis(min, max, out float enter, out float exit))
                        continue;
                    float t = (enter + exit) * 0.5f;
                    float width = Mix(reach.Upstream.HalfWidth, reach.Downstream.HalfWidth, t);
                    if (width <= halfWidth)
                        continue;
                    halfWidth = width;
                    point = Vector2.Lerp(reach.Upstream.Center, reach.Downstream.Center, t);
                }
            }
            return halfWidth > 0f;
        }

        private bool Intersects(Vector2 center, float radius, bool valley)
        {
            int lastX = Bucket(center.X + radius), lastZ = Bucket(center.Y + radius);
            for (int z = Bucket(center.Y - radius); z <= lastZ; z++)
            for (int x = Bucket(center.X - radius); x <= lastX; x++)
            {
                int bucket = x + z * buckets;
                for (int i = offsets[bucket]; i < offsets[bucket + 1]; i++)
                {
                    RiverReach reach = Reaches[candidates[i]];
                    float span = valley ? MathF.Max(reach.Upstream.Valley, reach.Downstream.Valley)
                        : MathF.Max(reach.Upstream.HalfWidth, reach.Downstream.HalfWidth) + 8f;
                    float distance = radius + span;
                    if (reach.DistanceSquared(center, out _) < distance * distance)
                        return true;
                }
            }
            return false;
        }

        internal int CandidatesAt(Vector2 point)
        {
            int bucket = Bucket(point.X) + Bucket(point.Y) * buckets;
            return offsets[bucket + 1] - offsets[bucket];
        }

        private int Bucket(float coordinate) => Math.Clamp((int)((coordinate + half) / BucketWidth), 0, buckets - 1);

        private void AddToBuckets(int index, int[] write)
        {
            Vector2 minimum = Reaches[index].Minimum, maximum = Reaches[index].Maximum;
            int lastX = Bucket(maximum.X), lastZ = Bucket(maximum.Y);
            for (int z = Bucket(minimum.Y); z <= lastZ; z++)
            for (int x = Bucket(minimum.X); x <= lastX; x++)
            {
                int bucket = x + z * buckets;
                if (write == null)
                    offsets[bucket + 1]++;
                else
                    candidates[write[bucket]++] = index;
            }
        }

        private static RiverReach[] BuildReaches(RegionalDrainage drainage, int side, int step, int half, bool hasLake,
            Func<float, float, float> heightAt)
        {
            const float MinimumCoastalLengthInSteps = 2.84f;
            const float StrongCoastalRunoff = 48f;
            int count = drainage.Ground.Length;
            var inlet = new int[count];
            var wet = new bool[count];
            var upstreamLength = new float[count];
            Array.Fill(inlet, -1);
            heightAt ??= SampleDrainageHeight;

            for (int rank = drainage.Count - 1; rank >= 0; rank--)
            {
                int index = drainage.Order[rank], next = drainage.Downstream[index];
                if (next < 0)
                    continue;
                Vector2 flow = Position(next) - Position(index);
                float distance = flow.Length();
                float slope = MathF.Max(0f, drainage.Ground[index] - drainage.Ground[next]) / distance;
                float threshold = MinimumRunoff * (1.08f - 0.21f * Ramp(slope / 0.13f));
                if (upstreamLength[index] == 0f && (drainage.Area[index] < 14 || drainage.Runoff[index] < threshold
                    || !InValley(index, flow / distance)))
                    continue;
                wet[index] = true;
                upstreamLength[next] = MathF.Max(upstreamLength[next], upstreamLength[index] + distance);
            }

            for (int rank = 0; rank < drainage.Count; rank++)
            {
                int index = drainage.Order[rank];
                int next = drainage.Downstream[index];
                if (next < 0)
                {
                    // A wet short catchment can have a real coastal creek. Marginal
                    // one- or two-edge catchments retain the natural landform.
                    wet[index] = next != RegionalDrainage.Ocean || upstreamLength[index] > MinimumCoastalLengthInSteps * step
                        || drainage.Runoff[index] >= StrongCoastalRunoff;
                    continue;
                }

                wet[index] &= wet[next];
                if (!wet[index])
                    continue;
                if (inlet[next] < 0 || drainage.Runoff[index] > drainage.Runoff[inlet[next]])
                    inlet[next] = index;
            }

            var centers = new Vector2[count];
            var outlets = new int[count];
            var receivers = (int[])drainage.Downstream.Clone();
            bool rerouted = false;
            for (int rank = 0; rank < drainage.Count; rank++)
            {
                int index = drainage.Order[rank];
                int next = receivers[index];
                if (!wet[index] || (next < 0 && inlet[index] < 0))
                    continue;
                Vector2 point = Position(index);
                if (next >= 0 && inlet[index] >= 0)
                {
                    point = point * 0.72f + (Position(inlet[index]) + Position(next)) * 0.14f;
                    point = FollowValley(point, Direction(index));
                }
                centers[index] = point;
                outlets[index] = next < 0 ? index + 1 : outlets[next];
                if (next < 0 || receivers[next] < 0)
                    continue;
                receivers[index] = FindReceiver(index, next);
                rerouted |= receivers[index] != next;
            }

            float[] runoffAt = drainage.Runoff;
            if (rerouted)
            {
                runoffAt = new float[count];
                for (int rank = 0; rank < drainage.Count; rank++)
                {
                    int index = drainage.Order[rank];
                    if (!wet[index])
                        continue;
                    runoffAt[index] += drainage.Runoff[index];
                    int next = drainage.Downstream[index];
                    if (next >= 0)
                        runoffAt[next] -= drainage.Runoff[index];
                }
                // Move each node's lateral inflow once. The parent catchment
                // retains its original total even when two local valleys join earlier.
                for (int rank = drainage.Count - 1; rank >= 0; rank--)
                {
                    int index = drainage.Order[rank], next = receivers[index];
                    if (wet[index] && next >= 0)
                        runoffAt[next] += runoffAt[index];
                }
                Array.Fill(inlet, -1);
                for (int rank = drainage.Count - 1; rank >= 0; rank--)
                {
                    int index = drainage.Order[rank], next = receivers[index];
                    if (!wet[index] || next < 0)
                        continue;
                    if (inlet[index] < 0 && runoffAt[index] < MinimumRunoff * 0.87f)
                    {
                        wet[index] = false;
                        continue;
                    }
                    int previous = inlet[next];
                    if (previous < 0 || runoffAt[index] > runoffAt[previous])
                        inlet[next] = index;
                }
            }

            var channel = new List<ChannelNode>();
            var nodeAt = new int[count];
            for (int rank = 0; rank < drainage.Count; rank++)
            {
                int index = drainage.Order[rank], next = receivers[index];
                if (!wet[index] || (next < 0 && inlet[index] < 0))
                    continue;
                Vector2 point = centers[index], direction = Direction(index);
                // Runoff is a depth per coarse node. Convert it to the reference
                // catchment area before using it to size a wet cross-section.
                float runoff = runoffAt[index] * (step / 250f) * (step / 250f);
                float scale = MathF.Sqrt(runoff);
                bool tapered = inlet[index] < 0 || (next == RegionalDrainage.Basin && !hasLake);
                float width = tapered ? 0f : Math.Clamp(1.45f * MathF.Pow(MathF.Max(0f, runoff - 18f), 0.63f), 9f, 96f);
                float depth = tapered ? 0f : MathF.Min(MaximumDepth, 1.15f + 0.16f * scale);
                float outletLevel = next == RegionalDrainage.Basin && hasLake ? drainage.Filled[index] : 0f;
                int receiver = -1;
                if (next >= 0)
                {
                    receiver = nodeAt[next];
                    ChannelNode end = channel[receiver];
                    outletLevel = end.OutletLevel;
                    Vector2 axis = end.Center - point;
                    float length = axis.Length();
                    int divisions = Math.Max(1, (int)MathF.Ceiling(length / 64f));
                    Vector2 startTangent = direction * length - axis;
                    Vector2 endTangent = Direction(next) * length - axis;
                    for (int part = divisions - 1; part > 0; part--)
                    {
                        float t = part / (float)divisions;
                        Vector2 center = point + axis * t + t * (1f - t) * ((1f - t) * startTangent - t * endTangent);
                        center = FollowValley(center, axis / length);
                        receiver = AddNode(center, receiver, outletLevel, Mix(width, end.Width, t), Mix(depth, end.Depth, t),
                            fixedLevel: false);
                    }
                }

                nodeAt[index] = AddNode(point, receiver, outletLevel, width, depth,
                    fixedLevel: next == RegionalDrainage.Ocean || next == RegionalDrainage.Basin && hasLake);
            }

            var primaryInlet = new int[channel.Count];
            Array.Fill(primaryInlet, -1);
            for (int i = 0; i < channel.Count; i++)
            {
                ChannelNode node = channel[i];
                if (node.Receiver < 0)
                    continue;
                int primary = primaryInlet[node.Receiver];
                if (primary < 0 || node.Width > channel[primary].Width)
                    primaryInlet[node.Receiver] = i;
            }
            for (int i = 0; i < channel.Count; i++)
            {
                ChannelNode node = channel[i];
                Vector2 incoming = primaryInlet[i] < 0 ? Vector2.Zero : node.Center - channel[primaryInlet[i]].Center;
                Vector2 outgoing = node.Receiver < 0 ? Vector2.Zero : channel[node.Receiver].Center - node.Center;
                node.Tangent = CenterlineTangent(incoming, outgoing);
                if (!node.FixedLevel && node.Width > 0f)
                {
                    // A steep reach carries its discharge in a narrower section.
                    // Use the fall across the adjoining stations to avoid abrupt width jumps.
                    float length = incoming.Length() + outgoing.Length();
                    float high = primaryInlet[i] < 0 ? node.Ground : channel[primaryInlet[i]].Ground;
                    float low = node.Receiver < 0 ? node.Ground : channel[node.Receiver].Ground;
                    float slope = MathF.Max(0f, high - low) / MathF.Max(1f, length);
                    node.Width = MathF.Max(9f, node.Width / MathF.Sqrt(1f + 4f * slope));
                    float ceiling = BankCeiling(node.Center, node.Tangent, node.Width, node.Level);
                    node.Level = MathF.Max(node.OutletLevel, MathF.Min(node.Level, ceiling));
                }
                channel[i] = node;
            }

            // A low bank can occur between stations. Both endpoints constrain the
            // intervening section, so lowering one must also preserve the other joins.
            for (int i = channel.Count - 1; i >= 0; i--)
            {
                ChannelNode node = channel[i];
                if (node.Receiver < 0 || node.FixedLevel || node.Width == 0f)
                    continue;
                ChannelNode receiver = channel[node.Receiver];
                if (receiver.FixedLevel || receiver.Width == 0f)
                    continue;
                var (start, end) = BankTangents(node, receiver);
                float drop = 0f;
                for (int part = 1; part <= 3; part++)
                {
                    float t = part * 0.25f;
                    Vector2 middle = Vector2.Lerp(node.Center, receiver.Center, t);
                    Vector2 tangent = Unit(Vector2.Lerp(start, end, t));
                    float level = Mix(node.Level, receiver.Level, t);
                    float width = Mix(node.Width, receiver.Width, t);
                    drop = MathF.Max(drop, level - BankCeiling(middle, tangent, width, level));
                }
                if (drop <= 0f)
                    continue;
                node.Level = MathF.Max(node.OutletLevel, node.Level - drop);
                receiver.Level = MathF.Max(receiver.OutletLevel, receiver.Level - drop);
                channel[i] = node;
                channel[node.Receiver] = receiver;
            }

            // Give each route an incision budget. If draining a low spot would require
            // cutting through a ridge, keep only the connected downstream channels.
            float maximumIncision = step * 0.35f;
            var minimumGrade = new float[channel.Count];
            var connected = new bool[channel.Count];
            for (int i = 0; i < channel.Count; i++)
            {
                ChannelNode node = channel[i];
                if (node.Receiver < 0)
                {
                    connected[i] = true;
                    minimumGrade[i] = node.OutletLevel;
                    continue;
                }
                minimumGrade[i] = MathF.Max(minimumGrade[node.Receiver], node.Ground + node.Depth - maximumIncision);
                connected[i] = connected[node.Receiver] && node.Level >= minimumGrade[i];
            }

            // Route the grade downstream without using a sill to raise the surface
            // above the upstream valley. All branches use the final shared section.
            var incomingCount = new int[channel.Count];
            for (int i = channel.Count - 1; i >= 0; i--)
            {
                ChannelNode node = channel[i];
                if (!connected[i] || node.Receiver < 0)
                    continue;
                incomingCount[node.Receiver]++;
                ChannelNode receiver = channel[node.Receiver];
                float fall = Vector2.Distance(node.Center, receiver.Center) * 0.001f;
                float limit = MathF.Max(minimumGrade[node.Receiver], node.Level - fall);
                if (receiver.Level > limit)
                {
                    receiver.Level = limit;
                    channel[node.Receiver] = receiver;
                }
            }

            SmoothCenterline(channel, primaryInlet, connected, incomingCount, step, maximumIncision, heightAt, BankCeiling);

            var sections = new RiverSection[channel.Count];
            for (int i = 0; i < channel.Count; i++)
            {
                ChannelNode node = channel[i];
                float width = incomingCount[i] > 0 ? node.Width : 0f;
                float left = BankDistance(node, width, 1), right = BankDistance(node, width, -1);
                sections[i] = new RiverSection(node.Center, node.Ground, node.Level, width,
                    width > 0f ? node.Depth : 0f, node.Tangent, left, right);
            }

            var result = new List<RiverReach>(channel.Count);
            for (int i = 0; i < channel.Count; i++)
            {
                ChannelNode node = channel[i];
                if (!connected[i] || node.Receiver < 0)
                    continue;
                ChannelNode receiver = channel[node.Receiver];
                Vector2 approach = Unit(receiver.Center - node.Center);
                // A steeply angled tributary needs the common pool to cover the
                // wedge between its end plane and the receiving reach.
                bool junctionCap = incomingCount[node.Receiver] > 1 && Vector2.Dot(approach, receiver.Tangent) < 0.5f;
                result.Add(new RiverReach(sections[i], sections[node.Receiver], receiver.Receiver < 0 || junctionCap));
            }
            return result.ToArray();

            Vector2 Position(int index) => new(index % side * step - half, index / side * step - half);

            Vector2 Direction(int index)
            {
                int prev = inlet[index], next = receivers[index];
                Vector2 center = Position(index);
                Vector2 from = prev >= 0 ? Position(prev) : center;
                Vector2 to = next >= 0 ? Position(next) : center;
                Vector2 span = to - from;
                return Unit(span);
            }

            int FindReceiver(int index, int next)
            {
                Vector2 point = centers[index], original = centers[next] - point;
                float length = original.Length();
                if (length == 0f)
                    return next;
                Vector2 direction = original / length;
                int targetX = next % side, targetZ = next / side;
                float height = Elevation(point), best = float.PositiveInfinity;
                int receiver = next;
                for (int z = Math.Max(0, targetZ - 1); z <= Math.Min(side - 1, targetZ + 1); z++)
                for (int x = Math.Max(0, targetX - 1); x <= Math.Min(side - 1, targetX + 1); x++)
                {
                    int candidate = x + z * side;
                    if (candidate == index || candidate == next || outlets[candidate] != outlets[next]
                        || receivers[candidate] < 0 || drainage.Runoff[candidate] < drainage.Runoff[next] * 1.20f)
                        continue;
                    Vector2 span = centers[candidate] - point;
                    float distance = span.Length();
                    if (distance > length + step * 0.24f || distance < 24f
                        || Vector2.Dot(span, direction) < distance * 0.55f)
                        continue;
                    float end = Elevation(centers[candidate]);
                    if (end >= height || end > Elevation(centers[next]) + 2f)
                        continue;
                    // A join across a low valley is valid only if the intervening land
                    // stays close to the fall between both channels.
                    float rise = float.NegativeInfinity;
                    for (int part = 1; part < 6; part++)
                    {
                        float t = part / 6f;
                        rise = MathF.Max(rise, Elevation(point + span * t) - Mix(height, end, t));
                    }
                    float cost = rise + distance * 0.015f;
                    if (rise <= 2f && cost < best)
                    {
                        receiver = candidate;
                        best = cost;
                    }
                }
                return receiver;
            }

            bool InValley(int index, Vector2 direction)
            {
                Vector2 p = Position(index);
                Vector2 across = new Vector2(-direction.Y, direction.X) * (step * 0.8f);
                // The midpoint curvature cancels the cross-slope of a tilted plane.
                float left = SampleDrainageHeight(p.X - across.X, p.Y - across.Y);
                float right = SampleDrainageHeight(p.X + across.X, p.Y + across.Y);
                return (left + right) * 0.5f - drainage.Ground[index] >= step * 0.006f;
            }

            Vector2 FollowValley(Vector2 point, Vector2 direction)
            {
                if (direction == Vector2.Zero)
                    return point;
                float offset = step * 0.11f;
                Vector2 across = new Vector2(-direction.Y, direction.X) * offset;
                Vector2 best = point;
                float lowest = heightAt(point.X, point.Y) + offset * 0.18f;
                float left = Elevation(point - across * 5f);
                float middle = Elevation(point - across * 4f);
                for (int i = -4; i <= 4; i++)
                {
                    float right = Elevation(point + across * (i + 1));
                    float score = middle + offset * (0.04f * Math.Abs(i) + 0.025f * i * i);
                    if (middle <= left && middle <= right && score < lowest)
                    {
                        lowest = score;
                        // Fit the bottom between the sampled offsets when the valley has curvature.
                        float curvature = left + right - 2f * middle;
                        float delta = curvature > 0.01f ? 0.5f * (left - right) / curvature : 0f;
                        best = point + across * (i + delta);
                    }
                    left = middle;
                    middle = right;
                }
                return best;
            }

            float Elevation(Vector2 point) => heightAt(point.X, point.Y);

            float BankCeiling(Vector2 center, Vector2 tangent, float width, float level)
            {
                Vector2 normal = new(-tangent.Y, tangent.X);
                return MathF.Min(BankHeight(normal), BankHeight(-normal)) - 0.5f;

                float BankHeight(Vector2 lateral)
                {
                    float highest = float.NegativeInfinity;
                    for (float distance = width; distance <= width + 40f; distance += 8f)
                    {
                        highest = MathF.Max(highest, Elevation(center + lateral * distance));
                        if (highest >= level + 0.5f)
                            break;
                    }
                    return highest;
                }
            }

            float BankDistance(ChannelNode node, float width, int sideSign)
            {
                if (width == 0f || node.FixedLevel)
                    return 0f;
                Vector2 lateral = new Vector2(-node.Tangent.Y, node.Tangent.X) * sideSign;
                float previous = Elevation(node.Center + lateral * width);
                if (previous >= node.Level)
                    return width;
                for (float distance = width + 8f; distance <= width + 40f; distance += 8f)
                {
                    float ground = Elevation(node.Center + lateral * distance);
                    if (ground >= node.Level)
                        return distance - 8f * (ground - node.Level) / (ground - previous);
                    previous = ground;
                }
                return width + 40f;
            }

            int AddNode(Vector2 point, int receiver, float outlet, float width, float depth, bool fixedLevel)
            {
                float ground = heightAt(point.X, point.Y);
                float level = fixedLevel ? outlet : MathF.Max(outlet, ground - depth * 0.6f);
                channel.Add(new ChannelNode(point, receiver, ground, level, outlet, width, depth, fixedLevel));
                return channel.Count - 1;
            }

            float SampleDrainageHeight(float x, float z)
            {
                x = Math.Clamp((x + half) / step, 0f, side - 1f);
                z = Math.Clamp((z + half) / step, 0f, side - 1f);
                int ix = Math.Min((int)x, side - 2), iz = Math.Min((int)z, side - 2);
                int a = ix + iz * side;
                return Mix(Mix(drainage.Ground[a], drainage.Ground[a + 1], x - ix),
                    Mix(drainage.Ground[a + side], drainage.Ground[a + side + 1], x - ix), z - iz);
            }
        }

        private static void SmoothCenterline(List<ChannelNode> channel, int[] inlet, bool[] connected, int[] incomingCount,
            int step, float maximumIncision, Func<float, float, float> elevation,
            Func<Vector2, Vector2, float, float, float> bankCeiling)
        {
            // Low points found on individual cross sections can alternate between
            // sides of the valley. Fit the alignment to the connected grade.
            if (channel.Count < 3)
                return;
            const float cosineOfTwentyDegrees = 0.939693f;
            float maxShift = step * 0.30f;
            ChannelNode[] original = channel.ToArray();
            // Several sections can share the valley at a junction. Keep that
            // approach stable while smoothing the single-channel reaches.
            var junctionDistance = new float[channel.Count];
            Array.Fill(junctionDistance, float.PositiveInfinity);
            var nearby = new Dictionary<(int X, int Z), List<int>>();
            for (int i = 0; i < original.Length; i++)
            {
                int next = original[i].Receiver;
                if (incomingCount[i] > 1)
                    junctionDistance[i] = 0f;
                if (next < 0 || !connected[i])
                    continue;
                if (incomingCount[i] <= 1 && junctionDistance[next] < step)
                    junctionDistance[i] = junctionDistance[next] + Vector2.Distance(original[i].Center, original[next].Center);
                var (minX, minZ, maxX, maxZ) = BucketRange(original[i].Center, original[next].Center, maxShift);
                for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    var key = (x, z);
                    if (!nearby.TryGetValue(key, out var edges))
                        nearby.Add(key, edges = new List<int>());
                    edges.Add(i);
                }
            }
            for (int i = original.Length - 1; i >= 0; i--)
            {
                int next = original[i].Receiver;
                if (connected[i] && next >= 0 && junctionDistance[i] < step)
                    junctionDistance[next] = MathF.Min(junctionDistance[next], junctionDistance[i]
                        + Vector2.Distance(original[i].Center, original[next].Center));
            }

            for (int sweep = 0; sweep < 2; sweep++)
            for (int offset = 0; offset < channel.Count; offset++)
            {
                int i = sweep == 0 ? offset : channel.Count - 1 - offset;
                ChannelNode node = channel[i];
                int previous = inlet[i], next = node.Receiver;
                if (previous < 0 || next < 0 || !connected[i] || !connected[previous] || incomingCount[i] != 1
                    || junctionDistance[i] < step || incomingCount[previous] > 1 || incomingCount[next] > 1)
                    continue;

                ChannelNode upstream = channel[previous], downstream = channel[next];
                Vector2 start = upstream.Center, end = downstream.Center;
                Vector2 before = node.Center - start, after = end - node.Center;
                float a = before.Length(), b = after.Length();
                if (a < 1f || b < 1f || Vector2.DistanceSquared(start, end) < 1f
                    || Vector2.Dot(before, after) >= cosineOfTwentyDegrees * a * b)
                    continue;
                Vector2 target = Vector2.Lerp(start, end, a / (a + b));
                ChannelNode reference = original[i];
                float tolerance = MathF.Min(2f, node.Depth * 0.65f);
                for (float fraction = 0.8f; fraction >= 0.2f; fraction *= 0.5f)
                {
                    Vector2 candidate = Vector2.Lerp(node.Center, target, fraction);
                    if (Vector2.DistanceSquared(candidate, reference.Center) > maxShift * maxShift
                        || Vector2.DistanceSquared(candidate, start) < 1f || Vector2.DistanceSquared(candidate, end) < 1f)
                        continue;
                    float ground = elevation(candidate.X, candidate.Y);
                    if (MathF.Abs(ground - reference.Ground) > tolerance
                        || !WithinCutLimit(ground, reference.Ground, reference.Level - reference.Depth, tolerance)
                        || !FitsTerrain(start, candidate, original[previous], reference, tolerance)
                        || !FitsTerrain(candidate, end, reference, original[next], tolerance)
                        || AddsCrossing(previous, i, start, candidate) || AddsCrossing(i, next, candidate, end))
                        continue;

                    ChannelNode moved = node;
                    moved.Center = candidate;
                    moved.Ground = ground;
                    channel[i] = moved;
                    UpdateTangent(previous);
                    UpdateTangent(i);
                    UpdateTangent(next);
                    if (KeepsBanks(previous) && KeepsBanks(i) && KeepsBanks(next)
                        && KeepsReachBanks(inlet[previous], 2) && KeepsReachBanks(previous, 1)
                        && KeepsReachBanks(i, 1) && KeepsReachBanks(next, 2))
                        break;

                    channel[previous] = upstream;
                    channel[i] = node;
                    channel[next] = downstream;
                }
            }

            bool FitsTerrain(Vector2 start, Vector2 end, ChannelNode from, ChannelNode to, float tolerance)
            {
                int intervals = Math.Max(2, (int)MathF.Ceiling(Vector2.Distance(start, end) / 16f));
                for (int part = 1; part < intervals; part++)
                {
                    float t = part / (float)intervals;
                    Vector2 point = Vector2.Lerp(start, end, t), reference = Vector2.Lerp(from.Center, to.Center, t);
                    float bed = Mix(from.Level - from.Depth, to.Level - to.Depth, t);
                    if (!WithinCutLimit(elevation(point.X, point.Y), elevation(reference.X, reference.Y), bed, tolerance))
                        return false;
                }
                return true;
            }

            bool WithinCutLimit(float height, float reference, float bed, float tolerance) =>
                height <= MathF.Min(reference + tolerance, MathF.Max(reference, bed + maximumIncision));

            void UpdateTangent(int i)
            {
                ChannelNode node = channel[i];
                Vector2 incoming = inlet[i] < 0 ? Vector2.Zero : node.Center - channel[inlet[i]].Center;
                Vector2 outgoing = node.Receiver < 0 ? Vector2.Zero : channel[node.Receiver].Center - node.Center;
                node.Tangent = CenterlineTangent(incoming, outgoing);
                channel[i] = node;
            }

            bool KeepsBanks(int i)
            {
                ChannelNode node = channel[i], reference = original[i];
                return node.FixedLevel || node.Width <= 0f || node.Level <= 0f
                    || SupportsLevel(node.Center, node.Tangent, reference.Center, reference.Tangent, node.Width, node.Level);
            }

            bool KeepsReachBanks(int i, int sampleStride)
            {
                if (i < 0 || !connected[i] || channel[i].Receiver < 0)
                    return true;
                int next = channel[i].Receiver;
                ChannelNode a = channel[i], b = channel[next], oldA = original[i], oldB = original[next];
                if (b.FixedLevel || a.Width <= 0f || b.Width <= 0f)
                    return true;
                var (start, end) = BankTangents(a, b);
                var (oldStart, oldEnd) = BankTangents(oldA, oldB);
                for (int part = sampleStride; part < 4; part += sampleStride)
                {
                    float t = part * 0.25f;
                    float level = Mix(a.Level, b.Level, t);
                    if (level > 0f && !SupportsLevel(Vector2.Lerp(a.Center, b.Center, t), Unit(Vector2.Lerp(start, end, t)),
                        Vector2.Lerp(oldA.Center, oldB.Center, t), Unit(Vector2.Lerp(oldStart, oldEnd, t)),
                        Mix(a.Width, b.Width, t), level))
                        return false;
                }
                return true;
            }

            bool SupportsLevel(Vector2 point, Vector2 tangent, Vector2 reference, Vector2 oldTangent, float width, float level)
            {
                float ceiling = bankCeiling(point, tangent, width, level);
                return ceiling >= level - 0.1f || ceiling >= bankCeiling(reference, oldTangent, width, level) - 0.1f;
            }

            bool AddsCrossing(int from, int to, Vector2 start, Vector2 end)
            {
                var (minX, minZ, maxX, maxZ) = BucketRange(start, end);
                Vector2 oldStart = channel[from].Center, oldEnd = channel[to].Center;
                for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    if (!nearby.TryGetValue((x, z), out var edges))
                        continue;
                    foreach (int other in edges)
                    {
                        int receiver = channel[other].Receiver;
                        if (other == from || other == to || receiver == from || receiver == to)
                            continue;
                        Vector2 a = channel[other].Center, b = channel[receiver].Center;
                        if (Intersects(start, end, a, b) && !Intersects(oldStart, oldEnd, a, b))
                            return true;
                    }
                }
                return false;
            }

            static (int, int, int, int) BucketRange(Vector2 a, Vector2 b, float margin = 0f)
            {
                Vector2 min = Vector2.Min(a, b) - new Vector2(margin), max = Vector2.Max(a, b) + new Vector2(margin);
                return (BucketAt(min.X), BucketAt(min.Y), BucketAt(max.X), BucketAt(max.Y));

                static int BucketAt(float value) => (int)MathF.Floor(value / BucketWidth);
            }

            static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                Vector2 low = Vector2.Max(Vector2.Min(a, b), Vector2.Min(c, d));
                Vector2 high = Vector2.Min(Vector2.Max(a, b), Vector2.Max(c, d));
                return low.X <= high.X && low.Y <= high.Y && Straddles(a, b, c, d) && Straddles(c, d, a, b);

                static bool Straddles(Vector2 start, Vector2 end, Vector2 p, Vector2 q)
                {
                    Vector2 axis = end - start, u = p - start, v = q - start;
                    float one = axis.X * u.Y - axis.Y * u.X, two = axis.X * v.Y - axis.Y * v.X;
                    return MathF.Min(one, two) <= 0f && MathF.Max(one, two) >= 0f;
                }
            }
        }

        private static (Vector2 Start, Vector2 End) BankTangents(ChannelNode a, ChannelNode b)
        {
            Vector2 axis = Unit(b.Center - a.Center);
            Vector2 start = RiverReach.SectionTangent(a.Tangent, axis), end = RiverReach.SectionTangent(b.Tangent, axis);
            return (start * Vector2.Dot(axis, end), end * Vector2.Dot(axis, start));
        }

        private struct ChannelNode
        {
            internal readonly int Receiver;
            internal readonly float OutletLevel, Depth;
            internal readonly bool FixedLevel;
            internal Vector2 Center, Tangent;
            internal float Ground, Level, Width;

            internal ChannelNode(Vector2 center, int receiver, float ground, float level, float outletLevel,
                float width, float depth, bool fixedLevel)
            {
                Center = center;
                Receiver = receiver;
                Ground = ground;
                Level = level;
                OutletLevel = outletLevel;
                Width = width;
                Depth = depth;
                FixedLevel = fixedLevel;
                Tangent = Vector2.Zero;
            }
        }

        private static Vector2 CenterlineTangent(Vector2 incoming, Vector2 outgoing)
        {
            Vector2 tangent = Unit(Unit(incoming) + Unit(outgoing));
            return tangent == Vector2.Zero ? Unit(outgoing) : tangent;
        }

        private static Vector2 Unit(Vector2 direction) =>
            direction.LengthSquared() > 0f ? Vector2.Normalize(direction) : Vector2.Zero;
    }
}
