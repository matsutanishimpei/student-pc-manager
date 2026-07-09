using Share.Models;
using System.Threading.Tasks;

namespace Server.Services
{
    public interface IInteractiveTaskExecutor
    {
        Task<byte[]?> GetScreenshotAsync();
        Task<string> GetActiveAppAsync();
        Task<string> GetProcessesJsonAsync();
        Task<CommandResponse> ExecuteCommandAsync(string command, bool runInUserSession);
    }
}
