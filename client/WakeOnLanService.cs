using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace client
{
    internal static class WakeOnLanService
    {
        public static async Task SendAsync(string macAddress)
        {
            string cleanMac = Regex.Replace(macAddress, "[-:]", string.Empty);
            if (cleanMac.Length != 12)
            {
                throw new ArgumentException("無効なMACアドレスです。12桁の16進数で入力してください。", nameof(macAddress));
            }

            byte[] macBytes = Convert.FromHexString(cleanMac);
            byte[] packet = new byte[102];
            Array.Fill(packet, (byte)0xFF, 0, 6);
            for (int i = 0; i < 16; i++)
            {
                Buffer.BlockCopy(macBytes, 0, packet, 6 + i * 6, macBytes.Length);
            }

            using var client = new UdpClient { EnableBroadcast = true };
            await client.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
        }
    }
}
