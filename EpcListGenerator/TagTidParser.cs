using System;
using System.Collections.Generic;

namespace OctaneTagWritingTest.Helpers
{
    public class TagTidParser : IDisposable
    {
        private readonly byte[] _tid;
        private bool _disposed;

        private static readonly Dictionary<string, string> KnownTidPrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "E2801190", "Impinj Monza R6" },
            { "E2801191", "Impinj M730" },
            { "E28011A0", "Impinj M770" },
            { "E28011B0", "Impinj M830/M850" },
            { "E2806915", "NXP UCODE 9" },
            { "E2806995", "NXP UCODE 9" },
            { "E2827802", "Fudan FM13UF00051E" },
            { "E2827882", "Fudan FM13UF00051E" },
            { "E2827803", "Fudan FM13UF011E" },
            { "E2827883", "Fudan FM13UF011E" },
            { "E2827804", "Fudan FM13UF011X" },
            { "E2827884", "Fudan FM13UF011X" },
            // Adicione mais conforme necessário
        };

        public TagTidParser(string tidHex)
        {
            if (string.IsNullOrWhiteSpace(tidHex))
                throw new ArgumentNullException(nameof(tidHex));

            tidHex = tidHex.Replace(" ", "").Replace("-", "");

            if (tidHex.Length != 24)
                throw new ArgumentException("TID must be 24 hex characters (96 bits)", nameof(tidHex));

            _tid = new byte[12];
            for (int i = 0; i < 12; i++)
                _tid[i] = Convert.ToByte(tidHex.Substring(i * 2, 2), 16);
        }

        public string Get40BitSerialHex()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TagTidParser));

            if (IsNxpUcode9Tid())
            {
                ulong serial = 0;
                for (int i = 7; i <= 11; i++)
                    serial = (serial << 8) | _tid[i];

                return serial.ToString("X10");
            }

            if (IsImpinjTid())
            {
                ulong serial = 0;
                for (int i = 6; i <= 10; i++)
                    serial = (serial << 8) | _tid[i];

                return serial.ToString("X10");
            }

            return GetFallbackSerialHex();
        }

        public ulong Get40BitSerialDecimal()
        {
            string hex = Get40BitSerialHex();
            return Convert.ToUInt64(hex, 16);
        }

        private string GetFallbackSerialHex()
        {
            return BitConverter.ToString(_tid, _tid.Length - 5, 5).Replace("-", "");
        }

        private bool IsImpinjTid()
        {
            return _tid[0] == 0xE2 && _tid[1] == 0x80 && (_tid[2] >> 4) == 0x1;
        }

        private bool IsNxpUcode9Tid()
        {
            return _tid[0] == 0xE2 && _tid[1] == 0x80 && _tid[2] == 0x69 && (_tid[3] == 0x15 || _tid[3] == 0x95);
        }

        public int GetMonzaSeriesId()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TagTidParser));
            return (_tid[10] >> 6) & 0b11;
        }

        public string GetTagModelNumber()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TagTidParser));
            int tmn = ((_tid[2] & 0x0F) << 8) | _tid[3];
            return tmn.ToString("X3");
        }

        public string GetTagModelName()
        {
            int tmn = ((_tid[2] & 0x0F) << 8) | _tid[3];
            return TagModelMap.TryGetValue(tmn, out var name) ? name : $"Desconhecido (TMN 0x{tmn:X3})";
        }

        public string GetVendorFromTid()
        {
            string prefix = BitConverter.ToString(_tid, 0, 4).Replace("-", "");
            return KnownTidPrefixes.TryGetValue(prefix, out var vendor) ? vendor : "Desconhecido";
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Array.Clear(_tid, 0, _tid.Length);
                _disposed = true;
            }
        }

        // Suporte para nome de modelo se necessário
        private static readonly Dictionary<int, string> TagModelMap = new()
        {
            // Impinj M700 Series
            { 0x190, "Impinj M750" },
            { 0x191, "Impinj M730" },
            { 0x1A0, "Impinj M770" },

            // Impinj M800 Series
            { 0x1B0, "Impinj M830/M850" },

            // Impinj Monza R6 Family
            { 0x120, "Impinj Monza R6" },
            { 0x121, "Impinj Monza R6-A" },
            { 0x122, "Impinj Monza R6-P" },

            // Impinj Monza 4 Series
            { 0x0B2, "Impinj Monza 4D" },
            { 0x0B3, "Impinj Monza 4E" },
            { 0x0B4, "Impinj Monza 4U" },
            { 0x0B5, "Impinj Monza 4QT" },

            // Impinj Monza 5 Series
            { 0x0C0, "Impinj Monza 5" },

                     // NXP UCODE 9
            { 0x915, "NXP UCODE 9" },
            { 0x995, "NXP UCODE 9" },

            // NXP UCODE 8 (prefixos comuns identificados)
            { 0x910, "NXP UCODE 8" },
            { 0x990, "NXP UCODE 8" },

            // NXP UCODE 7 (valor comum usado)
            { 0x970, "NXP UCODE 7" }

        };
    }
}
