/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// .NET 4.0 compatibility: the C# compiler needs these types to compile tuple syntax such as
// (string, int) or var (a, b) = ...; they only ship with .NET 4.7+ (or the System.ValueTuple
// package, which has no .NET 4.0 build). Only the arities this codebase uses are defined.
/*==========================================================*/

using System.Collections.Generic;

namespace System
{
    public struct ValueTuple<T1, T2> : IEquatable<ValueTuple<T1, T2>>
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        public bool Equals(ValueTuple<T1, T2> other)
        {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                && EqualityComparer<T2>.Default.Equals(Item2, other.Item2);
        }

        public override bool Equals(object obj)
        {
            return obj is ValueTuple<T1, T2> && Equals((ValueTuple<T1, T2>)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (EqualityComparer<T1>.Default.GetHashCode(Item1) * 397)
                    ^ EqualityComparer<T2>.Default.GetHashCode(Item2);
            }
        }

        public override string ToString()
        {
            return "(" + Item1 + ", " + Item2 + ")";
        }
    }

    public struct ValueTuple<T1, T2, T3> : IEquatable<ValueTuple<T1, T2, T3>>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }

        public bool Equals(ValueTuple<T1, T2, T3> other)
        {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                && EqualityComparer<T2>.Default.Equals(Item2, other.Item2)
                && EqualityComparer<T3>.Default.Equals(Item3, other.Item3);
        }

        public override bool Equals(object obj)
        {
            return obj is ValueTuple<T1, T2, T3> && Equals((ValueTuple<T1, T2, T3>)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = EqualityComparer<T1>.Default.GetHashCode(Item1);
                hash = (hash * 397) ^ EqualityComparer<T2>.Default.GetHashCode(Item2);
                return (hash * 397) ^ EqualityComparer<T3>.Default.GetHashCode(Item3);
            }
        }

        public override string ToString()
        {
            return "(" + Item1 + ", " + Item2 + ", " + Item3 + ")";
        }
    }
}

namespace System.Runtime.CompilerServices
{
    /// <summary>Lets the compiler record tuple element names (e.g. (string Name, int Id)) in signatures.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Event | AttributeTargets.Field | AttributeTargets.Parameter
        | AttributeTargets.Property | AttributeTargets.ReturnValue | AttributeTargets.Struct)]
    public sealed class TupleElementNamesAttribute : Attribute
    {
        private readonly string[] _transformNames;

        public TupleElementNamesAttribute(string[] transformNames)
        {
            _transformNames = transformNames;
        }

        public IList<string> TransformNames
        {
            get { return _transformNames; }
        }
    }
}
