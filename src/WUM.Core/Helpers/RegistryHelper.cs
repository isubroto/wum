// src/WUM.Core/Helpers/RegistryHelper.cs
using System;
using Microsoft.Win32;

namespace WUM.Core.Helpers
{
    public class RegistryHelper
    {
        private readonly RegistryHive _hive;

        public RegistryHelper(RegistryHive hive = RegistryHive.LocalMachine)
        {
            _hive = hive;
        }

        // ── Get ───────────────────────────────────────────────────────────
        public T? GetValue<T>(string keyPath, string valueName, T? defaultValue)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(
                    _hive, RegistryView.Registry64);
                using var key  = root.OpenSubKey(keyPath);

                var val = key?.GetValue(valueName);
                if (val == null) return defaultValue;

                // Handle string? case
                if (typeof(T) == typeof(string))
                    return (T)(object)val.ToString()!;

                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        // ── Set ───────────────────────────────────────────────────────────
        public void SetValue(string keyPath, string valueName, object value)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(
                    _hive, RegistryView.Registry64);
                using var key  = root.OpenSubKey(keyPath, writable: true)
                    ?? root.CreateSubKey(keyPath);

                var kind = value switch
                {
                    int    => RegistryValueKind.DWord,
                    long   => RegistryValueKind.QWord,
                    _      => RegistryValueKind.String
                };

                key?.SetValue(valueName, value, kind);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to set registry '{valueName}': {ex.Message}", ex);
            }
        }

        // ── Delete ────────────────────────────────────────────────────────
        public void DeleteValue(string keyPath, string valueName)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(
                    _hive, RegistryView.Registry64);
                using var key  = root.OpenSubKey(keyPath, writable: true);
                key?.DeleteValue(valueName, throwOnMissingValue: false);
            }
            catch { /* ignore */ }
        }

        // ── Key Exists ────────────────────────────────────────────────────
        public bool KeyExists(string keyPath)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(
                    _hive, RegistryView.Registry64);
                using var key  = root.OpenSubKey(keyPath);
                return key != null;
            }
            catch { return false; }
        }
    }
}