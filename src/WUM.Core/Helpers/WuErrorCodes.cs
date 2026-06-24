// src/WUM.Core/Helpers/WuErrorCodes.cs
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WUM.Core.Helpers
{
    /// <summary>
    /// Maps common Windows Update / WININET HRESULT codes (0x8024xxxx,
    /// 0x80072xxx, 0x80070xxx) to human-readable causes, and extracts a code
    /// from a raw COM exception message.
    /// </summary>
    public static class WuErrorCodes
    {
        // Well-known codes — kept small + high-signal, not exhaustive.
        private static readonly Dictionary<string, string> Map =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Connectivity (WININET) ───────────────────────────────────
            ["0x80072EE2"] = "Connection timed out reaching Windows Update (WININET timeout). Check network/proxy.",
            ["0x80072EE7"] = "Server name could not be resolved (DNS). Check DNS / hosts file.",
            ["0x80072EFD"] = "Cannot connect to the update server. Check firewall / proxy.",
            ["0x80072EFE"] = "Connection to the server was abnormally terminated.",
            ["0x80072F8F"] = "System clock is wrong, breaking TLS. Fix date/time.",

            // ── HTTP-level (PT) ──────────────────────────────────────────
            ["0x8024401C"] = "Server returned HTTP 408 (request timeout).",
            ["0x80244022"] = "Server returned HTTP 503 (service unavailable) — WSUS/IIS down.",
            ["0x80244019"] = "Server returned HTTP 404 — update content not found.",
            ["0x80244018"] = "Server returned HTTP 403 — access forbidden.",
            ["0x8024402C"] = "Proxy/connection error contacting the update server. Check proxy settings.",
            ["0x8024402F"] = "External cab processing error (often a proxy intercepting content).",

            // ── Service / policy ─────────────────────────────────────────
            ["0x80070422"] = "Windows Update service is disabled. Set 'wuauserv' to Manual and start it.",
            ["0x8024002E"] = "Access to the Windows Update server is blocked by policy (managed/WSUS).",
            ["0x8024A005"] = "Automatic Updates is disabled by policy.",
            ["0x80244007"] = "SOAP client failed — usually a TLS/SSL or proxy interception problem.",

            // ── Search / catalog ─────────────────────────────────────────
            ["0x80240024"] = "No updates are applicable to this computer.",
            ["0x8024000B"] = "Operation was cancelled.",
            ["0x8024000C"] = "No operation was required (already up to date).",
            ["0x80240438"] = "Cannot establish a connection to the update service (no transport).",

            // ── Install / download ───────────────────────────────────────
            ["0x80240034"] = "An update failed to download.",
            ["0x80246007"] = "An update has not been downloaded.",
            ["0x800F0922"] = "Install failed — often insufficient WinRE/recovery partition space.",
        };

        private static readonly Regex CodeRx =
            new(@"0x[0-9A-Fa-f]{8}", RegexOptions.Compiled);

        /// <summary>Returns the description for an exact code, or null.</summary>
        public static string? Lookup(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            return Map.TryGetValue(code.Trim(), out var desc) ? desc : null;
        }

        /// <summary>
        /// Finds the first 0x-prefixed HRESULT in <paramref name="text"/> and
        /// returns "0xCODE — meaning". Returns null when no code is present;
        /// returns "0xCODE — unrecognized…" for an unknown code.
        /// </summary>
        public static string? Decode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = CodeRx.Match(text);
            if (!m.Success) return null;

            string code = m.Value.ToUpperInvariant();
            string desc = Map.TryGetValue(code, out var d)
                ? d
                : "Unrecognized Windows Update error code.";
            return code + " — " + desc;
        }
    }
}
