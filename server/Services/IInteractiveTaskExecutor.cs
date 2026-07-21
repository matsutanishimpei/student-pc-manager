using Share.Models;
using System.Threading.Tasks;
using System.Threading;

namespace Server.Services
{
    public interface IInteractiveTaskExecutor
    {
        Task<byte[]?> GetScreenshotAsync(CancellationToken cancellationToken = default);
        Task<string> GetActiveAppAsync(CancellationToken cancellationToken = default);
        Task<string> GetProcessesJsonAsync(CancellationToken cancellationToken = default);
        Task<CommandResponse> ExecuteCommandAsync(string command, bool runInUserSession, CancellationToken cancellationToken = default);
    }
}
