using System.Threading.Tasks;

namespace Splice.Services;

public interface INotificationService
{
    Task ShowErrorAsync(string title, string message);
    Task<bool> ConfirmStopAndExitAsync();
}
