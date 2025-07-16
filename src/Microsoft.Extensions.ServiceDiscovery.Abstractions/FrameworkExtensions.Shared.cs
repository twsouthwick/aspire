// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NETFRAMEWORK
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Microsoft.Extensions.ServiceDiscovery
{
    internal static partial class FrameworkExtensions
    {
        extension(StringSplitOptions)
        {
            public static StringSplitOptions TrimEntries => (StringSplitOptions)2;
        }

        extension(string s)
        {
            public bool StartsWith(char c) => s is [{ } first, ..] && first == c;
        }

        extension(char)
        {
            public static bool IsAsciiLetterOrDigit(char c) => char.IsAsciiLetter(c) | char.IsBetween(c, '0', '9');
            public static bool IsAsciiLetter(char c) => (uint)((c | 0x20) - 'a') <= 'z' - 'a';
            public static bool IsBetween(char c, char minInclusive, char maxInclusive) =>
                (uint)(c - minInclusive) <= (uint)(maxInclusive - minInclusive);
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

        extension(ValueTask)
        {
            public static ValueTask<T> FromResult<T>(T value)
            {
                return new ValueTask<T>(value);
            }
        }

        extension(Span<byte> span)
        {
            public bool Contains(byte b)
            {
                return span.IndexOf(b) != -1;
            }
        }

        extension(Encoding encoding)
        {
            public bool TryGetBytes(string str, Span<byte> bytes, out int bytesWritten)
            {
                int required = encoding.GetByteCount(str);
                if (required <= bytes.Length)
                {
                    var result = encoding.GetBytes(str);

                    if (result.Length <= bytes.Length)
                    {
                        result.CopyTo(bytes);
                        bytesWritten = result.Length;
                        return true;
                    }
                }

                bytesWritten = 0;
                return false;
            }
        }

        extension(ArgumentOutOfRangeException)
        {
            public static void ThrowIfGreaterThan<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
                where T : IComparable<T>
            {
                if (value.CompareTo(other) > 0)
                {
                    ArgumentOutOfRangeException.Throw(value, other, paramName);
                }
            }

            public static void ThrowIfLessThanOrEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
                where T : IComparable<T>
            {
                if (value.CompareTo(other) <= 0)
                {
                    ArgumentOutOfRangeException.Throw(value, other, paramName);
                }
            }

            [DoesNotReturn]
            private static void Throw<T>(T value, T other, string? paramName) =>
                throw new ArgumentOutOfRangeException(paramName, value, "Argument was out of range");
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

        extension<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
        {
            public bool TryAdd(TKey key, TValue value)
            {
                if (dictionary.ContainsKey(key))
                {
                    return false;
                }
                dictionary.Add(key, value);
                return true;
            }
        }

        extension<T>(ArraySegment<T>)
        {
            public static ArraySegment<T> Empty => new([], 0, 0);
        }

        extension(Socket socket)
        {
            public async Task<int> SendAsync(ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, EndPoint endpoint, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();

                var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);

                try
                {
                    buffer.CopyTo(rented);
                    var tcs = new TaskCompletionSource<int>();

                    using var _ = token.Register(() => tcs.TrySetCanceled());

                    using var args = new SocketAsyncEventArgs()
                    {
                        RemoteEndPoint = endpoint,
                        SocketFlags = socketFlags,
                    };

                    args.SetBuffer(rented, 0, buffer.Length);

                    args.Completed += (object sender, SocketAsyncEventArgs e) =>
                    {
                        if (e.ConnectByNameError is { } exception)
                        {
                            tcs.SetException(exception);
                        }

                        tcs.SetResult(e.BytesTransferred);
                    };

                    if (!socket.SendAsync(args))
                    {
                        // If it's false, the operation completed synchronously.
                        tcs.SetResult(args.BytesTransferred);
                    }

                    return await tcs.Task.ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }

            public async Task<int> ReceiveAsync(Memory<byte> buffer, SocketFlags socketFlags, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();

                var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);

                try
                {
                    var tcs = new TaskCompletionSource<int>();

                    using var _ = token.Register(() => tcs.TrySetCanceled());

                    using var args = new SocketAsyncEventArgs()
                    {
                        SocketFlags = socketFlags,
                    };

                    args.SetBuffer(rented, 0, buffer.Length);

                    args.Completed += (object sender, SocketAsyncEventArgs e) =>
                    {
                        if (e.ConnectByNameError is { } exception)
                        {
                            tcs.SetException(exception);
                        }

                        tcs.SetResult(e.BytesTransferred);
                    };

                    if (!socket.ReceiveAsync(args))
                    {
                        // If it's false, the operation completed synchronously.
                        tcs.SetResult(args.BytesTransferred);
                    }

                    var count = await tcs.Task.ConfigureAwait(false);

                    rented.AsMemory(0, count).CopyTo(buffer);

                    return count;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }

            private void Args_Completed(object sender, SocketAsyncEventArgs e)
            {
                throw new NotImplementedException();
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

namespace System.Diagnostics
{
    internal sealed class UnreachableException : Exception
    {
    }
}

namespace System.Buffers
{
    internal static class SearchValues
    {
        public static SearchValues<T> Create<T>(string str)
        {
            return new SearchValues<T>(str);
        }
    }

    internal class SearchValues<T>
    {
        internal SearchValues(string str)
        {
            _ = str;
            // Implementation for SearchValues
        }
    }
}

#endif
