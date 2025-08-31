namespace QHotUpdateSystem.Security
{
    /// <summary>
    /// 简易 CRC32
    /// </summary>
    public static class CrcUtility
    {
        static readonly uint[] Table = InitTable();

        static uint[] InitTable()
        {
            const uint poly = 0xEDB88320u;
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ poly : crc >> 1;
                table[i] = crc;
            }
            return table;
        }

        public static uint Compute(byte[] data)
        {
            if (data == null) return 0;
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
                crc = (crc >> 8) ^ Table[(crc ^ data[i]) & 0xFF];
            return crc ^ 0xFFFFFFFFu;
        }
    }
}