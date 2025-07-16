// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NETFRAMEWORK
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Microsoft.Extensions.ServiceDiscovery
{
    internal static partial class FrameworkExtensions
    {
        extension(string s)
        {
            public bool StartsWith(char c) => s is [{ } first, ..] && first == c;
        }

        extension(ArgumentException)
        {
            public static void ThrowIfNullOrEmpty([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
            {
                if (string.IsNullOrEmpty(argument))
                {
                    ThrowNullOrEmptyException(argument, paramName);
                }
            }
        }

        extension(ExceptionDispatchInfo)
        {
            [DoesNotReturn]
            public static void Throw(Exception ex)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }

        extension(ArgumentNullException)
        {
            public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
            {
                if (argument is null)
                {
                    Throw(paramName);
                }
            }
        }

        extension(ObjectDisposedException)
        {
            public static void ThrowIf([DoesNotReturnIf(true)] bool condition, object instance)
            {
                if (condition)
                {
                    throw new ObjectDisposedException(instance?.GetType().FullName);
                }
            }
        }

        extension<TKey, TValue>(KeyValuePair<TKey, TValue> pair)
        {
            public void Deconstruct(out TKey key, out TValue value)
            {
                key = pair.Key;
                value = pair.Value;
            }
        }

        extension<TKey, TValue>(ConcurrentDictionary<TKey, TValue> dictionary)
        {
            public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
            {
                if (dictionary.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                return dictionary.GetOrAdd(key, valueFactory(key));
            }

            public TValue GetOrAdd<TState>(TKey key, Func<TKey, TState, TValue> valueFactory, TState state)
            {
                if (dictionary.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                return dictionary.GetOrAdd(key, valueFactory(key, state));
            }

            public void TryRemove(TKey key)
            {
                dictionary.TryRemove(key, out _);
            }

            public void TryRemove(KeyValuePair<TKey, TValue> pair)
            {
                if (dictionary.TryRemove(pair.Key, out var existing) && !EqualityComparer<TValue>.Default.Equals(existing, pair.Value))
                {
                    dictionary.TryAdd(pair.Key, pair.Value);
                }
            }
        }

        [DoesNotReturn]
        internal static void Throw(string? paramName) => throw new ArgumentNullException(paramName);

        [DoesNotReturn]
        private static void ThrowNullOrEmptyException(string? argument, string? paramName)
        {
            ArgumentNullException.ThrowIfNull(argument, paramName);
            throw new ArgumentException("The value cannot be an empty string.", paramName);
        }
    }

    internal static class KeyValuePair
    {
        public static KeyValuePair<TKey, TValue> Create<TKey, TValue>(TKey key, TValue value)
        {
            return new KeyValuePair<TKey, TValue>(key, value);
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
    {
        public string ParameterName => parameterName;
    }
}
#endif
