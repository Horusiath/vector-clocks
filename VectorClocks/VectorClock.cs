﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace VectorClocks
{
    public readonly struct VectorClock : IPartiallyComparable<VectorClock>, IEquatable<VectorClock>,
        IEnumerable<KeyValuePair<string, long>>
    {
        public static readonly VectorClock Empty =
            new VectorClock(0, Array.Empty<int>(), Array.Empty<long>(), Array.Empty<string>());

        //TODO: use length-based choice to pick vecorization for lookups instead of binary search.
        // Should be faster at least for small lengths.
        private readonly int length;

        //TODO: turn nodeHashes and values to pointers - potentially stackalloc for small number of nodes?
        private readonly int[] nodeHashes;
        private readonly long[] values;

        private readonly string[] nodes;

        private VectorClock(int length, int[] nodeHashes, long[] values, string[] nodes)
        {
            this.length = length;
            this.nodeHashes = nodeHashes;
            this.values = values;
            this.nodes = nodes;
        }

        public VectorClock(ImmutableSortedDictionary<string, long> nodes)
        {
            this.length = nodes.Count;
            this.nodeHashes = new int[this.length];
            this.values = new long[this.length];
            this.nodes = new string[this.length];
            var i = 0;
            foreach (var node in nodes)
            {
                this.nodeHashes[i] = node.Key.GetHashCode();
                this.nodes[i] = node.Key.ToString();
                this.values[i] = node.Value;
                i++;
            }
        }

        public int Count => length;

        public IEnumerable<string> Nodes => nodes;

        public IEnumerable<long> Values => values;

        public long this[string node] => TryGetValue(node, out var value) ? value : 0;

        public bool TryGetValue(string node, out long value)
        {
            var idx = Array.BinarySearch(nodes, node);
            if (idx >= 0)
            {
                value = values[idx];
                return true;
            }

            if (idx >= 0)
            {
                if (nodes[idx] == node)
                {
                    value = values[idx];
                    return true;
                }
            }

            value = default;
            return false;
        }

        public bool ContainsNode(string node)
        {
            return TryGetValue(node, out _);
        }

        public VectorClock Prune(string node)
        {
            var idx = Array.BinarySearch(nodes, node);
            if (idx >= 0)
            {
                var newLength = length - 1;
                var newNodeHashes = RemoveAt(nodeHashes, newLength, idx);
                var newValues = RemoveAt(values, newLength, idx);
                var newNodes = RemoveAt(nodes, newLength, idx);
                
                return new VectorClock(newLength, newNodeHashes, newValues, newNodes);
            }
            else return this;
        }

        private static T[] RemoveAt<T>(T[] source, int length, int index)
        {
            var dest = new T[length];
            if (index > 0)
                Array.Copy(source, dest, index);

            if (index < length)
                Array.Copy(source, index, dest, index + 1, length - index);

            return dest;
        }

        private static T[] InsertAt<T>(T[] source, int length, T element, int index)
        {
            var dest = new T[length];
            if (index > 0)
                Array.Copy(source, dest, index);

            dest[index] = element;

            if (index < length)
                Array.Copy(source, index + 1, dest, index, length - index - 1);

            return dest;
        }

        private bool HaveSameNodes(VectorClock other)
        {
            if (length != other.length) return false;

            // check hashes of nodes - since nodes are stored in sorted fashion
            // this is semi-guarantee that they are equal
            ReadOnlySpan<int> xs = nodeHashes;
            ReadOnlySpan<int> ys = other.nodeHashes;
            if (!xs.SequenceEqual(ys)) return false;

            for (int i = 0; i < length; i++)
            {
                if (nodes[i] != other.nodes[i]) return false;
            }

            return true;
        }

        public VectorClock Merge(in VectorClock other)
        {
            if (ReferenceEquals(nodeHashes, other.nodeHashes)
                && ReferenceEquals(nodes, other.nodes)
                && ReferenceEquals(values, other.values)) return this;

            if (HaveSameNodes(other))
            {
                return FastMerge(other);
            }
            else
            {
                var resultHashes = new int[length * 2];
                var resultValues = new long[length * 2];
                var resultNodes = new string[length * 2];
                int i = 0, j = 0, k = 0;

                var a = nodes[i];
                var b = other.nodes[j];
                do
                {
                    var comparisonResult = string.Compare(a, b, StringComparison.InvariantCultureIgnoreCase);
                    if (comparisonResult == 0)
                    {
                        resultHashes[k] = nodeHashes[i];
                        resultNodes[k] = nodes[i];
                        resultValues[k] = values[i];
                        i++;
                        j++;
                        k++;
                    }
                    else if (comparisonResult < 0)
                    {
                        resultHashes[k] = nodeHashes[i];
                        resultNodes[k] = nodes[i];
                        resultValues[k] = values[i];
                        i++;
                        k++;
                    }
                    else
                    {
                        resultHashes[k] = nodeHashes[j];
                        resultNodes[k] = nodes[j];
                        resultValues[k] = values[j];
                        j++;
                        k++;
                    }

                } while (i < length || j < length);

                if (k == length * 2)
                {
                    return new VectorClock(k, resultHashes, resultValues, resultNodes);
                }
                else
                {
                    var x = new int[k];
                    resultHashes.CopyTo(x, 0);

                    var y = new long[k];
                    resultValues.CopyTo(y, 0);
                    var z = new string[k];
                    resultNodes.CopyTo(z, 0);

                    return new VectorClock(k, x, y, z);
                }
            }
        }

        private unsafe VectorClock FastMerge(in VectorClock other)
        {
            var others = other.values;
            var resultValues = new long[length];

            var i = 0;
            if (Vector.IsHardwareAccelerated && length >= Vector<long>.Count)
            {
                var nLength = length - Vector<long>.Count;
                do
                {
                    var a = new Vector<long>(values, i);
                    var b = new Vector<long>(others, i);
                    var c = Vector.Max(a, b);
                    c.CopyTo(resultValues, i);

                    i += Vector<long>.Count;
                } while (nLength >= i);
            }

            while (i < length)
            {
                resultValues[i] = Math.Max(values[i], others[i]);
                i++;
            }

            return new VectorClock(length, nodeHashes, resultValues, nodes);
        }

        public int? PartiallyCompareTo(VectorClock other)
        {
            if (ReferenceEquals(nodeHashes, other.nodeHashes)
                && ReferenceEquals(nodes, other.nodes)
                && ReferenceEquals(values, other.values)) return 0;

            // empty vector clock comparisons
            if (length == 0)
            {
                // if both clock are empty, they are equal,
                // if other was not empty, current is always less then
                return other.length == 0 ? 0 : -1;
            }
            else if (other.length == 0)
            {
                // this current clock is not-empty, but other was
                // current is always greater
                return 1;
            }

            if (HaveSameNodes(other))
            {
                return FastCompareTo(other);
            }
            else
            {
                return ExactCompareTo(other);
            }
        }

        private int? ExactCompareTo(in VectorClock other)
        {
            bool isGraterOrEqual = false, isLessOrEqual = false;
            int i = 0, j = 0, prevCmp = 0;
            do
            {
                var a = nodes[i];
                var b = other.nodes[j];

                var cmp = string.Compare(a, b, StringComparison.InvariantCulture);

                if (cmp == 0)
                {
                    var x = values[i].CompareTo(other.values[j]);
                    if (CompareValues(x, ref isLessOrEqual, ref isGraterOrEqual)) return null;

                    i++;
                    j++;
                }
                else if (cmp > 0)
                {
                    if (prevCmp < 0)
                    {
                        // current i-key is greater than j-key, but previously it was lesser
                        // it means, that key doesn't exists in second clock, so it's greater
                        isGraterOrEqual = true;
                    }

                    j++;
                }
                else // cmp < 0
                {
                    if (prevCmp > 0)
                    {
                        isLessOrEqual = true;
                    }

                    i++;
                }

                prevCmp = cmp;

            } while (i < length || j < other.length);

            if (i == length) // reached the end of this clock
            {
                if (length > other.length) // this is longer than other
                {
                    if (isGraterOrEqual) return 1;  // it was greater all the time
                    else return null;               // if it was not greater, it was not equal anyway,
                                                    //   because of different vector lengths, so it
                                                    //   was less at least once, so in summary it's concurrent
                }
                else // other is longer
                {
                    if (isLessOrEqual) return -1;   // reasoning similar as one above
                    else return null;
                }
            }
            else if (j == length) // reached the end of other clock but not this one
            {
                if (isLessOrEqual) return null;     // we haven't read entire current clock, so it will
                                                    //   be greater as some point, so if it was less once
                                                    //   it's actually concurrent
                else return 1;
            }
            else
            {
                // both clocks reached their ends - the result is ultimate
                if (isGraterOrEqual) return 1;
                else return -1;                 // implicit isLessOrEqual
            }
        }

        private static bool CompareValues(int x, ref bool isLessOrEqual, ref bool isGraterOrEqual)
        {
            if (x >= 0)
            {
                if (isLessOrEqual)
                {
                    // current value is greater, but it was less once -> we have concurrency
                    return true;
                }
                else
                {
                    isGraterOrEqual = true;
                }
            }
            else if (x <= 0)
            {
                if (isGraterOrEqual)
                {
                    // current value is lesser, but it was greater once -> we have concurrency
                    return true;
                }
                else
                {
                    isLessOrEqual = true;
                }
            }

            return false;
        }

        private int? FastCompareTo(in VectorClock other)
        {
            var others = other.values;
            var i = 0;
            bool isGraterOrEqual = false, isLessOrEqual = false, isEqual = true;
            if (Vector.IsHardwareAccelerated && length >= Vector<long>.Count)
            {
                var nLength = length - Vector<long>.Count;
                do
                {
                    var a = new Vector<long>(values, i);
                    var b = new Vector<long>(others, i);

                    isGraterOrEqual |= Vector.GreaterThanOrEqualAll(a, b);
                    isLessOrEqual |= Vector.GreaterThanOrEqualAll(a, b);
                    isEqual &= Vector.EqualsAll(a, b);

                    // if we had oposing values at least once, clocks are concurrent
                    if (isGraterOrEqual && isLessOrEqual) return null;

                    i += Vector<long>.Count;
                } while (nLength >= i);
            }

            while (i < length)
            {
                var a = values[i];
                var b = others[i];

                isGraterOrEqual |= (a >= b);
                isLessOrEqual |= (a <= b);
                isEqual &= (a == b);

                // if we had oposing values at least once, clocks are concurrent
                if (isGraterOrEqual && isLessOrEqual) return null;

                i++;
            }

            // at this point, we already covered all concurrent cases
            if (isEqual) return 0;          // both clocks were equal for entire time
            else if (isGraterOrEqual) return 1;
            else return -1;                     // implicitly this must be isLessOrEqual
        }

        public VectorClock Increment(string node)
        {
            int lo = 0, hi = length - 1;
            int i = 0;
            while (lo <= hi)
            {
                i = (int)(((uint)hi + (uint)lo) >> 1);

                int c = string.Compare(node, nodes[i], StringComparison.InvariantCulture);
                if (c == 0)
                {
                    var newValues = new long[length];
                    Array.Copy(this.values, newValues, length);
                    newValues[i]++;

                    return new VectorClock(length, nodeHashes, newValues, nodes);
                }
                else if (c > 0)
                {
                    lo = i + 1;
                }
                else
                {
                    hi = i - 1;
                }
            }

            if (i < lo)
            {
                // add new element after i
                var newLength = length + 1;
                var newNodeHashes = InsertAt(nodeHashes, newLength, node.GetHashCode(), i);
                var newNodes = InsertAt(nodes, newLength, node, i);
                var newValues = InsertAt(values, newLength, 1, i);

                return new VectorClock(newLength, newNodeHashes, newValues, newNodes);
            }
            else
            {
                // add new element before i
                var newLength = length + 1;
                var newNodeHashes = InsertAt(nodeHashes, newLength, node.GetHashCode(), hi);
                var newNodes = InsertAt(nodes, newLength, node, hi);
                var newValues = InsertAt(values, newLength, 1, hi);

                return new VectorClock(newLength, newNodeHashes, newValues, newNodes);
            }
        }

        public bool IsConcurrentWith(in VectorClock other)
        {
            if (ReferenceEquals(nodeHashes, other.nodeHashes)
                && ReferenceEquals(nodes, other.nodes)
                && ReferenceEquals(values, other.values)) return false;

            if (HaveSameNodes(other))
            {
                return FastIsConcurrentWith(other);
            }
            else
            {
                // btw. at this point equality is not an option.
                return !ExactCompareTo(other).HasValue;
            }
        }

        private bool FastIsConcurrentWith(in VectorClock other)
        {
            // FastIsBefore requires the same length on both clocks
            // empty clock cannot occur before itself
            if (length == 0) return false;

            var others = other.values;
            var i = 0;
            bool isGraterOrEqual = false, isLessOrEqual = false;
            if (Vector.IsHardwareAccelerated && length >= Vector<long>.Count)
            {
                var nLength = length - Vector<long>.Count;
                do
                {
                    var a = new Vector<long>(values, i);
                    var b = new Vector<long>(others, i);

                    isGraterOrEqual |= Vector.GreaterThanOrEqualAll(a, b);
                    isLessOrEqual |= Vector.GreaterThanOrEqualAll(a, b);

                    // if we had oposing values at least once, clocks are concurrent
                    if (isGraterOrEqual && isLessOrEqual) return true;

                    i += Vector<long>.Count;
                } while (nLength >= i);
            }

            while (i < length)
            {
                var a = values[i];
                var b = others[i];

                isGraterOrEqual |= (a >= b);
                isLessOrEqual |= (a <= b);

                // if we had oposing values at least once, clocks are concurrent
                if (isGraterOrEqual && isLessOrEqual) return true;

                i++;
            }

            return false;
        }

        public bool IsBefore(in VectorClock other)
        {
            if (ReferenceEquals(nodeHashes, other.nodeHashes)
                && ReferenceEquals(nodes, other.nodes)
                && ReferenceEquals(values, other.values)) return false;

            if (HaveSameNodes(other))
            {
                return FastIsBefore(other);
            }
            else
            {
                // btw. at this point equality is not an option.
                var x = ExactCompareTo(other);
                return x.HasValue && x.Value < 0;
            }
        }

        private bool FastIsBefore(in VectorClock other)
        {
            // FastIsBefore requires the same length on both clocks
            // empty clock cannot occur before itself
            if (length == 0) return false;

            var others = other.values;
            var i = 0;
            var isEqual = true;
            if (Vector.IsHardwareAccelerated && length >= Vector<long>.Count)
            {
                var nLength = length - Vector<long>.Count;
                do
                {
                    var a = new Vector<long>(values, i);
                    var b = new Vector<long>(others, i);

                    if (!Vector.LessThanOrEqualAll(a, b)) return false;
                    isEqual &= Vector.EqualsAll(a, b);

                    i += Vector<long>.Count;
                } while (nLength >= i);
            }

            while (i < length)
            {
                var a = values[i];
                var b = others[i];
                if (a > b) return false;
                isEqual &= (a == b); 
                i++;
            }

            return !isEqual; // if clocks are equal one cannot occur before another
        }


        public bool IsAfter(in VectorClock other)
        {
            if (ReferenceEquals(nodeHashes, other.nodeHashes)
                && ReferenceEquals(nodes, other.nodes)
                && ReferenceEquals(values, other.values)) return false;

            if (HaveSameNodes(other))
            {
                return FastIsAfter(other);
            }
            else
            {
                // btw. at this point equality is not an option.
                var x = ExactCompareTo(other);
                return x.HasValue && x.Value > 0;
            }
        }

        private bool FastIsAfter(in VectorClock other)
        {
            // FastIsBefore requires the same length on both clocks
            // empty clock cannot occur after itself
            if (length == 0) return false;

            var others = other.values;
            var i = 0;
            var isEqual = true;
            if (Vector.IsHardwareAccelerated && length >= Vector<long>.Count)
            {
                var nLength = length - Vector<long>.Count;
                do
                {
                    var a = new Vector<long>(values, i);
                    var b = new Vector<long>(others, i);

                    if (!Vector.GreaterThanOrEqualAll(a, b)) return false;
                    isEqual &= Vector.EqualsAll(a, b);

                    i += Vector<long>.Count;
                } while (nLength >= i);
            }

            while (i < length)
            {
                var a = values[i];
                var b = others[i];
                if (a < b) return false;
                isEqual &= (a == b);
                i++;
            }

            return !isEqual; // if clocks are equal one cannot occur after another
        }

        public bool IsSameAs(in VectorClock other)
        {
            if (length != other.length) return false;
            if (ReferenceEquals(nodeHashes, other.nodeHashes)
                && ReferenceEquals(nodes, other.nodes)
                && ReferenceEquals(values, other.values)) return true;

            if (!HaveSameNodes(other)) return false;

            // if hashes were equal this "probably" means that clock have equal corresponding nodes
            ReadOnlySpan<long> xv = values;
            ReadOnlySpan<long> yv = other.values;
            if (!xv.SequenceEqual(yv)) return false;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(VectorClock other) => IsSameAs(other);

        public IEnumerator<KeyValuePair<string, long>> GetEnumerator()
        {
            for (int i = 0; i < length; i++)
            {
                yield return new KeyValuePair<string, long>(nodes[i], values[i]);
            }
        }

        public override bool Equals(object obj)
        {
            return obj is VectorClock clock && IsSameAs(clock);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = length;
                for (int i = 0; i < length; i++)
                {
                    hashCode = (hashCode * 397) ^ this.nodeHashes[i] ^ this.values[i].GetHashCode();
                }
                return hashCode;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(VectorClock a, VectorClock b) => a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(VectorClock a, VectorClock b) => !(a == b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(VectorClock a, VectorClock b) => a.IsBefore(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(VectorClock a, VectorClock b) => a.IsAfter(b);
    }
}