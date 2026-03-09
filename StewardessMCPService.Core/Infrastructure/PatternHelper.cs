// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace StewardessMCPService.Infrastructure
{
    /// <summary>
    /// Helpers for detecting and working with filename search patterns.
    /// </summary>
    internal static class PatternHelper
    {
        // Characters that appear in .NET regex syntax but not in ordinary file paths or glob patterns.
        // Presence of any one of these (plus backslash) strongly suggests regex intent.
        private static readonly char[] RegexSignals = { '^', '$', '(', ')', '[', ']', '{', '}', '|', '+' };

        /// <summary>
        /// Returns <c>true</c> when <paramref name="pattern"/> appears to be a .NET regular expression:
        /// it contains at least one regex metacharacter (<c>^ $ ( ) [ ] { } | + \</c>) AND
        /// it compiles without error as a <see cref="Regex"/>.
        /// Ordinary substrings and glob patterns (containing only <c>*</c> or <c>?</c>) return <c>false</c>.
        /// </summary>
        public static bool IsLikelyRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;

            // Quick scan: at least one regex signal character must be present.
            bool hasSignal = pattern.Contains('\\') || pattern.Any(c => Array.IndexOf(RegexSignals, c) >= 0);
            if (!hasSignal) return false;

            // Validate that it is actually a legal regex before committing to regex mode.
            try { _ = new Regex(pattern); return true; }
            catch (ArgumentException) { return false; }
        }
    }
}
