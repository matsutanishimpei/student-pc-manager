using System;

namespace Share.Models
{
    public class CommandRequest
    {
        public string Command { get; set; } = string.Empty;
    }

    public class CommandResponse
    {
        public int ExitCode { get; set; }
        public string Stdout { get; set; } = string.Empty;
        public string Stderr { get; set; } = string.Empty;
    }

    public class ServerInfoResponse
    {
        public string MachineName { get; set; } = string.Empty;
    }

    public class MacAddressResponse
    {
        public string MacAddress { get; set; } = string.Empty;
    }
}
