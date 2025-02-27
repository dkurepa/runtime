﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System.Collections.Frozen
{
    /// <summary>
    /// Provides a set of initialization methods for instances of the <see cref="FrozenDictionary{TKey, TValue}"/> class.
    /// </summary>
    /// <remarks>
    /// Frozen collections are immutable and are optimized for situations where a collection
    /// is created very infrequently but is used very frequently at runtime. They have a relatively high
    /// cost to create but provide excellent lookup performance. Thus, these are ideal for cases
    /// where a collection is created once, potentially at the startup of an application, and used throughout
    /// the remainder of the life of the application. Frozen collections should only be initialized with
    /// trusted input.
    /// </remarks>
    public static class FrozenDictionary
    {
        /// <summary>Creates a <see cref="FrozenDictionary{TKey, TValue}"/> with the specified key/value pairs.</summary>
        /// <param name="source">The key/value pairs to use to populate the dictionary.</param>
        /// <param name="comparer">The comparer implementation to use to compare keys for equality. If null, <see cref="EqualityComparer{TKey}.Default"/> is used.</param>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
        /// <remarks>
        /// If the same key appears multiple times in the input, the latter one in the sequence takes precedence. This differs from <see cref="M:System.Linq.Enumerable.ToDictionary"/>,
        /// with which multiple duplicate keys will result in an exception.
        /// </remarks>
        /// <returns>A <see cref="FrozenDictionary{TKey, TValue}"/> that contains the specified keys and values.</returns>
        public static FrozenDictionary<TKey, TValue> ToFrozenDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source, IEqualityComparer<TKey>? comparer = null)
            where TKey : notnull
        {
            ThrowHelper.ThrowIfNull(source);
            comparer ??= EqualityComparer<TKey>.Default;

            // If the source is already frozen with the same comparer, it can simply be returned.
            if (source is FrozenDictionary<TKey, TValue> existing &&
                existing.Comparer.Equals(comparer))
            {
                return existing;
            }

            // Ensure we have a Dictionary<,> using the specified comparer such that all keys
            // are non-null and unique according to that comparer.
            if (source is not Dictionary<TKey, TValue> uniqueValues ||
                (uniqueValues.Count != 0 && !uniqueValues.Comparer.Equals(comparer)))
            {
                uniqueValues = new Dictionary<TKey, TValue>(comparer);
                foreach (KeyValuePair<TKey, TValue> pair in source)
                {
                    uniqueValues[pair.Key] = pair.Value;
                }
            }

            return Freeze(uniqueValues);
        }

        /// <summary>Creates a <see cref="FrozenDictionary{TKey, TSource}"/> from an <see cref="IEnumerable{TSource}"/> according to specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <typeparam name="TKey">The type of the key returned by <paramref name="keySelector"/>.</typeparam>
        /// <param name="source">An <see cref="IEnumerable{TSource}"/> from which to create a <see cref="FrozenDictionary{TKey, TSource}"/>.</param>
        /// <param name="keySelector">A function to extract a key from each element.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer{TKey}"/> to compare keys.</param>
        /// <returns>A <see cref="FrozenDictionary{TKey, TElement}"/> that contains the keys and values selected from the input sequence.</returns>
        public static FrozenDictionary<TKey, TSource> ToFrozenDictionary<TSource, TKey>(
            this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer = null)
            where TKey : notnull =>
            Freeze(source.ToDictionary(keySelector, comparer));

        /// <summary>Creates a <see cref="FrozenDictionary{TKey, TElement}"/> from an <see cref="IEnumerable{TSource}"/> according to specified key selector and element selector functions.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <typeparam name="TKey">The type of the key returned by <paramref name="keySelector"/>.</typeparam>
        /// <typeparam name="TElement">The type of the value returned by <paramref name="elementSelector"/>.</typeparam>
        /// <param name="source">An <see cref="IEnumerable{TSource}"/> from which to create a <see cref="FrozenDictionary{TKey, TElement}"/>.</param>
        /// <param name="keySelector">A function to extract a key from each element.</param>
        /// <param name="elementSelector">A transform function to produce a result element value from each element.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer{TKey}"/> to compare keys.</param>
        /// <returns>A <see cref="FrozenDictionary{TKey, TElement}"/> that contains the keys and values selected from the input sequence.</returns>
        public static FrozenDictionary<TKey, TElement> ToFrozenDictionary<TSource, TKey, TElement>(
            this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey>? comparer = null)
            where TKey : notnull =>
            Freeze(source.ToDictionary(keySelector, elementSelector, comparer));

        private static FrozenDictionary<TKey, TValue> Freeze<TKey, TValue>(Dictionary<TKey, TValue> source)
            where TKey : notnull
        {
            // If the input was empty, simply return the empty frozen dictionary singleton. The comparer is ignored.
            if (source.Count == 0)
            {
                return FrozenDictionary<TKey, TValue>.Empty;
            }

            IEqualityComparer<TKey> comparer = source.Comparer;

            if (typeof(TKey).IsValueType)
            {
                // Optimize for value types when the default comparer is being used. In such a case, the implementation
                // may use EqualityComparer<TKey>.Default.Equals/GetHashCode directly, with generic specialization enabling
                // the Equals/GetHashCode methods to be devirtualized and possibly inlined.
                if (ReferenceEquals(comparer, EqualityComparer<TKey>.Default))
                {
#if NET7_0_OR_GREATER
                    static FrozenDictionary<TKey, TValue> PickIntegerDictionary<TInt>(Dictionary<TKey, TValue> source)
                        where TInt : struct, IBinaryInteger<TInt>
                    {
                        TInt[] keys = (TInt[])(object)source.Keys.ToArray();
                        TValue[] values = source.Values.ToArray();

                        Array.Sort(keys, values);

                        TInt min = keys[0];
                        TInt max = keys[^1];
                        ulong range = ulong.CreateTruncating(max - min);

                        if (keys.Length <= Constants.MaxItemsInSmallIntegerFrozenCollection)
                        {
                            return (FrozenDictionary<TKey, TValue>)(object)new SmallIntegerFrozenDictionary<TInt, TValue>(keys, values);
                        }
                        else if (typeof(TInt) == typeof(int))
                        {
                            return (FrozenDictionary<TKey, TValue>)(object)new Int32FrozenDictionary<TValue>((Dictionary<int, TValue>)(object)source);
                        }
                        else
                        {
                            return (FrozenDictionary<TKey, TValue>)(object)new IntegerFrozenDictionary<TInt, TValue>((Dictionary<TInt, TValue>)(object)source);
                        }
                    }

                    if (typeof(TKey) == typeof(int))
                    {
                        return PickIntegerDictionary<int>(source);
                    }
                    else if (typeof(TKey) == typeof(uint))
                    {
                        return PickIntegerDictionary<uint>(source);
                    }
                    else if (typeof(TKey) == typeof(long))
                    {
                        return PickIntegerDictionary<long>(source);
                    }
                    else if (typeof(TKey) == typeof(ulong))
                    {
                        return PickIntegerDictionary<ulong>(source);
                    }
                    else if (typeof(TKey) == typeof(short))
                    {
                        return PickIntegerDictionary<short>(source);
                    }
                    else if (typeof(TKey) == typeof(ushort))
                    {
                        return PickIntegerDictionary<ushort>(source);
                    }
                    else if (typeof(TKey) == typeof(byte))
                    {
                        return PickIntegerDictionary<byte>(source);
                    }
                    else if (typeof(TKey) == typeof(sbyte))
                    {
                        return PickIntegerDictionary<sbyte>(source);
                    }

#else

                    if (typeof(TKey) == typeof(int))
                    {
                        int[] keys = (int[])(object)source.Keys.ToArray();
                        TValue[] values = source.Values.ToArray();

                        Array.Sort(keys, values);

                        int min = keys[0];
                        int max = keys[keys.Length - 1];
                        int range = max - min + 1;

                        if (keys.Length <= Constants.MaxItemsInSmallIntegerFrozenCollection)
                        {
                            return (FrozenDictionary<TKey, TValue>)(object)new SmallInt32FrozenDictionary<TValue>(keys, values);
                        }
                        else
                        {
                            return (FrozenDictionary<TKey, TValue>)(object)new Int32FrozenDictionary<TValue>((Dictionary<int, TValue>)(object)source);
                        }
                    }
#endif
                    else
                    {
                        return new ValueTypeDefaultComparerFrozenDictionary<TKey, TValue>(source);
                    }
                }
            }
            else if (typeof(TKey) == typeof(string))
            {
                // If the key is a string and the comparer is known to provide ordinal (case-sensitive or case-insensitive) semantics,
                // we can use an implementation that's able to examine and optimize based on lengths and/or subsequences within those strings.
                if (ReferenceEquals(comparer, EqualityComparer<TKey>.Default) ||
                    ReferenceEquals(comparer, StringComparer.Ordinal) ||
                    ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase))
                {
                    Dictionary<string, TValue> stringEntries = (Dictionary<string, TValue>)(object)source;
                    IEqualityComparer<string> stringComparer = (IEqualityComparer<string>)(object)comparer;

                    FrozenDictionary<string, TValue>? frozenDictionary = LengthBucketsFrozenDictionary<TValue>.CreateLengthBucketsFrozenDictionaryIfAppropriate(stringEntries, stringComparer);
                    if (frozenDictionary is not null)
                    {
                        return (FrozenDictionary<TKey, TValue>)(object)frozenDictionary;
                    }

                    var entries = (string[])(object)source.Keys.ToArray();

                    KeyAnalyzer.Analyze(entries, ReferenceEquals(stringComparer, StringComparer.OrdinalIgnoreCase), out KeyAnalyzer.AnalysisResults results);
                    if (results.SubstringHashing)
                    {
                        if (results.RightJustifiedSubstring)
                        {
                            if (results.IgnoreCase)
                            {
                                frozenDictionary = results.AllAscii
                                    ? new OrdinalStringFrozenDictionary_RightJustifiedCaseInsensitiveAsciiSubstring<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff, results.HashIndex, results.HashCount)
                                    : new OrdinalStringFrozenDictionary_RightJustifiedCaseInsensitiveSubstring<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff, results.HashIndex, results.HashCount);
                            }
                            else
                            {
                                frozenDictionary = results.HashCount == 1
                                    ? new OrdinalStringFrozenDictionary_RightJustifiedSingleChar<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff, results.HashIndex)
                                    : new OrdinalStringFrozenDictionary_RightJustifiedSubstring<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff, results.HashIndex, results.HashCount);
                            }
                        }
                        else
                        {
                            if (results.IgnoreCase)
                            {
                                frozenDictionary = results.AllAscii
                                    ? new OrdinalStringFrozenDictionary_LeftJustifiedCaseInsensitiveAsciiSubstring<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff, results.HashIndex, results.HashCount)
                                    : new OrdinalStringFrozenDictionary_LeftJustifiedCaseInsensitiveSubstring<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff, results.HashIndex, results.HashCount);
                            }
                            else
                            {
                                    frozenDictionary = results.HashCount == 1
                                        ? new OrdinalStringFrozenDictionary_LeftJustifiedSingleChar<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff, results.HashIndex)
                                        : new OrdinalStringFrozenDictionary_LeftJustifiedSubstring<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff, results.HashIndex, results.HashCount);
                            }
                        }
                    }
                    else
                    {
                        if (results.IgnoreCase)
                        {
                            frozenDictionary = results.AllAscii
                                ? new OrdinalStringFrozenDictionary_FullCaseInsensitiveAscii<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff)
                                : new OrdinalStringFrozenDictionary_FullCaseInsensitive<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff);
                        }
                        else
                        {
                            frozenDictionary = new OrdinalStringFrozenDictionary_Full<TValue>(stringEntries, entries, stringComparer, results.MinimumLength, results.MaximumLengthDiff);
                        }
                    }

                    return (FrozenDictionary<TKey, TValue>)(object)frozenDictionary;
                }
            }

            if (source.Count <= Constants.MaxItemsInSmallFrozenCollection)
            {
                // use the specialized dictionary for low item counts
                return new SmallFrozenDictionary<TKey, TValue>(source, comparer);
            }

            // No special-cases apply. Use the default frozen dictionary.
            return new DefaultFrozenDictionary<TKey, TValue>(source, comparer);
        }
    }

    /// <summary>Provides an immutable, read-only dictionary optimized for fast lookup and enumeration.</summary>
    /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of the values in this dictionary.</typeparam>
    /// <remarks>
    /// Frozen collections are immutable and are optimized for situations where a collection
    /// is created very infrequently but is used very frequently at runtime. They have a relatively high
    /// cost to create but provide excellent lookup performance. Thus, these are ideal for cases
    /// where a collection is created once, potentially at the startup of an application, and used throughout
    /// the remainder of the life of the application. Frozen collections should only be initialized with
    /// trusted input.
    /// </remarks>
    [DebuggerTypeProxy(typeof(ImmutableDictionaryDebuggerProxy<,>))]
    [DebuggerDisplay("Count = {Count}")]
    public abstract class FrozenDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDictionary
        where TKey : notnull
    {
        /// <summary>Initialize the dictionary.</summary>
        /// <param name="comparer">The comparer to use and to expose from <see cref="Comparer"/>.</param>
        private protected FrozenDictionary(IEqualityComparer<TKey> comparer) => Comparer = comparer;

        /// <summary>Gets an empty <see cref="FrozenDictionary{TKey, TValue}"/>.</summary>
        public static FrozenDictionary<TKey, TValue> Empty { get; } = new EmptyFrozenDictionary<TKey, TValue>();

        /// <summary>Gets the comparer used by this dictionary.</summary>
        public IEqualityComparer<TKey> Comparer { get; }

        /// <summary>
        /// Gets a collection containing the keys in the dictionary.
        /// </summary>
        /// <remarks>
        /// The order of the keys in the dictionary is unspecified, but it is the same order as the associated values returned by the <see cref="Values"/> property.
        /// </remarks>
        public ImmutableArray<TKey> Keys => ImmutableArrayFactory.Create(KeysCore);

        /// <inheritdoc cref="Keys" />
        private protected abstract TKey[] KeysCore { get; }

        /// <inheritdoc />
        ICollection<TKey> IDictionary<TKey, TValue>.Keys =>
            Keys is { Length: > 0 } keys ? keys : Array.Empty<TKey>();

        /// <inheritdoc />
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys =>
            ((IDictionary<TKey, TValue>)this).Keys;

        /// <inheritdoc />
        ICollection IDictionary.Keys => Keys;

        /// <summary>
        /// Gets a collection containing the values in the dictionary.
        /// </summary>
        /// <remarks>
        /// The order of the values in the dictionary is unspecified, but it is the same order as the associated keys returned by the <see cref="Keys"/> property.
        /// </remarks>
        public ImmutableArray<TValue> Values => ImmutableArrayFactory.Create(ValuesCore);

        /// <inheritdoc cref="Values" />
        private protected abstract TValue[] ValuesCore { get; }

        ICollection<TValue> IDictionary<TKey, TValue>.Values =>
            Values is { Length: > 0 } values ? values : Array.Empty<TValue>();

        /// <inheritdoc />
        ICollection IDictionary.Values => Values;

        /// <inheritdoc />
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values =>
            ((IDictionary<TKey, TValue>)this).Values;

        /// <summary>Gets the number of key/value pairs contained in the dictionary.</summary>
        public int Count => CountCore;

        /// <inheritdoc cref="Count" />
        private protected abstract int CountCore { get; }

        /// <summary>Copies the elements of the dictionary to an array of type <see cref="KeyValuePair{TKey, TValue}"/>, starting at the specified <paramref name="destinationIndex"/>.</summary>
        /// <param name="destination">The array that is the destination of the elements copied from the dictionary.</param>
        /// <param name="destinationIndex">The zero-based index in <paramref name="destination"/> at which copying begins.</param>
        public void CopyTo(KeyValuePair<TKey, TValue>[] destination, int destinationIndex)
        {
            ThrowHelper.ThrowIfNull(destination);
            CopyTo(destination.AsSpan(destinationIndex));
        }

        /// <summary>Copies the elements of the dictionary to a span of type <see cref="KeyValuePair{TKey, TValue}"/>.</summary>
        /// <param name="destination">The span that is the destination of the elements copied from the dictionary.</param>
        public void CopyTo(Span<KeyValuePair<TKey, TValue>> destination)
        {
            if (destination.Length < Count)
            {
                ThrowHelper.ThrowIfDestinationTooSmall();
            }

            TKey[] keys = KeysCore;
            TValue[] values = ValuesCore;
            Debug.Assert(keys.Length == values.Length);

            for (int i = 0; i < keys.Length; i++)
            {
                destination[i] = new KeyValuePair<TKey, TValue>(keys[i], values[i]);
            }
        }

        /// <inheritdoc />
        void ICollection.CopyTo(Array array, int index)
        {
            ThrowHelper.ThrowIfNull(array);

            if (array.Rank != 1)
            {
                throw new ArgumentException(SR.Arg_RankMultiDimNotSupported, nameof(array));
            }

            if (array.GetLowerBound(0) != 0)
            {
                throw new ArgumentException(SR.Arg_NonZeroLowerBound, nameof(array));
            }

            if ((uint)index > (uint)array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), SR.ArgumentOutOfRange_NeedNonNegNum);
            }

            if (array.Length - index < Count)
            {
                throw new ArgumentException(SR.Arg_ArrayPlusOffTooSmall, nameof(array));
            }

            if (array is KeyValuePair<TKey, TValue>[] pairs)
            {
                foreach (KeyValuePair<TKey, TValue> item in this)
                {
                    pairs[index++] = new KeyValuePair<TKey, TValue>(item.Key, item.Value);
                }
            }
            else if (array is DictionaryEntry[] dictEntryArray)
            {
                foreach (KeyValuePair<TKey, TValue> item in this)
                {
                    dictEntryArray[index++] = new DictionaryEntry(item.Key, item.Value);
                }
            }
            else
            {
                if (array is not object[] objects)
                {
                    throw new ArgumentException(SR.Argument_IncompatibleArrayType, nameof(array));
                }

                try
                {
                    foreach (KeyValuePair<TKey, TValue> item in this)
                    {
                        objects[index++] = new KeyValuePair<TKey, TValue>(item.Key, item.Value);
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    throw new ArgumentException(SR.Argument_IncompatibleArrayType, nameof(array));
                }
            }
        }

        /// <inheritdoc />
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => true;

        /// <inheritdoc />
        bool IDictionary.IsReadOnly => true;

        /// <inheritdoc />
        bool IDictionary.IsFixedSize => true;

        /// <inheritdoc />
        bool ICollection.IsSynchronized => false;

        /// <inheritdoc />
        object ICollection.SyncRoot => this;

        /// <inheritdoc />
        object? IDictionary.this[object key]
        {
            get
            {
                ThrowHelper.ThrowIfNull(key);
                return key is TKey tkey && TryGetValue(tkey, out TValue? value) ?
                    value :
                    (object?)null;
            }
            set => throw new NotSupportedException();
        }

        /// <summary>Gets either a reference to a <typeparamref name="TValue"/> in the dictionary or a null reference if the key does not exist in the dictionary.</summary>
        /// <param name="key">The key used for lookup.</param>
        /// <returns>A reference to a <typeparamref name="TValue"/> in the dictionary or a null reference if the key does not exist in the dictionary.</returns>
        /// <remarks>The null reference can be detected by calling <see cref="Unsafe.IsNullRef{T}(ref T)"/>.</remarks>
        public ref readonly TValue GetValueRefOrNullRef(TKey key)
        {
            if (key is null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(key));
            }

            return ref GetValueRefOrNullRefCore(key);
        }

        /// <inheritdoc cref="GetValueRefOrNullRef" />
        private protected abstract ref readonly TValue GetValueRefOrNullRefCore(TKey key);

        /// <summary>Gets a reference to the value associated with the specified key.</summary>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>A reference to the value associated with the specified key.</returns>
        /// <exception cref="KeyNotFoundException"><paramref name="key"/> does not exist in the collection.</exception>
        public ref readonly TValue this[TKey key]
        {
            get
            {
                ref readonly TValue valueRef = ref GetValueRefOrNullRef(key);

                if (Unsafe.IsNullRef(ref Unsafe.AsRef(in valueRef)))
                {
                    ThrowHelper.ThrowKeyNotFoundException();
                }

                return ref valueRef;
            }
        }

        /// <inheritdoc />
        TValue IDictionary<TKey, TValue>.this[TKey key]
        {
            get => this[key];
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        TValue IReadOnlyDictionary<TKey, TValue>.this[TKey key] =>
            this[key];

        /// <summary>Determines whether the dictionary contains the specified key.</summary>
        /// <param name="key">The key to locate in the dictionary.</param>
        /// <returns><see langword="true"/> if the dictionary contains an element with the specified key; otherwise, <see langword="false"/>.</returns>
        public bool ContainsKey(TKey key) =>
            !Unsafe.IsNullRef(ref Unsafe.AsRef(in GetValueRefOrNullRef(key)));

        /// <inheritdoc />
        bool IDictionary.Contains(object key)
        {
            ThrowHelper.ThrowIfNull(key);
            return key is TKey tkey && ContainsKey(tkey);
        }

        /// <inheritdoc />
        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) =>
            TryGetValue(item.Key, out TValue? value) &&
            EqualityComparer<TValue>.Default.Equals(value, item.Value);

        /// <summary>Gets the value associated with the specified key.</summary>
        /// <param name="key">The key of the value to get.</param>
        /// <param name="value">
        /// When this method returns, contains the value associated with the specified key, if the key is found;
        /// otherwise, the default value for the type of the value parameter.
        /// </param>
        /// <returns><see langword="true"/> if the dictionary contains an element with the specified key; otherwise, <see langword="false"/>.</returns>
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            ref readonly TValue valueRef = ref GetValueRefOrNullRef(key);

            if (!Unsafe.IsNullRef(ref Unsafe.AsRef(in valueRef)))
            {
                value = valueRef;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>Returns an enumerator that iterates through the dictionary.</summary>
        /// <returns>An enumerator that iterates through the dictionary.</returns>
        public Enumerator GetEnumerator() => GetEnumeratorCore();

        /// <inheritdoc cref="GetEnumerator" />
        private protected abstract Enumerator GetEnumeratorCore();

        /// <inheritdoc />
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
            Count == 0 ? ((IList<KeyValuePair<TKey, TValue>>)Array.Empty<KeyValuePair<TKey, TValue>>()).GetEnumerator() :
            GetEnumerator();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() =>
            Count == 0 ? Array.Empty<KeyValuePair<TKey, TValue>>().GetEnumerator() :
            GetEnumerator();

        /// <inheritdoc />
        IDictionaryEnumerator IDictionary.GetEnumerator() =>
            new DictionaryEnumerator<TKey, TValue>(GetEnumerator());

        /// <inheritdoc />
        void IDictionary<TKey, TValue>.Add(TKey key, TValue value) => throw new NotSupportedException();

        /// <inheritdoc />
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => throw new NotSupportedException();

        /// <inheritdoc />
        void IDictionary.Add(object key, object? value) => throw new NotSupportedException();

        /// <inheritdoc />
        bool IDictionary<TKey, TValue>.Remove(TKey key) => throw new NotSupportedException();

        /// <inheritdoc />
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => throw new NotSupportedException();

        /// <inheritdoc />
        void IDictionary.Remove(object key) => throw new NotSupportedException();

        /// <inheritdoc />
        void ICollection<KeyValuePair<TKey, TValue>>.Clear() => throw new NotSupportedException();

        /// <inheritdoc />
        void IDictionary.Clear() => throw new NotSupportedException();

        /// <summary>Enumerates the elements of a <see cref="FrozenDictionary{TKey, TValue}"/>.</summary>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly TKey[] _keys;
            private readonly TValue[] _values;
            private int _index;

            /// <summary>Initialize the enumerator with the specified keys and values.</summary>
            internal Enumerator(TKey[] keys, TValue[] values)
            {
                Debug.Assert(keys.Length == values.Length);
                _keys = keys;
                _values = values;
                _index = -1;
            }

            /// <inheritdoc cref="IEnumerator.MoveNext" />
            public bool MoveNext()
            {
                _index++;
                if ((uint)_index < (uint)_keys.Length)
                {
                    return true;
                }

                _index = _keys.Length;
                return false;
            }

            /// <inheritdoc cref="IEnumerator{T}.Current" />
            public readonly KeyValuePair<TKey, TValue> Current
            {
                get
                {
                    if ((uint)_index >= (uint)_keys.Length)
                    {
                        ThrowHelper.ThrowInvalidOperationException();
                    }

                    return new KeyValuePair<TKey, TValue>(_keys[_index], _values[_index]);
                }
            }

            /// <inheritdoc />
            object IEnumerator.Current => Current;

            /// <inheritdoc />
            void IEnumerator.Reset() => _index = -1;

            /// <inheritdoc />
            void IDisposable.Dispose() { }
        }
    }
}
