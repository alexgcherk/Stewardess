// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace StewardessMCPService.Infrastructure
{
    /// <summary>
    /// Minimal thread-safe IoC container used as the application's dependency
    /// resolution root.
    ///
    /// Supports:
    ///   • Singleton instances registered at startup.
    ///   • Per-resolve factory delegates for transient lifetime.
    ///   • Keyed (named) registrations for multiple implementations of the same interface.
    ///
    /// Replace with a full-featured container (Unity, Autofac, etc.) if advanced
    /// features such as property injection or child scopes are required.
    /// </summary>
    public static class ServiceLocator
    {
        // ── Storage ──────────────────────────────────────────────────────────────

        // Key: typeof(TInterface)
        private static readonly ConcurrentDictionary<Type, Func<object>> _factories =
            new ConcurrentDictionary<Type, Func<object>>();

        // Key: (typeof(TInterface), name)
        private static readonly ConcurrentDictionary<(Type, string), Func<object>> _namedFactories =
            new ConcurrentDictionary<(Type, string), Func<object>>();

        // ── Registration ─────────────────────────────────────────────────────────

        /// <summary>
        /// Registers a singleton instance for <typeparamref name="TInterface"/>.
        /// The same instance is returned on every <see cref="Resolve{T}()"/> call.
        /// </summary>
        public static void RegisterSingleton<TInterface>(TInterface instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            _factories[typeof(TInterface)] = () => instance;
        }

        /// <summary>
        /// Registers a factory delegate for <typeparamref name="TInterface"/>.
        /// The delegate is invoked on every <see cref="Resolve{T}()"/> call (transient).
        /// </summary>
        public static void RegisterFactory<TInterface>(Func<TInterface> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _factories[typeof(TInterface)] = () => factory()!;
        }

        /// <summary>
        /// Registers a singleton instance under a named key.
        /// Useful when multiple implementations of the same interface coexist.
        /// </summary>
        public static void RegisterSingleton<TInterface>(string name, TInterface instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            _namedFactories[(typeof(TInterface), name)] = () => instance;
        }

        // ── Resolution ───────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the registered service for <typeparamref name="T"/>.
        /// Throws <see cref="InvalidOperationException"/> when no registration exists.
        /// </summary>
        public static T Resolve<T>()
        {
            if (_factories.TryGetValue(typeof(T), out var factory))
                return (T)factory();

            throw new InvalidOperationException(
                $"No service registered for type '{typeof(T).FullName}'. " +
                 "Call ServiceLocator.Register* before calling Resolve.");
        }

        /// <summary>
        /// Resolves the named registration for <typeparamref name="T"/>.
        /// Throws <see cref="InvalidOperationException"/> when not found.
        /// </summary>
        public static T Resolve<T>(string name)
        {
            if (_namedFactories.TryGetValue((typeof(T), name), out var factory))
                return (T)factory();

            throw new InvalidOperationException(
                $"No service registered for type '{typeof(T).FullName}' with name '{name}'.");
        }

        /// <summary>
        /// Attempts to resolve the service; returns false and sets
        /// <paramref name="service"/> to default when the registration is absent.
        /// </summary>
        public static bool TryResolve<T>(out T service)
        {
            service = default!;
            if (!_factories.TryGetValue(typeof(T), out var factory)) return false;
            service = (T)factory();
            return true;
        }

        /// <summary>Returns true when a registration exists for <typeparamref name="T"/>.</summary>
        public static bool IsRegistered<T>() => _factories.ContainsKey(typeof(T));

        // ── Lifecycle ────────────────────────────────────────────────────────────

        /// <summary>
        /// Removes all registrations.  Intended for unit test isolation; do not
        /// call in production code.
        /// </summary>
        public static void Reset()
        {
            _factories.Clear();
            _namedFactories.Clear();
        }

        /// <summary>Returns a snapshot of all currently registered type names (for diagnostics).</summary>
        public static IReadOnlyList<string> GetRegisteredTypeNames()
        {
            var names = new List<string>();
            foreach (var key in _factories.Keys)
                names.Add(key.FullName!);
            return names;
        }
    }
}
